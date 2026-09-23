using Microsoft.Playwright;

namespace CafeOrder;

internal sealed partial class SupplierSessionManager
{
    internal Task<MegaProductLookupResult> LookupPieceProductAsync(string url)
    {
        if (!PieceCakeProductLookup.TryProductUrl(url, out var uri, out var productNumber))
        {
            log?.Write(LogLevel.WARN, "PRODUCT_LOOKUP_FAILED", "파미유 상품 URL 형식이 올바르지 않습니다.",
                supplier: "piece", result: "FAILED", reason: "INVALID_URL");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.InvalidUrl));
        }
        lock (sync)
        {
            if (disposed) return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed));
            var slot = slots["piece"];
            var prior = slot.Running;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            slot.Cancel = cancel;
            var task = RunPieceProductAfterAsync(prior, slot, uri!, productNumber, cancel);
            slot.Running = task;
            return LogPieceLookupAsync(task);
        }
    }

    private async Task<MegaProductLookupResult> LogPieceLookupAsync(Task<MegaProductLookupResult> task)
    {
        var result = await task;
        if (result.Status == MegaProductLookupStatus.Success)
            log?.Write(LogLevel.INFO, "PRODUCT_LOOKUP_SUCCESS", "파미유 상품정보와 이미지를 확인했습니다.",
                supplier: "piece", result: "SUCCESS");
        else
            log?.Write(LogLevel.WARN, "PRODUCT_LOOKUP_FAILED",
                result.Reason ?? "파미유 상품 조회를 완료하지 못했습니다.", supplier: "piece",
                result: "FAILED", reason: result.Status.ToString().ToUpperInvariant());
        return result;
    }

    private async Task<MegaProductLookupResult> RunPieceProductAfterAsync(Task? prior, SessionSlot slot,
        Uri uri, string productNumber, CancellationTokenSource cancel)
    {
        try
        {
            if (prior != null) await prior;
            cancel.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(ProfilePath("piece"));
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(ProfilePath("piece"), new()
            { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
            slot.Context = context;
            var probe = new PieceCakeLoginProbe();
            var vault = new BrowserCookieVault(ProfilePath("piece"), "piece");
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
                    vault.Clear(); Publish("piece", SupplierLoginState.LoginRequired);
                    return new(MegaProductLookupStatus.LoginRequired);
                }
                if (state != SupplierLoginState.LoggedIn)
                    return new(MegaProductLookupStatus.Failed, Reason: "파미유 로그인 상태를 확인할 수 없습니다.");
                cancel.Token.ThrowIfCancellationRequested();
                var product = await PieceCakeProductLookup.FetchAsync(page, context, uri, productNumber, probe);
                await vault.SaveAsync(context, probe.HomeUrl);
                Publish("piece", SupplierLoginState.LoggedIn);
                return new(MegaProductLookupStatus.Success, product);
            }
            catch (MegaCoffeeLoginRequiredException)
            {
                vault.Clear(); Publish("piece", SupplierLoginState.LoginRequired);
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
            log?.Write(LogLevel.ERROR, "PRODUCT_LOOKUP_EXCEPTION", "파미유 조회 중 브라우저 또는 파일 처리가 실패했습니다.",
                supplier: "piece", result: "FAILED", error: ex);
            return new(MegaProductLookupStatus.Failed, ErrorType: ex.GetType().Name);
        }
        finally
        {
            lock (sync) { if (ReferenceEquals(slot.Cancel, cancel)) slot.Cancel = null; }
            cancel.Dispose();
        }
    }
}
