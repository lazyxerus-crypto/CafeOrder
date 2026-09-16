using System.Text.Json;

namespace CafeOrder;

public sealed class UiPreferences
{
    public int Columns { get; set; } = 3;
    public Dictionary<string, float> FontSizes { get; set; } = [];
    public WindowPlacement? Window { get; set; }
}
public record WindowPlacement(int X, int Y, int Width, int Height, bool Maximized);
public record StoredProduct(int Id, string SupplierId, string Name, decimal Price, string PriceNote, string Category, string Url, bool IsActive, bool Available);

// Temporary mock storage. No credentials, cart, order history or browser state belong here.
public sealed class LocalState
{
    public string DirectoryPath { get; }
    public UiPreferences Preferences { get; }
    public LocalState(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "Mockup");
        Preferences = Read<UiPreferences>("ui-state.json") ?? new();
        Preferences.Columns = Math.Clamp(Preferences.Columns, 3, 5);
        Preferences.FontSizes ??= [];
    }
    private T? Read<T>(string name)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(DirectoryPath, name))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return default; }
    }
    private void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(DirectoryPath);
        string path = Path.Combine(DirectoryPath, name), pending = path + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(pending, path, true);
    }
    public void SavePreferences() => Write("ui-state.json", Preferences);
    public List<StoredProduct> LoadProducts() => Read<List<StoredProduct>>("catalog-state.json") ?? [];
    public void SaveProducts(IEnumerable<Product> products) => Write("catalog-state.json", products.Select(p =>
        new StoredProduct(p.Id, p.Supplier.Id, p.Name, p.Price, p.PriceNote, p.Category, p.Url, p.IsActive, p.Available)).ToArray());
    public string ManualImagePath(int id) => Path.Combine(DirectoryPath, "manual-images", id + ".webp");
}

// The future SQLite repository / XLSX adapter boundary; XLSX is never a live store.
public record ProductTransferRow(int ProductId, string Supplier, string ProductName, decimal Price, string PriceNote, string Category, string ProductUrl, bool IsActive);
public interface IProductWorkbook
{
    void Export(string xlsxPath, IReadOnlyList<ProductTransferRow> products);
    IReadOnlyList<ProductTransferRow> Import(string xlsxPath);
}
