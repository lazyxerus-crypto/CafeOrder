namespace CafeOrder;

public sealed partial class SampleData
{
    private sealed record ResolvedImport(MegaCoffeeProductSnapshot Product, string PendingPath, string FinalPath);

    internal async Task<ProductImportPlan> PrepareImportAsync(string path,
        Func<string, Task<MegaProductLookupResult>> lookup, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var read = await Task.Run(() => Workbook().Import(path), cancellationToken);
        var issues = ValidateImportRows(read, true);
        if (issues.Count != 0) return new([], issues);
        var resolved = new Dictionary<int, ResolvedImport>();
        bool transferred = false;
        try
        {
            var linked = read.Rows.Where(row => row.LookupRequested).ToArray();
            for (int index = 0; index < linked.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = linked[index];
                progress?.Report($"상품 조회 {index + 1}/{linked.Length} · {row.SheetRow}행");
                MegaProductLookupResult result;
                try { result = await lookup(row.ProductUrl).WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { result = new(MegaProductLookupStatus.Failed); }
                if (result.Status != MegaProductLookupStatus.Success || result.Product == null)
                {
                    string reason = result.Status switch
                    {
                        MegaProductLookupStatus.LoginRequired => "메가커피 로그인이 필요합니다.",
                        MegaProductLookupStatus.InvalidUrl => "메가커피 상품 URL이 올바르지 않습니다.",
                        _ => result.Reason ?? "상품 조회에 실패했습니다. 상품명·가격·이미지·품절 상태를 확인해주세요."
                    };
                    issues.Add(new(row.SheetRow, "ProductUrl", reason));
                    continue;
                }
                string pending = Store.NewPendingImagePath();
                try
                {
                    await Task.Run(() => ManualImages.Save(result.Product.ImageBytes, pending), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    resolved.Add(row.SheetRow, new(result.Product, pending, Store.NewWebImagePath()));
                }
                catch (OperationCanceledException)
                { if (File.Exists(pending)) File.Delete(pending); throw; }
                catch (Exception)
                {
                    if (File.Exists(pending)) File.Delete(pending);
                    issues.Add(new(row.SheetRow, "이미지", "상품 이미지를 저장할 수 없습니다."));
                }
            }
            if (issues.Count != 0) return new([], issues);
            var plan = BuildImportPlan(read, resolved, issues);
            transferred = true;
            return plan;
        }
        finally
        {
            // A returned plan owns its staged files; failed or cancelled preparation does not.
            if (!transferred)
                foreach (var item in resolved.Values)
                    if (File.Exists(item.PendingPath)) File.Delete(item.PendingPath);
        }
    }

    private List<ProductWorkbookIssue> ValidateImportRows(ProductWorkbookRead read, bool allowLookup)
    {
        var issues = read.Issues.ToList();
        var known = Products.ToDictionary(product => product.Id);
        var seenIds = new HashSet<int>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in read.Rows)
        {
            if (row.ProductId is int id)
            {
                if (!seenIds.Add(id)) issues.Add(new(row.SheetRow, "ProductId", "파일에서 중복된 상품 ID입니다."));
                else if (!known.ContainsKey(id)) issues.Add(new(row.SheetRow, "ProductId", "존재하지 않는 상품 ID입니다."));
            }
            if (row.ProductUrl.Length != 0)
            {
                string key = CanonicalUrl(row.ProductUrl);
                if (!seenUrls.Add(key)) issues.Add(new(row.SheetRow, "ProductUrl", "파일에서 중복된 상품 URL입니다."));
            }
            if (!row.LookupRequested) continue;
            if (!allowLookup) { issues.Add(new(row.SheetRow, "ProductUrl", "이 행은 로그인된 상품조회가 필요합니다.")); continue; }
            if (!MegaCoffeeProductLookup.TryProductUrl(row.ProductUrl, out _, out _))
            {
                string reason = MegaCoffeeProductLookup.IsMegaHost(row.ProductUrl)
                    ? "메가커피 상품 URL 형식이 올바르지 않습니다."
                    : "이 판매처의 URL 조회는 아직 지원하지 않습니다.";
                issues.Add(new(row.SheetRow, "ProductUrl", reason)); continue;
            }
            var matching = Products.Where(product => CanonicalUrl(product.Url) == CanonicalUrl(row.ProductUrl)).ToArray();
            if (row.ProductId == null && matching.Length != 0)
                issues.Add(new(row.SheetRow, "ProductUrl", "이미 등록된 URL입니다. 기존 ProductId를 지정해주세요."));
            else if (row.ProductId is int own && matching.Any(product => product.Id != own))
                issues.Add(new(row.SheetRow, "ProductUrl", "다른 ProductId에 이미 등록된 URL입니다."));
        }
        return issues;
    }

    private ProductImportPlan BuildImportPlan(ProductWorkbookRead read,
        IReadOnlyDictionary<int, ResolvedImport>? resolved, List<ProductWorkbookIssue>? validated)
    {
        var issues = validated ?? ValidateImportRows(read, false);
        if (issues.Count != 0) return new([], issues);
        var known = Products.ToDictionary(product => product.Id);
        var stored = Store.Database.ReadProducts(Suppliers).Select(product => product.Id).ToHashSet();
        var changes = new List<ProductImportChange>();
        foreach (var row in read.Rows)
        {
            Product? existing = row.ProductId is int id ? known[id] : null;
            Product proposed;
            string? pending = null;
            if (row.LookupRequested)
            {
                var resolvedRow = resolved![row.SheetRow];
                var item = resolvedRow.Product;
                var seller = Suppliers.Single(supplier => supplier.Id == "mega");
                string category = InferCategory(item.Name);
                proposed = existing == null
                    ? new Product(0, item.Name, item.Price, "", seller, category, item.Available, 0)
                        { DisplayPrice = item.DisplayPrice, Url = item.ProductUrl, ImageUrl = item.ImageUrl,
                            ImageCachePath = resolvedRow.FinalPath, DataOrigin = "UserMock" }
                    : existing with { Name = item.Name, Price = item.Price, PriceNote = "", DisplayPrice = item.DisplayPrice,
                        Supplier = seller, Category = category, Url = item.ProductUrl, IsActive = true,
                        Available = item.Available, ImageUrl = item.ImageUrl, ImageCachePath = resolvedRow.FinalPath };
                pending = resolvedRow.PendingPath;
            }
            else
            {
                var seller = Suppliers.Single(supplier => supplier.Name == row.Supplier ||
                    string.Equals(supplier.Id, row.Supplier, StringComparison.OrdinalIgnoreCase));
                string? display = string.IsNullOrWhiteSpace(row.DisplayPrice) ? null : row.DisplayPrice;
                proposed = existing == null
                    ? new Product(0, row.Name, row.Price, "", seller, row.Category, true, 0)
                        { DisplayPrice = display, Url = row.ProductUrl, IsActive = row.IsActive, DataOrigin = "UserMock" }
                    : existing with { Name = row.Name, Price = row.Price, DisplayPrice = display, Supplier = seller,
                        Category = row.Category, Url = row.ProductUrl, IsActive = row.IsActive };
                if (existing != null && existing.Name == proposed.Name && existing.Price == proposed.Price &&
                    existing.PriceText == proposed.PriceText && existing.Supplier.Id == proposed.Supplier.Id &&
                    existing.Category == proposed.Category && existing.Url == proposed.Url && existing.IsActive == proposed.IsActive)
                    continue;
            }
            changes.Add(new(existing, proposed, existing != null && stored.Contains(existing.Id), row.SheetRow, pending));
        }
        return new(changes, issues);
    }

    private static string CanonicalUrl(string text) => Uri.TryCreate(text, UriKind.Absolute, out var uri)
        ? uri.AbsoluteUri : text.Trim();

    internal static string InferCategory(string name)
    {
        var matches = new HashSet<string>();
        if (name.Contains("원두", StringComparison.Ordinal)) matches.Add("원두");
        if (name.Contains("파우더", StringComparison.Ordinal)) matches.Add("파우더");
        if (name.Contains("농축액", StringComparison.Ordinal) || name.Contains("베이스", StringComparison.Ordinal)) matches.Add("베이스/농축액");
        if (name.Contains("시럽", StringComparison.Ordinal) || name.Contains("소스", StringComparison.Ordinal)) matches.Add("시럽/소스");
        if (name.Contains("티백", StringComparison.Ordinal)) matches.Add("티백");
        if (name.Contains("스트로우", StringComparison.Ordinal) || name.Contains("빨대", StringComparison.Ordinal) ||
            name.Contains("아이스컵", StringComparison.Ordinal) || name.Contains("뚜껑", StringComparison.Ordinal)) matches.Add("일회용품");
        if (name.Contains("우유", StringComparison.Ordinal) || name.Contains("휘핑크림", StringComparison.Ordinal)) matches.Add("유제품");
        return matches.Count == 1 ? matches.Single() : "기타";
    }
}
