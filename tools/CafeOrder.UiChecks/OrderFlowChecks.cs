using CafeOrder;
using System.Diagnostics;
using System.Collections;
using System.Reflection;
using Microsoft.Playwright;

internal static partial class Program
{
    private static void CheckOrderFlowLocal()
    {
        var targets = new SiteCartTarget[]
        {
            new(1, "79359", "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359", "A", 1, 1000),
            new(2, "1000002613", "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613", "B", 2, 2000),
            new(3, "1000027811", "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000027811", "C", 1, 3000)
        };
        SiteCartEntry[] actual =
        [new("1000027811", 1, "", "C"), new("79359", 1, "", "A"), new("1000002613", 2, "", "B")];
        Require(MegaCoffeeSiteCart.Matches(actual, targets),
            "MegaCoffee 1/2/1 site cart matches by goodsNo and quantity regardless of row order");
        Require(!MegaCoffeeSiteCart.Matches(actual[..2], targets) &&
            !MegaCoffeeSiteCart.Matches([actual[0], actual[0], actual[2]], targets),
            "Missing or duplicated site rows cannot be marked READY");

        var data = new SampleData(new LocalState(CheckDirectory("order-restore")));
        var product = data.Products.Single(item => item.Id == 1);
        product.Url = targets[0].ProductUrl;
        data.Store.Database.SaveProduct(product);
        data.AddToCart(product);
        var attempt = data.Store.Database.CreateSiteCartAttempt("mega", [targets[0] with { Price = product.Price,
            Name = product.Name }]);
        data.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, "READY", "VERIFIED");
        var sessions = new SupplierSessionManager(Path.Combine(CheckDirectory("order-profiles"), "profiles"));
        try
        {
            using (var form = new OrderForm(data, data.Cart.ToArray()) { Sessions = sessions })
            {
                form.Show(); Application.DoEvents();
                Require(Find<Label>(form, "OrderStatus_1").Text.Contains("확인 필요", StringComparison.Ordinal) &&
                    Find<Button>(form, "OrderAction_1").Text == "사이트 확인" &&
                    data.Cart.Single().Quantity == 1, "Persisted READY needs a read-only recheck after restart");
                form.Close();
            }
            data.Store.Database.MarkSiteCartBrowserOpened(attempt.AttemptId);
            data.ChangeQuantity(data.Cart.Single(), 1);
            data.Shipping["mega"] = 0;
            using (var form = new OrderForm(data, data.Cart.ToArray()) { Sessions = sessions })
            {
                form.Show(); Application.DoEvents();
                Require(Find<Label>(form, "OrderStatus_1").Text.Contains("확인 필요", StringComparison.Ordinal) &&
                    !Find<Button>(form, "OrderAction_1").Enabled &&
                    Find<Button>(form, "OrderSite_1").Enabled &&
                    Find<Button>(form, "StartAllOrders").Enabled,
                    $"Browser-opened state: status={Find<Label>(form, "OrderStatus_1").Text}, " +
                    $"action={Find<Button>(form, "OrderAction_1").Text}/{Find<Button>(form, "OrderAction_1").Enabled}, " +
                    $"new={Find<Button>(form, "OrderSite_1").Enabled}, all={Find<Button>(form, "StartAllOrders").Enabled}");
                var card = ((IEnumerable)typeof(OrderForm).GetField("cards", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(form)!).Cast<object>().Single();
                var fresh = (SiteCartAttempt)typeof(OrderForm).GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(form, [card])!;
                Require(fresh.AttemptId != attempt.AttemptId && fresh.Targets.Single().Quantity == 2 &&
                    data.Store.Database.ReadLatestSiteCartAttempt("mega", fresh.Targets)?.Attempt.AttemptId == fresh.AttemptId,
                    "Explicit new-order path can save a fresh SQLite snapshot without changing the site cart");
                form.Close();
            }
            var sample = data.Products.Single(item => item.Id == 2);
            data.AddToCart(sample);
            using (var form = new OrderForm(data,
                [data.Cart.Single(line => line.Product == sample)]) { Sessions = sessions })
            {
                form.Show(); Application.DoEvents();
                Require(!Find<Button>(form, $"OrderAction_{sample.Id}").Enabled &&
                    Find<Button>(form, $"OrderAction_{sample.Id}").Text == "상품 링크 확인 필요",
                    "An unverified sample URL cannot start a real site-cart preparation");
                form.Close();
            }
        }
        finally { sessions.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        var launched = new List<ProcessStartInfo>();
        var original = SellerLinks.Launch;
        SellerLinks.Launch = info => launched.Add(info);
        try
        {
            var coupang = new Product(999, "상품", 1000, "",
                data.Suppliers.Single(item => item.Id == "coupang"), "기타", true, 0)
            { Url = "https://mc.coupang.com/vp/products/7281521260?vendorItemId=95887985069&sourceType=HOME_FBI" };
            var naver = new Product(1000, "상품", 1000, "",
                data.Suppliers.Single(item => item.Id == "naver"), "기타", true, 0)
            { Url = "https://brand.naver.com/example/products/7419367541?tracking=1" };
            Require(SellerLinks.OpenManualProduct(coupang) && SellerLinks.OpenManualProduct(naver) &&
                launched.Count == 2 && launched[0].FileName == coupang.Url &&
                launched[1].FileName == naver.Url && launched.All(info => info.UseShellExecute),
                "Manual suppliers keep the exact product URL and use the Windows default browser");
            coupang.Url = "https://example.invalid/product/1";
            Require(!SellerLinks.OpenManualProduct(coupang) && launched.Count == 2,
                "Unverified sample links never launch a browser");

            var savedManual = data.Products.First(item => item.Supplier.Id == "coupang");
            savedManual.Url = "https://mc.coupang.com/ssr/sdp/link?vendorItemId=86533357952";
            data.Store.Database.SaveProduct(savedManual);
            data.AddToCart(savedManual);
            var manualLine = data.Cart.Single(line => line.Product == savedManual);
            using var manualOrder = new OrderForm(data, [manualLine]);
            manualOrder.Show(); Application.DoEvents();
            Find<Button>(manualOrder, "StartAllOrders").PerformClick(); Application.DoEvents();
            Require(launched.Count == 2 &&
                Find<Label>(manualOrder, $"OrderStatus_{savedManual.Id}").Text.Contains("직접 주문", StringComparison.Ordinal) &&
                Find<Button>(manualOrder, "NextOrderSite").Text == "다음 상품 열기",
                "Batch start leaves manual product URLs waiting instead of opening every browser window");
            Find<Button>(manualOrder, "NextOrderSite").PerformClick(); Application.DoEvents();
            Require(launched.Count == 3 && launched[2].FileName == savedManual.Url &&
                data.Cart.Contains(manualLine),
                "Next manual product opens only one original URL and preserves the local cart");
            manualOrder.Close();
        }
        finally { SellerLinks.Launch = original; }
    }

    private static async Task CheckMegaCartDelayedReadAsync()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        int reads = 0, writes = 0;
        string[] codes = ["79359", "1000002613", "1000027811"];
        int[] quantities = [1, 2, 1];
        await page.RouteAsync("**/*", async route =>
        {
            if (route.Request.Url == "https://www.megacoffee.co.kr/order/cart.php" &&
                route.Request.Method == "GET")
            {
                int visible = Math.Min(++reads, codes.Length);
                string rows = string.Join("", Enumerable.Range(0, visible).Select(index =>
                    $"<tr><td><input name='cartSno[]' value='{index + 1}'></td>" +
                    $"<td><a href='/goods/goods_view.php?goodsNo={codes[index]}'>상품 {index + 1}</a></td>" +
                    $"<td><span class='order_goods_num'>{quantities[index]}개</span></td></tr>"));
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/html; charset=utf-8",
                    Body = "<a href='/member/logout.php'>로그아웃</a><form id='frmCart'><table>" + rows +
                        "</table></form>" });
            }
            else { if (route.Request.Method != "GET") writes++; await route.AbortAsync(); }
        });
        var targets = codes.Select((code, index) => new SiteCartTarget(index + 1, code,
            $"https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo={code}",
            "상품 " + (index + 1), quantities[index], 1000)).ToArray();
        var result = await MegaCoffeeSiteCart.ReadUntilMatchedAsync(page,
            new MegaCoffeeLoginProbe(), targets, TimeSpan.FromSeconds(5));
        Require(result.Matched && reads == 3 && writes == 0 &&
            MegaCoffeeSiteCart.Matches(result.Items, targets),
            "Delayed 1/2/1 cart rows are re-read to READY without repeating a site write");
    }

    private static async Task CheckCheckoutWindowsReadOnlyAsync()
    {
        await using var manager = new SupplierSessionManager();
        var slots = (IDictionary)typeof(SupplierSessionManager).GetField("slots",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var paths = new Dictionary<string, string>
        {
            ["mega"] = "/order/cart.php", ["piece"] = "/affiliate/prdorder",
            ["nuldam"] = "/order/basket.html"
        };
        foreach (var (supplier, path) in paths)
        {
            object slot = slots[supplier]!;
            var contextField = slot.GetType().GetField("CheckoutContext", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var driverField = slot.GetType().GetField("CheckoutDriver", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                var opened = await manager.OpenSiteCartForUserAsync(supplier);
                var context = (IBrowserContext)contextField.GetValue(slot)!;
                var page = context.Pages.LastOrDefault(item => !item.IsClosed) ??
                    throw new Exception(supplier + " dedicated Edge window did not remain visible");
                if (opened.State == "WAITING_FOR_USER")
                {
                    Require(opened.Reason is "LOGIN_REQUIRED" or "LOGIN_UNVERIFIED",
                        supplier + " does not falsely mark an unverified session as opened");
                    Console.WriteLine("WAITING: " + supplier + " dedicated Edge requires user login; cart was not verified");
                }
                else
                {
                    Require(opened.State == "OPENED" && new Uri(page.Url).AbsolutePath == path,
                        supplier + " opens the verified cart URL in its dedicated profile: " + opened.Reason);
                    Console.WriteLine("PASS: " + supplier + " headed Edge cart opened read-only at " + path);
                }
            }
            finally
            {
                if (contextField.GetValue(slot) is IBrowserContext context)
                    await context.CloseAsync();
                if (driverField.GetValue(slot) is IPlaywright driver) driver.Dispose();
            }
        }
    }

    private static async Task CheckOrderReviewReadOnlyAsync()
    {
        await using var manager = new SupplierSessionManager();
        var opened = await manager.OpenSiteCartForUserAsync("mega");
        Require(opened.State == "OPENED", "MegaCoffee logged-in cart window is visible: " + opened.Reason);
        var slots = (IDictionary)typeof(SupplierSessionManager).GetField("slots",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        object slot = slots["mega"]!;
        var context = (IBrowserContext)slot.GetType().GetField("CheckoutContext",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slot)!;
        var page = context.Pages.Last(item => !item.IsClosed);
        var actual = await MegaCoffeeSiteCart.ReadAsync(page, new MegaCoffeeLoginProbe());
        SiteCartTarget[] targets = actual.Select((item, index) => new SiteCartTarget(index + 1,
            item.ExternalProductId,
            "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=" + item.ExternalProductId,
            item.Name, item.Quantity, item.UnitPrice ?? 0, item.OptionKey)).ToArray();
        var match = await manager.VerifySiteCartAsync("mega", targets);
        Require(match.State == "READY" && match.LastVerifiedStage == "READ_ONLY_VERIFIED",
            "An already-open Edge cart can be checked without a new site-cart write");
        SiteCartTarget[] changed = targets.Length == 0
            ? [new SiteCartTarget(1, "79359", "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=79359", "", 1, 0)]
            : targets.Select((item, index) => index == 0 ? item with { Quantity = item.Quantity + 1 } : item).ToArray();
        var mismatch = await manager.VerifySiteCartAsync("mega", changed);
        Require(mismatch.State == "UNKNOWN" && mismatch.Reason == "SITE_CART_MISMATCH",
            "A changed target remains unconfirmed rather than being marked READY");
        Require(MegaCoffeeSiteCart.Matches(await MegaCoffeeSiteCart.ReadAsync(page, new MegaCoffeeLoginProbe()), targets),
            "Read-only site confirmation leaves the live cart unchanged");
        Console.WriteLine($"PASS: open-window read-only site confirmation; rows={actual.Count}, matching=READY, mismatch=UNKNOWN, cart unchanged");
    }
}
