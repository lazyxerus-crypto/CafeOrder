using CafeOrder;
using Microsoft.Playwright;

internal static class NuldamLoginChecks
{
    internal static async Task RunAsync()
    {
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("nuldam");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
        try
        {
            var vault = new BrowserCookieVault(profile, "nuldam");
            var probe = new NuldamLoginProbe();
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync("https://nuldampartners.com/member/login.html",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            Console.WriteLine("OPEN: Nuldam Edge login window. Waiting for user; credentials are not read or logged.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            while (!timeout.IsCancellationRequested && context.Pages.Any(item => !item.IsClosed))
            {
                foreach (var candidate in context.Pages.Where(item => !item.IsClosed))
                {
                    if (await probe.CheckAsync(candidate) != SupplierLoginState.LoggedIn) continue;
                    await vault.SaveAsync(context, "https://nuldampartners.com/");
                    Console.WriteLine("PASS: Nuldam logged-in module appeared and session saved");
                    return;
                }
                await Task.Delay(1500, timeout.Token);
            }
            throw new InvalidDataException("늘담 로그인 완료를 확인하지 못했습니다.");
        }
        finally { await context.CloseAsync(); }
    }
}
