using CafeOrder;
using System.Diagnostics;
using System.Drawing.Imaging;

internal static partial class Program
{
    private const string NaverOne = "https://brand.naver.com/macrocom/products/12639611652";
    private const string NaverTwo = "https://brand.naver.com/otokimall/products/11668648365";
    private const string CoupangShort = "https://mc.coupang.com/ssr/sdp/link?vendorItemId=86533357952";
    private const string CoupangDirect = "https://www.coupang.com/vp/products/6718538845?vendorItemId=88353437196";

    private static async Task CheckManualStoreAsync()
    {
        Require(ProductUrlIdentity.Key("naver", NaverOne + "?NaPm=abc") == ProductUrlIdentity.Key("naver", NaverOne) &&
            ProductUrlIdentity.Key("naver", NaverOne) != ProductUrlIdentity.Key("naver", NaverTwo) &&
            ProductUrlIdentity.Key("coupang", CoupangShort) == ProductUrlIdentity.Key("coupang",
                "https://www.coupang.com/vp/products/106013?vendorItemId=86533357952&tracking=x") &&
            ProductUrlIdentity.Key("coupang", CoupangShort) != ProductUrlIdentity.Key("coupang", CoupangDirect),
            "Manual store identity uses store/product or vendor item, ignoring tracking");
        Require(ProductUrlIdentity.CoupangProductConflict(
                "https://www.coupang.com/vp/products/100?vendorItemId=86533357952", null,
                "https://www.coupang.com/vp/products/200?vendorItemId=86533357952"),
            "The same vendor item cannot silently merge different Coupang product IDs");
        string dir = CheckDirectory("manual-store");
        var data = new SampleData(new LocalState(dir));
        foreach (string url in new[] { NaverOne, CoupangShort, "https://link.coupang.com/a/unknown" })
        {
            try { data.RegisterMock(url, "기타"); throw new Exception("Real manual URL copied mock data"); }
            catch (ArgumentException) { }
        }
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Green);
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png);
        byte[] bytes = stream.ToArray();
        string existingImage = data.Store.NewManualImagePath(40);
        ManualImages.Save(bytes, existingImage);
        var legacy = new Product(40, "샘플 상품명", 777, "", data.Suppliers.Single(s => s.Id == "naver"), "일회용품", true, 0)
            { Url = NaverOne, ManualImagePath = existingImage, DataOrigin = "UserMock" };
        data.Store.Database.SaveProduct(legacy); data.Products.Add(legacy); data.AddToCart(legacy);
        string web = data.Store.NewWebImagePath(); ManualImages.Save(bytes, web);
        var restored = data.SaveManualStoreProduct(NaverOne, "일회용품", new("실제 확인한 상품", 12900, null),
            new("실제 확인한 상품", 12900, "12,900원", NaverOne, "https://shop-phinf.pstatic.net/sample.jpg", bytes, true),
            web, null, legacy);
        Require(restored.Id == 40 && restored.Name == "실제 확인한 상품" && restored.Price == 12900 &&
            restored.Category == "일회용품" && restored.ManualImagePath == existingImage &&
            data.Cart.Single().Product.Id == 40 && data.FindManualStoreProduct(NaverOne + "?NaPm=tracking")?.Id == 40,
            "Legacy dummy is corrected in place without losing category, manual image or cart");
        var restarted = new SampleData(new LocalState(dir));
        Require(restarted.Products.Single(p => p.Id == 40).Name == "실제 확인한 상품" &&
            restarted.Products.Single(p => p.Id == 40).ImageCachePath == web && restarted.Cart.Single().Product.Id == 40,
            "Manual store edit and image survive restart");
        var coupangLegacy = new Product(41, "샘플 쿠팡 상품", 999, "", data.Suppliers.Single(s => s.Id == "coupang"), "기타", true, 0)
            { Url = CoupangShort, DataOrigin = "UserMock" };
        data.Store.Database.SaveProduct(coupangLegacy); data.Products.Add(coupangLegacy);
        string chosen = data.Store.NewManualImagePath(41); ManualImages.Save(bytes, chosen);
        data.SaveManualStoreProduct(CoupangShort, "기타", new("사용자가 확인한 쿠팡 상품", 5600, chosen),
            null, null, chosen, coupangLegacy,
            "https://www.coupang.com/vp/products/106013?vendorItemId=86533357952");
        Require(coupangLegacy.Id == 41 && coupangLegacy.Url == CoupangShort &&
            coupangLegacy.ResolvedProductUrl?.Contains("/106013?") == true &&
            coupangLegacy.ManualImagePath == chosen && coupangLegacy.Name != "샘플 쿠팡 상품",
            "Coupang manual correction preserves original short URL, resolved product ID and ProductId");
        var launch = SellerLinks.Launch;
        var opened = new List<ProcessStartInfo>();
        try
        {
            SellerLinks.Launch = opened.Add;
            Require(SellerLinks.OpenManualProduct(legacy) && SellerLinks.OpenManualProduct(coupangLegacy) &&
                opened.Select(info => info.FileName).SequenceEqual([NaverOne, CoupangShort]) &&
                opened.All(info => info.UseShellExecute),
                "Manual order opens each original URL through the Windows default browser");
        }
        finally { SellerLinks.Launch = launch; }

        int calls = 0;
        Task<MegaProductLookupResult> Lookup(string url)
        {
            calls++;
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Success,
                new MegaCoffeeProductSnapshot("확인된 " + calls, 1500 + calls, $"{1500 + calls:N0}원", url,
                    "https://shop-phinf.pstatic.net/sample.jpg", bytes, true)));
        }
        Task<MegaProductLookupResult> NoOther(string _) => throw new Exception("Wrong supplier lookup selected");
        string workbook = MakeWorkbook(dir, "manual-links", sheet =>
        {
            sheet.Cell(2, 7).Value = NaverTwo;
            sheet.Cell(3, 7).Value = CoupangDirect;
            sheet.Cell(4, 7).Value = NaverTwo + "?NaPm=tracking";
        });
        using (var plan = await data.PrepareImportAsync(workbook, NoOther, null, CancellationToken.None,
            NoOther, NoOther, Lookup))
        {
            Require(plan.Issues.Count == 0 && plan.Added == 2 && plan.Skipped == 1 && calls == 2 &&
                plan.Changes.All(c => File.Exists(c.PendingImagePath)),
                "Naver/Coupang URL-only rows use actual lookup path and skip tracking duplicate");
            data.ApplyImport(plan);
        }
        Require(data.ExportRows().Any(p => p.Name == "확인된 1" && p.Price == 1501) &&
            data.Store.Database.ReadProducts(data.Suppliers).Count == 4,
            "XLSX export uses saved lookup name and price, not samples");
        string update = MakeWorkbook(dir, "manual-update", sheet =>
        { sheet.Cell(2, 1).Value = 40; sheet.Cell(2, 7).Value = NaverOne; });
        using (var plan = await data.PrepareImportAsync(update, NoOther, null, CancellationToken.None,
            NoOther, NoOther, Lookup))
        {
            Require(plan.Updated == 1 && plan.Issues.Count == 0 && plan.Changes.Single().Proposed.ManualImagePath == existingImage &&
                plan.Changes.Single().Proposed.Category == "일회용품",
                "Naver ProductId URL-only update preserves manual image and category");
            data.ApplyImport(plan);
        }
        Require(data.Products.Single(p => p.Id == 40).Id == 40 && data.Cart.Single().Product.Id == 40,
            "XLSX manual-store update keeps ProductId and cart binding");
        int count = data.Store.Database.ReadProducts(data.Suppliers).Count;
        string bad = MakeWorkbook(dir, "manual-failed", sheet =>
        { sheet.Cell(2, 7).Value = "https://mc.coupang.com/ssr/sdp/link?vendorItemId=86533357953"; });
        using (var plan = await data.PrepareImportAsync(bad, NoOther, null, CancellationToken.None,
            NoOther, NoOther, _ => Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed,
                Reason: "HTTP 403 접근 제한"))))
        {
            Require(plan.Issues.Single().Reason.Contains("403") && plan.Changes.Count == 0 &&
                data.Store.Database.ReadProducts(data.Suppliers).Count == count,
                "Blocked URL reports row reason and never creates a dummy or partial DB change");
        }
        string conflict = MakeWorkbook(dir, "manual-conflict", sheet =>
        {
            sheet.Cell(2, 7).Value = "https://www.coupang.com/vp/products/100?vendorItemId=86533357955";
            sheet.Cell(3, 7).Value = "https://www.coupang.com/vp/products/200?vendorItemId=86533357955";
        });
        using (var plan = await data.PrepareImportAsync(conflict, NoOther, null, CancellationToken.None,
            NoOther, NoOther, _ => throw new Exception("Conflicting products must not be looked up")))
        {
            Require(plan.Issues.Any(issue => issue.Reason.Contains("다른 상품 ID")) && plan.Changes.Count == 0 &&
                data.Store.Database.ReadProducts(data.Suppliers).Count == count,
                "Conflicting Coupang product IDs abort before lookup or DB changes");
        }
        // Construct the WinForms dialog only after awaited import work to avoid capturing
        // a UI synchronization context without a message loop in this command-line check.
        var originalSample = new Product(41, "아이스컵 20온스 1,000개", 67000, "", coupangLegacy.Supplier, "기타", true, 0)
            { Url = CoupangShort, DataOrigin = "UserMock" };
        using (var fallback = new ManualStoreProductDialog("쿠팡", CoupangShort, originalSample, null, "HTTP 403"))
            Require(All(fallback).OfType<TextBox>().Single(box => box.MaxLength == 500).Text == "" &&
                All(fallback).OfType<TextBox>().Single(box => box.MaxLength == 24).Text == "",
                "Unverified legacy product dialog never pre-fills copied sample name or price");
        var userEdited = originalSample with { Name = "사용자가 고친 상품명" };
        using (var fallback = new ManualStoreProductDialog("쿠팡", CoupangShort, userEdited, null, "HTTP 403"))
            Require(All(fallback).OfType<TextBox>().Single(box => box.MaxLength == 500).Text == userEdited.Name,
                "Lookup failure does not erase a user's existing manual edit");
        results.Add("Manual stores: URL identity, no mock registration, legacy ID repair, XLSX lookup/skip/failure PASS");
    }

    private static async Task CheckManualStoreLiveAsync()
    {
        foreach (var (url, expectedPrice) in new[] { (NaverOne, 12900m), (NaverTwo, 16900m) })
        {
            var result = await ManualStoreProductLookup.LookupAsync(url);
            Require(result.Status == MegaProductLookupStatus.Success && result.Product is { ImageBytes.Length: > 100 } &&
                result.Product.Price == expectedPrice && result.Product.Name.Length > 5 && result.Product.ProductUrl == url,
                "Naver displayed price/name/image verified for " + new Uri(url).AbsolutePath);
            Console.WriteLine($"NAVER SUCCESS price={result.Product!.Price} imageBytes={result.Product.ImageBytes.Length}");
        }
        foreach (string url in new[] { CoupangShort, CoupangDirect })
        {
            var result = await ManualStoreProductLookup.LookupAsync(url);
            Require(result.Status != MegaProductLookupStatus.Success || result.Product is { ImageBytes.Length: > 100, Price: > 0 },
                "Coupang never reports success without real product data");
            Console.WriteLine($"COUPANG {result.Status} reason={result.Reason} resolvedProductId={result.ResolvedProductUrl?.Split('/').Last().Split('?')[0] ?? "unknown"}");
        }
    }

}
