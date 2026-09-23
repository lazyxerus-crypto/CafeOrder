using Microsoft.Playwright;

namespace CafeOrder;

internal sealed partial class SupplierSessionManager
{
    internal Task<MegaProductLookupResult> LookupNuldamProductAsync(string url)
    {
        if (!NuldamProductLookup.TryProductUrl(url, out var uri, out var productNumber))
        {
            log?.Write(LogLevel.WARN, "PRODUCT_LOOKUP_FAILED", "널담 상품 URL 형식이 올바르지 않습니다.",
                supplier: "nuldam", result: "FAILED", reason: "INVALID_URL");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.InvalidUrl));
        }
        lock (sync)
        {
            if (disposed) return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed));
            var slot = slots["nuldam"];
            var prior = slot.Running;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            slot.Cancel = cancel;
            var task = RunNuldamProductAfterAsync(prior, slot, uri!, productNumber, cancel);
            slot.Running = task;
            return LogNuldamLookupAsync(task);
        }
    }

    private async Task<MegaProductLookupResult> LogNuldamLookupAsync(Task<MegaProductLookupResult> task)
    {
        var result = await task;
        log?.Write(result.Status == MegaProductLookupStatus.Success ? LogLevel.INFO : LogLevel.WARN,
            result.Status == MegaProductLookupStatus.Success ? "PRODUCT_LOOKUP_SUCCESS" : "PRODUCT_LOOKUP_FAILED",
            result.Status == MegaProductLookupStatus.Success ? "널담 상품정보와 이미지를 확인했습니다." :
                result.Reason ?? "널담 상품 조회를 완료하지 못했습니다.",
            supplier: "nuldam", result: result.Status == MegaProductLookupStatus.Success ? "SUCCESS" : "FAILED",
            reason: result.Status == MegaProductLookupStatus.Success ? null : result.Status.ToString().ToUpperInvariant());
        return result;
    }

    private async Task<MegaProductLookupResult> RunNuldamProductAfterAsync(Task? prior, SessionSlot slot,
        Uri uri, string productNumber, CancellationTokenSource cancel)
    {
        try
        {
            if (prior != null) await prior;
            cancel.Token.ThrowIfCancellationRequested();
            string profile = ProfilePath("nuldam");
            Directory.CreateDirectory(profile);
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
            slot.Context = context;
            var probe = new NuldamLoginProbe();
            var vault = new BrowserCookieVault(profile, "nuldam");
            try
            {
                await vault.RestoreAsync(context);
                cancel.Token.ThrowIfCancellationRequested();
                var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync(probe.HomeUrl, new()
                { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                var state = await probe.CheckAsync(page);
                if (state == SupplierLoginState.LoginRequired)
                {
                    vault.Clear(); Publish("nuldam", SupplierLoginState.LoginRequired);
                    return new(MegaProductLookupStatus.LoginRequired);
                }
                if (state != SupplierLoginState.LoggedIn)
                    return new(MegaProductLookupStatus.Failed, Reason: "널담 로그인 상태를 확인할 수 없습니다.");
                cancel.Token.ThrowIfCancellationRequested();
                var product = await NuldamProductLookup.FetchAsync(page, context, uri, productNumber, probe);
                await vault.SaveAsync(context, probe.HomeUrl);
                Publish("nuldam", SupplierLoginState.LoggedIn);
                return new(MegaProductLookupStatus.Success, product);
            }
            catch (MegaCoffeeLoginRequiredException)
            {
                vault.Clear(); Publish("nuldam", SupplierLoginState.LoginRequired);
                return new(MegaProductLookupStatus.LoginRequired);
            }
            finally
            {
                try { await context.CloseAsync(); } catch (PlaywrightException) { }
                slot.Context = null;
            }
        }
        catch (OperationCanceledException) { return new(MegaProductLookupStatus.Failed); }
        catch (InvalidDataException ex)
        { return new(MegaProductLookupStatus.Failed, Reason: ex.Message, ErrorType: ex.GetType().Name); }
        catch (Exception ex)
        {
            log?.Write(LogLevel.ERROR, "PRODUCT_LOOKUP_EXCEPTION", "널담 조회 중 브라우저 또는 파일 처리가 실패했습니다.",
                supplier: "nuldam", result: "FAILED", error: ex);
            return new(MegaProductLookupStatus.Failed, ErrorType: ex.GetType().Name);
        }
        finally
        {
            lock (sync) { if (ReferenceEquals(slot.Cancel, cancel)) slot.Cancel = null; }
            cancel.Dispose();
        }
    }
}
