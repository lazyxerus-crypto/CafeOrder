namespace CafeOrder;

internal sealed record ProductImportChange(Product? Existing, Product Proposed, bool WasStored, int SheetRow,
    string? PendingImagePath = null);

internal sealed class ProductImportPlan(IReadOnlyList<ProductImportChange> changes, IReadOnlyList<ProductWorkbookIssue> issues) : IDisposable
{
    internal IReadOnlyList<ProductImportChange> Changes { get; } = changes;
    internal IReadOnlyList<ProductWorkbookIssue> Issues { get; } = issues;
    internal bool Applied { get; set; }
    internal int Added => Changes.Count(change => change.Existing == null);
    internal int Updated => Changes.Count(change => change.Existing != null);
    internal int Deactivated => Changes.Count(change => change.Existing is { IsActive: true } && !change.Proposed.IsActive);
    internal int Failed => Issues.Select(issue => issue.Row).Distinct().Count();
    public void Dispose()
    {
        foreach (var path in Changes.Select(change => change.PendingImagePath).OfType<string>())
            if (File.Exists(path)) File.Delete(path);
    }
}
