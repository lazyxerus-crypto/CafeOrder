using CafeOrder;
using Microsoft.Playwright;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

internal static class LoginChecks
{
    internal static void CurrentMegaUiStatus()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var form = new MainForm { ShowInTaskbar = false, Opacity = 0 };
        var status = form.Controls.Find("LoginStatus_mega", true).Single();
        string observed = "확인 전";
        form.Shown += async (_, _) =>
        {
            try
            {
                for (int i = 0; i < 120; i++)
                {
                    observed = status.Text;
                    if (observed is "로그인 상태: 로그인 완료" or "로그인 상태: 로그인 필요" or "로그인 상태: 확인 실패") break;
                    await Task.Delay(250);
                }
            }
            finally { form.Close(); }
        };
        Application.Run(form);
        Console.WriteLine("CafeOrder supplier UI state: " + observed);
    }

    internal static async Task CurrentMegaSessionAsync()
    {
        await using var manager = new SupplierSessionManager();
        SupplierLoginState state = SupplierLoginState.NotChecked;
        manager.StateChanged += (id, next) => { if (id == "mega") state = next; };
        await manager.CheckAsync("mega");
        Console.WriteLine("MegaCoffee DOM session state: " + state);
        Console.WriteLine("Protected session snapshot exists: " +
            File.Exists(Path.Combine(manager.ProfilePath("mega"), "session-cookies.dpapi")));
    }

    internal static async Task DiagnoseOneManualClickAsync()
    {
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("mega");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
        var observations = new ConcurrentQueue<string>();
        var clicked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int loginRequests = 0, loginResponses = 0, newWindows = 0;
        string initialUrl = "";
        bool navigated = false;
        try
        {
            context.Request += (_, request) =>
            {
                if (!IsLoginRequest(request.Url)) return;
                Interlocked.Increment(ref loginRequests);
                observations.Enqueue("login-request:" + request.Method);
            };
            context.Response += (_, response) =>
            {
                if (!IsLoginRequest(response.Url)) return;
                Interlocked.Increment(ref loginResponses);
                observations.Enqueue("login-http:" + response.Status);
            };
            context.RequestFailed += (_, request) =>
            {
                if (!IsLoginRequest(request.Url)) return;
                var code = Regex.Match(request.Failure ?? "", @"net::ERR_[A-Z_]+").Value;
                observations.Enqueue("login-failure:" + (code.Length == 0 ? "other" : code));
            };
            context.Page += (_, _) => { Interlocked.Increment(ref newWindows); observations.Enqueue("new-window"); };
            context.Dialog += async (_, dialog) =>
            {
                observations.Enqueue("dialog:" + dialog.Type + ":" + SafeSiteMessage(dialog.Message));
                try { await dialog.DismissAsync(); } catch (PlaywrightException) { }
            };
            await context.AddInitScriptAsync("""
                (() => {
                  document.addEventListener('DOMContentLoaded', () => {
                    const form = document.querySelector('#formLogin');
                    const button = form?.querySelector('button[type="submit"]');
                    if (!form || !button) return;
                    console.info('__cafe_diag__:disabled:' + (button.disabled ? '1' : '0'));
                    document.addEventListener('click', event => {
                      if (button.contains(event.target)) console.info('__cafe_diag__:click');
                    }, true);
                    document.addEventListener('submit', event => {
                      if (event.target !== form) return;
                      console.info('__cafe_diag__:submit');
                      queueMicrotask(() => console.info('__cafe_diag__:submitPrevented:' + (event.defaultPrevented ? '1' : '0')));
                    }, true);
                    new MutationObserver(() => console.info('__cafe_diag__:disabled:' + (button.disabled ? '1' : '0')))
                      .observe(button, { attributes: true, attributeFilter: ['disabled'] });
                  }, { once: true });
                })();
                """);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            page.Console += (_, message) =>
            {
                string value = message.Text;
                if (Regex.IsMatch(value, @"^__cafe_diag__:(click|submit|submitPrevented:[01]|disabled:[01])$"))
                {
                    observations.Enqueue(value[14..]);
                    if (value == "__cafe_diag__:click") clicked.TrySetResult();
                }
                else if (message.Type == "error") observations.Enqueue("console-error:" + SafeJavaScriptError(value));
            };
            page.PageError += (_, error) => observations.Enqueue("javascript-error:" + SafeJavaScriptError(error));
            page.FrameNavigated += (_, frame) =>
            {
                if (frame != page.MainFrame) return;
                if (initialUrl.Length > 0 && frame.Url != initialUrl) navigated = true;
                observations.Enqueue("navigation:" + SafeLocation(frame.Url));
            };
            await page.GotoAsync("https://www.megacoffee.co.kr/member/login.php",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            await page.WaitForTimeoutAsync(1000);
            initialUrl = page.Url;
            bool buttonEnabled = await page.Locator("#formLogin button[type='submit']").IsEnabledAsync();
            bool passwordInForm = await page.EvaluateAsync<bool>("document.querySelector('#loginPwd')?.form?.id === 'formLogin'");
            while (observations.TryDequeue(out _)) { }
            Console.WriteLine($"READY: sandbox enabled; buttonEnabled={buttonEnabled}; passwordEnterUsesForm={passwordInForm}. Enter credentials in Edge, then click the login button ONCE.");
            if (await Task.WhenAny(clicked.Task, Task.Delay(TimeSpan.FromMinutes(5))) != clicked.Task)
            { Console.WriteLine("No login click observed; no submission was attempted by the diagnostic."); return; }
            await Task.Delay(TimeSpan.FromSeconds(15));
            bool inlineError = false, finalDisabled = false;
            if (!page.IsClosed)
            {
                try
                {
                    inlineError = await page.Locator("#formLogin .js_caution_msg1:visible").CountAsync() > 0;
                    finalDisabled = await page.Locator("#formLogin button[type='submit']:disabled").CountAsync() > 0;
                }
                catch (PlaywrightException) { }
            }
            Console.WriteLine($"RESULT: clicks=1 observed; requests={loginRequests}; responses={loginResponses}; urlChanged={navigated}; newWindows={newWindows}; finalButtonDisabled={finalDisabled}; inlineCredentialError={inlineError}");
            foreach (string item in observations)
                Console.WriteLine(item);
            Console.WriteLine("finalLocation:" + SafeLocation(page.IsClosed ? "" : page.Url));
        }
        finally { await context.CloseAsync(); }
    }

    private static bool IsLoginRequest(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           uri.Host is "www.megacoffee.co.kr" or "megacoffee.co.kr" &&
           uri.AbsolutePath.Equals("/member/login_ps.php", StringComparison.OrdinalIgnoreCase);

    private static string SafeLocation(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "closed-or-unknown";
        string path = uri.AbsolutePath is "/" or "/member/login.php" or "/member/login_ps.php"
            ? uri.AbsolutePath : "/[path redacted]";
        return uri.Host + path;
    }

    private static string SafeSiteMessage(string message)
        => message.Trim() == "아이디, 비밀번호가 일치하지 않습니다. 다시 입력해 주세요." ? "아이디/비밀번호 불일치 안내" : "기타 안내(원문 비공개)";

    private static string SafeJavaScriptError(string message)
    {
        var kind = Regex.Match(message, @"\b(TypeError|ReferenceError|SyntaxError|Error)\b").Value;
        var symbol = Regex.Match(message, @"\b(CryptoJS|jQuery|validate|PBKDF2|_\.isUndefined)\b").Value;
        return (kind.Length == 0 ? "other" : kind) + (symbol.Length == 0 ? "" : ":" + symbol);
    }

    internal static async Task DiagnosePageAsync(string output)
    {
        string profile = Path.Combine(output, "diagnostic-edge-profile");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false });
        try
        {
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            int consoleErrors = 0, pageErrors = 0, loginRequests = 0;
            var consoleKinds = new ConcurrentBag<string>();
            var failedScripts = new ConcurrentBag<string>();
            page.Console += (_, message) =>
            {
                if (message.Type != "error") return;
                Interlocked.Increment(ref consoleErrors);
                string value = message.Text;
                consoleKinds.Add(value.Contains("Failed to load resource", StringComparison.OrdinalIgnoreCase) ? "resource-load"
                    : value.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase) ? "content-security-policy"
                    : value.Contains("CryptoJS", StringComparison.OrdinalIgnoreCase) ? "crypto-library"
                    : value.Contains("validate", StringComparison.OrdinalIgnoreCase) ? "form-validator" : "other");
            };
            page.PageError += (_, _) => Interlocked.Increment(ref pageErrors);
            page.Request += (_, request) => { if (new Uri(request.Url).AbsolutePath.EndsWith("/member/login_ps.php", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref loginRequests); };
            page.Response += (_, response) =>
            {
                if (response.Status >= 400 && new Uri(response.Url).AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                    failedScripts.Add($"script HTTP {response.Status}");
            };
            await page.GotoAsync("https://www.megacoffee.co.kr/member/login.php",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            await page.WaitForTimeoutAsync(1500);
            bool jquery = await page.EvaluateAsync<bool>("typeof window.jQuery === 'function'");
            bool validator = await page.EvaluateAsync<bool>("!!window.jQuery && !!jQuery.fn.validate && !!jQuery('#formLogin').data('validator')");
            bool crypto = await page.EvaluateAsync<bool>("!!window.CryptoJS && !!CryptoJS.PBKDF2 && !!CryptoJS.AES && !!CryptoJS.algo.SHA512");
            bool underscore = await page.EvaluateAsync<bool>("!!window._ && !!_.isUndefined");
            bool button = await page.Locator("#formLogin button[type='submit']").IsEnabledAsync();
            Console.WriteLine($"Login page ready: button={button}, jQuery={jquery}, submitValidator={validator}, CryptoJS={crypto}, underscore={underscore}");
            Console.WriteLine($"Page load: consoleErrors={consoleErrors} ({string.Join(',', consoleKinds.Order())}), pageErrors={pageErrors}, loginRequests={loginRequests}; failedJs={failedScripts.Count}");
            foreach (var failed in failedScripts.Order()) Console.WriteLine($"Failed JS: {failed}");
        }
        finally { await context.CloseAsync(); }
    }

    internal static async Task InteractiveAsync()
    {
        await using var manager = new SupplierSessionManager();
        SupplierLoginState state = SupplierLoginState.NotChecked;
        manager.StateChanged += (id, next) => { if (id == "mega") { state = next; Console.WriteLine($"MegaCoffee: {next}"); } };
        await manager.OpenLoginAsync("mega");
        Check(state == SupplierLoginState.LoggedIn, "MegaCoffee interactive login did not complete");
        await manager.CheckAsync("mega");
        Check(state == SupplierLoginState.LoggedIn, "MegaCoffee session was not restored after Edge restart");
        Console.WriteLine("PASS: MegaCoffee user login and Edge session reuse");
    }

    internal static async Task LiveAsync(string output)
    {
        await using var manager = new SupplierSessionManager(Path.Combine(output, "live-browser-profiles"));
        SupplierLoginState state = SupplierLoginState.NotChecked;
        manager.StateChanged += (id, next) => { if (id == "mega") state = next; };
        await manager.CheckAsync("mega");
        Check(state is SupplierLoginState.LoginRequired or SupplierLoginState.LoggedIn or SupplierLoginState.WaitingForUser,
            "MegaCoffee live page could not be checked by installed Edge");
        Console.WriteLine($"MegaCoffee live Edge check: {state}");
    }

    internal static async Task RunAsync(string output)
    {
        string root = Path.Combine(output, "browser-profiles");
        Directory.CreateDirectory(root);
        await using var manager = new SupplierSessionManager(root);
        string[] ids = ["mega", "piece", "food", "wym", "nuldam"];
        Check(ids.Select(manager.ProfilePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 5, "AUTO profile separation");
        Check(!manager.IsConfigured("piece") && manager.IsConfigured("mega"), "Only MegaCoffee has a live probe");
        try { manager.ProfilePath("coupang"); throw new Exception("Manual supplier received an AUTO profile"); }
        catch (ArgumentException) { }

        using var playwright = await Playwright.CreateAsync();
        var probe = new MegaCoffeeLoginProbe();
        const string home = "https://www.megacoffee.co.kr/";
        const string loginHtml = "<html><body><a href='/member/login.php'>로그인</a><form id='formLogin'><input id='loginId'><input id='loginPwd' type='password'><button type='submit'>로그인</button></form></body></html>";
        const string loggedHtml = "<html><body><a href='/member/logout.php'>로그아웃</a></body></html>";
        const string challengeHtml = "<html><body><input name='otp' placeholder='OTP'><button>확인</button></body></html>";
        var options = new BrowserTypeLaunchPersistentContextOptions { Channel = "msedge", Headless = true, ChromiumSandbox = true, AcceptDownloads = false };
        var vault = new BrowserCookieVault(manager.ProfilePath("mega"), "mega");
        string cookieValue = Guid.NewGuid().ToString("N");
        var first = await playwright.Chromium.LaunchPersistentContextAsync(manager.ProfilePath("mega"), options);
        try
        {
            var page = first.Pages.FirstOrDefault() ?? await first.NewPageAsync();
            await page.RouteAsync("**/www.megacoffee.co.kr/**", route => route.FulfillAsync(new() { Body = loginHtml, ContentType = "text/html" }));
            await page.GotoAsync(home);
            Check(await probe.CheckAsync(page) == SupplierLoginState.LoginRequired, "Login form is not a valid session");
            await page.SetContentAsync(loggedHtml);
            Check(await probe.CheckAsync(page) == SupplierLoginState.LoggedIn, "Logout control proves logged-in fixture");
            await page.SetContentAsync(challengeHtml);
            Check(await probe.CheckAsync(page) == SupplierLoginState.WaitingForUser, "Unknown challenge waits for user");
            await first.AddCookiesAsync([new Cookie { Name = "cafeorder_test", Value = cookieValue, Domain = "www.megacoffee.co.kr", Path = "/" }]);
            await page.EvaluateAsync("localStorage.setItem('cafeorder_test', 'separate')");
            await vault.SaveAsync(first, home);
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(manager.ProfilePath("mega"), "session-cookies.dpapi"))).Contains(cookieValue), "Cookie snapshot is encrypted");
        }
        finally { await first.CloseAsync(); }

        var reopened = await playwright.Chromium.LaunchPersistentContextAsync(manager.ProfilePath("mega"), options);
        try
        {
            await vault.RestoreAsync(reopened);
            Check((await reopened.CookiesAsync([home])).Any(c => c.Name == "cafeorder_test" && c.Value == cookieValue), "Protected session cookie restores after restart");
            var page = reopened.Pages.FirstOrDefault() ?? await reopened.NewPageAsync();
            await page.RouteAsync("**/www.megacoffee.co.kr/**", route => route.FulfillAsync(new() { Body = loginHtml, ContentType = "text/html" }));
            await page.GotoAsync(home);
            Check(await page.EvaluateAsync<string>("localStorage.getItem('cafeorder_test')") == "separate", "Edge profile persists local storage");
        }
        finally { await reopened.CloseAsync(); }
        var separate = await playwright.Chromium.LaunchPersistentContextAsync(manager.ProfilePath("piece"), options);
        try { Check(!(await separate.CookiesAsync([home])).Any(c => c.Name == "cafeorder_test"), "Different supplier cannot read session cookie"); }
        finally { await separate.CloseAsync(); }
        await using (var expired = new SupplierSessionManager(root, probes: new() { ["mega"] = new FakeProbe(false) }))
        {
            SupplierLoginState expiredState = SupplierLoginState.NotChecked;
            expired.StateChanged += (id, state) => { if (id == "mega") expiredState = state; };
            await expired.CheckAsync("mega");
            Check(expiredState == SupplierLoginState.LoginRequired, "Expired session requires login");
        }
        Check(!File.Exists(Path.Combine(manager.ProfilePath("mega"), "session-cookies.dpapi")), "Login-required state discards saved cookies");
        var statuses = new ConcurrentDictionary<string, SupplierLoginState>();
        await using (var isolated = new SupplierSessionManager(Path.Combine(output, "isolated-profiles"), probes: new()
        {
            ["mega"] = new FakeProbe(false), ["piece"] = new FakeProbe(true)
        }))
        {
            isolated.StateChanged += (id, state) => statuses[id] = state;
            var watch = Stopwatch.StartNew(); isolated.StartBackgroundChecks(); watch.Stop();
            Check(watch.Elapsed < TimeSpan.FromSeconds(2), "Startup checks do not block the UI");
            await Task.WhenAll(isolated.CheckAsync("mega"), isolated.CheckAsync("piece"));
            Check(statuses["mega"] == SupplierLoginState.LoginRequired && statuses["piece"] == SupplierLoginState.Error,
                "One supplier failure does not stop another supplier check");
        }
        Console.WriteLine("PASS: installed Edge launched; MegaCoffee login/logout/challenge elements; protected session reuse and profile separation.");
    }

    private sealed class FakeProbe(bool fail) : ISupplierLoginProbe
    {
        public string HomeUrl => "about:blank";
        public string LoginUrl => HomeUrl;
        public Task<SupplierLoginState> CheckAsync(IPage page)
            => fail ? throw new InvalidOperationException("fixture failure") : Task.FromResult(SupplierLoginState.LoginRequired);
        public Task SubmitAsync(IPage page, LoginCredentials credentials) => Task.CompletedTask;
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
