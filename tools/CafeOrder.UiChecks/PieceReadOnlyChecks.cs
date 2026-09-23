using CafeOrder;
using Microsoft.Playwright;

internal static class PieceReadOnlyChecks
{
    internal static async Task RunAsync()
    {
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("piece");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
        var vault = new BrowserCookieVault(profile, "piece");
        try
        {
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            var login = new PieceCakeLoginProbe();
            await page.GotoAsync(login.HomeUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("파미유 로그인 세션이 유지되지 않았습니다.");
            const string url = "https://www.piececake.co.kr/product/product_view?prodNo=PD2637";
            var product = await PieceCakeProductLookup.FetchAsync(page, context, new Uri(url), "PD2637", login);
            if (product.Name.Length == 0 || product.Price <= 0 || product.ImageBytes.Length == 0)
                throw new InvalidDataException("파미유 상품 조회 결과가 비어 있습니다.");
            var cart = await PieceCakeSiteCart.ReadAsync(page, login);
            foreach (var item in cart)
            {
                string itemUrl = "https://www.piececake.co.kr/product/product_view?prodNo=" + item.ExternalProductId;
                var existingProduct = await PieceCakeProductLookup.NavigateAndReadAsync(page,
                    new Uri(itemUrl), item.ExternalProductId, login);
                if (!existingProduct.Available || existingProduct.Price != item.UnitPrice ||
                    PieceCakeSiteCart.PurchaseKey(existingProduct.Name) != item.OptionKey ||
                    existingProduct.InitialQuantity != 1 || !existingProduct.CartConfigured)
                    throw new InvalidDataException("기존 파미유 장바구니 상품을 원래대로 복원할 조건을 확인하지 못했습니다.");
            }
            Console.WriteLine("PASS: PieceCake existing cart products remain available at matching price and purchase unit");
            if (!Same(cart, await PieceCakeSiteCart.ReadAsync(page, login)))
                throw new InvalidDataException("파미유 읽기 검사 동안 사이트 장바구니가 변경됐습니다.");
            await vault.SaveAsync(context, login.HomeUrl);
            Console.WriteLine($"PASS: PieceCake product {product.Name} · {product.DisplayPrice} · image {product.ImageBytes.Length} bytes · available={product.Available}");
            Console.WriteLine("PASS: PieceCake site cart read-only " +
                string.Join(", ", cart.Select(item => $"{item.ExternalProductId}×{item.Quantity}")));
        }
        finally { await context.CloseAsync(); }
    }

    private static bool Same(IReadOnlyList<SiteCartEntry> first, IReadOnlyList<SiteCartEntry> second) =>
        first.Count == second.Count && first.All(item => second.Any(other =>
            other.ExternalProductId == item.ExternalProductId && other.Quantity == item.Quantity &&
            other.OptionKey == item.OptionKey && other.UnitPrice == item.UnitPrice));
}
