using CafeOrder;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    private static string CheckDirectory(string name) => Path.Combine(Path.GetTempPath(), "CafeOrderDatabaseChecks", name + "-" + Guid.NewGuid().ToString("N"));
    private static long SqlNumber(SqliteConnection db, string query)
    { using var cmd = db.CreateCommand(); cmd.CommandText = query; return (long)cmd.ExecuteScalar()!; }
    private static void CheckDatabase()
    {
        string directory = CheckDirectory("persistence");
        var data = new SampleData(new LocalState(directory));
        Require(data.Store.Database.Path == Path.Combine(directory, "Data", "CafeOrder.db"), "Isolated DB location");
        using (var db = data.Store.Database.Connect())
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader(); var names = new List<string>(); while (reader.Read()) names.Add(reader.GetString(0));
            Require(names.SequenceEqual(["CartItems", "Products", "SchemaMigrations", "SiteCartAttemptItems", "SiteCartAttempts", "Suppliers"]),
                "Catalog, cart and site-cart snapshot tables");
            reader.Close();
            Require(SqlNumber(db, "SELECT COUNT(*) FROM Products") == 0 && SqlNumber(db, "SELECT COUNT(*) FROM CartItems") == 0 &&
                SqlNumber(db, "SELECT COUNT(*) FROM Suppliers") == 7 && SqlNumber(db, "SELECT MAX(Version) FROM SchemaMigrations") == 3,
                "Fresh DB keeps untouched examples virtual and does not seed sample cart");
            using var integrity = db.CreateCommand(); integrity.CommandText = "PRAGMA integrity_check";
            Require((string?)integrity.ExecuteScalar() == "ok", "SQLite integrity check");
        }
        Product first = data.Products.Single(p => p.Id == 7), other = data.Products.Single(p => p.Id == 10);
        data.AddToCart(first); data.AddToCart(other); data.AddToCart(first);
        Require(data.Cart.Select(l => l.Product.Id).SequenceEqual([7, 10]) && data.Cart[0].Quantity == 2 && data.Cart[1].Quantity == 0, "Cart add/order/manual quantity");
        data.ChangeQuantity(data.Cart[0], 3);
        data.SetCategory(first, "과일"); data.SetActive(data.Products.Single(p => p.Id == 21), false);
        string image = Path.Combine(directory, "manual-images", "7-test.webp"); Directory.CreateDirectory(Path.GetDirectoryName(image)!); File.WriteAllBytes(image, [1, 2, 3]);
        data.SetManualImage(first, image);
        var registered = data.RegisterMock("https://megacoffee.example.invalid/product/1000002613", "티백");
        Require(registered.Id == 25 && registered.DataOrigin == "UserMock", "Registered ID and origin");
        data.AddToCart(registered);
        var again = new SampleData(new LocalState(directory));
        Require(again.Products.Single(p => p.Id == 7).Category == "과일" && again.Products.Single(p => p.Id == 7).ManualImagePath == image &&
            !again.Products.Single(p => p.Id == 21).IsActive && again.Products.Single(p => p.Id == 25).Url == registered.Url &&
            again.Cart.Select(l => (l.Product.Id, l.Quantity)).SequenceEqual(new[] { (25, 1), (7, 5), (10, 0) }), "Product/cart changes survive restart");
        first.Url = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        data.Store.Database.SaveProduct(first);
        var target = new SiteCartTarget(7, "79359", first.Url,
            first.Name, 5, first.Price);
        var attempt = data.Store.Database.CreateSiteCartAttempt("mega", [target]);
        data.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, "READY", "VERIFIED");
        using (var db = data.Store.Database.Connect())
        {
            using var query = db.CreateCommand();
            query.CommandText = "SELECT State,Quantity,ExternalProductId FROM SiteCartAttempts JOIN SiteCartAttemptItems USING(AttemptId) WHERE AttemptId=$id";
            query.Parameters.AddWithValue("$id", attempt.AttemptId);
            using var row = query.ExecuteReader();
            Require(row.Read() && row.GetString(0) == "READY" && row.GetInt32(1) == 5 && row.GetString(2) == "79359",
                "Site cart target and progress persist before remote mutation");
        }
        Require(MegaCoffeeSiteCart.Matches([new("79359", 5, "", first.Name)], [target]) &&
            !MegaCoffeeSiteCart.Matches([new("79359", 4, "", first.Name)], [target]) &&
            !MegaCoffeeSiteCart.Matches([new("79359", 5, "다른 옵션", first.Name)], [target]),
            "Site cart verification requires exact goodsNo, option and quantity");
        again.ChangeQuantity(again.Cart.Single(l => l.Product.Id == 7), -2);
        Require(new SampleData(new LocalState(directory)).Cart.Single(l => l.Product.Id == 7).Quantity == 3, "AUTO +/- persists without a site request");
        again.SetActive(again.Products.Single(p => p.Id == 21), true); again.SetManualImage(again.Products.Single(p => p.Id == 7), null);
        foreach (var line in again.Cart.ToArray()) again.Remove(line);
        var emptied = new SampleData(new LocalState(directory));
        Require(emptied.Cart.Count == 0 && emptied.Products.Single(p => p.Id == 21).IsActive && emptied.Products.Single(p => p.Id == 7).ManualImagePath == null,
            "Cart empty, undo and manual image removal survive restart");

        string legacyDirectory = CheckDirectory("legacy"); Directory.CreateDirectory(legacyDirectory);
        var templates = new SampleData(new LocalState(CheckDirectory("baseline"))).Products.Where(p => p.Id <= 24).OrderBy(p => p.Id).ToArray();
        var legacyRows = templates.Select(p => new StoredProduct(p.Id, p.Supplier.Id, p.Name, p.Price, p.PriceNote,
            p.Id == 7 ? "과일" : p.Category, p.Url, p.Id != 8, p.Available)).ToList();
        legacyRows.Add(new StoredProduct(25, "mega", "사용자 등록 상품", 12000, "", "기타", "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=999", true, true));
        string catalog = Path.Combine(legacyDirectory, "catalog-state.json"); File.WriteAllText(catalog, JsonSerializer.Serialize(legacyRows));
        byte[] original = SHA256.HashData(File.ReadAllBytes(catalog));
        string oldImage = Path.Combine(legacyDirectory, "manual-images", "2.webp"); Directory.CreateDirectory(Path.GetDirectoryName(oldImage)!); File.WriteAllBytes(oldImage, [4, 5, 6]);
        var migrated = new SampleData(new LocalState(legacyDirectory));
        Require(migrated.Products.Single(p => p.Id == 7).Category == "과일" && !migrated.Products.Single(p => p.Id == 8).IsActive &&
            migrated.Products.Single(p => p.Id == 2).ManualImagePath == oldImage && migrated.Products.Single(p => p.Id == 25).Name == "사용자 등록 상품" &&
            migrated.Cart.Count == 0 && original.SequenceEqual(SHA256.HashData(File.ReadAllBytes(catalog))) && File.ReadAllBytes(oldImage).SequenceEqual(new byte[] { 4, 5, 6 }),
            "Legacy edits/registration/image import without changing legacy files or seeding cart");
        using (var db = migrated.Store.Database.Connect())
            Require(SqlNumber(db, "SELECT COUNT(*) FROM Products") == 4 && SqlNumber(db, "SELECT COUNT(*) FROM Products WHERE DataOrigin='UserMock'") == 1,
                "Only changed examples and user registration materialized");
        Require(Directory.GetFiles(Path.Combine(legacyDirectory, "Data", "backups"), "*before-legacy-import*.db").Length == 1,
            "Legacy import has a pre-import SQLite backup");
        migrated.SetCategory(migrated.Products.Single(p => p.Id == 7), "원두");
        File.WriteAllText(catalog, JsonSerializer.Serialize(legacyRows.Select(p => p.Id == 7 ? p with { Category = "기타" } : p)));
        Require(new SampleData(new LocalState(legacyDirectory)).Products.Single(p => p.Id == 7).Category == "원두", "Legacy import runs once");

        string brokenDirectory = CheckDirectory("broken"); Directory.CreateDirectory(brokenDirectory);
        string brokenJson = Path.Combine(brokenDirectory, "catalog-state.json"); File.WriteAllText(brokenJson, "{broken");
        try { _ = new SampleData(new LocalState(brokenDirectory)); throw new Exception("Corrupt legacy file was silently ignored"); }
        catch (InvalidDataException) { }
        File.WriteAllText(brokenJson, "[]");
        var recovered = new SampleData(new LocalState(brokenDirectory));
        using (var db = recovered.Store.Database.Connect()) Require(SqlNumber(db, "SELECT MAX(Version) FROM SchemaMigrations") == 3, "Failed import retries safely");

        string rollbackDirectory = CheckDirectory("rollback"); var rollback = new SampleData(new LocalState(rollbackDirectory));
        using (var db = rollback.Store.Database.Connect())
        { using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TRIGGER reject_cart BEFORE INSERT ON CartItems BEGIN SELECT RAISE(ABORT, 'test rollback'); END"; cmd.ExecuteNonQuery(); }
        try { rollback.AddToCart(rollback.Products[0]); throw new Exception("Rejected cart insert silently succeeded"); }
        catch (IOException) { }
        Require(rollback.Cart.Count == 0, "Failed cart insert does not mutate memory");
        using (var db = rollback.Store.Database.Connect()) Require(SqlNumber(db, "SELECT COUNT(*) FROM Products") == 0 && SqlNumber(db, "SELECT COUNT(*) FROM CartItems") == 0,
            "Cart insert rolls back product and cart together");

        string schemaDirectory = CheckDirectory("schema"); string schemaPath = Path.Combine(schemaDirectory, "Data", "CafeOrder.db"); Directory.CreateDirectory(Path.GetDirectoryName(schemaPath)!);
        using (var db = new SqliteConnection($"Data Source={schemaPath}"))
        { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL); CREATE TABLE Sentinel(Value TEXT); INSERT INTO Sentinel VALUES('preserve')"; cmd.ExecuteNonQuery(); }
        _ = new SampleData(new LocalState(schemaDirectory));
        string[] backups = Directory.GetFiles(Path.Combine(schemaDirectory, "Data", "backups"), "*.db");
        Require(backups.Any(path => path.Contains("before-schema-1")), "Existing DB backed up before schema migration");
        using (var db = new SqliteConnection($"Data Source={backups.Single(path => path.Contains("before-schema-1"))}"))
        { db.Open(); Require(SqlNumber(db, "SELECT COUNT(*) FROM Sentinel") == 1 && SqlNumber(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='Products'") == 0, "Pre-schema backup is an intact original"); }
        string priorDirectory = CheckDirectory("schema-2");
        string priorPath = Path.Combine(priorDirectory, "Data", "CafeOrder.db");
        Directory.CreateDirectory(Path.GetDirectoryName(priorPath)!);
        using (var db = new SqliteConnection($"Data Source={priorPath}"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = """
                CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL);
                INSERT INTO SchemaMigrations VALUES(2,'legacy-import-once','2026-01-01');
                CREATE TABLE Suppliers(SupplierId TEXT PRIMARY KEY,Name TEXT NOT NULL,IsManual INTEGER NOT NULL);
                INSERT INTO Suppliers VALUES('mega','메가커피',0);
                CREATE TABLE Products(ProductId INTEGER PRIMARY KEY,SupplierId TEXT NOT NULL,Name TEXT NOT NULL,
                    Price TEXT NOT NULL,PriceNote TEXT NOT NULL,PriceText TEXT NOT NULL,Category TEXT NOT NULL,
                    ProductUrl TEXT NOT NULL,ImageUrl TEXT,ImageCachePath TEXT,ManualImagePath TEXT,
                    IsAvailable INTEGER NOT NULL,IsActive INTEGER NOT NULL,DataOrigin TEXT NOT NULL);
                INSERT INTO Products VALUES(25,'mega','이전 상품','1000','','1,000원','기타',
                    'https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359',NULL,NULL,NULL,1,1,'UserMock');
                CREATE TABLE CartItems(ProductId INTEGER PRIMARY KEY,Quantity INTEGER NOT NULL,AddedOrder INTEGER NOT NULL);
                INSERT INTO CartItems VALUES(25,2,1);
                """; cmd.ExecuteNonQuery();
        }
        var upgraded = new SampleData(new LocalState(priorDirectory));
        Require(upgraded.Products.Single(p => p.Id == 25).LastSuccessfulCheckAtUtc == null &&
            upgraded.Cart.Single().Quantity == 2 &&
            Directory.GetFiles(Path.Combine(priorDirectory, "Data", "backups"), "*before-schema-3*.db").Length == 1,
            "Version 2 product/cart survive version 3 migration with backup and unknown lookup time");
        string futureDirectory = CheckDirectory("future"); string futurePath = Path.Combine(futureDirectory, "Data", "CafeOrder.db"); Directory.CreateDirectory(Path.GetDirectoryName(futurePath)!);
        using (var db = new SqliteConnection($"Data Source={futurePath}"))
        { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL); INSERT INTO SchemaMigrations VALUES(4,'future','2026-01-01')"; cmd.ExecuteNonQuery(); }
        try { _ = new SampleData(new LocalState(futureDirectory)); throw new Exception("Future DB version silently changed"); }
        catch (InvalidDataException) { }
        using (var db = new SqliteConnection($"Data Source={futurePath}"))
        { db.Open(); Require(SqlNumber(db, "SELECT MAX(Version) FROM SchemaMigrations") == 4, "Future DB version remains unchanged"); }
        results.Add("SQLite: catalog/cart/order snapshot tables, legacy import, restart persistence, rollback, backup, x64 PASS");
    }
}
