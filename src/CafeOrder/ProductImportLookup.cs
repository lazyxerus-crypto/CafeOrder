namespace CafeOrder;

internal static class ProductUrlIdentity
{
    internal static string? Key(string supplierId, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (supplierId == "mega" && MegaCoffeeProductLookup.TryProductUrl(url, out _, out var goodsNo))
            return "mega|" + (goodsNo.TrimStart('0') is { Length: > 0 } normalized ? normalized : "0");
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? supplierId + "|" + uri.AbsoluteUri : supplierId + "|" + url.Trim();
    }
}

public sealed partial class SampleData
{
    private sealed record ResolvedImport(MegaCoffeeProductSnapshot Product, string PendingPath, string FinalPath);
    private sealed record ImportSelection(ProductWorkbookRead Read, List<ProductWorkbookIssue> Issues,
        List<ProductImportSkip> Skipped);

    internal async Task<ProductImportPlan> PrepareImportAsync(string path,
        Func<string, Task<MegaProductLookupResult>> lookup, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var selection = SelectImportRows(await Task.Run(() => Workbook().Import(path), cancellationToken), true);
        var read = selection.Read;
        var issues = selection.Issues;
        if (issues.Count != 0)
        {
            var rejected = new ProductImportPlan([], issues, selection.Skipped);
            LogImportPreview(rejected); return rejected;
        }
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
                MegaCoffeeProductLookup.TryProductUrl(row.ProductUrl, out var cleanUrl, out _);
                try { result = await lookup(cleanUrl!.ToString()).WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Store.Log.Write(LogLevel.ERROR, "PRODUCT_LOOKUP_EXCEPTION", "XLSX 상품조회 호출에 실패했습니다.",
                        supplier: "mega", row: row.SheetRow, result: "FAILED", error: ex);
                    result = new(MegaProductLookupStatus.Failed);
                }
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
                catch (Exception ex)
                {
                    if (File.Exists(pending)) File.Delete(pending);
                    Store.Log.Write(LogLevel.ERROR, "IMAGE_SAVE_FAILED", "XLSX 조회 이미지를 WebP로 저장하지 못했습니다.",
                        supplier: "mega", row: row.SheetRow, result: "FAILED", error: ex);
                    issues.Add(new(row.SheetRow, "이미지", "상품 이미지를 저장할 수 없습니다."));
                }
            }
            if (issues.Count != 0)
            {
                var rejected = new ProductImportPlan([], issues, selection.Skipped);
                LogImportPreview(rejected); return rejected;
            }
            var plan = BuildImportPlan(selection, resolved);
            LogImportPreview(plan);
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

    private void LogImportPreview(ProductImportPlan plan)
    {
        foreach (var skip in plan.SkippedRows)
            Store.Log.Write(LogLevel.INFO, "XLSX_ROW_SKIPPED", "중복 상품 URL 행을 조회와 DB 반영에서 건너뛰었습니다.",
                row: skip.SheetRow, result: "SKIPPED", reason: skip.Reason);
        foreach (var issue in plan.Issues)
            Store.Log.Write(LogLevel.WARN, "XLSX_ROW_FAILED", $"{issue.Column} 열: {issue.Reason}",
                row: issue.Row, result: "FAILED", reason: issue.Column.ToUpperInvariant());
        Store.Log.Write(plan.Issues.Count == 0 ? LogLevel.INFO : LogLevel.WARN, "XLSX_IMPORT_PREPARED",
            $"XLSX 검증 결과: 추가 {plan.Added}건, 수정 {plan.Updated}건, 건너뜀 {plan.Skipped}건, 실패 {plan.Failed}건.",
            result: plan.Issues.Count == 0 ? "READY" : "FAILED");
    }

    private ImportSelection SelectImportRows(ProductWorkbookRead read, bool allowLookup)
    {
        var issues = read.Issues.ToList();
        var known = Products.ToDictionary(product => product.Id);
        var stored = Store.Database.ReadProducts(Suppliers);
        var seenIds = new HashSet<int>();
        var firstByUrl = new Dictionary<string, ProductTransferRow>(StringComparer.OrdinalIgnoreCase);
        var selected = new List<ProductTransferRow>();
        var skipped = new List<ProductImportSkip>();
        foreach (var row in read.Rows)
        {
            if (row.ProductId is int id)
            {
                if (!seenIds.Add(id)) { issues.Add(new(row.SheetRow, "ProductId", "파일에서 중복된 상품 ID입니다.")); continue; }
                if (!known.ContainsKey(id)) { issues.Add(new(row.SheetRow, "ProductId", "존재하지 않는 상품 ID입니다.")); continue; }
            }
            if (row.LookupRequested && !allowLookup)
            { issues.Add(new(row.SheetRow, "ProductUrl", "이 행은 로그인된 상품조회가 필요합니다.")); continue; }
            if (row.LookupRequested && !MegaCoffeeProductLookup.TryProductUrl(row.ProductUrl, out _, out _))
            {
                string reason = MegaCoffeeProductLookup.IsMegaHost(row.ProductUrl)
                    ? "메가커피 상품 URL 형식이 올바르지 않습니다."
                    : "이 판매처의 URL 조회는 아직 지원하지 않습니다.";
                issues.Add(new(row.SheetRow, "ProductUrl", reason)); continue;
            }
            string supplierId = row.LookupRequested ? "mega" : Suppliers.First(supplier =>
                supplier.Name == row.Supplier || string.Equals(supplier.Id, row.Supplier, StringComparison.OrdinalIgnoreCase)).Id;
            string? key = ProductUrlIdentity.Key(supplierId, row.ProductUrl);
            if (key == null) { selected.Add(row); continue; }
            bool storedByOther = stored.Any(product =>
                ProductUrlIdentity.Key(product.Supplier.Id, product.Url)?.Equals(key, StringComparison.OrdinalIgnoreCase) == true &&
                product.Id != row.ProductId);
            if (row.ProductId == null && storedByOther)
            { skipped.Add(new(row.SheetRow, "DB_DUPLICATE_PRODUCT_URL")); continue; }
            if (row.ProductId != null && storedByOther)
            { issues.Add(new(row.SheetRow, "ProductUrl", "다른 ProductId에 이미 등록된 상품 URL입니다.")); continue; }
            if (!firstByUrl.TryGetValue(key, out var first))
            { firstByUrl.Add(key, row); selected.Add(row); continue; }
            if (row.ProductId == null)
            { skipped.Add(new(row.SheetRow, "FILE_DUPLICATE_PRODUCT_URL")); continue; }
            if (first.ProductId == null)
            {
                selected.Remove(first);
                skipped.Add(new(first.SheetRow, "FILE_DUPLICATE_PRODUCT_URL"));
                firstByUrl[key] = row; selected.Add(row); continue;
            }
            issues.Add(new(row.SheetRow, "ProductUrl", "서로 다른 ProductId가 같은 상품 URL을 수정하려 합니다."));
        }
        return new(new(selected, read.Issues), issues, skipped);
    }

    private ProductImportPlan BuildImportPlan(ImportSelection selection,
        IReadOnlyDictionary<int, ResolvedImport>? resolved)
    {
        var issues = selection.Issues;
        if (issues.Count != 0) return new([], issues, selection.Skipped);
        var read = selection.Read;
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
        return new(changes, issues, selection.Skipped);
    }

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
