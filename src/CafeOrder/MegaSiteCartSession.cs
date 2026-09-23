using Microsoft.Playwright;

namespace CafeOrder;

internal sealed partial class SupplierSessionManager
{
    internal Task<SiteCartPreparationResult> PrepareMegaSiteCartAsync(SiteCartAttempt attempt,
        CatalogDatabase database, Func<IReadOnlyList<SiteCartEntry>, Task<bool>> confirmExisting)
    {
        if (attempt.SupplierId != "mega") throw new ArgumentException("메가커피 주문 대상이 아닙니다.");
        lock (sync)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SupplierSessionManager));
            var slot = slots["mega"];
            var prior = slot.Running;
            var task = PrepareMegaAfterAsync(prior, slot, attempt, database, confirmExisting);
            slot.Running = task;
            return task;
        }
    }

    private async Task<SiteCartPreparationResult> PrepareMegaAfterAsync(Task? prior, SessionSlot slot,
        SiteCartAttempt attempt, CatalogDatabase database,
        Func<IReadOnlyList<SiteCartEntry>, Task<bool>> confirmExisting)
    {
        SiteCartPreparationResult result;
        try
        {
            if (prior != null) await prior;
            string profile = ProfilePath("mega");
            Directory.CreateDirectory(profile);
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
            slot.Context = context;
            var login = new MegaCoffeeLoginProbe();
            var vault = new BrowserCookieVault(profile, "mega");
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
                    Publish("mega", state);
                    result = new("FAILED", state == SupplierLoginState.LoginRequired ? "LOGIN_REQUIRED" : "LOGIN_UNVERIFIED");
                }
                else
                {
                    result = await MegaCoffeeSiteCart.PrepareAsync(page, attempt, login, confirmExisting,
                        dialog => log?.Write(dialog.Accepted ? LogLevel.INFO : LogLevel.WARN,
                            "SITE_CART_DIALOG",
                            dialog.IsClearConfirmation
                                ? "메가커피 장바구니 비우기 확인창: 장바구니를 비우시겠습니까?"
                                : "예상하지 못한 브라우저 확인창이 표시되어 자동 진행을 중단했습니다.",
                            supplier: "mega", result: dialog.Accepted ? "ACCEPTED" : "NOT_ACCEPTED",
                            reason: dialog.Type.ToUpperInvariant()));
                    if (result.Reason.Contains("LOGIN_REQUIRED", StringComparison.Ordinal))
                    { vault.Clear(); Publish("mega", SupplierLoginState.LoginRequired); }
                    else
                    { await vault.SaveAsync(context, login.HomeUrl); Publish("mega", SupplierLoginState.LoggedIn); }
                }
            }
            finally
            {
                try { await context.CloseAsync(); } catch (PlaywrightException) { }
                slot.Context = null;
            }
        }
        catch (MegaCoffeeLoginRequiredException)
        { Publish("mega", SupplierLoginState.LoginRequired); result = new("FAILED", "LOGIN_REQUIRED"); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            log?.Write(LogLevel.ERROR, "SITE_CART_PREPARE_EXCEPTION", "메가커피 사이트 장바구니 준비를 완료하지 못했습니다.",
                supplier: "mega", result: "UNKNOWN", error: ex);
            result = new("UNKNOWN", ex.GetType().Name);
        }
        database.SetSiteCartAttemptState(attempt.AttemptId, result.State, result.Reason);
        if (result.Reason is "SITE_CART_ITEM_MISMATCH" or "SITE_CART_MISMATCH")
        {
            static string Item(string code, int quantity, string option) =>
                code + "×" + quantity + (option.Length == 0 ? "" : " [" + option + "]");
            string expected = string.Join(",", attempt.Targets.Select(item =>
                Item(item.ExternalProductId, item.Quantity, item.OptionKey)));
            string actual = result.Existing == null ? "읽기 실패" : string.Join(",", result.Existing.Select(item =>
                Item(item.ExternalProductId, item.Quantity, item.OptionKey)));
            string mismatch = result.Existing == null ? "현재 목록 확인 실패" : string.Join(",",
                attempt.Targets.Where(item => !result.Existing.Any(found =>
                    found.ExternalProductId == item.ExternalProductId &&
                    found.OptionKey == item.OptionKey && found.Quantity == item.Quantity))
                    .Select(item => item.ExternalProductId).Concat(result.Existing.Where(found =>
                    !attempt.Targets.Any(item => item.ExternalProductId == found.ExternalProductId &&
                    item.OptionKey == found.OptionKey && item.Quantity == found.Quantity))
                    .Select(item => item.ExternalProductId)).Distinct());
            log?.Write(LogLevel.WARN, "SITE_CART_MISMATCH_DETAIL",
                $"메가커피 장바구니 비교 불일치: 기대 {expected}; 실제 {actual}; 불일치 상품 {mismatch}; 마지막 확인 단계 {result.LastVerifiedStage}.",
                supplier: "mega", result: result.State, reason: result.Reason);
        }
        log?.Write(result.State == "READY" ? LogLevel.INFO : LogLevel.WARN,
            result.State == "READY" ? "SITE_CART_VERIFIED" : "SITE_CART_PREPARE_STOPPED",
            result.State == "READY" ? "메가커피 사이트 장바구니 상품·옵션·수량을 검증했습니다." :
                "메가커피 사이트 장바구니 준비를 중단했습니다.", supplier: "mega",
            result: result.State, reason: result.Reason);
        return result;
    }
}
