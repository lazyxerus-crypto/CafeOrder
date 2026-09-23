using Microsoft.Playwright;

namespace CafeOrder;

internal enum SupplierLoginState { NotChecked, Checking, LoginRequired, LoggedIn, WaitingForUser, Error, NotConfigured }
internal sealed record LoginCredentials(string Id, string Password);

// A Windows credential-store implementation can replace this without changing the browser flow.
internal interface ILoginCredentialSource
{
    ValueTask<LoginCredentials?> ReadAsync(string supplierId, CancellationToken cancellationToken);
}

internal sealed class NoStoredLoginCredentials : ILoginCredentialSource
{
    public ValueTask<LoginCredentials?> ReadAsync(string supplierId, CancellationToken cancellationToken)
        => ValueTask.FromResult<LoginCredentials?>(null);
}

internal interface ISupplierLoginProbe
{
    string HomeUrl { get; }
    string LoginUrl { get; }
    Task<SupplierLoginState> CheckAsync(IPage page);
    Task SubmitAsync(IPage page, LoginCredentials credentials);
}

internal sealed class MegaCoffeeLoginProbe : ISupplierLoginProbe
{
    public string HomeUrl => "https://www.megacoffee.co.kr/";
    public string LoginUrl => "https://www.megacoffee.co.kr/member/login.php";

    public async Task<SupplierLoginState> CheckAsync(IPage page)
    {
        // A URL or a cookie by itself cannot prove that the server accepted the session.
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ||
            uri.Host is not ("www.megacoffee.co.kr" or "megacoffee.co.kr"))
            return SupplierLoginState.WaitingForUser;
        if (await page.Locator("a[href*='logout.php']").CountAsync() > 0)
            return SupplierLoginState.LoggedIn;
        if (await page.Locator("#formLogin #loginId:visible").CountAsync() > 0 ||
            await page.Locator("a[href*='/member/login.php']:visible").CountAsync() > 0)
            return SupplierLoginState.LoginRequired;
        return SupplierLoginState.WaitingForUser;
    }

    public async Task SubmitAsync(IPage page, LoginCredentials credentials)
    {
        if (page.Url != LoginUrl) await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        await page.Locator("#formLogin #loginId").FillAsync(credentials.Id);
        await page.Locator("#formLogin #loginPwd").FillAsync(credentials.Password);
        await page.Locator("#formLogin button[type='submit']").ClickAsync(new() { Timeout = 15000 });
    }
}

internal sealed class SupplierSessionManager : IAsyncDisposable
{
    private static readonly string[] AutoIds = ["mega", "piece", "food", "wym", "nuldam"];
    private readonly Dictionary<string, SessionSlot> slots = AutoIds.ToDictionary(id => id, _ => new SessionSlot());
    private readonly Dictionary<string, ISupplierLoginProbe> probes;
    private readonly ILoginCredentialSource credentials;
    private readonly OperationalLog? log;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object sync = new();
    private readonly string profileRoot;
    private bool disposed;

    internal event Action<string, SupplierLoginState>? StateChanged;

    internal SupplierSessionManager(string? profileRoot = null, ILoginCredentialSource? credentials = null,
        Dictionary<string, ISupplierLoginProbe>? probes = null, OperationalLog? log = null)
    {
        this.profileRoot = profileRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "BrowserProfiles");
        this.credentials = credentials ?? new NoStoredLoginCredentials();
        this.probes = probes ?? new() { ["mega"] = new MegaCoffeeLoginProbe() };
        this.log = log;
    }

    internal string ProfilePath(string supplierId)
    {
        if (!slots.ContainsKey(supplierId)) throw new ArgumentException("AUTO 판매처가 아닙니다.", nameof(supplierId));
        return Path.Combine(profileRoot, supplierId);
    }

    internal bool IsConfigured(string supplierId) => probes.ContainsKey(supplierId);

    internal void StartBackgroundChecks()
    {
        foreach (var id in AutoIds.Where(IsConfigured)) _ = Begin(id, false, null);
    }

    internal Task CheckAsync(string supplierId) => Begin(supplierId, false, null);

    internal Task OpenLoginAsync(string supplierId, LoginCredentials? entered = null)
        => Begin(supplierId, true, entered);

    internal Task<MegaProductLookupResult> LookupMegaProductAsync(string url)
    {
        if (!MegaCoffeeProductLookup.TryProductUrl(url, out var uri, out var goodsNo))
        {
            log?.Write(LogLevel.WARN, "PRODUCT_LOOKUP_FAILED", "메가커피 상품 URL 형식이 올바르지 않습니다.",
                supplier: "mega", result: "FAILED", reason: "INVALID_URL");
            return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.InvalidUrl));
        }
        lock (sync)
        {
            if (disposed) return Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed));
            var slot = slots["mega"];
            var prior = slot.Running;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            slot.Cancel = cancel;
            var task = RunMegaProductAfterAsync(prior, slot, uri!, goodsNo, cancel);
            slot.Running = task;
            return LogLookupAsync(task);
        }
    }

    private async Task<MegaProductLookupResult> LogLookupAsync(Task<MegaProductLookupResult> task)
    {
        var result = await task;
        if (result.Status == MegaProductLookupStatus.Success)
            log?.Write(LogLevel.INFO, "PRODUCT_LOOKUP_SUCCESS", "메가커피 상품정보와 이미지를 확인했습니다.",
                supplier: "mega", result: "SUCCESS");
        else
        {
            string reason = result.Status switch
            {
                MegaProductLookupStatus.LoginRequired => "LOGIN_REQUIRED",
                MegaProductLookupStatus.InvalidUrl => "INVALID_URL",
                _ when result.Reason?.Contains("상품명", StringComparison.Ordinal) == true => "NAME_UNAVAILABLE",
                _ when result.Reason?.Contains("판매가", StringComparison.Ordinal) == true ||
                    result.Reason?.Contains("가격", StringComparison.Ordinal) == true => "PRICE_UNAVAILABLE",
                _ when result.Reason?.Contains("이미지", StringComparison.Ordinal) == true => "IMAGE_UNAVAILABLE",
                _ when result.Reason?.Contains("품절", StringComparison.Ordinal) == true => "STOCK_UNAVAILABLE",
                _ => "PAGE_OR_BROWSER_ERROR"
            };
            log?.Write(LogLevel.WARN, "PRODUCT_LOOKUP_FAILED", "메가커피 상품 조회를 완료하지 못했습니다.",
                supplier: "mega", result: "FAILED", reason: reason);
            if (result.Reason?.Contains("이미지 파일", StringComparison.Ordinal) == true)
                log?.Write(LogLevel.ERROR, "IMAGE_DOWNLOAD_FAILED", "메가커피 상품 이미지 파일을 내려받지 못했습니다.",
                    supplier: "mega", result: "FAILED", reason: "IMAGE_RESPONSE_INVALID");
            else if (reason == "IMAGE_UNAVAILABLE")
                log?.Write(LogLevel.WARN, "IMAGE_SOURCE_FAILED", "메가커피 상품 이미지 주소를 확인하지 못했습니다.",
                    supplier: "mega", result: "FAILED", reason: "IMAGE_SOURCE_MISSING");
        }
        return result;
    }

    private async Task<MegaProductLookupResult> RunMegaProductAfterAsync(Task? prior, SessionSlot slot,
        Uri uri, string goodsNo, CancellationTokenSource cancel)
    {
        try
        {
            if (prior != null) await prior;
            cancel.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(ProfilePath("mega"));
            using var playwright = await Playwright.CreateAsync();
            var context = await playwright.Chromium.LaunchPersistentContextAsync(ProfilePath("mega"), new()
            {
                Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false
            });
            slot.Context = context;
            var probe = new MegaCoffeeLoginProbe();
            var vault = new BrowserCookieVault(ProfilePath("mega"), "mega");
            try
            {
                await vault.RestoreAsync(context);
                cancel.Token.ThrowIfCancellationRequested();
                var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
                await page.GotoAsync(probe.HomeUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                var loginState = await probe.CheckAsync(page);
                if (loginState == SupplierLoginState.LoginRequired)
                {
                    vault.Clear(); Publish("mega", SupplierLoginState.LoginRequired);
                    return new(MegaProductLookupStatus.LoginRequired);
                }
                if (loginState != SupplierLoginState.LoggedIn) return new(MegaProductLookupStatus.Failed);
                cancel.Token.ThrowIfCancellationRequested();
                var product = await MegaCoffeeProductLookup.FetchAsync(page, context, uri, goodsNo, probe);
                await vault.SaveAsync(context, probe.HomeUrl);
                Publish("mega", SupplierLoginState.LoggedIn);
                return new(MegaProductLookupStatus.Success, product);
            }
            catch (MegaCoffeeLoginRequiredException)
            {
                vault.Clear(); Publish("mega", SupplierLoginState.LoginRequired);
                return new(MegaProductLookupStatus.LoginRequired);
            }
            finally
            {
                try { await context.CloseAsync(); } catch (PlaywrightException) { }
                slot.Context = null;
            }
        }
        catch (OperationCanceledException) { return new(MegaProductLookupStatus.Failed); }
        catch (InvalidDataException ex) { return new(MegaProductLookupStatus.Failed, Reason: ex.Message, ErrorType: ex.GetType().Name); }
        catch (Exception ex)
        {
            log?.Write(LogLevel.ERROR, "PRODUCT_LOOKUP_EXCEPTION", "메가커피 조회 중 브라우저 또는 파일 처리가 실패했습니다.",
                supplier: "mega", result: "FAILED", error: ex);
            return new(MegaProductLookupStatus.Failed, ErrorType: ex.GetType().Name);
        }
        finally
        {
            lock (sync) { if (ReferenceEquals(slot.Cancel, cancel)) slot.Cancel = null; }
            cancel.Dispose();
        }
    }

    private Task Begin(string id, bool interactive, LoginCredentials? entered)
    {
        if (!slots.TryGetValue(id, out var slot)) throw new ArgumentException("AUTO 판매처가 아닙니다.", nameof(id));
        if (!probes.TryGetValue(id, out var probe)) { Publish(id, SupplierLoginState.NotConfigured); return Task.CompletedTask; }
        lock (sync)
        {
            if (disposed) return Task.CompletedTask;
            slot.Cancel?.Cancel();
            var prior = slot.Running;
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            slot.Cancel = cancel;
            slot.Running = RunAfterAsync(prior, id, slot, probe, interactive, entered, cancel);
            return slot.Running;
        }
    }

    private async Task RunAfterAsync(Task? prior, string id, SessionSlot slot, ISupplierLoginProbe probe,
        bool interactive, LoginCredentials? entered, CancellationTokenSource cancel)
    {
        try
        {
            if (prior != null) await prior;
            cancel.Token.ThrowIfCancellationRequested();
            await RunAsync(id, slot, probe, interactive, entered, cancel.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested)
            {
                log?.Write(LogLevel.ERROR, "SUPPLIER_LOGIN_CHECK_FAILED", "판매처 로그인 상태를 확인하지 못했습니다.",
                    supplier: id, result: "FAILED", error: ex);
                Publish(id, SupplierLoginState.Error);
            }
        }
        finally
        {
            lock (sync) { if (ReferenceEquals(slot.Cancel, cancel)) slot.Cancel = null; }
            cancel.Dispose();
        }
    }

    private async Task RunAsync(string id, SessionSlot slot, ISupplierLoginProbe probe,
        bool interactive, LoginCredentials? entered, CancellationToken cancellationToken)
    {
        Publish(id, SupplierLoginState.Checking);
        Directory.CreateDirectory(ProfilePath(id));
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(ProfilePath(id), new()
        {
            Channel = "msedge", Headless = !interactive, ChromiumSandbox = true, AcceptDownloads = false
        });
        slot.Context = context;
        var vault = new BrowserCookieVault(ProfilePath(id), id);
        try
        {
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync(interactive ? probe.LoginUrl : probe.HomeUrl,
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            var state = await probe.CheckAsync(page);
            if (state == SupplierLoginState.LoginRequired) vault.Clear();
            if (!interactive)
            {
                if (state == SupplierLoginState.LoggedIn) await vault.SaveAsync(context, probe.HomeUrl);
                Publish(id, state); return;
            }
            if (state == SupplierLoginState.LoggedIn)
            { await vault.SaveAsync(context, probe.HomeUrl); Publish(id, state); return; }
            var supplied = entered ?? await credentials.ReadAsync(id, cancellationToken);
            bool submitted = false;
            if (supplied is { Id.Length: > 0, Password.Length: > 0 })
            {
                try { await probe.SubmitAsync(page, supplied); submitted = true; }
                catch (PlaywrightException) { Publish(id, SupplierLoginState.WaitingForUser); }
                supplied = null;
            }
            else Publish(id, SupplierLoginState.WaitingForUser);

            while (!cancellationToken.IsCancellationRequested && !page.IsClosed)
            {
                state = await probe.CheckAsync(page);
                if (state == SupplierLoginState.LoggedIn)
                { await vault.SaveAsync(context, probe.HomeUrl); Publish(id, state); return; }
                if (state == SupplierLoginState.LoginRequired) vault.Clear();
                if (state is SupplierLoginState.WaitingForUser or SupplierLoginState.LoginRequired)
                    Publish(id, state == SupplierLoginState.LoginRequired && !submitted
                        ? SupplierLoginState.WaitingForUser : state);
                await Task.Delay(1500, cancellationToken);
            }
            if (!cancellationToken.IsCancellationRequested) Publish(id, SupplierLoginState.LoginRequired);
        }
        finally
        {
            try { await context.CloseAsync(); } catch (PlaywrightException) { }
            slot.Context = null;
        }
    }

    private void Publish(string id, SupplierLoginState state)
    {
        var slot = slots[id];
        if (slot.State == state) return;
        slot.State = state;
        log?.Write(state == SupplierLoginState.Error ? LogLevel.ERROR : LogLevel.INFO,
            "SUPPLIER_LOGIN_STATE", "판매처 로그인 상태가 변경됐습니다.", supplier: id,
            result: state.ToString().ToUpperInvariant());
        StateChanged?.Invoke(id, state);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] running;
        lock (sync)
        {
            if (disposed) return;
            disposed = true; shutdown.Cancel();
            running = slots.Values.Select(slot => slot.Running).OfType<Task>().ToArray();
        }
        await Task.WhenAll(running);
        shutdown.Dispose();
    }

    private sealed class SessionSlot
    {
        internal SupplierLoginState State;
        internal CancellationTokenSource? Cancel;
        internal Task? Running;
        internal IBrowserContext? Context;
    }
}
