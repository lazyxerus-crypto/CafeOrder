using CafeOrder;
using Microsoft.Playwright;

internal static class NuldamSiteCartLiveChecks
{
    internal static async Task RunAsync()
    {
        // Deliberate live test only; never part of the default regression suite.
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("nuldam");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
        var vault = new BrowserCookieVault(profile, "nuldam");
        IPage? page = null;
        SampleData? testData = null;
        SiteCartAttempt? attempt = null;
        var login = new NuldamLoginProbe();
        try
        {
            await vault.RestoreAsync(context);
            page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync(login.HomeUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("늘담 로그인이 필요합니다.");
            var before = await NuldamSiteCart.ReadAsync(page, login);
            Console.WriteLine("Nuldam before=" + Snapshot(before));
            const string code = "250";
            string url = "https://nuldampartners.com/product/detail.html?product_no=" + code;
            var target = await NuldamProductLookup.NavigateAndReadAsync(page, new Uri(url), code, login);
            if (!target.Available || target.InitialQuantity != 1 || target.HasSelectableOptions)
                throw new InvalidDataException("시험 상품의 구매 조건을 확인할 수 없습니다.");

            // The scratch SQLite order target is persisted before any remote cart write.
            string directory = Path.Combine(Path.GetTempPath(), "CafeOrderChecks", "nuldam-live-" + Guid.NewGuid().ToString("N"));
            testData = new SampleData(new LocalState(directory));
            var seller = testData.Suppliers.Single(item => item.Id == "nuldam");
            var stored = testData.Store.Database.InsertProduct(new Product(0, target.Name, target.Price, "",
                seller, "디저트/스낵", true, 0)
            { Url = url, DisplayPrice = target.DisplayPrice, DataOrigin = "UserMock" });
            testData.Products.Add(stored);
            testData.AddToCart(stored);
            testData.AddToCart(stored);
            attempt = testData.Store.Database.CreateSiteCartAttempt("nuldam",
                [new SiteCartTarget(stored.Id, code, url, target.Name, 2, target.Price, target.PackageKey)]);
            var prepared = await NuldamSiteCart.PrepareAsync(page, attempt, login);
            testData.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, prepared.State, prepared.Reason);
            Console.WriteLine("Nuldam preparation=" + prepared.State + " reason=" + prepared.Reason);
            var after = await NuldamSiteCart.ReadAsync(page, login);
            Console.WriteLine("Nuldam after=" + Snapshot(after));
            if (prepared.State != "READY" || !NuldamSiteCart.Matches(after, attempt.Targets))
                throw new InvalidDataException("사이트 장바구니 대상 검증에 실패했습니다. 추가 조작을 중단합니다.");
            await vault.SaveAsync(context, login.HomeUrl);
            Console.WriteLine("PASS: Nuldam site cart contains product_no 250×2 PACK:15; no order/payment action");
        }
        catch
        {
            if (attempt != null && testData != null)
            {
                try { testData.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, "UNKNOWN", "LIVE_TEST_STOPPED"); }
                catch { /* Keep the original failure and the saved preparation record. */ }
            }
            if (page != null)
            {
                try { Console.WriteLine("Nuldam current cart after stop=" +
                    Snapshot(await NuldamSiteCart.ReadAsync(page, login))); }
                catch { Console.WriteLine("Nuldam current cart after stop=UNVERIFIED"); }
            }
            throw;
        }
        finally { await context.CloseAsync(); }
    }

    private static string Snapshot(IReadOnlyList<SiteCartEntry> items) =>
        string.Join(",", items.OrderBy(item => item.ExternalProductId)
            .Select(item => $"{item.ExternalProductId}:{item.Quantity}:{item.OptionKey}:{item.UnitPrice}"));
}
