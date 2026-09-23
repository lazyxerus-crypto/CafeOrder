using CafeOrder;
using Microsoft.Playwright;

internal static class PieceSiteCartLiveChecks
{
    internal static async Task RunAsync()
    {
        // Explicit live test only; never part of the default regression run.
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("piece");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
        var vault = new BrowserCookieVault(profile, "piece");
        IPage? page = null;
        SampleData? testData = null;
        SiteCartAttempt? attempt = null;
        var login = new PieceCakeLoginProbe();
        try
        {
            await vault.RestoreAsync(context);
            page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync(login.HomeUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("파미유 로그인이 필요합니다.");
            var before = await PieceCakeSiteCart.ReadAsync(page, login);
            Console.WriteLine("PieceCake before=" + Snapshot(before));
            const string code = "PD2637";
            string url = "https://www.piececake.co.kr/product/product_view?prodNo=" + code;
            var target = await PieceCakeProductLookup.NavigateAndReadAsync(page, new Uri(url), code, login);
            if (!target.Available || target.InitialQuantity != 1 || !target.CartConfigured ||
                PieceCakeSiteCart.PurchaseKey(target.Name) is not { } unit)
                throw new InvalidDataException("시험 상품의 구매 조건을 확인할 수 없습니다.");

            // Persist the exact local order target before any site cart mutation.
            string directory = Path.Combine(Path.GetTempPath(), "CafeOrderChecks", "piece-live-" + Guid.NewGuid().ToString("N"));
            testData = new SampleData(new LocalState(directory));
            var seller = testData.Suppliers.Single(item => item.Id == "piece");
            var stored = testData.Store.Database.InsertProduct(new Product(0, target.Name, target.Price, "",
                seller, "디저트/스낵", true, 0)
            { Url = url, DisplayPrice = target.DisplayPrice, DataOrigin = "UserMock" });
            testData.Products.Add(stored);
            testData.AddToCart(stored);
            testData.AddToCart(stored);
            attempt = testData.Store.Database.CreateSiteCartAttempt("piece",
                [new SiteCartTarget(stored.Id, code, url, target.Name, 2, target.Price, unit)]);
            var prepared = await PieceCakeSiteCart.PrepareAsync(page, attempt, login);
            testData.Store.Database.SetSiteCartAttemptState(attempt.AttemptId, prepared.State, prepared.Reason);
            Console.WriteLine("PieceCake preparation=" + prepared.State + " reason=" + prepared.Reason);
            var after = await PieceCakeSiteCart.ReadAsync(page, login);
            Console.WriteLine("PieceCake after=" + Snapshot(after));
            if (prepared.State != "READY" || !PieceCakeSiteCart.Matches(after, attempt.Targets))
                throw new InvalidDataException("사이트 장바구니 대상 검증에 실패했습니다. 추가 조작을 중단합니다.");
            await vault.SaveAsync(context, login.HomeUrl);
            Console.WriteLine("PASS: PieceCake site cart contains PD2637×2 BOX; no order/payment action");
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
                try { Console.WriteLine("PieceCake current cart after stop=" +
                    Snapshot(await PieceCakeSiteCart.ReadAsync(page, login))); }
                catch { Console.WriteLine("PieceCake current cart after stop=UNVERIFIED"); }
            }
            throw;
        }
        finally { await context.CloseAsync(); }
    }

    private static string Snapshot(IReadOnlyList<SiteCartEntry> items) =>
        string.Join(",", items.OrderBy(item => item.ExternalProductId)
            .Select(item => $"{item.ExternalProductId}:{item.Quantity}:{item.OptionKey}:{item.UnitPrice}"));
}
