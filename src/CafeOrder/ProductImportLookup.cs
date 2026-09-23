namespace CafeOrder;

internal static class ProductUrlIdentity
{
    internal static string? MegaGoodsNo(string supplierId, string url) =>
        supplierId == "mega" && MegaCoffeeProductLookup.TryProductUrl(url, out _, out var goodsNo) ? goodsNo : null;

    internal static string? Key(string supplierId, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (supplierId == "mega" && MegaCoffeeProductLookup.TryProductUrl(url, out _, out var goodsNo))
            return "mega|" + (goodsNo.TrimStart('0') is { Length: > 0 } normalized ? normalized : "0");
        if (supplierId == "piece" && PieceCakeProductLookup.TryProductUrl(url, out _, out var productNumber))
            return "piece|" + productNumber;
        if (supplierId == "nuldam" && NuldamProductLookup.TryProductUrl(url, out _, out var nuldamNumber))
            return "nuldam|" + nuldamNumber;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? supplierId + "|" + uri.AbsoluteUri : supplierId + "|" + url.Trim();
    }
}

public sealed partial class SampleData
{
    private sealed record ResolvedImport(MegaCoffeeProductSnapshot Product, string SupplierId,
        string PendingPath, string FinalPath, DateTimeOffset CheckedAtUtc);
    private sealed record ImportSelection(ProductWorkbookRead Read, List<ProductWorkbookIssue> Issues,
        List<ProductImportSkip> Skipped);
    private sealed record ImportCandidate(ProductTransferRow Row, string? Key, string SupplierId, int? OwnerId);

    internal async Task<ProductImportPlan> PrepareImportAsync(string path,
        Func<string, Task<MegaProductLookupResult>> lookup, IProgress<string>? progress,
        CancellationToken cancellationToken, Func<string, Task<MegaProductLookupResult>>? pieceLookup = null,
        Func<string, Task<MegaProductLookupResult>>? nuldamLookup = null)
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
                bool isMega = MegaCoffeeProductLookup.TryProductUrl(row.ProductUrl, out var megaUrl, out _);
                bool isPiece = PieceCakeProductLookup.TryProductUrl(row.ProductUrl, out var pieceUrl, out _);
                bool isNuldam = NuldamProductLookup.TryProductUrl(row.ProductUrl, out var nuldamUrl, out _);
                string supplierId = isMega ? "mega" : isPiece ? "piece" : isNuldam ? "nuldam" : "";
                var selectedLookup = isMega ? lookup : isPiece ? pieceLookup : nuldamLookup;
                var cleanUrl = isMega ? megaUrl : isPiece ? pieceUrl : isNuldam ? nuldamUrl : null;
                if (cleanUrl == null)
                {
                    issues.Add(new(row.SheetRow, "ProductUrl", "이 판매처의 URL 조회는 아직 지원하지 않습니다."));
                    continue;
                }
                try
                {
                    result = selectedLookup == null
                        ? new(MegaProductLookupStatus.Failed, Reason: "해당 판매처 상품 조회를 사용할 수 없습니다.")
                        : await selectedLookup(cleanUrl.ToString()).WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Store.Log.Write(LogLevel.ERROR, "PRODUCT_LOOKUP_EXCEPTION", "XLSX 상품조회 호출에 실패했습니다.",
                        supplier: supplierId, row: row.SheetRow, result: "FAILED", error: ex);
                    result = new(MegaProductLookupStatus.Failed);
                }
                if (result.Status != MegaProductLookupStatus.Success || result.Product == null)
                {
                    string reason = result.Status switch
                    {
                        MegaProductLookupStatus.LoginRequired => (isMega ? "메가커피" : isPiece ? "파미유" : "널담") + " 로그인이 필요합니다.",
                        MegaProductLookupStatus.InvalidUrl => (isMega ? "메가커피" : isPiece ? "파미유" : "널담") + " 상품 URL이 올바르지 않습니다.",
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
                    resolved.Add(row.SheetRow, new(result.Product, supplierId, pending,
                        Store.NewWebImagePath(), DateTimeOffset.UtcNow));
                }
                catch (OperationCanceledException)
                { if (File.Exists(pending)) File.Delete(pending); throw; }
                catch (Exception ex)
                {
                    if (File.Exists(pending)) File.Delete(pending);
                    Store.Log.Write(LogLevel.ERROR, "IMAGE_SAVE_FAILED", "XLSX 조회 이미지를 WebP로 저장하지 못했습니다.",
                        supplier: supplierId, row: row.SheetRow, result: "FAILED", error: ex);
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
            var used = plan.Changes.Select(change => change.PendingImagePath).OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in resolved.Values.Where(item => !used.Contains(item.PendingPath)))
                if (File.Exists(item.PendingPath)) File.Delete(item.PendingPath);
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
            Store.Log.Write(LogLevel.INFO, "XLSX_ROW_SKIPPED",
                skip.Reason == "UNCHANGED_PRODUCT" ? "변경되지 않은 상품 행을 건너뛰었습니다." :
                    "중복 상품 URL 행을 조회와 DB 반영에서 건너뛰었습니다.",
                supplier: skip.SupplierId, productId: skip.ExistingProductId, goodsNo: skip.GoodsNo,
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
        var candidates = new List<ImportCandidate>();
        var skipped = new List<ProductImportSkip>();
        foreach (var row in read.Rows)
        {
            if (row.ProductId is int id)
            {
                if (!known.ContainsKey(id)) { issues.Add(new(row.SheetRow, "ProductId", "존재하지 않는 상품 ID입니다.")); continue; }
            }
            string declaredSupplierId = row.LookupRequested ? DetectSupplier(row.ProductUrl)?.Id ?? "" :
                Suppliers.First(supplier => supplier.Name == row.Supplier ||
                    string.Equals(supplier.Id, row.Supplier, StringComparison.OrdinalIgnoreCase)).Id;
            bool megaUrl = MegaCoffeeProductLookup.TryProductUrl(row.ProductUrl, out _, out _);
            bool pieceUrl = PieceCakeProductLookup.TryProductUrl(row.ProductUrl, out _, out _);
            bool nuldamUrl = NuldamProductLookup.TryProductUrl(row.ProductUrl, out _, out _);
            string supplierId = megaUrl ? "mega" : pieceUrl ? "piece" : nuldamUrl ? "nuldam" : declaredSupplierId;
            string? key = ProductUrlIdentity.Key(supplierId, row.ProductUrl);
            Product[] owners = key == null ? [] : stored.Where(product => string.Equals(
                ProductUrlIdentity.Key(product.Supplier.Id, product.Url), key, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (owners.Length > 1)
            {
                issues.Add(new(row.SheetRow, "ProductUrl", "DB에 동일 상품 URL의 ProductId가 여러 개 있습니다.")); continue;
            }
            int? ownerId = owners.FirstOrDefault()?.Id;
            string? goodsNo = ProductUrlIdentity.MegaGoodsNo(supplierId, row.ProductUrl);
            if (ownerId != null && ownerId != row.ProductId)
            {
                skipped.Add(new(row.SheetRow, "DB_DUPLICATE_PRODUCT_URL", ownerId, supplierId, goodsNo));
                continue;
            }
            if (!row.LookupRequested && (megaUrl && declaredSupplierId != "mega" ||
                pieceUrl && declaredSupplierId != "piece" || nuldamUrl && declaredSupplierId != "nuldam") &&
                (row.ProductId == null || ownerId == row.ProductId))
            {
                issues.Add(new(row.SheetRow, "Supplier", "상품 URL과 판매처가 일치하지 않습니다."));
                continue;
            }
            ProductTransferRow selected = row;
            if (row.ProductId is int existingId && key != null && !string.Equals(
                ProductUrlIdentity.Key(known[existingId].Supplier.Id, known[existingId].Url), key,
                StringComparison.OrdinalIgnoreCase))
            {
                // A different goodsNo is a new product, never a replacement for the old ProductId.
                if (!megaUrl && !pieceUrl && !nuldamUrl)
                {
                    issues.Add(new(row.SheetRow, "ProductUrl", "새 상품 URL은 메가커피·파미유·널담 실제 조회만 지원합니다."));
                    continue;
                }
                selected = row with { ProductId = null, LookupRequested = true };
                supplierId = megaUrl ? "mega" : pieceUrl ? "piece" : "nuldam";
            }
            if (selected.LookupRequested && !allowLookup)
            { issues.Add(new(row.SheetRow, "ProductUrl", "이 행은 로그인된 상품조회가 필요합니다.")); continue; }
            if (selected.LookupRequested && !megaUrl && !pieceUrl && !nuldamUrl)
            {
                string reason = MegaCoffeeProductLookup.IsMegaHost(row.ProductUrl)
                    ? "메가커피 상품 URL 형식이 올바르지 않습니다."
                    : PieceCakeProductLookup.IsPieceHost(row.ProductUrl)
                    ? "파미유 상품 URL 형식이 올바르지 않습니다."
                    : NuldamProductLookup.IsNuldamHost(row.ProductUrl)
                    ? "널담 상품 URL 형식이 올바르지 않습니다."
                    : "이 판매처의 URL 조회는 아직 지원하지 않습니다.";
                issues.Add(new(row.SheetRow, "ProductUrl", reason)); continue;
            }
            candidates.Add(new(selected, key, supplierId, ownerId));
        }
        var preferred = candidates.Where(candidate => candidate.Key != null)
            .GroupBy(candidate => candidate.Key!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.FirstOrDefault(candidate => candidate.OwnerId == candidate.Row.ProductId &&
                    candidate.OwnerId != null) ?? group.First(), StringComparer.OrdinalIgnoreCase);
        var selectedRows = new List<ProductTransferRow>();
        var seenIds = new HashSet<int>();
        foreach (var candidate in candidates)
        {
            if (candidate.Key != null && !ReferenceEquals(preferred[candidate.Key], candidate))
            {
                var winner = preferred[candidate.Key];
                skipped.Add(new(candidate.Row.SheetRow, "FILE_DUPLICATE_PRODUCT_URL",
                    winner.OwnerId ?? winner.Row.ProductId, candidate.SupplierId,
                    ProductUrlIdentity.MegaGoodsNo(candidate.SupplierId, candidate.Row.ProductUrl)));
                continue;
            }
            if (candidate.Row.ProductId is int id && !seenIds.Add(id))
            { issues.Add(new(candidate.Row.SheetRow, "ProductId", "파일에서 중복된 상품 ID입니다.")); continue; }
            selectedRows.Add(candidate.Row);
        }
        return new(new(selectedRows, read.Issues), issues, skipped);
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
                var seller = Suppliers.Single(supplier => supplier.Id == resolvedRow.SupplierId);
                string category = InferCategory(item.Name);
                proposed = existing == null
                    ? new Product(0, item.Name, item.Price, "", seller, category, item.Available, 0)
                        { DisplayPrice = item.DisplayPrice, Url = item.ProductUrl, ImageUrl = item.ImageUrl,
                            ImageCachePath = resolvedRow.FinalPath, DataOrigin = "UserMock",
                            LastSuccessfulCheckAtUtc = resolvedRow.CheckedAtUtc }
                    : existing with { Name = item.Name, Price = item.Price, PriceNote = "", DisplayPrice = item.DisplayPrice,
                        Supplier = seller, Category = category, Url = item.ProductUrl, IsActive = true,
                        Available = item.Available, ImageUrl = item.ImageUrl, ImageCachePath = resolvedRow.FinalPath,
                        LastSuccessfulCheckAtUtc = resolvedRow.CheckedAtUtc };
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
            }
            if (existing != null && existing.Name == proposed.Name && existing.Price == proposed.Price &&
                existing.PriceText == proposed.PriceText && existing.Supplier.Id == proposed.Supplier.Id &&
                existing.Category == proposed.Category && existing.IsActive == proposed.IsActive &&
                existing.Available == proposed.Available && string.Equals(
                    ProductUrlIdentity.Key(existing.Supplier.Id, existing.Url),
                    ProductUrlIdentity.Key(proposed.Supplier.Id, proposed.Url), StringComparison.OrdinalIgnoreCase) &&
                (!row.LookupRequested || existing.ImageUrl == proposed.ImageUrl &&
                    existing.LastSuccessfulCheckAtUtc == proposed.LastSuccessfulCheckAtUtc &&
                    existing.ImageCachePath is { } imagePath && File.Exists(imagePath)))
            {
                selection.Skipped.Add(new(row.SheetRow, "UNCHANGED_PRODUCT", existing.Id,
                    existing.Supplier.Id, ProductUrlIdentity.MegaGoodsNo(existing.Supplier.Id, existing.Url)));
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
        if (new[] { "도넛", "치아바타", "크루아상", "소금빵", "쿠키", "케이크", "낭시에" }
            .Any(word => name.Contains(word, StringComparison.Ordinal))) matches.Add("디저트/스낵");
        return matches.Count == 1 ? matches.Single() : "기타";
    }
}
