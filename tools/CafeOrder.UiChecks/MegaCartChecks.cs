using CafeOrder;
using System.Drawing.Imaging;

internal static partial class Program
{
    private static async Task CheckMegaCartLiveAsync()
    {
        string dir = CheckDirectory("mega-cart-live");
        var data = new SampleData(new LocalState(dir));
        const string url = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        using var bitmap = new Bitmap(50, 90);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Red);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        var product = data.RegisterMegaProduct(new("조회 전 저장 상품", 1, "1원", url,
            "https://megacotr3116.cdn-nhncommerce.com/data/goods/test.jpg", bytes, true),
            "파우더", SaveInitialImage(data, bytes));
        product.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        data.Store.Database.SaveProduct(product);
        string initialImage = product.ImageCachePath!;
        await using var sessions = new SupplierSessionManager(log: data.Store.Log);
        Task? refresh = null;
        data.FirstMegaCartAdded += (item, line) => refresh = data.RefreshFirstMegaCartAsync(item, line, sessions.LookupMegaProductAsync);
        data.AddToCart(product);
        Require(refresh != null && data.Cart.Single().Quantity == 1, "Real lookup starts after immediate local cart insertion");
        await refresh!;
        Require(!data.MegaCartLookupFailed(product) && product.Price > 1 && product.Name != "조회 전 저장 상품" &&
            product.ImageCachePath != initialImage && File.Exists(product.ImageCachePath),
            "Logged-in Edge refreshes real name, price, stock and stored image");
        var restarted = new SampleData(new LocalState(dir));
        var saved = restarted.Products.Single(item => item.Id == product.Id);
        Require(saved.Price == product.Price && saved.Available == product.Available &&
            saved.ImageCachePath == product.ImageCachePath && restarted.Cart.Single().Quantity == 1,
            "Real first-add refresh survives SQLite restart");
        Console.WriteLine($"PASS: real first-cart MegaCoffee lookup; price={product.Price:N0}, available={product.Available}");
    }

    private static async Task CheckMegaCartRefreshAsync()
    {
        string dir = CheckDirectory("mega-cart-refresh");
        var data = new SampleData(new LocalState(dir));
        const string firstUrl = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        const string secondUrl = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613";
        const string thirdUrl = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000027811";
        byte[] ImageBytes(Color color)
        {
            using var bitmap = new Bitmap(50, 90);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(color);
            using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
        }
        MegaCoffeeProductSnapshot Snapshot(string url, string name, decimal price, bool available, Color color) =>
            new(name, price, $"{price:N0}원", url, "https://megacotr3116.cdn-nhncommerce.com/data/goods/test.jpg",
                ImageBytes(color), available);
        Product Register(string url, string name)
        {
            var snapshot = Snapshot(url, name, 1000, true, Color.Red);
            string path = data.Store.NewWebImagePath(); ManualImages.Save(snapshot.ImageBytes, path);
            return data.RegisterMegaProduct(snapshot, "파우더", path);
        }
        var ageData = new SampleData(new LocalState(CheckDirectory("mega-cart-age")));
        var ageSnapshot = Snapshot(firstUrl, "시각 검사 상품", 1000, true, Color.Red);
        var ageProduct = ageData.RegisterMegaProduct(ageSnapshot, "파우더",
            SaveInitialImage(ageData, ageSnapshot.ImageBytes));
        int ageLookups = 0; Task? ageTask = null;
        ageData.FirstMegaCartAdded += (item, line) => ageTask = ageData.RefreshFirstMegaCartAsync(item, line, _ =>
        { ageLookups++; return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success, ageSnapshot)); });
        ageData.AddToCart(ageProduct);
        Require(ageLookups == 0, "First card add reuses product checked within 24 hours");
        ageProduct.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        ageData.Store.Database.SaveProduct(ageProduct);
        ageData.AddToCart(ageProduct);
        await ageTask!;
        ageData.ChangeQuantity(ageData.Cart.Single(), 1);
        ageData.ChangeQuantity(ageData.Cart.Single(), -1);
        Require(ageLookups == 1 && ageData.Cart.Single().Quantity == 2 &&
            new SampleData(new LocalState(ageData.Store.DirectoryPath)).Products.Single(p => p.Id == ageProduct.Id).LastSuccessfulCheckAtUtc is { } checkedAt &&
            DateTimeOffset.UtcNow - checkedAt < TimeSpan.FromMinutes(1),
            "Stale card re-add fetches once; +/- are local and success time survives restart");
        var first = Register(firstUrl, "처음 상품");
        first.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        data.Store.Database.SaveProduct(first);
        var requests = new List<Task>();
        int lookups = 0;
        var firstResponse = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, Task<MegaProductLookupResult>> lookup = _ =>
        {
            lookups++;
            return (lookups == 1 ? firstResponse : secondResponse).Task;
        };
        data.FirstMegaCartAdded += (product, line) => requests.Add(data.RefreshFirstMegaCartAsync(product, line, lookup));
        string previousImage = first.ImageCachePath!;
        data.AddToCart(first);
        Require(data.Cart.Single().Quantity == 1 && lookups == 1 && data.IsMegaCartLookupPending(first) &&
            !data.CanStartLocalOrder(data.Cart.Single()),
            "First add immediately persists local cart and starts one lookup");
        data.AddToCart(first);
        var current = data.Cart.Single();
        data.ChangeQuantity(current, 1); data.ChangeQuantity(current, -1);
        Require(current.Quantity == 2 && lookups == 1 && data.Store.Database.ReadCart(data.Products).Single().Quantity == 2,
            "Repeated card add and plus/minus only change local SQLite quantity");
        firstResponse.SetResult(new(MegaProductLookupStatus.Success,
            Snapshot(firstUrl, "가격 갱신 상품", 2000, true, Color.Blue)));
        await requests[0];
        Require(first.Name == "가격 갱신 상품" && first.Price == 2000 && data.Subtotal(data.Cart) == 4000 &&
            current.Quantity == 2 && first.ImageCachePath != previousImage && File.Exists(first.ImageCachePath) &&
            !data.IsMegaCartLookupPending(first), "Successful lookup refreshes product, cart subtotal and stored web image without resetting quantity");
        string manualPath = data.Store.NewManualImagePath(first.Id);
        ManualImages.Save(ImageBytes(Color.Yellow), manualPath); data.SetManualImage(first, manualPath);
        data.Remove(current);
        data.AddToCart(first);
        Require(lookups == 1 && data.Cart.Single().Quantity == 1, "Recent successful lookup skips a new first-add web request");
        data.Remove(data.Cart.Single());
        first.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        data.Store.Database.SaveProduct(first);
        data.AddToCart(first);
        Require(lookups == 2 && data.Cart.Single().Quantity == 1, "Stale lookup refreshes after removal and first add");
        secondResponse.SetResult(new(MegaProductLookupStatus.Success,
            Snapshot(firstUrl, "품절 변경 상품", 3000, false, Color.Green)));
        await requests[1];
        var chosen = ProductImages.Resolve(first);
        try { Require(chosen.Manual && first.Price == 3000 && !first.Available &&
            data.Cart.Single().Quantity == 1 && !data.CanStartLocalOrder(data.Cart.Single()) && first.ImageCachePath != previousImage &&
            data.Store.Database.ReadProducts(data.Suppliers).Single(p => p.Id == first.Id).Available == false,
            "Sold-out result stays in cart, updates price, and retains manual-image priority"); }
        finally { if (chosen.Owned) chosen.Image.Dispose(); }

        var failedData = new SampleData(new LocalState(CheckDirectory("mega-cart-failed")));
        var failedProduct = failedData.RegisterMegaProduct(Snapshot(secondUrl, "저장 상품", 1000, true, Color.Red),
            "파우더", SaveInitialImage(failedData, ImageBytes(Color.Red)));
        failedProduct.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        failedData.Store.Database.SaveProduct(failedProduct);
        Task? failedTask = null;
        failedData.FirstMegaCartAdded += (product, line) => failedTask = failedData.RefreshFirstMegaCartAsync(product, line,
            _ => Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.LoginRequired)));
        string failedImage = failedProduct.ImageCachePath!;
        failedData.AddToCart(failedProduct); await failedTask!;
        Require(failedProduct.Price == 1000 && failedProduct.ImageCachePath == failedImage &&
            failedData.Cart.Single().Quantity == 1 && failedData.MegaCartLookupFailed(failedProduct) &&
            !failedData.CanStartLocalOrder(failedData.Cart.Single()),
            "Login expiry keeps stored product, image, and cart quantity");

        var raceData = new SampleData(new LocalState(CheckDirectory("mega-cart-race")));
        var raceProduct = raceData.RegisterMegaProduct(Snapshot(thirdUrl, "경합 상품", 1000, true, Color.Red),
            "파우더", SaveInitialImage(raceData, ImageBytes(Color.Red)));
        raceProduct.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        raceData.Store.Database.SaveProduct(raceProduct);
        var oldResponse = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newResponse = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int raceLookups = 0;
        Task? raceTask = null;
        raceData.FirstMegaCartAdded += (product, line) =>
        {
            var task = raceData.RefreshFirstMegaCartAsync(product, line, _ =>
            { raceLookups++; return raceLookups == 1 ? oldResponse.Task : newResponse.Task; });
            raceTask ??= task;
        };
        raceData.AddToCart(raceProduct);
        var oldLine = raceData.Cart.Single(); raceData.Remove(oldLine); raceData.AddToCart(raceProduct);
        Require(raceLookups == 1 && raceData.Cart.Single() != oldLine,
            "Re-add during an in-flight lookup queues rather than starts a concurrent lookup");
        oldResponse.SetResult(new(MegaProductLookupStatus.Success,
            Snapshot(thirdUrl, "폐기할 응답", 9000, true, Color.Blue)));
        for (int attempt = 0; attempt < 50 && raceLookups != 2; attempt++) await Task.Delay(10);
        Require(raceLookups == 2 && raceProduct.Price == 1000,
            "Stale first response is discarded and the replacement first-add lookup begins");
        newResponse.SetResult(new(MegaProductLookupStatus.Success,
            Snapshot(thirdUrl, "최신 응답", 3500, true, Color.Green)));
        await raceTask!;
        Require(raceProduct.Price == 3500 && raceData.Cart.Single().Quantity == 1,
            "Queued lookup updates product without resurrecting or changing removed cart line");

        var removedData = new SampleData(new LocalState(CheckDirectory("mega-cart-removed")));
        var removedProduct = removedData.RegisterMegaProduct(Snapshot(thirdUrl, "삭제 전 상품", 1000, true, Color.Red),
            "파우더", SaveInitialImage(removedData, ImageBytes(Color.Red)));
        removedProduct.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        removedData.Store.Database.SaveProduct(removedProduct);
        var removedResponse = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? removedTask = null;
        removedData.FirstMegaCartAdded += (product, line) => removedTask = removedData.RefreshFirstMegaCartAsync(product, line,
            _ => removedResponse.Task);
        removedData.AddToCart(removedProduct); removedData.Remove(removedData.Cart.Single());
        removedResponse.SetResult(new(MegaProductLookupStatus.Success,
            Snapshot(thirdUrl, "삭제 후 응답", 9000, true, Color.Blue)));
        await removedTask!;
        Require(removedData.Cart.Count == 0 && removedData.Store.Database.ReadCart(removedData.Products).Count == 0 &&
            removedProduct.Price == 1000 && removedProduct.Name == "삭제 전 상품",
            "Lookup completing after removal neither changes product nor recreates cart line");

        var restarted = new SampleData(new LocalState(dir));
        var saved = restarted.Products.Single(p => p.Id == first.Id);
        Require(saved.Price == 3000 && !saved.Available && saved.ManualImagePath == manualPath &&
            File.Exists(saved.ImageCachePath) && restarted.Cart.Single().Product.Id == first.Id,
            "Price, stock, web/manual images and cart survive restart");
        await data.Store.Log.FlushAsync();
        string log = await data.Store.Log.ReadRecentAsync();
        Require(log.Contains("CART_FIRST_LOOKUP_STARTED") && log.Contains("CART_FIRST_LOOKUP_SUCCESS") &&
            log.Contains("CART_FIRST_PRICE_CHANGED") && log.Contains("CART_FIRST_STOCK_CHANGED") &&
            log.Contains("goodsNo=79359") && log.Contains("oldPrice=1000 newPrice=2000") &&
            !log.Contains("goodsNo=" + "79359&"), "First-add events include IDs, goodsNo and price differences without URL data");
        results.Add("Mega cart first add: one lookup, local quantity, stale-response guard, price/stock/image persistence PASS");
    }

    private static string SaveInitialImage(SampleData data, byte[] bytes)
    { string path = data.Store.NewWebImagePath(); ManualImages.Save(bytes, path); return path; }

    private static int SeedMegaCartUi(string directory)
    {
        var data = new SampleData(new LocalState(directory));
        const string url = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359";
        using var bitmap = new Bitmap(50, 90);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Red);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        var product = data.RegisterMegaProduct(new("화면 검사 상품", 1000, "1,000원", url,
            "https://megacotr3116.cdn-nhncommerce.com/data/goods/test.jpg", bytes, true),
            "파우더", SaveInitialImage(data, bytes));
        product.LastSuccessfulCheckAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        data.Store.Database.SaveProduct(product);
        return product.Id;
    }

    private static async Task CheckMegaCartUi(MainForm main)
    {
        var data = Data(main);
        var product = data.Products.Single(p => p.Name == "화면 검사 상품");
        var view = All(main).OfType<ProductsView>().Single();
        var waiting = new TaskCompletionSource<MegaProductLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        view.MegaLookup = _ => { requests++; return waiting.Task; };
        var card = Find<ProductCard>(main, $"Product_{product.Id}");
        Mouse(card, MouseButtons.Left, new Point(45, 40)); Pump();
        var row = Find<CartProductRow>(main, $"CartRow_{product.Id}");
        var price = Find<TextBox>(row, $"CartPrice_{product.Id}");
        Require(data.Cart.Single().Quantity == 1 && requests == 1 && price.Text.Contains("확인 중"),
            "Card click immediately shows cart row and marks stored price pending");
        Mouse(card, MouseButtons.Left, new Point(45, 40)); Pump();
        Require(requests == 1 && data.Cart.Single().Quantity == 2,
            "Second product card click increases quantity without another lookup");
        Find<Button>(row, $"Plus_{product.Id}").PerformClick();
        Find<Button>(row, $"Minus_{product.Id}").PerformClick();
        Require(requests == 1 && data.Cart.Single().Quantity == 2, "Cart +/- never call the lookup delegate");
        using var bitmap = new Bitmap(50, 90);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Blue);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        waiting.SetResult(new(MegaProductLookupStatus.Success,
            new("화면 갱신 상품", 2500, "2,500원", product.Url,
                "https://megacotr3116.cdn-nhncommerce.com/data/goods/test2.jpg", stream.ToArray(), false)));
        for (int attempt = 0; attempt < 100 && (product.Price != 2500 || data.IsMegaCartLookupPending(product)); attempt++)
            await Task.Delay(10);
        Pump();
        Require(product.Price == 2500 && !product.Available && price.Text.Contains("2,500원") &&
            price.Text.Contains("품절") && !Find<Button>(main, "SupplierOrder_mega").Enabled &&
            !Find<Button>(main, "OrderAll").Enabled, "Price/stock result refreshes cart row and disables ordering sold-out items");
        var cartImage = Find<PictureBox>(row, $"CartImage_{product.Id}").Image;
        var cardImage = (Image?)typeof(ProductCard).GetField("selectedImage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(card);
        Require(cartImage != null && cardImage != null, "Product and cart images are loaded");
        using var cartBitmap = new Bitmap(cartImage!);
        using var cardBitmap = new Bitmap(cardImage!);
        Require(cartBitmap.GetPixel(cartBitmap.Width / 2, cartBitmap.Height / 2).B > 150 &&
            cardBitmap.GetPixel(cardBitmap.Width / 2, cardBitmap.Height / 2).B > 150 &&
            data.Subtotal(data.Cart) == 5000 &&
            All(main).OfType<Label>().Any(label => label.Text == "소계 5,000원"),
            "Stored web image, product card, cart row and supplier subtotal refresh together");
        results.Add("Mega cart UI: immediate row, pending price, sold-out order guard, card/cart image and totals PASS");
    }
}
