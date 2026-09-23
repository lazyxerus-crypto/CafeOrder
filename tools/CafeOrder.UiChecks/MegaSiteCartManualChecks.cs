using CafeOrder;
using Microsoft.Playwright;

internal static class MegaSiteCartManualChecks
{
    internal static async Task RunAsync()
    {
        await using var manager = new SupplierSessionManager();
        string profile = manager.ProfilePath("mega");
        using var playwright = await Playwright.CreateAsync();
        var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
        { Channel = "msedge", Headless = false, ChromiumSandbox = true, AcceptDownloads = false });
        try
        {
            var vault = new BrowserCookieVault(profile, "mega");
            await vault.RestoreAsync(context);
            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            var login = new MegaCoffeeLoginProbe();
            await page.GotoAsync("https://www.megacoffee.co.kr/order/cart.php",
                new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("MegaCoffee login is not active");
            var initial = await MegaCoffeeSiteCart.ReadAsync(page, login);
            Console.WriteLine("Initial site cart: " + string.Join(", ",
                initial.Select(x => x.ExternalProductId + " x" + x.Quantity)));
            page.Dialog += (_, dialog) =>
            {
                string message = dialog.Type == "confirm" &&
                    dialog.Message == MegaCartDialogMonitor.ClearMessage
                    ? MegaCartDialogMonitor.ClearMessage : "[예상하지 못한 문구: 기록 생략]";
                Console.WriteLine("WAITING_FOR_USER dialog type=" + dialog.Type + " message=" + message);
                // In headed manual mode, the user handles the visible dialog. Never auto-dismiss it.
            };
            Console.WriteLine("WAITING_FOR_USER: Edge cart window is open. Type 'status' or 'done'.");
            while (true)
            {
                string? input = await Task.Run(Console.ReadLine);
                if (input is null || input.Trim().Equals("done", StringComparison.OrdinalIgnoreCase))
                {
                    if (page.IsClosed) break;
                    Console.WriteLine("WAITING_FOR_USER: close the Edge window yourself, then type 'done'.");
                    continue;
                }
                if (!input.Trim().Equals("status", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    // Read from a separate tab so a user-owned dialog is never dismissed by navigation.
                    var reader = await context.NewPageAsync();
                    IReadOnlyList<SiteCartEntry> items;
                    try { items = await MegaCoffeeSiteCart.ReadAsync(reader, login); }
                    finally { await reader.CloseAsync(); }
                    Console.WriteLine("Site cart: " + string.Join(", ", items.Select(x => x.ExternalProductId + " x" + x.Quantity)));
                }
                catch (Exception ex) when (ex is PlaywrightException or InvalidDataException)
                { Console.WriteLine("WAITING_FOR_USER: site cart read stopped: " + ex.GetType().Name); }
            }
            if (!page.IsClosed) await vault.SaveAsync(context, login.HomeUrl);
        }
        finally { try { await context.CloseAsync(); } catch (PlaywrightException) { } }
    }
}
