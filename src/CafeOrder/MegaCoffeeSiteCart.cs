using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal sealed record SiteCartEntry(string ExternalProductId, int Quantity, string OptionKey, string Name,
    decimal? UnitPrice = null);
internal sealed record SiteCartPreparationResult(string State, string Reason, IReadOnlyList<SiteCartEntry>? Existing = null);

internal static class MegaCoffeeSiteCart
{
    private static readonly Uri CartUrl = new("https://www.megacoffee.co.kr/order/cart.php");
    private static readonly Regex Quantity = new(@"^([1-9][0-9]*)\s*개(?:\s|$)", RegexOptions.CultureInvariant);

    internal static bool Matches(IReadOnlyList<SiteCartEntry> actual, IReadOnlyList<SiteCartTarget> target)
    {
        if (actual.Count != target.Count) return false;
        var expected = target.ToDictionary(x => (x.ExternalProductId, x.OptionKey), x => x.Quantity);
        return actual.Select(x => (x.ExternalProductId, x.OptionKey)).Distinct().Count() == actual.Count &&
            actual.All(x => expected.TryGetValue((x.ExternalProductId, x.OptionKey), out int quantity) &&
                quantity == x.Quantity);
    }

    internal static async Task<IReadOnlyList<SiteCartEntry>> ReadAsync(IPage page, MegaCoffeeLoginProbe login)
    {
        var response = await page.GotoAsync(CartUrl.ToString(), new()
        { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
            throw new MegaCoffeeLoginRequiredException();
        if (response?.Status != 200 || page.Url != CartUrl.ToString() ||
            await page.Locator("#frmCart").CountAsync() != 1)
            throw new InvalidDataException("메가커피 장바구니 페이지를 확인할 수 없습니다.");
        var checks = page.Locator("#frmCart input[name='cartSno[]']");
        int count = await checks.CountAsync();
        if (count == 0)
        {
            string emptyText = await page.Locator("#frmCart").InnerTextAsync();
            if (!emptyText.Contains("장바구니에 담겨있는 상품이 없습니다.", StringComparison.Ordinal) &&
                !emptyText.Contains("장바구니에 담긴 상품이 없습니다.", StringComparison.Ordinal))
                throw new InvalidDataException("빈 장바구니 상태를 확인할 수 없습니다.");
            return [];
        }
        var items = new List<SiteCartEntry>(count);
        for (int index = 0; index < count; index++)
        {
            var row = checks.Nth(index).Locator("xpath=ancestor::tr[1]");
            if (await row.CountAsync() != 1) throw new InvalidDataException("장바구니 상품 행을 확인할 수 없습니다.");
            var productLinks = row.Locator("a[href*='goods_view.php?goodsNo=']");
            if (await productLinks.CountAsync() == 0 ||
                !MegaCoffeeProductLookup.TryProductUrl(
                    new Uri(CartUrl, await productLinks.First.GetAttributeAsync("href") ?? "").ToString(),
                    out _, out string goodsNo))
                throw new InvalidDataException("장바구니 상품 코드를 확인할 수 없습니다.");
            var quantityField = row.Locator(".order_goods_num");
            if (await quantityField.CountAsync() != 1)
                throw new InvalidDataException("장바구니 수량을 확인할 수 없습니다.");
            var quantityText = (await quantityField.InnerTextAsync()).Trim();
            var match = Quantity.Match(quantityText);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int quantity))
                throw new InvalidDataException("장바구니 수량을 읽지 못했습니다.");
            var optionBoxes = row.Locator(".pick_option_box");
            var options = new List<string>();
            for (int option = 0; option < await optionBoxes.CountAsync(); option++)
            {
                string value = Regex.Replace(await optionBoxes.Nth(option).InnerTextAsync(), @"\s+", " ").Trim();
                if (value.Length != 0) options.Add(value);
            }
            string name = Regex.Replace(await productLinks.First.InnerTextAsync(), @"\s+", " ").Trim();
            items.Add(new(goodsNo, quantity, string.Join(" | ", options), name));
        }
        return items;
    }

    internal static async Task<SiteCartPreparationResult> PrepareAsync(IPage page, SiteCartAttempt attempt,
        MegaCoffeeLoginProbe login, Func<IReadOnlyList<SiteCartEntry>, Task<bool>> confirmExisting,
        Action<MegaCartDialogObservation>? onDialog = null)
    {
        if (attempt.SupplierId != "mega" || attempt.Targets.Count == 0)
            return new("FAILED", "INVALID_SNAPSHOT");
        IReadOnlyList<SiteCartEntry> existing;
        try
        {
        // Validate every target before touching the pre-existing site cart.
        foreach (var target in attempt.Targets)
        {
            if (!MegaCoffeeProductLookup.TryProductUrl(target.ProductUrl, out var url, out var goodsNo) ||
                goodsNo != target.ExternalProductId)
                return new("FAILED", "INVALID_GOODS_NO");
            var response = await page.GotoAsync(url!.ToString(), new()
            { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new MegaCoffeeLoginRequiredException();
            if (response?.Status != 200) return new("FAILED", "PRODUCT_PAGE_UNAVAILABLE");
            var parsed = await MegaCoffeeProductLookup.ReadPageAsync(page, goodsNo);
            if (!parsed.Available) return new("FAILED", "SOLD_OUT");
            if (parsed.Price != target.Price) return new("FAILED", "PRICE_CHANGED");
            if (await page.Locator(".item_info_box select:visible").CountAsync() != 0 ||
                await page.Locator(".item_info_box input[name='goodsCnt[]']").CountAsync() != 1)
                return new("FAILED", "OPTION_OR_PURCHASE_UNIT_REQUIRES_REVIEW");
        }

        existing = await ReadAsync(page, login);
        }
        catch (MegaCoffeeLoginRequiredException) { return new("FAILED", "LOGIN_REQUIRED_BEFORE_MUTATION"); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException)
        { return new("FAILED", "PRECHECK_" + ex.GetType().Name); }
        if (Matches(existing, attempt.Targets)) return new("READY", "ALREADY_MATCHED");
        if (existing.Count > 0 && !await confirmExisting(existing))
            return new("WAITING_FOR_USER", "EXISTING_CART_NOT_CONFIRMED", existing);

        bool mutationStarted = false;
        string stage = "CART_CLEAR";
        using var dialogs = new MegaCartDialogMonitor(page, onDialog);
        try
        {
            if (existing.Count > 0)
            {
                var clear = page.Locator("#frmCart button").Filter(new() { HasText = "장바구니 비우기" });
                if (await clear.CountAsync() != 1) return new("FAILED", "CART_CLEAR_CONTROL_UNAVAILABLE", existing);
                var clearFrameResponse = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                void CaptureClearResponse(object? _, IResponse response)
                {
                    if (Uri.TryCreate(response.Url, UriKind.Absolute, out var uri) &&
                        uri.Host == CartUrl.Host && uri.AbsolutePath == "/order/cart_ps.php" &&
                        uri.Query == "?mode=remove")
                        clearFrameResponse.TrySetResult(response);
                }
                var clearPage = page;
                clearPage.Response += CaptureClearResponse;
                try
                {
                    mutationStarted = true;
                    stage = "CART_CLEAR_CLICK";
                    try { await clear.ClickAsync(new() { Timeout = 10000 }); }
                    catch (PlaywrightException)
                    { /* A navigation may interrupt ClickAsync. Read the site state; never click twice. */ }

                    MegaCartDialogObservation? dialog = null;
                    try { dialog = await dialogs.First.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (TimeoutException) { }
                    if (dialog?.Accepted == true)
                    {
                        // The clear request runs in the site's hidden ifrmProcess frame.
                        // Wait for its response before navigating the cart page to read the result.
                        try
                        {
                            var response = await clearFrameResponse.Task.WaitAsync(TimeSpan.FromSeconds(12));
                            await response.FinishedAsync();
                        }
                        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                        { /* The cart read below, rather than this event, decides the remote state. */ }
                    }
                    stage = "CART_CLEAR_READ";
                    var clearRead = await ReadAfterMutationAsync(page, login);
                    page = clearRead.Page;
                    dialogs.Attach(page);
                    var afterClear = clearRead.Items;
                    if (dialogs.HasUnexpected || dialog is { IsClearConfirmation: false } or { Handled: false })
                        return new(afterClear.Count == 0 ? "UNKNOWN" : "WAITING_FOR_USER",
                            "CART_CLEAR_UNEXPECTED_DIALOG", afterClear);
                    if (dialog?.Accepted != true)
                        return new(afterClear.Count == 0 ? "UNKNOWN" : "WAITING_FOR_USER",
                            "CART_CLEAR_DIALOG_NOT_CONFIRMED", afterClear);
                    if (afterClear.Count != 0)
                        return new("UNKNOWN", "CART_CLEAR_UNVERIFIED", afterClear);
                }
                finally { clearPage.Response -= CaptureClearResponse; }
            }
            foreach (var target in attempt.Targets)
            {
                stage = "PRODUCT_REOPEN";
                var url = new Uri(target.ProductUrl);
                var response = await page.GotoAsync(url.ToString(), new()
                { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
                if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                    throw new MegaCoffeeLoginRequiredException();
                if (response?.Status != 200 ||
                    await page.Locator("#frmView input[name='goodsCnt[]']").CountAsync() != 1 ||
                    await page.Locator("#cartBtn:visible").CountAsync() != 1)
                    return new("FAILED", "PRODUCT_CONTROLS_CHANGED");
                var quantity = page.Locator("#frmView input[name='goodsCnt[]']");
                stage = "QUANTITY_SET";
                await quantity.FillAsync(target.Quantity.ToString(CultureInfo.InvariantCulture));
                await quantity.PressAsync("Tab");
                if (await quantity.InputValueAsync() != target.Quantity.ToString(CultureInfo.InvariantCulture))
                    return new("FAILED", "QUANTITY_LIMIT_OR_CONTROL_CHANGED");
                mutationStarted = true;
                stage = "CART_ADD_CLICK";
                try { await page.Locator("#cartBtn").ClickAsync(new() { Timeout = 10000 }); }
                catch (PlaywrightException)
                {
                    // Read the site cart below to resolve a navigation/response race. No re-add.
                }
                stage = "CART_ADD_READ";
                var addRead = await ReadAfterMutationAsync(page, login);
                page = addRead.Page;
                dialogs.Attach(page);
                var current = addRead.Items;
                if (dialogs.HasUnexpected) return new("UNKNOWN", "UNEXPECTED_DIALOG_AFTER_ADD", current);
                if (!current.Any(x => x.ExternalProductId == target.ExternalProductId &&
                    x.OptionKey == target.OptionKey && x.Quantity == target.Quantity))
                    return new("FAILED", "SITE_CART_ITEM_MISMATCH");
            }
            stage = "FINAL_CART_READ";
            if (dialogs.HasUnexpected) return new("UNKNOWN", "UNEXPECTED_DIALOG_BEFORE_FINAL_READ");
            return Matches(await ReadAsync(page, login), attempt.Targets)
                ? new("READY", "VERIFIED") : new("FAILED", "SITE_CART_MISMATCH");
        }
        catch (MegaCoffeeLoginRequiredException)
        { return new(mutationStarted ? "UNKNOWN" : "FAILED", "LOGIN_REQUIRED_" + stage); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException)
        { return new(mutationStarted ? "UNKNOWN" : "FAILED", stage + "_" + ex.GetType().Name); }
    }

    private static async Task<(IPage Page, IReadOnlyList<SiteCartEntry> Items)> ReadAfterMutationAsync(
        IPage page, MegaCoffeeLoginProbe login)
    {
        for (int attempt = 0; ; attempt++)
        {
            if (page.IsClosed) page = await page.Context.NewPageAsync();
            try { return (page, await ReadAsync(page, login)); }
            catch (PlaywrightException) when (attempt < 2)
            {
                // A parent-page reload can interrupt a read after the hidden frame completes.
                // Retry only this read. Never repeat the site-cart mutation.
                if (!page.IsClosed)
                    try { await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 5000 }); }
                    catch (PlaywrightException) { }
            }
        }
    }
}
