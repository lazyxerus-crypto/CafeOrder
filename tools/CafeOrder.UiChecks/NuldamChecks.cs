using CafeOrder;
using System.Drawing.Imaging;

internal static partial class Program
{
    private const string NuldamUrl = "https://nuldampartners.com/product/detail.html?product_no=250";
    private const string NuldamName = "(널담)뚱낭시에-솔티드바닐라(15개입)";

    private static async Task CheckNuldamXlsxAsync()
    {
        string dir = CheckDirectory("nuldam-xlsx");
        var data = new SampleData(new LocalState(dir));
        using var bitmap = new Bitmap(30, 30);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        decimal price = 24000;
        int calls = 0;
        Task<MegaProductLookupResult> Lookup(string requested)
        {
            calls++;
            Require(requested == NuldamUrl, "Nuldam URL is canonicalized before lookup");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success,
                new MegaCoffeeProductSnapshot(NuldamName, price, $"{price:N0}원", NuldamUrl,
                    "https://nuldampartners.com/web/product/big/202305/test.jpg", bytes, true)));
        }
        Task<MegaProductLookupResult> NoOther(string _) => throw new Exception("Nuldam import called another supplier lookup");
        string file = MakeWorkbook(dir, "nuldam-links", sheet =>
        {
            sheet.Cell(2, 7).Value = NuldamUrl + "&cate_no=1&display_group=9";
            sheet.Cell(3, 7).Value = NuldamUrl + "&cate_no=2&display_group=3&tracking=duplicate";
        });
        using (var plan = await data.PrepareImportAsync(file, NoOther, null, CancellationToken.None, NoOther, Lookup))
        {
            Require(plan.Issues.Count == 0 && plan.Added == 1 && plan.Skipped == 1 && calls == 1 &&
                plan.Changes.Single().Proposed.Supplier.Id == "nuldam" &&
                plan.Changes.Single().Proposed.Category == "디저트/스낵" &&
                File.Exists(plan.Changes.Single().PendingImagePath),
                "Nuldam URL-only XLSX imports once and skips duplicate product_no before lookup");
            data.ApplyImport(plan);
        }
        var saved = data.Products.Single(product => product.Url == NuldamUrl);
        Require(saved.Price == 24000 && saved.IsActive && saved.Available &&
            saved.ImageCachePath is { } && File.Exists(saved.ImageCachePath) &&
            saved.LastSuccessfulCheckAtUtc != null,
            "Nuldam product, image and lookup time saved to SQLite");
        string manual = data.Store.NewManualImagePath(saved.Id);
        ManualImages.Save(bytes, manual); data.SetManualImage(saved, manual);
        price = 25000;
        string update = MakeWorkbook(dir, "nuldam-update", sheet =>
        { sheet.Cell(2, 1).Value = saved.Id; sheet.Cell(2, 7).Value = NuldamUrl; });
        using (var plan = await data.PrepareImportAsync(update, NoOther, null, CancellationToken.None, NoOther, Lookup))
        {
            Require(plan.Issues.Count == 0 && plan.Updated == 1 && plan.Changes.Single().Proposed.ManualImagePath == manual,
                "Nuldam ProductId plus URL updates product and protects manual image");
            data.ApplyImport(plan);
        }
        var restored = new SampleData(new LocalState(dir));
        var persistent = restored.Products.Single(product => product.Id == saved.Id);
        Require(persistent.Price == 25000 && persistent.ManualImagePath == manual &&
            persistent.ImageCachePath == saved.ImageCachePath && persistent.Url == NuldamUrl &&
            restored.Store.Database.ReadProducts(restored.Suppliers).Count == 1,
            "Nuldam ProductId, images and URL survive SQLite restart");
    }

    private static async Task CheckNuldamDraft(MainForm main)
    {
        var data = Data(main);
        var view = All(main).OfType<ProductsView>().Single();
        var grid = Find<ProductGrid>(main, "ProductList");
        using var bitmap = new Bitmap(40, 40);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        int calls = 0;
        view.NuldamLookup = requested =>
        {
            calls++;
            Require(requested == NuldamUrl, "Nuldam Draft keeps entered URL for validated lookup");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success,
                new MegaCoffeeProductSnapshot(NuldamName, 24000, "24,000원", NuldamUrl,
                    "https://nuldampartners.com/web/product/big/202305/test.jpg", stream.ToArray(), true)));
        };
        Find<Button>(main, "AddProduct").PerformClick();
        var draft = grid.Items[0];
        Find<TextBox>(draft, "DraftUrl").Text = NuldamUrl;
        draft.RegisterDraft();
        for (int i = 0; i < 100 && draft.IsDraft; i++) { await Task.Delay(30); Pump(); }
        Require(!draft.IsDraft && draft.Product is { Supplier.Id: "nuldam", Price: 24000 } &&
            calls == 1 && File.Exists(draft.Product.ImageCachePath) && grid.Items[0] == draft,
            "Nuldam Draft registers verified product in place with saved image");
        var product = draft.Product!;
        data.AddToCart(product);
        Require(data.Cart.Any(line => line.Product.Id == product.Id) &&
            Find<PictureBox>(main, $"CartImage_{product.Id}").Image != null,
            "Nuldam cart uses saved image without another web lookup");
        var restarted = new SampleData(new LocalState(data.Store.DirectoryPath));
        Require(restarted.Products.Single(saved => saved.Id == product.Id).ImageCachePath == product.ImageCachePath &&
            restarted.Cart.Single(line => line.Product.Id == product.Id).Quantity == 1,
            "Nuldam product and cart survive SQLite restart");
    }
}
