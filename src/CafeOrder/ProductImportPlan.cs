namespace CafeOrder;

internal sealed record ProductImportChange(Product? Existing, Product Proposed, bool WasStored, int SheetRow);

internal sealed class ProductImportPlan(IReadOnlyList<ProductImportChange> changes, IReadOnlyList<ProductWorkbookIssue> issues)
{
    internal IReadOnlyList<ProductImportChange> Changes { get; } = changes;
    internal IReadOnlyList<ProductWorkbookIssue> Issues { get; } = issues;
    internal bool Applied { get; set; }
    internal int Added => Changes.Count(change => change.Existing == null);
    internal int Updated => Changes.Count(change => change.Existing != null);
    internal int Deactivated => Changes.Count(change => change.Existing is { IsActive: true } && !change.Proposed.IsActive);
}
