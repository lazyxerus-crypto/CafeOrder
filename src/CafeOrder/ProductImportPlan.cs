namespace CafeOrder;

internal sealed record ProductImportChange(Product? Existing, Product Proposed, bool WasStored, int SheetRow,
    string? PendingImagePath = null);
internal sealed record ProductImportSkip(int SheetRow, string Reason, int? ExistingProductId = null,
    string? SupplierId = null, string? GoodsNo = null);

internal sealed class ProductImportPlan(IReadOnlyList<ProductImportChange> changes,
    IReadOnlyList<ProductWorkbookIssue> issues, IReadOnlyList<ProductImportSkip>? skipped = null) : IDisposable
{
    internal IReadOnlyList<ProductImportChange> Changes { get; } = changes;
    internal IReadOnlyList<ProductWorkbookIssue> Issues { get; } = issues;
    internal bool Applied { get; set; }
    internal int Added => Changes.Count(change => change.Existing == null);
    internal int Updated => Changes.Count(change => change.Existing != null);
    internal int Deactivated => Changes.Count(change => change.Existing is { IsActive: true } && !change.Proposed.IsActive);
    internal int Failed => Issues.Select(issue => issue.Row).Distinct().Count();
    internal IReadOnlyList<ProductImportSkip> SkippedRows { get; } = skipped ?? [];
    internal int Skipped => SkippedRows.Count;
    internal string DuplicateDetails
    {
        get
        {
            var duplicates = SkippedRows.Where(skip => skip.Reason == "DB_DUPLICATE_PRODUCT_URL" &&
                skip.ExistingProductId != null).ToArray();
            if (duplicates.Length == 0) return "";
            string lines = string.Join(Environment.NewLine, duplicates.Take(10).Select(skip =>
                $"{skip.SheetRow}행 → 기존 ProductId {skip.ExistingProductId}"));
            return $"\n중복 상품:\n{lines}" + (duplicates.Length > 10 ? $"\n외 {duplicates.Length - 10}건" : "");
        }
    }
    public void Dispose()
    {
        foreach (var path in Changes.Select(change => change.PendingImagePath).OfType<string>())
            if (File.Exists(path)) File.Delete(path);
    }
}
