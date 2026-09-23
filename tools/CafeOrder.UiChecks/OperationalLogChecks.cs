using CafeOrder;

internal static partial class Program
{
    private static async Task CheckOperationalLogAsync()
    {
        string directory = CheckDirectory("operational-log");
        var data = new SampleData(new LocalState(directory));
        var product = data.Products.Single(item => item.Id == 7);
        data.AddToCart(product);
        data.ChangeQuantity(data.Cart.Single(), 1);
        data.Remove(data.Cart.Single());
        data.SetCategory(product, "기타");
        data.SetActive(product, false);
        await using (var sessions = new SupplierSessionManager(Path.Combine(directory, "browser-profiles"), log: data.Store.Log))
        {
            await sessions.CheckAsync("piece");
            Require((await sessions.LookupMegaProductAsync("http://invalid.example/product")).Status == MegaProductLookupStatus.InvalidUrl,
                "Invalid URL is rejected without opening Edge");
        }
        string workbook = MakeWorkbook(directory, "backup-failure", sheet =>
            WriteProductRow(sheet, 2, product.Id, "메가커피", "백업 실패 검사", 100, "100원", "기타", "", false));
        using (var plan = data.PrepareImport(workbook))
        {
            string backupFolder = Path.Combine(directory, "Data", "backups");
            string heldFolder = Path.Combine(directory, "Data", "backups-held");
            Directory.Move(backupFolder, heldFolder);
            File.WriteAllText(backupFolder, "blocking fixture");
            try
            {
                try { data.ApplyImport(plan); throw new Exception("Blocked backup unexpectedly succeeded"); }
                catch (IOException) { }
            }
            finally { File.Delete(backupFolder); Directory.Move(heldFolder, backupFolder); }
        }
        Require(product.Name != "백업 실패 검사", "Backup failure leaves product unchanged");
        data.Store.Log.Write(LogLevel.ERROR, "TEST_SANITIZATION",
            "https://example.invalid/?token=PRIVATE_VALUE cookie=PRIVATE_VALUE password=PRIVATE_VALUE",
            error: new IOException());
        await data.Store.Log.FlushAsync();
        string text = await new OperationalLog(Path.Combine(directory, "Logs")).ReadRecentAsync();
        Require(text.Contains("DB_OPENED") && text.Contains("DB_MIGRATION_APPLIED") &&
            text.Contains("SUPPLIER_LOGIN_STATE") && text.Contains("PRODUCT_LOOKUP_FAILED") &&
            text.Contains("CART_ITEM_ADDED") &&
            text.Contains("CART_QUANTITY_CHANGED") && text.Contains("CART_ITEM_REMOVED") &&
            text.Contains("PRODUCT_UPDATED") && text.Contains("PRODUCT_DELETED") &&
            text.Contains("DB_BACKUP_FAILED") && text.Contains("XLSX_IMPORT_FAILED") &&
            text.Contains("errorType=IOException") && !text.Contains("PRIVATE_VALUE") &&
            !text.Contains("https://example.invalid"), "Real events persist after logger restart without secrets");
        for (int index = 0; index < 5_200; index++)
            data.Store.Log.Write(LogLevel.INFO, "ROTATION_FIXTURE", new string('A', 900));
        await data.Store.Log.FlushAsync();
        string logs = Path.Combine(directory, "Logs");
        long currentBytes = new FileInfo(data.Store.Log.FilePath).Length;
        bool archived = Enumerable.Range(1, 3).All(index => File.Exists(Path.Combine(logs, $"CafeOrder.log.{index}"))) &&
            !File.Exists(Path.Combine(logs, "CafeOrder.log.4"));
        Require(archived && currentBytes <= 1_000_000,
            $"Log files rotate with a 1 MB current-file cap and three archives (current={currentBytes}, archived={archived})");
        results.Add("Operational log persistence, sanitization and rotation PASS");
    }

    private static async Task CheckLogUi(MainForm main)
    {
        Find<TabControl>(main, "MainTabs").SelectedIndex = 3;
        Pump();
        var data = Data(main);
        data.AddToCart(data.Products.Single(product => product.Id == 7));
        var log = Find<RichTextBox>(main, "LogText");
        for (int attempt = 0; attempt < 100 && !log.Text.Contains("CART_ITEM_ADDED"); attempt++)
        { await Task.Delay(25); Pump(); }
        Require(log.Text.Contains("DB_OPENED") && log.Text.Contains("CART_ITEM_ADDED") &&
            !log.Text.Contains("UI 목업 시작"), "Log tab shows real events and no fixed sample text");
        Find<Button>(main, "CopyAllLogs").PerformClick();
        Require(Clipboard.GetText() == log.Text, "Copy all uses exactly the displayed real log");
        await data.Store.Log.FlushAsync();
    }

    private static async Task CheckLogRestart(MainForm main)
    {
        Find<TabControl>(main, "MainTabs").SelectedIndex = 3;
        Pump();
        var log = Find<RichTextBox>(main, "LogText");
        for (int attempt = 0; attempt < 100 && !log.Text.Contains("CART_ITEM_ADDED"); attempt++)
        { await Task.Delay(25); Pump(); }
        Require(log.Text.Contains("CART_ITEM_ADDED"), "Log tab restores prior run entries");
    }
}
