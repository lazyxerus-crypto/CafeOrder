using CafeOrder;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using System.Drawing.Imaging;

internal static partial class Program
{
    private static async Task CheckXlsxLiveAsync(string output)
    {
        string root = Path.GetFullPath(output) + Path.DirectorySeparatorChar;
        string isolated = Path.GetFullPath(Path.Combine(output, "isolated-live-" + Guid.NewGuid().ToString("N")));
        if (!isolated.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Isolated path escaped artifact directory");
        try
        {
            string workbook = MakeWorkbook(isolated, "live-products", sheet =>
            {
                sheet.Cell(2, 7).Value = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
                sheet.Cell(3, 7).Value = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613";
            });
            var data = new SampleData(new LocalState(isolated));
            await using var sessions = new SupplierSessionManager(log: data.Store.Log);
            using var plan = await data.PrepareImportAsync(workbook, sessions.LookupMegaProductAsync, null, CancellationToken.None);
            if (plan.Issues.Count != 0) throw new InvalidDataException(string.Join("; ", plan.Issues));
            Require(plan.Added == 2 && plan.Changes.All(change => File.Exists(change.PendingImagePath)),
                "Two real URLs prepare before DB change");
            await data.ApplyImportAsync(plan);
            var restarted = new SampleData(new LocalState(isolated));
            Require(restarted.Products.Count(product => product.ImageCachePath != null) == 2 &&
                restarted.Products.Where(product => product.ImageCachePath != null).All(product => File.Exists(product.ImageCachePath)),
                "Two real products and WebP images survive SQLite restart");
            await data.Store.Log.FlushAsync();
            string events = await data.Store.Log.ReadRecentAsync();
            Require(events.Split("PRODUCT_LOOKUP_SUCCESS").Length - 1 == 2 &&
                !events.Contains("goodsNo=") && !events.Contains("session-cookies"),
                "Real lookup successes are logged without product URLs or session values");
            foreach (var product in restarted.Products.Where(product => product.ImageCachePath != null))
                Console.WriteLine($"{product.Name}: {product.PriceText}, available={product.Available}");
            Console.WriteLine("PASS: two real MegaCoffee XLSX URLs, backup, SQLite restart, WebP images");
        }
        finally { if (Directory.Exists(isolated)) Directory.Delete(isolated, true); }
    }

    private static async Task CheckXlsxLookupAsync()
    {
        string dir = CheckDirectory("xlsx-lookup");
        var data = new SampleData(new LocalState(dir));
        const string yogurt = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        const string peach = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613";
        const string third = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000027811";
        const string fourth = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000027812";
        using var image = new Bitmap(30, 30);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        MegaCoffeeProductSnapshot Snapshot(string url, string name, decimal price, bool available) =>
            new(name, price, $"{price:N0}원", url,
                "https://megacotr3116.cdn-nhncommerce.com/data/goods/16/08/11/79359/79359_magnify_047.jpg",
                bytes, available);
        var current = new Dictionary<string, MegaCoffeeProductSnapshot>
        {
            [yogurt] = Snapshot(yogurt, "민트라벨 요거트 파우더 1kg", 15200, true),
            [peach] = Snapshot(peach, "복숭아 농축액 1.9kg", 20330, false),
            [third] = Snapshot(third, "rollback-new 파우더", 9000, true),
            [fourth] = Snapshot(fourth, "혼합 신규 원두", 11000, true)
        };
        int lookups = 0;
        Task<MegaProductLookupResult> Lookup(string url)
        { lookups++; return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success, current[url])); }
        string initial = MakeWorkbook(dir, "linked", sheet =>
        {
            sheet.Cell(2, 7).Value = yogurt;
            sheet.Cell(3, 7).Value = peach;
            WriteProductRow(sheet, 4, null, "파미유", "완성형 상품", 1200, "1,200원", "기타",
                "https://piececake.example.invalid/product/1", true);
        });
        var read = new ClosedXmlProductWorkbook(data.Suppliers, SampleData.Categories).Import(initial);
        Require(read.Issues.Count == 0 && read.Rows.Count(row => row.LookupRequested) == 2 &&
            !read.Rows.Single(row => row.SheetRow == 4).LookupRequested, "Only incomplete URL rows request lookup");
        var cancelledPlan = await data.PrepareImportAsync(initial, Lookup, null, CancellationToken.None);
        string[] pending = cancelledPlan.Changes.Select(change => change.PendingImagePath).OfType<string>().ToArray();
        Require(cancelledPlan.Issues.Count == 0 && cancelledPlan.Added == 3 && lookups == 2 &&
            cancelledPlan.Changes.Single(change => change.SheetRow == 2).Proposed.Category == "파우더" &&
            cancelledPlan.Changes.Single(change => change.SheetRow == 3).Proposed.Category == "베이스/농축액" &&
            pending.Length == 2 && pending.All(File.Exists) &&
            data.Store.Database.ReadProducts(data.Suppliers).Count == 0,
            "Lookup fills verified fields and stages images without touching SQLite");
        cancelledPlan.Dispose();
        Require(pending.All(path => !File.Exists(path)) &&
            Directory.GetFiles(Path.Combine(dir, "Data", "backups"), "*before-xlsx-import*.db").Length == 0,
            "Declined import cleans staged images and creates no import backup");

        using (var plan = await data.PrepareImportAsync(initial, Lookup, null, CancellationToken.None))
        {
            await data.ApplyImportAsync(plan);
            Require(plan.Applied && plan.Added == 3, "Confirmed import applies complete and linked rows together");
        }
        var yogurtProduct = data.Products.Single(product => product.Url == yogurt);
        var peachProduct = data.Products.Single(product => product.Url == peach);
        Require(yogurtProduct.Price == 15200 && yogurtProduct.ImageCachePath is { } &&
            peachProduct.Available == false && peachProduct.IsActive &&
            data.Products.Single(product => product.Name == "완성형 상품").ImageCachePath == null,
            "Linked rows save price/image/availability while complete row avoids lookup");
        data.AddToCart(yogurtProduct);
        var restarted = new SampleData(new LocalState(dir));
        Require(restarted.Cart.Count(line => line.Product.Url == yogurt) == 1 &&
            restarted.Products.Where(product => product.Url is yogurt or peach).All(product => File.Exists(product.ImageCachePath)),
            "Imported images and all cart lines survive restart without web lookup");
        string[] Backups() => Directory.GetFiles(Path.Combine(dir, "Data", "backups"), "*before-xlsx-import*.db");
        Require(Backups().Length == 1, "Import creates a pre-transaction backup");

        string manual = data.Store.NewManualImagePath(yogurtProduct.Id);
        ManualImages.Save(bytes, manual); data.SetManualImage(yogurtProduct, manual);
        current[yogurt] = Snapshot(yogurt, "민트라벨 요거트 파우더 2kg", 16000, false);
        string update = MakeWorkbook(dir, "linked-update", sheet =>
        { sheet.Cell(2, 1).Value = yogurtProduct.Id; sheet.Cell(2, 7).Value = yogurt; });
        using (var plan = await data.PrepareImportAsync(update, Lookup, null, CancellationToken.None))
        {
            Require(plan.Issues.Count == 0 && plan.Updated == 1 && plan.Added == 0 &&
                plan.Changes.Single().Proposed.ManualImagePath == manual, "ID + URL updates same product and keeps manual image");
            data.ApplyImport(plan);
        }
        Require(yogurtProduct.Id == restarted.Products.Single(product => product.Url == yogurt).Id &&
            yogurtProduct.Price == 16000 && !yogurtProduct.Available && yogurtProduct.IsActive &&
            yogurtProduct.ManualImagePath == manual && data.Cart.Single(line => line.Product.Id == yogurtProduct.Id).Product == yogurtProduct,
            "Linked update preserves ID, manual image, active state, and cart reference");

        int beforeCount = data.Store.Database.ReadProducts(data.Suppliers).Count;
        int beforeBackups = Backups().Length;
        string duplicate = MakeWorkbook(dir, "linked-duplicate", sheet =>
        {
            sheet.Cell(2, 7).Value = third + "&utm_source=x";
            sheet.Cell(3, 7).Value = third.Replace("www.", "") + "&tracking=1";
        });
        int beforeDuplicateLookups = lookups;
        using (var plan = await data.PrepareImportAsync(duplicate, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Count == 0 && plan.Added == 1 && plan.Skipped == 1 &&
                plan.SkippedRows.Single().SheetRow == 3 && lookups == beforeDuplicateLookups + 1,
                "Same goodsNo with tracking or host variation skips second row before lookup");
        string alreadyRegistered = MakeWorkbook(dir, "linked-existing", sheet => sheet.Cell(2, 7).Value = yogurt + "&utm_source=again");
        int beforeExistingLookups = lookups;
        using (var plan = await data.PrepareImportAsync(alreadyRegistered, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Count == 0 && plan.Changes.Count == 0 && plan.Skipped == 1 &&
                lookups == beforeExistingLookups, "Already registered goodsNo skips without lookup or DB change");
        string explicitUpdate = MakeWorkbook(dir, "linked-explicit-wins", sheet =>
        { sheet.Cell(2, 7).Value = yogurt; sheet.Cell(3, 1).Value = yogurtProduct.Id; sheet.Cell(3, 7).Value = yogurt + "&tracking=1"; });
        using (var plan = await data.PrepareImportAsync(explicitUpdate, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Count == 0 && plan.Skipped == 1 && plan.Updated == 1 &&
                plan.Changes.Single().SheetRow == 3, "Explicit ProductId update is not skipped by duplicate blank-ID row");
        string conflictingIds = MakeWorkbook(dir, "linked-conflicting-ids", sheet =>
        {
            sheet.Cell(2, 1).Value = yogurtProduct.Id; sheet.Cell(2, 7).Value = third;
            sheet.Cell(3, 1).Value = peachProduct.Id; sheet.Cell(3, 7).Value = third + "&tracking=1";
        });
        using (var plan = await data.PrepareImportAsync(conflictingIds, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Any(issue => issue.Row == 3 && issue.Reason.Contains("서로 다른 ProductId")),
                "Different ProductIds targeting one goodsNo fail without merge");
        string unsupported = MakeWorkbook(dir, "linked-unsupported", sheet =>
        { sheet.Cell(2, 7).Value = third; sheet.Cell(3, 7).Value = "https://www.piececake.co.kr/product/product_view?prodNo=PD2637"; });
        using (var plan = await data.PrepareImportAsync(unsupported, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Any(issue => issue.Row == 3 && issue.Reason.Contains("지원하지")), "Unsupported supplier is identified by row");
        string malformed = MakeWorkbook(dir, "linked-malformed", sheet => sheet.Cell(2, 7).Value =
            "http://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359");
        using (var plan = await data.PrepareImportAsync(malformed, Lookup, null, CancellationToken.None))
            Require(plan.Issues.Any(issue => issue.Row == 2 && issue.Reason.Contains("형식")), "Bad MegaCoffee URL is identified by row");
        string thirdOnly = MakeWorkbook(dir, "linked-third", sheet => sheet.Cell(2, 7).Value = third);
        using (var plan = await data.PrepareImportAsync(thirdOnly,
            _ => Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.LoginRequired)), null, CancellationToken.None))
            Require(plan.Issues.Any(issue => issue.Row == 2 && issue.Reason.Contains("로그인")), "Expired login leaves DB untouched");
        foreach (string reason in new[] { "상품명을 확인할 수 없습니다.", "숫자 판매가를 확인할 수 없습니다.", "상품 이미지를 확인할 수 없습니다." })
        {
            using var plan = await data.PrepareImportAsync(thirdOnly,
                _ => Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed, Reason: reason)),
                null, CancellationToken.None);
            Require(plan.Issues.Any(issue => issue.Row == 2 && issue.Reason == reason), "Lookup failure keeps row and precise reason");
        }
        using (var cancel = new CancellationTokenSource())
        {
            var waiting = data.PrepareImportAsync(thirdOnly,
                _ => new TaskCompletionSource<MegaProductLookupResult>().Task, null, cancel.Token);
            cancel.CancelAfter(50);
            try { await waiting; throw new Exception("Cancelled lookup unexpectedly completed"); }
            catch (OperationCanceledException) { }
        }
        Require(data.Store.Database.ReadProducts(data.Suppliers).Count == beforeCount && Backups().Length == beforeBackups,
            "Bad, duplicate, expired, and cancelled imports never change SQLite");

        string rollback = MakeWorkbook(dir, "linked-rollback", sheet =>
        { sheet.Cell(2, 1).Value = yogurtProduct.Id; sheet.Cell(2, 7).Value = yogurt; sheet.Cell(3, 7).Value = third; });
        using var rollbackPlan = await data.PrepareImportAsync(rollback, Lookup, null, CancellationToken.None);
        Require(rollbackPlan.Issues.Count == 0 && rollbackPlan.Changes.Count == 2, "Two linked rows are prepared before transaction");
        string[] finalImages = rollbackPlan.Changes.Select(change => change.Proposed.ImageCachePath!).ToArray();
        using (var db = data.Store.Database.Connect())
        {
            using var command = db.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_linked BEFORE INSERT ON Products WHEN NEW.Name='rollback-new 파우더' BEGIN SELECT RAISE(ABORT, 'test rollback'); END";
            command.ExecuteNonQuery();
        }
        try { data.ApplyImport(rollbackPlan); throw new Exception("Linked rollback fixture unexpectedly committed"); }
        catch (InvalidDataException ex) { Require(ex.Message.Contains("3행"), "Linked rollback identifies failing row"); }
        Require(data.Store.Database.ReadProducts(data.Suppliers).Count == beforeCount &&
            data.Products.Single(product => product.Id == yogurtProduct.Id).Price == 16000 &&
            finalImages.All(path => !File.Exists(path)) && Backups().Length == beforeBackups + 1,
            "Mid-import failure rolls back all rows, keeps backup, and removes prepared images");
        using var racePlan = await data.PrepareImportAsync(thirdOnly, Lookup, null, CancellationToken.None);
        string raceImage = racePlan.Changes.Single().Proposed.ImageCachePath!;
        data.Store.Database.InsertProduct(new Product(0, "다른 프로세스가 등록한 상품", 1, "",
            data.Suppliers.Single(supplier => supplier.Id == "mega"), "기타", true, 0) { Url = third });
        try { data.ApplyImport(racePlan); throw new Exception("Concurrent duplicate unexpectedly committed"); }
        catch (InvalidDataException ex) { Require(ex.Message.Contains("ProductUrl"), "Concurrent duplicate identifies URL"); }
        Require(!File.Exists(raceImage) && data.Store.Database.ReadProducts(data.Suppliers).Count == beforeCount + 1,
            "Duplicate appearing after preview cannot create a second product");
        string mixed = MakeWorkbook(dir, "linked-mixed-skip", sheet =>
        { sheet.Cell(2, 7).Value = third + "&tracking=again"; sheet.Cell(3, 7).Value = fourth; });
        int beforeMixedLookups = lookups;
        using (var plan = await data.PrepareImportAsync(mixed, Lookup, null, CancellationToken.None))
        {
            Require(plan.Issues.Count == 0 && plan.Skipped == 1 && plan.Added == 1 &&
                lookups == beforeMixedLookups + 1, "Existing and new URLs mix without requerying duplicate");
            data.ApplyImport(plan);
        }
        Require(data.Products.Single(product => product.Url == fourth).Name == "혼합 신규 원두" &&
            data.Store.Database.ReadProducts(data.Suppliers).Count == beforeCount + 2,
            "Mixed import saves only new goodsNo");
        await data.Store.Log.FlushAsync();
        string log = await data.Store.Log.ReadRecentAsync();
        Require(log.Contains("XLSX_ROW_ADDED") && log.Contains("XLSX_ROW_UPDATED") &&
            log.Contains("XLSX_ROW_SKIPPED") && log.Contains("XLSX_ROW_FAILED") &&
            log.Contains("XLSX_IMPORT_APPLIED") && !log.Contains("utm_source"),
            "XLSX row results are logged without URL tracking parameters");
        results.Add("Linked XLSX: lookup, update, normal rows, duplicate/errors, cancellation, backup/rollback, restart PASS");
    }
}
