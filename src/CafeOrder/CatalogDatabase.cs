using System.Globalization;
using Microsoft.Data.Sqlite;

namespace CafeOrder;

// Short, synchronous local transactions. No browser or network operations belong here.
internal sealed class CatalogDatabase
{
    internal string Path { get; }
    internal CatalogDatabase(string path) => Path = System.IO.Path.GetFullPath(path);
    internal SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path, ForeignKeys = true, Pooling = false, DefaultTimeout = 5 }.ToString());
        try { connection.Open(); return connection; } catch { connection.Dispose(); throw; }
    }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        var command = db.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        foreach (var item in values) command.Parameters.AddWithValue(item.Name, item.Value ?? DBNull.Value);
        return command;
    }
    private static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] values)
    { using var command = Command(db, tx, sql, values); command.ExecuteNonQuery(); }
    private static long Number(SqliteConnection db, SqliteTransaction? tx, string sql)
    { using var command = Command(db, tx, sql); return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture); }
    private T Access<T>(Func<SqliteConnection, T> action)
    {
        try { using var db = Connect(); return action(db); }
        catch (SqliteException ex) { throw new IOException("SQLite 저장소를 읽거나 저장하지 못했습니다. 기존 데이터는 초기화하지 않았습니다.", ex); }
    }
    private T Write<T>(Func<SqliteConnection, SqliteTransaction, T> action) => Access(db =>
    { using var tx = db.BeginTransaction(); var result = action(db, tx); tx.Commit(); return result; });

    internal void Initialize(LocalState legacy, IReadOnlyList<Supplier> suppliers, IReadOnlyList<Product> samples)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string mutexName = "Local\\CafeOrder.DbInit." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.ToUpperInvariant())));
        using var gate = new Mutex(false, mutexName); bool entered = false;
        try
        {
            try { entered = gate.WaitOne(TimeSpan.FromSeconds(30)); } catch (AbandonedMutexException) { entered = true; }
            if (!entered) throw new IOException("다른 창에서 데이터 전환 중입니다. 잠시 후 다시 실행해주세요.");
            bool existed = File.Exists(Path);
            Access(db =>
            {
                long hasLedger = Number(db, null, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SchemaMigrations'");
                if (hasLedger == 0 && Number(db, null, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'") != 0)
                    throw new InvalidDataException("알 수 없는 기존 DB입니다. 덮어쓰지 않았습니다.");
                long version = hasLedger == 0 ? 0 : Number(db, null, "SELECT COALESCE(MAX(Version),0) FROM SchemaMigrations");
                if (version > 2) throw new InvalidDataException("더 최신 버전의 DB입니다. 현재 앱으로 변경하지 않습니다.");
                if (version == 0)
                {
                    if (existed) Backup(db, "before-schema-1");
                    using var tx = db.BeginTransaction();
                    Execute(db, tx, """
                        CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL);
                        CREATE TABLE Suppliers (SupplierId TEXT PRIMARY KEY, Name TEXT NOT NULL, IsManual INTEGER NOT NULL CHECK(IsManual IN (0,1)));
                        CREATE TABLE Products (
                            ProductId INTEGER PRIMARY KEY CHECK(ProductId>0), SupplierId TEXT NOT NULL REFERENCES Suppliers(SupplierId),
                            Name TEXT NOT NULL, Price TEXT NOT NULL, PriceNote TEXT NOT NULL, PriceText TEXT NOT NULL,
                            Category TEXT NOT NULL, ProductUrl TEXT NOT NULL, ImageUrl TEXT, ImageCachePath TEXT, ManualImagePath TEXT,
                            IsAvailable INTEGER NOT NULL CHECK(IsAvailable IN (0,1)), IsActive INTEGER NOT NULL CHECK(IsActive IN (0,1)),
                            DataOrigin TEXT NOT NULL CHECK(DataOrigin IN ('Sample','UserMock')));
                        CREATE TABLE CartItems (ProductId INTEGER PRIMARY KEY REFERENCES Products(ProductId),
                            Quantity INTEGER NOT NULL CHECK(Quantity>=0), AddedOrder INTEGER NOT NULL);
                        INSERT INTO SchemaMigrations VALUES(1,'catalog-cart-schema',strftime('%Y-%m-%dT%H:%M:%fZ','now'));
                        """);
                    tx.Commit(); version = 1;
                }
                if (version == 1)
                {
                    // Strict parsing: damaged/unknown legacy data stops migration, never silently imports an empty list.
                    var saved = legacy.ReadLegacyProducts();
                    var candidates = new Dictionary<int, Product>();
                    foreach (var item in saved)
                    {
                        var seller = suppliers.FirstOrDefault(s => s.Id == item.SupplierId);
                        if (seller == null || item.Id <= 0 || item.Name == null || item.PriceNote == null || item.Url == null || item.Category == null || !SampleData.Categories.Skip(1).Contains(item.Category))
                            throw new InvalidDataException("기존 상품 데이터에 확인이 필요한 항목이 있습니다. 전환을 중단했습니다.");
                        if (candidates.ContainsKey(item.Id)) throw new InvalidDataException("기존 상품 ID가 중복되어 전환을 중단했습니다.");
                        var original = samples.SingleOrDefault(p => p.Id == item.Id);
                        var product = new Product(item.Id, item.Name, item.Price, item.PriceNote, seller, item.Category, item.Available, original?.SampleOrderCount ?? 0)
                        { Url = item.Url, IsActive = item.IsActive, DataOrigin = original == null ? "UserMock" : "Sample" };
                        candidates.Add(item.Id, product);
                    }
                    // Image-only edits also belong to the user even when no catalog JSON was written.
                    foreach (var sample in samples)
                        if (!candidates.ContainsKey(sample.Id) && File.Exists(legacy.LegacyManualImagePath(sample.Id))) candidates.Add(sample.Id, sample with { });
                    foreach (var product in candidates.Values)
                    { string image = legacy.LegacyManualImagePath(product.Id); if (File.Exists(image)) product.ManualImagePath = image; }
                    var imported = candidates.Values.Where(p => p.ManualImagePath != null ||
                        samples.All(s => s.Id != p.Id || s.Name != p.Name || s.Price != p.Price || s.PriceNote != p.PriceNote || s.Supplier.Id != p.Supplier.Id ||
                            s.Category != p.Category || s.Url != p.Url || s.IsActive != p.IsActive || s.Available != p.Available)).ToArray();
                    Backup(db, "before-legacy-import");
                    using var tx = db.BeginTransaction();
                    foreach (var supplier in suppliers) Execute(db, tx, "INSERT INTO Suppliers VALUES($id,$name,$manual) ON CONFLICT(SupplierId) DO NOTHING",
                        ("$id", supplier.Id), ("$name", supplier.Name), ("$manual", supplier.Manual));
                    foreach (var product in imported) Save(db, tx, product);
                    Execute(db, tx, "INSERT INTO SchemaMigrations VALUES(2,'legacy-import-once',strftime('%Y-%m-%dT%H:%M:%fZ','now'))");
                    tx.Commit();
                }
                return 0;
            });
        }
        finally { if (entered) gate.ReleaseMutex(); }
    }
    private void Backup(SqliteConnection source, string reason)
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups"); Directory.CreateDirectory(directory);
        string destination = System.IO.Path.Combine(directory, $"CafeOrder-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{reason}-{Guid.NewGuid():N}.db");
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        backup.Open(); source.BackupDatabase(backup);
    }
    private static void Save(SqliteConnection db, SqliteTransaction tx, Product p) => Execute(db, tx, """
        INSERT INTO Products VALUES($id,$supplier,$name,$price,$note,$display,$category,$url,$image,$cache,$manual,$available,$active,$origin)
        ON CONFLICT(ProductId) DO UPDATE SET SupplierId=excluded.SupplierId, Name=excluded.Name, Price=excluded.Price,
        PriceNote=excluded.PriceNote, PriceText=excluded.PriceText, Category=excluded.Category, ProductUrl=excluded.ProductUrl,
        ImageUrl=excluded.ImageUrl, ImageCachePath=excluded.ImageCachePath, ManualImagePath=excluded.ManualImagePath,
        IsAvailable=excluded.IsAvailable, IsActive=excluded.IsActive, DataOrigin=excluded.DataOrigin
        """, ("$id", p.Id), ("$supplier", p.Supplier.Id), ("$name", p.Name), ("$price", p.Price.ToString(CultureInfo.InvariantCulture)),
        ("$note", p.PriceNote), ("$display", p.PriceText), ("$category", p.Category), ("$url", p.Url), ("$image", p.ImageUrl),
        ("$cache", p.ImageCachePath), ("$manual", p.ManualImagePath), ("$available", p.Available), ("$active", p.IsActive), ("$origin", p.DataOrigin));
    internal void SaveProduct(Product product) => Write((db, tx) => { Save(db, tx, product); return 0; });
    internal Product InsertProduct(Product template) => Write((db, tx) =>
    {
        int id = checked((int)Number(db, tx, "SELECT MAX(24,COALESCE(MAX(ProductId),0))+1 FROM Products"));
        var product = template with { Id = id, DataOrigin = "UserMock" }; Save(db, tx, product); return product;
    });
    internal IReadOnlyList<Product> ApplyImport(IReadOnlyList<ProductImportChange> changes)
    {
        if (changes.Count == 0) return [];
        return Access(db =>
        {
            Backup(db, "before-xlsx-import");
            using var tx = db.BeginTransaction();
            long next = Number(db, tx, "SELECT MAX(24,COALESCE(MAX(ProductId),0))+1 FROM Products");
            var written = new List<Product>(changes.Count);
            foreach (var change in changes)
            {
                Product product = change.Proposed;
                if (change.Existing is { } existing)
                {
                    using var query = Command(db, tx, "SELECT COUNT(*) FROM Products WHERE ProductId=$id", ("$id", existing.Id));
                    bool present = Convert.ToInt64(query.ExecuteScalar(), CultureInfo.InvariantCulture) != 0;
                    if (present != change.WasStored)
                        throw new InvalidDataException($"{change.SheetRow}행 · ProductId: 확인 이후 DB가 변경됐습니다. 파일을 다시 가져오세요.");
                }
                else product = product with { Id = checked((int)next++) };
                try { Save(db, tx, product); }
                catch (SqliteException ex) { throw new InvalidDataException($"{change.SheetRow}행 · ProductId: DB 저장 실패 · {ex.Message}", ex); }
                written.Add(product);
            }
            tx.Commit(); return written;
        });
    }
    internal List<Product> ReadProducts(IReadOnlyList<Supplier> suppliers) => Access(db =>
    {
        var result = new List<Product>(); using var command = Command(db, null, "SELECT * FROM Products ORDER BY ProductId"); using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var supplier = suppliers.SingleOrDefault(s => s.Id == reader.GetString(1)) ?? throw new InvalidDataException("DB 판매처를 확인할 수 없습니다.");
            result.Add(new Product(reader.GetInt32(0), reader.GetString(2), decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture), reader.GetString(4), supplier, reader.GetString(6), reader.GetBoolean(11), 0)
            { DisplayPrice = reader.GetString(5), Url = reader.GetString(7), ImageUrl = reader.IsDBNull(8) ? null : reader.GetString(8), ImageCachePath = reader.IsDBNull(9) ? null : reader.GetString(9),
                ManualImagePath = reader.IsDBNull(10) ? null : reader.GetString(10), IsActive = reader.GetBoolean(12), DataOrigin = reader.GetString(13) });
        }
        return result;
    });
    internal List<CartLine> ReadCart(IReadOnlyList<Product> products) => Access(db =>
    {
        var result = new List<CartLine>(); using var command = Command(db, null, "SELECT ProductId,Quantity FROM CartItems ORDER BY AddedOrder DESC,ProductId"); using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new CartLine(products.Single(p => p.Id == reader.GetInt32(0)), reader.GetInt32(1)));
        return result;
    });
    internal int AddToCart(Product product) => Write((db, tx) =>
    {
        Save(db, tx, product);
        Execute(db, tx, """
            INSERT INTO CartItems VALUES($id,$quantity,(SELECT COALESCE(MAX(AddedOrder),0)+1 FROM CartItems))
            ON CONFLICT(ProductId) DO UPDATE SET Quantity=CASE WHEN $manual=1 THEN 0 ELSE CartItems.Quantity+1 END,AddedOrder=excluded.AddedOrder
            """, ("$id", product.Id), ("$quantity", product.Supplier.Manual ? 0 : 1), ("$manual", product.Supplier.Manual));
        using var query = Command(db, tx, "SELECT Quantity FROM CartItems WHERE ProductId=$id", ("$id", product.Id)); return Convert.ToInt32(query.ExecuteScalar());
    });
    internal void SetQuantity(int id, int quantity) => Write((db, tx) =>
    { Execute(db, tx, quantity == 0 ? "DELETE FROM CartItems WHERE ProductId=$id" : "UPDATE CartItems SET Quantity=$quantity WHERE ProductId=$id", ("$id", id), ("$quantity", quantity)); return 0; });
    internal void RemoveCart(IEnumerable<int> ids) => Write((db, tx) =>
    { foreach (int id in ids) Execute(db, tx, "DELETE FROM CartItems WHERE ProductId=$id", ("$id", id)); return 0; });
}
