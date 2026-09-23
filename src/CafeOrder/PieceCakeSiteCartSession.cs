using Microsoft.Playwright;

namespace CafeOrder;

internal sealed partial class SupplierSessionManager
{
    internal Task<SiteCartPreparationResult> PreparePieceSiteCartAsync(SiteCartAttempt attempt,
        CatalogDatabase database)
    {
        if (attempt.SupplierId != "piece") throw new ArgumentException("파미유 주문 대상이 아닙니다.");
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SupplierSessionManager));
            var slot = slots["piece"];
            var prior = slot.Running;
            var task = PreparePieceAfterAsync(prior, slot, attempt, database);
            slot.Running = task;
            return task;
        }
    }

    private async Task<SiteCartPreparationResult> PreparePieceAfterAsync(Task? prior, SessionSlot slot,
        SiteCartAttempt attempt, CatalogDatabase database)
    {
        SiteCartPreparationResult result;
        try
        {
            if (prior != null) await prior;
            string profile = ProfilePath("piece");
            Directory.CreateDirectory(profile);
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
            slot.Context = context;
            var login = new PieceCakeLoginProbe();
            var vault = new BrowserCookieVault(profile, "piece");
            try
            {
                await vault.RestoreAsync(context);
                var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync(login.HomeUrl, new()
                { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                var state = await login.CheckAsync(page);
                if (state != SupplierLoginState.LoggedIn)
                {
                    if (state == SupplierLoginState.LoginRequired) vault.Clear();
                    Publish("piece", state);
                    result = new("FAILED", state == SupplierLoginState.LoginRequired ? "LOGIN_REQUIRED" : "LOGIN_UNVERIFIED");
                }
                else
                {
                    result = await PieceCakeSiteCart.PrepareAsync(page, attempt, login);
                    if (result.Reason.Contains("LOGIN_REQUIRED", StringComparison.Ordinal))
                    { vault.Clear(); Publish("piece", SupplierLoginState.LoginRequired); }
                    else
                    { await vault.SaveAsync(context, login.HomeUrl); Publish("piece", SupplierLoginState.LoggedIn); }
                }
            }
            finally
            {
                try { await context.CloseAsync(); } catch (PlaywrightException) { }
                slot.Context = null;
            }
        }
        catch (MegaCoffeeLoginRequiredException)
        { Publish("piece", SupplierLoginState.LoginRequired); result = new("FAILED", "LOGIN_REQUIRED"); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Write(LogLevel.ERROR, "SITE_CART_PREPARE_EXCEPTION", "파미유 사이트 장바구니 준비를 완료하지 못했습니다.",
                supplier: "piece", result: "UNKNOWN", error: ex);
            result = new("UNKNOWN", ex.GetType().Name);
        }
        database.SetSiteCartAttemptState(attempt.AttemptId, result.State, result.Reason);
        log?.Write(result.State == "READY" ? LogLevel.INFO : LogLevel.WARN,
            result.State == "READY" ? "SITE_CART_VERIFIED" : "SITE_CART_PREPARE_STOPPED",
            result.State == "READY" ? "파미유 사이트 장바구니 상품·구매 단위·수량·가격을 검증했습니다." :
                "파미유 사이트 장바구니 준비를 중단했습니다.", supplier: "piece",
            result: result.State, reason: result.Reason);
        return result;
    }
}
