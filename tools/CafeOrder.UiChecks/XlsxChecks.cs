using CafeOrder;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

internal static partial class Program
{
    private sealed class CheckTransferDialogs : IProductTransferDialogs
    {
        internal string? ExportPath, ImportPath;
        internal ProductImportPlan? Preview;
        internal bool AllowApply = true;
        internal readonly List<string> Messages = [];
        public string? ChooseExport(IWin32Window owner) => ExportPath;
        public string? ChooseImport(IWin32Window owner) => ImportPath;
        public bool Confirm(IWin32Window owner, ProductImportPlan plan) { Preview = plan; return AllowApply; }
        public void Show(IWin32Window owner, string message, bool error) => Messages.Add(message);
        public void ShowIssues(IWin32Window owner, IReadOnlyList<ProductWorkbookIssue> issues) =>
            Messages.Add($"오류 {issues.Count}개 · " + string.Join("; ", issues));
    }
    private static void WriteProductRow(IXLWorksheet sheet, int row, int? id, string supplier, string name,
        decimal price, string display, string category, string url, bool active)
    {
        if (id is int value) sheet.Cell(row, 1).Value = value;
        sheet.Cell(row, 2).Value = supplier; sheet.Cell(row, 3).Value = name;
        sheet.Cell(row, 4).Value = (double)price; sheet.Cell(row, 5).Value = display;
        sheet.Cell(row, 6).Value = category; sheet.Cell(row, 7).Value = url; sheet.Cell(row, 8).Value = active;
    }
    private static string MakeWorkbook(string directory, string name, Action<IXLWorksheet> fill)
    {
        string path = Path.Combine(directory, name + ".xlsx"); Directory.CreateDirectory(directory);
        using var book = new XLWorkbook(); var sheet = book.Worksheets.Add("Products");
        for (int i = 0; i < ClosedXmlProductWorkbook.Headers.Length; i++) sheet.Cell(1, i + 1).Value = ClosedXmlProductWorkbook.Headers[i];
        fill(sheet); book.SaveAs(path); return path;
    }
    private static void CheckXlsx()
    {
        string dir = CheckDirectory("xlsx"); var data = new SampleData(new LocalState(dir));
        var sample = data.Products.Single(p => p.Id == 7); data.SetCategory(sample, "과일");
        string image = Path.Combine(dir, "manual-images", "7-xlsx.webp"); Directory.CreateDirectory(Path.GetDirectoryName(image)!); File.WriteAllBytes(image, [1, 2, 3]);
        data.SetManualImage(sample, image); data.AddToCart(sample);
        var registered = data.RegisterMock("https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613", "기타");
        data.SetActive(registered, false);
        string exported = Path.Combine(dir, "products.xlsx"); Require(data.ExportWorkbook(exported) == 2, "Only stored products exported");
        using (var book = new XLWorkbook(exported))
        {
            var sheet = book.Worksheet(1);
            Require(Enumerable.Range(1, 8).Select(i => sheet.Cell(1, i).GetString()).SequenceEqual(ClosedXmlProductWorkbook.Headers), "Exact XLSX column order");
            Require(sheet.LastRowUsed()!.RowNumber() == 3 && sheet.Cell(2, 1).GetDouble() == 7 &&
                sheet.Cell(3, 8).GetBoolean() == false && sheet.Cell(2, 4).DataType == XLDataType.Number, "Persisted IDs, numeric prices, inactive products");
            Require(!Enumerable.Range(1, 8).SelectMany(c => Enumerable.Range(1, 3).Select(r => sheet.Cell(r, c).GetString()))
                .Any(value => value.Contains(image) || value.Contains("CartItems")), "No cart or image path in XLSX");
        }
        var same = data.PrepareImport(exported);
        Require(same.Issues.Count == 0 && same.Added == 0 && same.Updated == 0, "Export roundtrip validates without spurious edits");
        data.ApplyImport(same);
        string[] BeforeBackups() => Directory.GetFiles(Path.Combine(dir, "Data", "backups"), "*before-xlsx-import*.db");
        Require(BeforeBackups().Length == 0, "No backup needed for unchanged roundtrip");

        string changed = MakeWorkbook(dir, "changed", sheet =>
        {
            WriteProductRow(sheet, 2, 7, "푸드레인", "변경된 원두", 42000, "42,000원 특가", "유제품", "", false);
            WriteProductRow(sheet, 3, null, "메가커피", registered.Name, 0, "", "기타", registered.Url, true);
        });
        var plan = data.PrepareImport(changed);
        Require(plan.Issues.Count == 0 && plan.Added == 1 && plan.Updated == 1 && plan.Deactivated == 1,
            "Preview counts add, edit and active-to-inactive before writing");
        var cartRef = data.Cart.Single().Product;
        data.ApplyImport(plan);
        Require(ReferenceEquals(cartRef, sample) && sample.Name == "변경된 원두" && sample.Price == 42000 &&
            sample.PriceText == "42,000원 특가" && sample.Supplier.Id == "food" && sample.Category == "유제품" &&
            sample.Url == "" && !sample.IsActive && sample.ManualImagePath == image && sample.DataOrigin == "Sample",
            "Existing ID and internal image/origin preserved with live cart reference");
        Require(data.Products.Single(p => p.Id == 26).Name == registered.Name && data.Products.Single(p => p.Id == 26).Url == registered.Url &&
            data.Products.Single(p => p.Id == 25).IsActive == false, "Blank ID creates a distinct product; omitted product stays stored");
        Require(BeforeBackups().Length == 1, "Backup created immediately before import");
        using (var db = new SqliteConnection($"Data Source={BeforeBackups().Single()}"))
        { db.Open(); Require(SqlNumber(db, "SELECT COUNT(*) FROM Products") == 2 && SqlNumber(db, "SELECT COUNT(*) FROM Products WHERE ProductId=26") == 0,
            "Pre-import backup has old product set"); }
        var restarted = new SampleData(new LocalState(dir));
        Require(restarted.Products.Single(p => p.Id == 7).Name == "변경된 원두" && restarted.Products.Single(p => p.Id == 26).Price == 0 &&
            restarted.Products.Single(p => p.Id == 7).ManualImagePath == image && restarted.Cart.Single().Product.Id == 7,
            "XLSX changes and cart association survive restart");

        var invalid = new (string Name, Action<IXLWorksheet> Edit, string Column)[]
        {
            ("price", sheet => sheet.Cell(2, 4).Value = "가격 아님", "Price"),
            ("negative", sheet => sheet.Cell(2, 4).Value = -1, "Price"),
            ("category", sheet => sheet.Cell(2, 6).Value = "없는 분류", "Category"),
            ("supplier", sheet => sheet.Cell(2, 2).Value = "없는 판매처", "Supplier"),
            ("id", sheet => sheet.Cell(2, 1).Value = 99999, "ProductId"),
            ("url", sheet => sheet.Cell(2, 7).Value = "ftp://example.com/item", "ProductUrl"),
            ("active", sheet => sheet.Cell(2, 8).Value = "yes", "IsActive"),
            ("formula", sheet => sheet.Cell(2, 3).FormulaA1 = "1+1", "Name")
        };
        int backupCount = BeforeBackups().Length;
        foreach (var item in invalid)
        {
            string path = MakeWorkbook(dir, "bad-" + item.Name, sheet =>
            { WriteProductRow(sheet, 2, 7, "푸드레인", "정상 상품", 1000, "1,000원", "원두", "", true); item.Edit(sheet); });
            var rejected = data.PrepareImport(path);
            Require(rejected.Issues.Any(issue => issue.Row == 2 && issue.Column == item.Column) && BeforeBackups().Length == backupCount,
                "Invalid " + item.Name + " reports row/column and leaves DB untouched");
        }
        string duplicate = MakeWorkbook(dir, "duplicate", sheet =>
        { WriteProductRow(sheet, 2, 7, "푸드레인", "A", 1, "1원", "원두", "", true);
          WriteProductRow(sheet, 3, 7, "푸드레인", "B", 1, "1원", "원두", "", true); });
        Require(data.PrepareImport(duplicate).Issues.Any(i => i.Row == 3 && i.Column == "ProductId"), "Duplicate IDs rejected");
        Require(data.Products.Single(p => p.Id == 7).Name == "변경된 원두", "Invalid files do not mutate memory");

        string failing = MakeWorkbook(dir, "rollback", sheet =>
        { WriteProductRow(sheet, 2, 7, "푸드레인", "첫 행 수정", 1234, "1,234원", "원두", "", false);
          WriteProductRow(sheet, 3, null, "메가커피", "fail-new", 1, "1원", "기타", "", true); });
        var failPlan = data.PrepareImport(failing); Require(failPlan.Issues.Count == 0 && failPlan.Changes.Count == 2, "Rollback fixture validates");
        using (var db = data.Store.Database.Connect())
        { using var cmd = db.CreateCommand(); cmd.CommandText = "CREATE TRIGGER fail_xlsx BEFORE INSERT ON Products WHEN NEW.Name='fail-new' BEGIN SELECT RAISE(ABORT, 'test rollback'); END"; cmd.ExecuteNonQuery(); }
        try { data.ApplyImport(failPlan); throw new Exception("Import failure unexpectedly committed"); }
        catch (InvalidDataException ex) { Require(ex.Message.Contains("3행") && ex.Message.Contains("ProductId"), "DB failure identifies the failing row"); }
        Require(!failPlan.Applied && data.Products.Single(p => p.Id == 7).Name == "변경된 원두" && BeforeBackups().Length == backupCount + 1,
            "Mid-import failure leaves memory unchanged and keeps pre-apply backup");
        using (var db = data.Store.Database.Connect())
        { Require(SqlNumber(db, "SELECT COUNT(*) FROM Products") == 3 && SqlNumber(db, "SELECT COUNT(*) FROM Products WHERE Name='첫 행 수정'") == 0,
            "First row rolled back when later insert failed"); }
        results.Add("XLSX: export/roundtrip, validation, duplicate/new IDs, backup, rollback, restart PASS");
    }
    private static Task CheckXlsxUi(MainForm main)
    {
        var data = Data(main); var grid = Find<ProductGrid>(main, "ProductList"); main.Size = new Size(1280, 720); main.Update();
        data.AddToCart(data.Products.Single(p => p.Id == 7));
        string path = MakeWorkbook(data.Store.DirectoryPath, "ui-import", sheet =>
        {
            WriteProductRow(sheet, 2, 7, "푸드레인", "XLSX 수정 원두", 19000, "19,000원", "원두", "", true);
            WriteProductRow(sheet, 3, null, "메가커피", "XLSX 신규 원두", 8000, "8,000원", "원두", "https://www.megacoffee.co.kr/item/1", true);
        });
        var dialogs = new CheckTransferDialogs { ImportPath = path };
        All(main).OfType<ProductsView>().Single().TransferDialogs = dialogs;
        grid.AutoScrollPosition = new Point(0, 80); Require(grid.AutoScrollPosition.Y < 0, "Import UI starts from scrolled catalog");
        dialogs.AllowApply = false; Find<Button>(main, "ImportProducts").PerformClick();
        Require(data.Store.Database.ReadProducts(data.Suppliers).Count == 1 &&
            Directory.GetFiles(Path.Combine(data.Store.DirectoryPath, "Data", "backups"), "*before-xlsx-import*.db").Length == 0,
            "Declining confirmation creates no import backup or DB change");
        dialogs.AllowApply = true;
        Find<Button>(main, "ImportProducts").PerformClick();
        Require(dialogs.Preview is { Added: 1, Updated: 1, Deactivated: 0, Issues.Count: 0 } &&
            grid.AutoScrollPosition == Point.Empty && grid.Controls.OfType<ProductCard>().Any(c => c.Product?.Name == "XLSX 신규 원두"),
            "Import button confirms, creates card and returns scroll to top");
        Require(Find<TextBox>(main, "CartPrice_7").Text == "19,000원" && All(main).Any(c => c.Name == "Cart_food"),
            "Imported price and supplier update the existing cart item");
        Find<ComboBox>(main, "CategoryFilter").SelectedItem = "원두";
        Require(grid.Items.Where(c => !c.IsDraft).All(c => c.Product!.Category == "원두") &&
            grid.Items.Any(c => c.Product?.Name == "XLSX 신규 원두"), "Current filter applies to imported card");
        string bad = MakeWorkbook(data.Store.DirectoryPath, "ui-bad", sheet =>
        { WriteProductRow(sheet, 2, 7, "푸드레인", "무효 가격", 1, "1원", "원두", "", true); sheet.Cell(2, 4).Value = "bad"; });
        dialogs.ImportPath = bad; Find<Button>(main, "ImportProducts").PerformClick();
        Require(dialogs.Messages.Last().Contains("2행 · Price") && data.Products.Single(p => p.Id == 7).Name == "XLSX 수정 원두",
            "Import button shows row/column errors without partial UI changes");
        Capture(main, "13-xlsx-import-filtered");
        return Task.CompletedTask;
    }
}
