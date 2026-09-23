using CafeOrder;
using Microsoft.Playwright;

internal static class MegaSiteCartReadOnlyChecks
{
    internal static async Task RunAsync()
    {
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("mega");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
        try
        {
            await new BrowserCookieVault(profile, "mega").RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            var login = new MegaCoffeeLoginProbe();
            await page.GotoAsync(login.HomeUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("MegaCoffee login is not active");
            var items = await MegaCoffeeSiteCart.ReadAsync(page, login);
            if (items.Any(item => item.Quantity < 1 || string.IsNullOrWhiteSpace(item.ExternalProductId)))
                throw new InvalidDataException("Site cart identity or quantity missing");
            Console.WriteLine("Current site cart: " + string.Join(", ",
                items.Select(item => item.ExternalProductId + "×" + item.Quantity)));
            var clear = page.Locator("#frmCart button").Filter(new() { HasText = "장바구니 비우기" });
            bool hiddenFrame = await page.Locator("iframe[name='ifrmProcess']").CountAsync() == 1;
            bool confirmThenFrame = await page.EvaluateAsync<bool>("""
                () => { const source=String(window.gd_cart_remove || '');
                  return source.includes('confirm("장바구니를 비우시겠습니까?")') &&
                    source.includes("ifrmProcess.location.replace('./cart_ps.php?mode=remove')"); }
                """);
            if (await clear.CountAsync() != 1 || !hiddenFrame || !confirmThenFrame)
                throw new InvalidDataException("Site clear dialog or hidden-frame structure changed");
            // Invoke only the browser confirm API, not gd_cart_remove or the clear button.
            // This proves Playwright's Dialog event and acceptance without a site-cart write.
            using (var dialogs = new MegaCartDialogMonitor(page))
            {
                bool accepted = await page.EvaluateAsync<bool>(
                    "() => window.confirm('장바구니를 비우시겠습니까?')");
                var observed = await dialogs.First.WaitAsync(TimeSpan.FromSeconds(5));
                if (!accepted || observed.Type != "confirm" ||
                    observed.Message != MegaCartDialogMonitor.ClearMessage || !observed.Accepted)
                    throw new InvalidDataException("Playwright confirmation dialog event failed");
                Console.WriteLine("Synthetic confirm event: type=" + observed.Type +
                    " message=" + observed.Message + " accepted=" + observed.Accepted);
            }
            var unrelated = await context.NewPageAsync();
            try
            {
                await unrelated.SetContentAsync("<title>Dialog guard test</title>");
                using var dialogs = new MegaCartDialogMonitor(unrelated);
                await unrelated.EvaluateAsync("() => window.alert('unrelated')");
                var observed = await dialogs.First.WaitAsync(TimeSpan.FromSeconds(5));
                if (observed.Type != "alert" || observed.Accepted || !observed.Handled || !dialogs.HasUnexpected)
                    throw new InvalidDataException("Unrelated dialog was not rejected");
                Console.WriteLine("Synthetic unrelated alert: accepted=False stopped=True");
            }
            finally { await unrelated.CloseAsync(); }
            if (!SameCart(await MegaCoffeeSiteCart.ReadAsync(page, login), items))
                throw new InvalidDataException("Read-only dialog probe changed the site cart");
            Console.WriteLine("Site clear structure: hiddenFrame=" + hiddenFrame +
                " confirmBeforeFrame=" + confirmThenFrame + "; cart unchanged=True");
            Console.WriteLine("PASS: authenticated site cart read and dialog/iframe inspection; no site-cart write");
        }
        finally { await context.CloseAsync(); }
        var browser = await playwright.Chromium.LaunchAsync(new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true });
        try
        {
            var guest = await browser.NewPageAsync();
            var guestResponse = await guest.GotoAsync("https://www.megacoffee.co.kr/order/cart.php",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            int formCount = await guest.Locator("#frmCart").CountAsync();
            string emptyText = await guest.Locator("#frmCart").InnerTextAsync();
            bool empty = formCount == 1 &&
                (emptyText.Contains("장바구니에 담겨있는 상품이 없습니다.", StringComparison.Ordinal) ||
                    emptyText.Contains("장바구니에 담긴 상품이 없습니다.", StringComparison.Ordinal));
            Console.WriteLine("Guest empty-cart structure=" + empty + " status=" + guestResponse?.Status +
                " path=" + new Uri(guest.Url).AbsolutePath + " form=" + formCount);
            if (!empty) throw new InvalidDataException("Empty cart structure differs; remote mutation remains blocked");
        }
        finally { await browser.CloseAsync(); }
    }

    private static bool SameCart(IReadOnlyList<SiteCartEntry> a, IReadOnlyList<SiteCartEntry> b) =>
        a.Count == b.Count && a.All(item => b.Any(other =>
            item.ExternalProductId == other.ExternalProductId && item.OptionKey == other.OptionKey &&
            item.Quantity == other.Quantity));
}
