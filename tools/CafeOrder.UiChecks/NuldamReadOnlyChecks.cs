using CafeOrder;
using Microsoft.Playwright;

internal static class NuldamReadOnlyChecks
{
    internal static async Task RunAsync()
    {
        const string url = "https://nuldampartners.com/product/detail.html?product_no=250&cate_no=1&display_group=9";
        Require(NuldamProductLookup.TryProductUrl(url, out _, out var productNumber) && productNumber == "250" &&
            ProductUrlIdentity.Key("nuldam", url) == ProductUrlIdentity.Key("nuldam",
                "https://nuldampartners.com/product/detail.html?cate_no=9&product_no=0250&display_group=2"),
            "Nuldam product_no stays stable across category/display variants");
        foreach (string invalid in new[]
        {
            "http://nuldampartners.com/product/detail.html?product_no=250",
            "https://other-nuldampartners.com/product/detail.html?product_no=250",
            "https://nuldampartners.com/product/detail.html?product_no=250&product_no=251",
            "https://nuldampartners.com/product/detail.html?product_no=0"
        }) Require(!NuldamProductLookup.TryProductUrl(invalid, out _, out _), "Invalid Nuldam URL rejected");
        await using var manager = new SupplierSessionManager();
        await manager.CheckAsync("nuldam");
        var result = await manager.LookupNuldamProductAsync(url);
        Require(result.Status == MegaProductLookupStatus.Success && result.Product is { } product &&
            product.Name == "(널담)뚱낭시에-솔티드바닐라(15개입)" && product.Price == 24000 &&
            product.DisplayPrice == "24,000원" && product.Available && product.ImageBytes.Length > 100 &&
            NuldamProductLookup.PackageKey(product.Name) == "PACK:15",
            "Nuldam logged-in product 250 name, price, pack label, image and stock verified");
        Console.WriteLine("PASS: Nuldam login profile reused; product_no=250 24,000원 15개입 available; image=" +
            result.Product!.ImageBytes.Length + " bytes");
        var simple = await manager.LookupNuldamProductAsync(
            "https://nuldampartners.com/product/detail.html?product_no=250");
        Require(simple.Status == MegaProductLookupStatus.Success && simple.Product?.Price == 24000,
            "Nuldam product_no alone opens the same verified product");
        string profile = manager.ProfilePath("nuldam");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
        try
        {
            var vault = new BrowserCookieVault(profile, "nuldam");
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            var cart = await NuldamSiteCart.ReadAsync(page, new NuldamLoginProbe());
            Console.WriteLine("PASS: Nuldam cart read-only " +
                string.Join(",", cart.Select(item => $"{item.ExternalProductId}×{item.Quantity}:{item.OptionKey}:{item.UnitPrice}")));
            await vault.SaveAsync(context, "https://nuldampartners.com/");
        }
        finally { await context.CloseAsync(); }
    }

    private static void Require(bool value, string description)
    {
        if (!value) throw new InvalidDataException(description);
    }
}
