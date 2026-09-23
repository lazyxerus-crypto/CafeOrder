using CafeOrder;
using System.Drawing.Imaging;
using System.Reflection;
using Microsoft.Playwright;

internal static partial class Program
{
    private static async Task CheckMegaParserAsync()
    {
        Require(MegaCoffeeProductLookup.TryProductUrl(
            "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359", out _, out var code)
            && code == "79359", "Official MegaCoffee product URL accepted");
        Require(MegaCoffeeProductLookup.TryProductUrl(
            "https://megacoffee.co.kr/goods/goods_view.php?utm_source=x&goodsNo=79359&tracking=1",
            out var clean, out var trackedCode) && trackedCode == code &&
            clean!.ToString() == "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359",
            "Tracking parameters and host variants normalize to one product URL");
        foreach (string invalid in new[]
        {
            "http://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359",
            "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=abc",
            "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359&goodsNo=1",
            "https://notmegacoffee.co.kr/goods/goods_view.php?goodsNo=79359"
        }) Require(!MegaCoffeeProductLookup.TryProductUrl(invalid, out _, out _), "Invalid product URL rejected");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        string html = "";
        await page.RouteAsync("**/*", route => route.Request.Url.Contains("/goods/goods_view.php", StringComparison.Ordinal)
            ? route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html }) : route.AbortAsync());
        const string url = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        string Page(string codeValue, string price, bool soldOut, bool image = true) =>
            $"<html><body><div class='item_info_box'><div>상품코드 : {codeValue}</div>" +
            "<div class='item_detail_tit'><span>검증 상품 1kg</span></div>" +
            $"<dl class='item_price'><dd>{price}</dd></dl>" +
            (soldOut ? "<span>품절</span>" : "") +
            $"<button id='cartBtn' {(soldOut ? "disabled" : "")}>담기</button>" +
            $"<button class='btn_add_order' {(soldOut ? "disabled" : "")}>구매</button></div>" +
            (image ? "<div class='item_photo_big'><img src='https://megacotr3116.cdn-nhncommerce.com/data/goods/16/08/11/79359/79359_magnify_047.jpg'></div>" : "") +
            "</body></html>";
        html = Page("79359", "15,200원", false);
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 5000 });
        var available = await MegaCoffeeProductLookup.ReadPageAsync(page, "79359");
        Require(available.Name == "검증 상품 1kg" && available.Price == 15200 &&
            available.DisplayPrice == "15,200원" && available.Available, "Available item parsed from verified fields");
        html = Page("79359", "15,200원", true);
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 5000 });
        Require(!(await MegaCoffeeProductLookup.ReadPageAsync(page, "79359")).Available,
            "Sold-out item recognized only with disabled purchase buttons and label");
        foreach (string bad in new[] { Page("999", "15,200원", false), Page("79359", "가격문의", false),
            Page("79359", "15,200원", false, false) })
        {
            html = bad; await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 5000 });
            bool rejected = false;
            try { await MegaCoffeeProductLookup.ReadPageAsync(page, "79359"); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Unverified code, price, or image never becomes a product");
        }
    }

    private static async Task CheckMegaDraft(MainForm main)
    {
        var data = Data(main);
        var view = All(main).OfType<ProductsView>().Single();
        var grid = Find<ProductGrid>(main, "ProductList");
        int before = data.Store.Database.ReadProducts(data.Suppliers).Count;
        var oldProduct = data.Products.First();
        string oldName = oldProduct.Name;
        decimal oldPrice = oldProduct.Price;
        var bytes = new MemoryStream();
        using (var bitmap = new Bitmap(40, 30))
        {
            using var g = Graphics.FromImage(bitmap);
            g.Clear(Color.CornflowerBlue);
            bitmap.Save(bytes, ImageFormat.Png);
        }
        const string productUrl = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        var snapshot = new MegaCoffeeProductSnapshot("민트라벨 그릭아침 요거트 파우더 1kg 2개세트",
            15200, "15,200원", productUrl,
            "https://megacotr3116.cdn-nhncommerce.com/data/goods/16/08/11/79359/79359_magnify_047.jpg",
            bytes.ToArray(), true);
        var next = new MegaProductLookupResult(MegaProductLookupStatus.Failed);
        view.MegaLookup = async _ => { await Task.Delay(30); return next; };
        Find<Button>(main, "AddProduct").PerformClick();
        var draft = grid.Items[0];
        var url = Find<TextBox>(draft, "DraftUrl");
        url.Text = productUrl;
        var initialOrder = grid.Items.ToArray();
        var failed = draft.RegisterMegaDraftAsync();
        Require(!url.Enabled && !failed.IsCompleted, "MegaCoffee lookup leaves the UI free while waiting");
        await failed;
        Require(draft.IsDraft && url.Enabled && url.Text == productUrl && initialOrder.SequenceEqual(grid.Items)
            && data.Store.Database.ReadProducts(data.Suppliers).Count == before,
            "Failed lookup retains its URL/draft and never inserts a product");

        next = new(MegaProductLookupStatus.InvalidUrl);
        await draft.RegisterMegaDraftAsync();
        Require(draft.IsDraft && url.Text == productUrl, "Invalid MegaCoffee URL retains the draft");
        next = new(MegaProductLookupStatus.LoginRequired);
        await draft.RegisterMegaDraftAsync();
        Require(draft.IsDraft && url.Text == productUrl, "Expired login retains the draft");
        next = new(MegaProductLookupStatus.Success, snapshot with { ImageBytes = [1, 2, 3] });
        await draft.RegisterMegaDraftAsync();
        Require(draft.IsDraft && data.Store.Database.ReadProducts(data.Suppliers).Count == before,
            "Broken image cannot create a partial product");

        next = new(MegaProductLookupStatus.Success, snapshot);
        grid.AutoScrollPosition = new Point(0, 30);
        var scroll = grid.AutoScrollPosition;
        await draft.RegisterMegaDraftAsync();
        Require(!draft.IsDraft && initialOrder.SequenceEqual(grid.Items) && grid.AutoScrollPosition == scroll,
            "Verified product adopts the same draft card without sorting or scrolling");
        var product = draft.Product!;
        Require(product.Supplier.Id == "mega" && product.Name == snapshot.Name && product.Price == snapshot.Price
            && product.PriceText == snapshot.DisplayPrice && product.Url == productUrl && product.Available
            && product.ImageUrl == snapshot.ImageUrl && File.Exists(product.ImageCachePath)
            && data.Store.Database.ReadProducts(data.Suppliers).Count == before + 1,
            "Verified product and cached image are saved to SQLite");
        Require(oldProduct.Name == oldName && oldProduct.Price == oldPrice,
            "New MegaCoffee lookup does not overwrite an existing product");
        var selected = typeof(ProductCard).GetField("selectedImage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var manual = typeof(ProductCard).GetField("manualImage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require(selected.GetValue(draft) is Image && manual.GetValue(draft) is false, "Cached web image is displayed");

        data.AddToCart(product);
        var cartImage = Find<PictureBox>(main, $"CartImage_{product.Id}");
        using var order = new OrderForm(data, [data.Cart.Single(line => line.Product.Id == product.Id)]);
        var orderImage = Find<PictureBox>(order, $"CartImage_{product.Id}");
        static Color Center(Image image)
        { using var copy = new Bitmap(image); return copy.GetPixel(copy.Width / 2, copy.Height / 2); }
        Require(Center(cartImage.Image!) == Center((Image)selected.GetValue(draft)!) &&
            Center(orderImage.Image!) == Center(cartImage.Image!),
            "Product, cart, and order use the same cached site image immediately");

        string manualSource = Path.Combine(data.Store.DirectoryPath, "test-manual.png");
        Directory.CreateDirectory(data.Store.DirectoryPath);
        using (var manualBitmap = new Bitmap(40, 30))
        {
            using var g = Graphics.FromImage(manualBitmap); g.Clear(Color.Firebrick);
            manualBitmap.Save(manualSource, ImageFormat.Png);
        }
        draft.SetManual(manualSource);
        Require(manual.GetValue(draft) is true && Center(cartImage.Image!).R > 140 &&
            Center(cartImage.Image!).G < 100 && Center(orderImage.Image!) == Center(cartImage.Image!),
            "Manual image immediately wins in product, cart, and open order");
        var reopened = new SampleData(new LocalState(data.Store.DirectoryPath));
        var persisted = reopened.Products.Single(p => p.Id == product.Id);
        Require(persisted.Name == snapshot.Name && persisted.Price == snapshot.Price && persisted.ImageCachePath == product.ImageCachePath
            && File.Exists(persisted.ManualImagePath), "Real product and manual image survive restart");
        draft.RemoveManual();
        Require(manual.GetValue(draft) is false && selected.GetValue(draft) is Image &&
            Center(cartImage.Image!) == Center((Image)selected.GetValue(draft)!) &&
            Center(orderImage.Image!) == Center(cartImage.Image!),
            "Removing manual image immediately restores the cached site image everywhere");
        var historyProduct = data.Products.Single(item => item.Id == 1);
        string historyPath = data.Store.NewManualImagePath(historyProduct.Id);
        ManualImages.Save(manualSource, historyPath);
        data.SetManualImage(historyProduct, historyPath);
        var historyImage = All(main).OfType<HistoryThumbnail>().Single(image => image.Product.Id == historyProduct.Id);
        Require(Center(historyImage.Image!).R > 140 && Center(historyImage.Image!).G < 100,
            "Existing order history row refreshes to the selected manual image");
        var reopenedCart = new SampleData(new LocalState(data.Store.DirectoryPath));
        using var restoredRow = new CartProductRow(reopenedCart, reopenedCart.Cart.Single(line => line.Product.Id == product.Id));
        var restoredImage = Find<PictureBox>(restoredRow, $"CartImage_{product.Id}");
        Require(Center(restoredImage.Image!) == Center(cartImage.Image!), "Cart image survives restart without web access");
        int storedBeforeDraft = data.Store.Database.ReadProducts(data.Suppliers).Count;
        Find<Button>(main, "AddProduct").PerformClick();
        Require(new SampleData(new LocalState(data.Store.DirectoryPath)).Products.Count(p => p.Id == product.Id) == 1
            && data.Store.Database.ReadProducts(data.Suppliers).Count == storedBeforeDraft,
            "Unfinished draft is never persisted");
        foreach (var supplier in data.Suppliers)
        {
            var item = data.Products.First(candidate => candidate.Supplier.Id == supplier.Id && candidate.Available);
            data.AddToCart(item);
            Require(Find<Control>(main, $"Cart_{supplier.Id}") != null, "Every supplier retains its cart card");
            var thumb = Find<PictureBox>(main, $"CartImage_{item.Id}");
            var expected = ProductImages.Resolve(item);
            try { Require(Center(thumb.Image!) == Center(expected.Image), "Cart image selection matches product for " + supplier.Id); }
            finally { if (expected.Owned) expected.Image.Dispose(); }
        }
        var placeholderProduct = data.Products.Single(item => item.Id == 6);
        data.SetCategory(placeholderProduct, "기타");
        Require(ReferenceEquals(Find<PictureBox>(main, $"CartImage_{placeholderProduct.Id}").Image,
            SampleImages.Catalog("기타")), "Cart placeholder follows category changes immediately");
    }
}
