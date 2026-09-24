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

// UI preferences and read-only legacy catalog live here; SQLite owns current catalog/cart data.
public sealed class LocalState
{
    public string DirectoryPath { get; }
    internal CatalogDatabase Database { get; }
    internal OperationalLog Log { get; }
    public UiPreferences Preferences { get; }
    public LocalState(string? directory = null)
    {
        DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "Mockup");
        string dataDirectory = directory == null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "Data")
            : Path.Combine(directory, "Data");
        string logDirectory = directory == null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "Logs")
            : Path.Combine(directory, "Logs");
        Log = directory == null ? OperationalLog.Default : new OperationalLog(logDirectory);
        Database = new CatalogDatabase(Path.Combine(dataDirectory, "CafeOrder.db"), Log);
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
    internal List<StoredProduct> ReadLegacyProducts()
    {
        string path = Path.Combine(DirectoryPath, "catalog-state.json");
        if (!File.Exists(path)) return [];
        try { return JsonSerializer.Deserialize<List<StoredProduct>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("기존 상품 파일을 읽을 수 없습니다."); }
        catch (JsonException ex) { throw new InvalidDataException("기존 상품 파일을 읽을 수 없습니다.", ex); }
    }
    internal string LegacyManualImagePath(int id) => Path.Combine(DirectoryPath, "manual-images", id + ".webp");
    internal string NewManualImagePath(int id) => Path.Combine(DirectoryPath, "manual-images", $"{id}-{Guid.NewGuid():N}.webp");
    internal string NewWebImagePath() => Path.Combine(DirectoryPath, "web-images", $"{Guid.NewGuid():N}.webp");
    internal string NewPendingImagePath() => Path.Combine(DirectoryPath, "pending-images", $"{Guid.NewGuid():N}.webp");
}

// XLSX is an interchange format; SQLite remains the live store.
public record ProductTransferRow(int? ProductId, string Supplier, string Name, decimal Price,
    string DisplayPrice, string Category, string ProductUrl, bool IsActive, int SheetRow = 0,
    bool LookupRequested = false, bool PriceKnown = true);
public record ProductWorkbookIssue(int Row, string Column, string Reason)
{
    public override string ToString() => $"{Row}행 · {Column}: {Reason}";
}
public record ProductWorkbookRead(IReadOnlyList<ProductTransferRow> Rows, IReadOnlyList<ProductWorkbookIssue> Issues);
public interface IProductWorkbook
{
    void Export(string xlsxPath, IReadOnlyList<ProductTransferRow> products);
    ProductWorkbookRead Import(string xlsxPath);
}
