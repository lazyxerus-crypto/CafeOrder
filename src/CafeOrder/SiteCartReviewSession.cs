using Microsoft.Playwright;

namespace CafeOrder;

internal sealed record CheckoutOpenResult(string State, string Reason);

internal sealed partial class SupplierSessionManager
{
    private static readonly Dictionary<string, string> CheckoutUrls = new()
    {
        ["mega"] = "https://www.megacoffee.co.kr/order/cart.php",
        // The supplied /affiliate/prorder path returned HTTP 500; the authenticated cart is /affiliate/prdorder.
        ["piece"] = "https://www.piececake.co.kr/affiliate/prdorder",
        ["nuldam"] = "https://nuldampartners.com/order/basket.html"
    };

    internal Task<SiteCartPreparationResult> VerifySiteCartAsync(string supplierId,
        IReadOnlyList<SiteCartTarget> targets)
    {
        if (!CheckoutUrls.ContainsKey(supplierId)) throw new ArgumentException("사이트 장바구니가 연결되지 않았습니다.");
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SupplierSessionManager));
            var slot = slots[supplierId];
            var prior = slot.Running;
            var task = VerifyAfterAsync(prior, slot, supplierId, targets);
            slot.Running = task;
            return task;
        }
    }

    internal Task CloseCheckoutForNewOrderAsync(string supplierId)
    {
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SupplierSessionManager));
            var slot = slots[supplierId];
            var task = CloseCheckoutAfterAsync(slot.Running, slot);
            slot.Running = task;
            return task;
        }
    }

    private static async Task CloseCheckoutAfterAsync(Task? prior, SessionSlot slot)
    {
        if (prior != null) await prior;
        if (slot.CheckoutContext != null)
        {
            try { await slot.CheckoutContext.CloseAsync(); } catch (PlaywrightException) { }
            slot.CheckoutDriver?.Dispose();
            slot.CheckoutContext = null; slot.CheckoutDriver = null;
            slot.CheckoutPage = null; slot.CheckoutNeedsCart = false;
        }
    }

    private async Task<SiteCartPreparationResult> VerifyAfterAsync(Task? prior, SessionSlot slot,
        string supplierId, IReadOnlyList<SiteCartTarget> targets)
    {
        try
        {
            if (prior != null) await prior;
            if (slot.CheckoutContext != null)
            {
                IPage? page = null;
                try { page = await slot.CheckoutContext.NewPageAsync(); }
                catch (PlaywrightException) { await CloseCheckoutAfterAsync(null, slot); }
                if (page != null)
                {
                    try { return await VerifyWithContextAsync(slot.CheckoutContext!, page, supplierId, targets); }
                    finally { try { await page.CloseAsync(); } catch (PlaywrightException) { } }
                }
            }
            string profile = ProfilePath(supplierId);
            Directory.CreateDirectory(profile);
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
            slot.Context = context;
            try
            {
                var vault = new BrowserCookieVault(profile, supplierId);
                await vault.RestoreAsync(context);
                var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                return await VerifyWithContextAsync(context, page, supplierId, targets);
            }
            finally
            {
                try { await context.CloseAsync(); } catch (PlaywrightException) { }
                slot.Context = null;
            }
        }
        catch (MegaCoffeeLoginRequiredException)
        {
            new BrowserCookieVault(ProfilePath(supplierId), supplierId).Clear();
            Publish(supplierId, SupplierLoginState.LoginRequired);
            return new("WAITING_FOR_USER", "LOGIN_REQUIRED");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Write(LogLevel.ERROR, "SITE_CART_RECHECK_FAILED", "판매처 장바구니를 읽기 전용으로 확인하지 못했습니다.",
                supplier: supplierId, result: "UNKNOWN", error: ex);
            return new("UNKNOWN", "READ_FAILED_" + ex.GetType().Name);
        }
    }

    private async Task<SiteCartPreparationResult> VerifyWithContextAsync(IBrowserContext context,
        IPage page, string supplierId, IReadOnlyList<SiteCartTarget> targets)
    {
        var probe = probes[supplierId];
        var vault = new BrowserCookieVault(ProfilePath(supplierId), supplierId);
        await page.GotoAsync(probe.HomeUrl, new()
            { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        var state = await probe.CheckAsync(page);
        if (state != SupplierLoginState.LoggedIn)
        {
            if (state == SupplierLoginState.LoginRequired) vault.Clear();
            Publish(supplierId, state);
            return new("WAITING_FOR_USER", state == SupplierLoginState.LoginRequired ?
                "LOGIN_REQUIRED" : "LOGIN_UNVERIFIED");
        }
        IReadOnlyList<SiteCartEntry> current = supplierId switch
        {
            "mega" => await MegaCoffeeSiteCart.ReadAsync(page, new MegaCoffeeLoginProbe()),
            "piece" => await PieceCakeSiteCart.ReadAsync(page, new PieceCakeLoginProbe()),
            "nuldam" => await NuldamSiteCart.ReadAsync(page, new NuldamLoginProbe()),
            _ => throw new InvalidDataException("판매처 장바구니를 확인할 수 없습니다.")
        };
        await vault.SaveAsync(context, probe.HomeUrl);
        Publish(supplierId, SupplierLoginState.LoggedIn);
        bool matches = supplierId switch
        {
            "mega" => MegaCoffeeSiteCart.Matches(current, targets),
            "piece" => PieceCakeSiteCart.Matches(current, targets),
            "nuldam" => NuldamSiteCart.Matches(current, targets),
            _ => false
        };
        return matches ? new("READY", "ALREADY_MATCHED", current, "READ_ONLY_VERIFIED") :
            new("UNKNOWN", "SITE_CART_MISMATCH", current, "READ_ONLY_VERIFIED");
    }

    internal Task<CheckoutOpenResult> OpenSiteCartForUserAsync(string supplierId)
    {
        if (!CheckoutUrls.ContainsKey(supplierId)) throw new ArgumentException("사이트 장바구니가 연결되지 않았습니다.");
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SupplierSessionManager));
            var slot = slots[supplierId];
            var prior = slot.Running;
            var task = OpenCheckoutAfterAsync(prior, slot, supplierId);
            slot.Running = task;
            return task;
        }
    }

    private async Task<CheckoutOpenResult> OpenCheckoutAfterAsync(Task? prior, SessionSlot slot,
        string supplierId)
    {
        try
        {
            if (prior != null) await prior;
            var existing = slot.CheckoutContext?.Pages.LastOrDefault(page => !page.IsClosed);
            if (existing != null)
            {
                slot.CheckoutPage = existing;
                if (slot.CheckoutNeedsCart)
                {
                    var probe = probes[supplierId];
                    var loginState = await probe.CheckAsync(existing);
                    if (loginState != SupplierLoginState.LoggedIn)
                    {
                        if (loginState == SupplierLoginState.LoginRequired)
                            new BrowserCookieVault(ProfilePath(supplierId), supplierId).Clear();
                        Publish(supplierId, loginState);
                        if (loginState == SupplierLoginState.LoginRequired && existing.Url != probe.LoginUrl)
                            await existing.GotoAsync(probe.LoginUrl, new()
                                { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                        await existing.BringToFrontAsync();
                        return new("WAITING_FOR_USER", loginState == SupplierLoginState.LoginRequired ?
                            "LOGIN_REQUIRED" : "LOGIN_UNVERIFIED");
                    }
                    var cartResponse = await existing.GotoAsync(CheckoutUrls[supplierId], new()
                    { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                    if (cartResponse?.Status != 200 ||
                        new Uri(existing.Url).AbsolutePath != new Uri(CheckoutUrls[supplierId]).AbsolutePath)
                        return new("UNKNOWN", "CART_PAGE_UNAVAILABLE");
                    await new BrowserCookieVault(ProfilePath(supplierId), supplierId)
                        .SaveAsync(slot.CheckoutContext!, probe.HomeUrl);
                    Publish(supplierId, SupplierLoginState.LoggedIn);
                    slot.CheckoutNeedsCart = false;
                }
                await existing.BringToFrontAsync();
                return new("OPENED", "EXISTING_WINDOW");
            }
            if (slot.CheckoutContext != null)
            {
                try { await slot.CheckoutContext.CloseAsync(); } catch (PlaywrightException) { }
                slot.CheckoutDriver?.Dispose();
                slot.CheckoutContext = null; slot.CheckoutDriver = null; slot.CheckoutPage = null;
            }
            string profile = ProfilePath(supplierId);
            Directory.CreateDirectory(profile);
            var driver = await Playwright.CreateAsync();
            IBrowserContext context;
            try
            {
                context = await driver.Chromium.LaunchPersistentContextAsync(profile, new()
                { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
            }
            catch { driver.Dispose(); throw; }
            slot.CheckoutDriver = driver; slot.CheckoutContext = context;
            var vault = new BrowserCookieVault(profile, supplierId);
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            slot.CheckoutPage = page;
            var response = await page.GotoAsync(CheckoutUrls[supplierId], new()
            { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            var login = probes[supplierId];
            var state = await login.CheckAsync(page);
            if (state != SupplierLoginState.LoggedIn)
            {
                slot.CheckoutNeedsCart = true;
                if (state == SupplierLoginState.LoginRequired) vault.Clear();
                Publish(supplierId, state);
                if (state == SupplierLoginState.LoginRequired && page.Url != login.LoginUrl)
                    await page.GotoAsync(login.LoginUrl, new()
                        { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                await page.BringToFrontAsync();
                return new("WAITING_FOR_USER", state == SupplierLoginState.LoginRequired ?
                    "LOGIN_REQUIRED" : "LOGIN_UNVERIFIED");
            }
            if (response?.Status != 200 || new Uri(page.Url).AbsolutePath != new Uri(CheckoutUrls[supplierId]).AbsolutePath)
                return new("UNKNOWN", "CART_PAGE_UNAVAILABLE");
            await vault.SaveAsync(context, login.HomeUrl);
            Publish(supplierId, SupplierLoginState.LoggedIn);
            await page.BringToFrontAsync();
            return new("OPENED", "CART_WINDOW_VISIBLE");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Write(LogLevel.ERROR, "SITE_CART_WINDOW_FAILED", "판매처 전용 Edge 장바구니 창을 열지 못했습니다.",
                supplier: supplierId, result: "UNKNOWN", error: ex);
            return new("UNKNOWN", "BROWSER_OPEN_FAILED_" + ex.GetType().Name);
        }
    }
}
