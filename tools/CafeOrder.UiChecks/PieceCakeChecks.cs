using CafeOrder;
using Microsoft.Playwright;
using System.Drawing.Imaging;

internal static partial class Program
{
    private static async Task CheckPieceParserAsync()
    {
        const string url = "https://www.piececake.co.kr/product/product_view?prodNo=PD2637";
        Require(PieceCakeProductLookup.TryProductUrl(url, out _, out var code) && code == "PD2637",
            "PieceCake official product URL accepted");
        Require(PieceCakeProductLookup.TryProductUrl(
            "https://piececake.co.kr/product/product_view?utm=x&prodNo=PD2637", out var clean, out _) &&
            clean!.ToString() == url && ProductUrlIdentity.Key("piece", clean.ToString()) ==
                ProductUrlIdentity.Key("piece", url), "PieceCake tracking URL has stable product identity");
        foreach (var invalid in new[]
        {
            "http://www.piececake.co.kr/product/product_view?prodNo=PD2637",
            "https://www.piececake.co.kr/product/product_view?prodNo=not-a-product",
            "https://www.piececake.co.kr/product/product_view?prodNo=PD2637&prodNo=PD2624",
            "https://notpiececake.co.kr/product/product_view?prodNo=PD2637"
        }) Require(!PieceCakeProductLookup.TryProductUrl(invalid, out _, out _), "Invalid PieceCake URL rejected");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true });
        var page = await browser.NewPageAsync();
        string html = "";
        string cartHtml = "";
        object[] cartItems = [];
        string detailCode = "PD2637";
        int detailPrice = 11100;
        await page.RouteAsync("**/*", route =>
        {
            if (route.Request.Url.Contains("/product/product_view", StringComparison.Ordinal))
                return route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = html });
            if (route.Request.Url.EndsWith("/affiliate/prdorder", StringComparison.Ordinal))
                return route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = cartHtml });
            if (route.Request.Url.EndsWith("/selectList", StringComparison.Ordinal))
            {
                if (route.Request.PostData?.Contains("getProdCartList", StringComparison.Ordinal) == true)
                    return route.FulfillAsync(new() { ContentType = "application/json", Body =
                        System.Text.Json.JsonSerializer.Serialize(new { list = cartItems }) });
                return route.FulfillAsync(new() { ContentType = "application/json", Body =
                    System.Text.Json.JsonSerializer.Serialize(new { list = new[] { new {
                        PROD_NO = detailCode, PROD_KNM = "미니도넛(바바리안)", UNIT_PRICE = detailPrice,
                        SOLDOUT = "N", DEFAULT_CNT = 1, CUST_PROD_NO = "configured"
                    } } }) });
            }
            return route.AbortAsync();
        });
        string Fixture(string amount, bool sold, bool image = true) =>
            "<html><body><a href='/logout'>로그아웃</a><a href='/affiliate/join_change'>정보수정</a>" +
            "<div id='prdView'><div class='imgBox'><div class='img'>" +
            (image ? "<img class='prdImage' src='/prodImg?p=mini_doughnut-02.jpg'>" : "") +
            $"<div class='sold_overlay' style='display:{(sold ? "block" : "none")};'>SOLD OUT</div>" +
            "</div></div><div class='infoBox'><div class='name'>미니도넛(바바리안)</div>" +
            $"<div class='price'><span class='value'>{amount}</span><span class='box'>(BOX 30개입)</span></div>" +
            "<input id='txtSaleQty' value='1' max='999'>" + (sold ? "" : "<a id='btnCart'>장바구니</a>") +
            "</div></div><script>fetch('/selectList',{method:'POST',body:'getProdDtpt'});</script></body></html>";
        html = Fixture("11,100", false);
        await page.GotoAsync(url);
        var parsed = await PieceCakeProductLookup.ReadPageAsync(page, "PD2637");
        Require(parsed.Name == "미니도넛(바바리안) (BOX 30개입)" && parsed.Price == 11100 &&
            parsed.DisplayPrice == "11,100원" && parsed.Available &&
            parsed.InitialQuantity == 1 && PieceCakeSiteCart.PurchaseKey(parsed.Name) == "BOX:30",
            "PieceCake name, price, unit and availability parsed from observed elements");
        Require(PieceCakeProductLookup.DisplayName("대파베이컨계란빵 (BOX 10개입)", "(BOX 10개입)") ==
            "대파베이컨계란빵 (BOX 10개입)", "PieceCake purchase unit is not duplicated in the name");
        var verified = await PieceCakeProductLookup.NavigateAndReadAsync(page, new Uri(url), "PD2637",
            new PieceCakeLoginProbe());
        Require(verified.CartConfigured && verified.Price == 11100,
            "PieceCake server product code and visible page data agree before registration");
        detailCode = "PD2624";
        bool wrongCode = false;
        try { await PieceCakeProductLookup.NavigateAndReadAsync(page, new Uri(url), "PD2637", new PieceCakeLoginProbe()); }
        catch (InvalidDataException) { wrongCode = true; }
        Require(wrongCode, "Different server prodNo is rejected even at the requested URL");
        detailCode = "PD2637"; detailPrice = 12345;
        bool wrongPrice = false;
        try { await PieceCakeProductLookup.NavigateAndReadAsync(page, new Uri(url), "PD2637", new PieceCakeLoginProbe()); }
        catch (InvalidDataException) { wrongPrice = true; }
        Require(wrongPrice, "Server and visible price mismatch is rejected");
        detailPrice = 11100;
        var target = new SiteCartTarget(27, "PD2637", url, parsed.Name, 2, parsed.Price, "BOX:30");
        Require(PieceCakeSiteCart.Matches([new("PD2637", 2, "BOX:30", parsed.Name, 11100)], [target]) &&
            !PieceCakeSiteCart.Matches([new("PD2637", 2, "BOX:12", parsed.Name, 11100)], [target]) &&
            !PieceCakeSiteCart.Matches([new("PD2637", 3, "BOX:30", parsed.Name, 11100)], [target]),
            "PieceCake cart matching requires code, purchase unit, quantity and price");
        html = Fixture("11,100", true);
        await page.GotoAsync(url);
        Require(!(await PieceCakeProductLookup.ReadPageAsync(page, "PD2637")).Available,
            "PieceCake sold-out overlay and missing cart control recognized");
        foreach (string bad in new[] { Fixture("가격문의", false), Fixture("11,100", false, false) })
        {
            html = bad;
            await page.GotoAsync(url);
            bool rejected = false;
            try { await PieceCakeProductLookup.ReadPageAsync(page, "PD2637"); }
            catch (Exception ex) when (ex is InvalidDataException or TimeoutException or PlaywrightException)
            { rejected = true; }
            Require(rejected, "Unverified PieceCake price or image never becomes a product");
        }
        const string cartHeader = "<html><body><a href='/logout'>로그아웃</a>" +
            "<a href='/affiliate/join_change'>정보수정</a><table><tbody id='prodTbody'>";
        const string cartTail = "</tbody></table><script>window.jQuery=row=>({data:key=>row.dataset.code});" +
            "fetch('/selectList',{method:'POST',body:'getProdCartList'});</script></body></html>";
        cartHtml = cartHeader + cartTail;
        var cartPage = await PieceCakeSiteCart.ReadAsync(page, new PieceCakeLoginProbe());
        Require(cartPage.Count == 0, "Empty PieceCake cart is accepted only after real list response");
        cartItems = [new { prod_no = "PD2637", sale_qty = 2 }];
        cartHtml = cartHeader + "<tr data-code='PD2637'><td class='proName'>" +
            "<strong class='_prodKnm'>미니도넛(바바리안)</strong><div class='_unitPrice'>11,100원 / BOX(30)개입</div></td>" +
            "<td><input name='volum' value='2'></td></tr>" + cartTail;
        cartPage = await PieceCakeSiteCart.ReadAsync(page, new PieceCakeLoginProbe());
        Require(PieceCakeSiteCart.Matches(cartPage, [target]),
            "PieceCake cart response and visible row agree on code, quantity, unit and price");
    }

    private static async Task CheckPieceDraft(MainForm main)
    {
        var data = Data(main);
        var view = All(main).OfType<ProductsView>().Single();
        var grid = Find<ProductGrid>(main, "ProductList");
        const string url = "https://www.piececake.co.kr/product/product_view?prodNo=PD2637";
        using var bitmap = new Bitmap(40, 30);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.CornflowerBlue);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        int lookupCount = 0;
        view.PieceLookup = requested =>
        {
            lookupCount++;
            Require(requested == url, "PieceCake draft keeps exact input URL for validated lookup");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success,
                new MegaCoffeeProductSnapshot("미니도넛(바바리안) (BOX 30개입)", 11100, "11,100원", url,
                    "https://www.piececake.co.kr/prodImg?p=mini_doughnut-02.jpg", stream.ToArray(), true)));
        };
        Find<Button>(main, "AddProduct").PerformClick();
        var draft = grid.Items[0];
        Find<TextBox>(draft, "DraftUrl").Text = url;
        draft.RegisterDraft();
        for (int i = 0; i < 100 && draft.IsDraft; i++) { await Task.Delay(30); Pump(); }
        Require(!draft.IsDraft && draft.Product is { Supplier.Id: "piece", Price: 11100 } &&
            lookupCount == 1 && File.Exists(draft.Product.ImageCachePath) && grid.Items[0] == draft,
            "PieceCake draft registers verified product in place with saved image");
        var product = draft.Product!;
        data.AddToCart(product);
        Require(data.Cart.Any(line => line.Product.Id == product.Id) &&
            Find<PictureBox>(main, $"CartImage_{product.Id}").Image != null,
            "PieceCake cart uses saved image without another web lookup");
        var restarted = new SampleData(new LocalState(data.Store.DirectoryPath));
        Require(restarted.Products.Single(saved => saved.Id == product.Id).ImageCachePath == product.ImageCachePath &&
            restarted.Cart.Single(line => line.Product.Id == product.Id).Quantity == 1,
            "PieceCake product and cart survive SQLite restart");
    }
}
