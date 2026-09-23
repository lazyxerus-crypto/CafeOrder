using CafeOrder;
using System.Drawing.Imaging;

internal static partial class Program
{
    private static async Task CheckPieceXlsxAsync()
    {
        string dir = CheckDirectory("piece-xlsx");
        var data = new SampleData(new LocalState(dir));
        const string url = "https://www.piececake.co.kr/product/product_view?prodNo=PD2637";
        using var bitmap = new Bitmap(30, 30);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        string name = "미니도넛(바바리안)";
        decimal price = 11100;
        int lookups = 0;
        Task<MegaProductLookupResult> PieceLookup(string requested)
        {
            lookups++;
            Require(requested == url, "PieceCake lookup receives normalized URL");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success,
                new MegaCoffeeProductSnapshot(name + " (BOX 30개입)", price, $"{price:N0}원", url,
                    "https://www.piececake.co.kr/prodImg?p=mini_doughnut-02.jpg", bytes, true)));
        }
        Task<MegaProductLookupResult> NoMega(string _) => throw new Exception("PieceCake import must not call MegaCoffee lookup");
        string workbook = MakeWorkbook(dir, "piece-links", sheet =>
        {
            sheet.Cell(2, 7).Value = url + "&utm_source=first";
            sheet.Cell(3, 7).Value = url.Replace("www.", "") + "&tracking=duplicate";
        });
        using (var plan = await data.PrepareImportAsync(workbook, NoMega, null, CancellationToken.None, PieceLookup))
        {
            Require(plan.Issues.Count == 0 && plan.Added == 1 && plan.Skipped == 1 && lookups == 1 &&
                plan.Changes.Single().Proposed.Supplier.Id == "piece" &&
                plan.Changes.Single().Proposed.Category == "디저트/스낵" &&
                File.Exists(plan.Changes.Single().PendingImagePath),
                "PieceCake URL-only XLSX imports one real lookup and skips duplicate prodNo before lookup");
            data.ApplyImport(plan);
        }
        var saved = data.Products.Single(product => product.Url == url);
        Require(saved.Price == 11100 && saved.IsActive && saved.Available &&
            saved.ImageCachePath is { } && File.Exists(saved.ImageCachePath) &&
            saved.LastSuccessfulCheckAtUtc != null, "PieceCake product, image and lookup time saved to SQLite");
        var restarted = new SampleData(new LocalState(dir));
        Require(restarted.Products.Single(product => product.Id == saved.Id).ImageCachePath == saved.ImageCachePath,
            "PieceCake image path restored after restart");
        string duplicate = MakeWorkbook(dir, "piece-existing", sheet => sheet.Cell(2, 7).Value = url + "&source=repeat");
        using (var plan = await data.PrepareImportAsync(duplicate, NoMega, null, CancellationToken.None, PieceLookup))
            Require(plan.Issues.Count == 0 && plan.Added == 0 && plan.Skipped == 1 && lookups == 1,
                "Existing PieceCake prodNo is skipped without lookup");

        string manual = data.Store.NewManualImagePath(saved.Id);
        ManualImages.Save(bytes, manual); data.SetManualImage(saved, manual);
        name = "미니도넛(초코)"; price = 12300;
        string update = MakeWorkbook(dir, "piece-update", sheet =>
        { sheet.Cell(2, 1).Value = saved.Id; sheet.Cell(2, 7).Value = url; });
        using (var plan = await data.PrepareImportAsync(update, NoMega, null, CancellationToken.None, PieceLookup))
        {
            Require(plan.Issues.Count == 0 && plan.Updated == 1 && plan.Changes.Single().Proposed.ManualImagePath == manual,
                "PieceCake ID plus URL updates same product and preserves manual image");
            data.ApplyImport(plan);
        }
        Require(saved.Id == restarted.Products.Single(product => product.Url == url).Id &&
            saved.Price == 12300 && saved.ManualImagePath == manual &&
            data.Store.Database.ReadProducts(data.Suppliers).Count == 1,
            "PieceCake XLSX update preserves ProductId and one-row DB identity");
        saved.Name = "대파베이컨계란빵"; saved.Price = 15500;
        saved.DisplayPrice = "15,500 (BOX 10개입)";
        data.Store.Database.SaveProduct(saved);
        data.AddToCart(saved); data.AddToCart(saved);
        var migrated = new SampleData(new LocalState(dir));
        var normalized = migrated.Products.Single(product => product.Id == saved.Id);
        Require(normalized.Name == "대파베이컨계란빵 (BOX 10개입)" && normalized.PriceText == "15,500원" &&
            normalized.ManualImagePath == manual && normalized.Url == url &&
            migrated.Cart.Single(line => line.Product.Id == saved.Id).Quantity == 2,
            "Existing PieceCake display migrates without changing ProductId, image, URL or BOX order quantity");
    }
}
