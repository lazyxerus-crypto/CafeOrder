using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

namespace CafeOrder;

internal static partial class PieceCakeSiteCart
{
    private const string DeleteConfirm = "해당 제품을 장바구니에서 삭제 하시겠습니까?";
    private const string AddConfirm = "제품이 장바구니에 추가되었습니다.\n장바구니로 이동하시겠습니까?";

    internal static async Task<SiteCartPreparationResult> PrepareAsync(IPage page, SiteCartAttempt attempt,
        PieceCakeLoginProbe login)
    {
        if (attempt.SupplierId != "piece" || attempt.Targets.Count == 0 ||
            attempt.Targets.Select(x => x.ExternalProductId).Distinct().Count() != attempt.Targets.Count)
            return new("FAILED", "INVALID_SNAPSHOT");
        IReadOnlyList<SiteCartEntry> current;
        try
        {
            // Read every product before changing the existing site cart.
            foreach (var target in attempt.Targets)
            {
                if (!PieceCakeProductLookup.TryProductUrl(target.ProductUrl, out var url, out var number) ||
                    number != target.ExternalProductId || target.Quantity <= 0)
                    return new("FAILED", "INVALID_PRODUCT_NUMBER");
                var product = await PieceCakeProductLookup.NavigateAndReadAsync(page, url!, number, login);
                if (!product.Available) return new("FAILED", "SOLD_OUT");
                if (product.Price != target.Price) return new("FAILED", "PRICE_CHANGED");
                if (!product.CartConfigured)
                    return new("WAITING_FOR_USER", "CART_PRICE_CONFIGURATION_REQUIRED");
                if (PurchaseKey(product.Name) != target.OptionKey || product.InitialQuantity != 1 ||
                    await page.Locator("#prdView select:visible").CountAsync() != 0)
                    return new("WAITING_FOR_USER", "OPTION_OR_PURCHASE_UNIT_REQUIRES_REVIEW");
                string? maximum = await page.Locator("#txtSaleQty").GetAttributeAsync("max");
                if (!int.TryParse(maximum, NumberStyles.None, CultureInfo.InvariantCulture, out int max) ||
                    target.Quantity > max)
                    return new("WAITING_FOR_USER", "QUANTITY_LIMIT_REQUIRES_REVIEW");
            }
            current = await ReadAsync(page, login);
        }
        catch (MegaCoffeeLoginRequiredException) { return new("FAILED", "LOGIN_REQUIRED_BEFORE_MUTATION"); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException or FormatException or JsonException)
        { return new("FAILED", "PRECHECK_" + ex.GetType().Name); }

        bool mutated = false;
        string stage = "CART_CLEAR";
        try
        {
            // The user starts a fresh site order. Delete each observed row once, then verify an empty list.
            while (current.Count > 0)
            {
                var old = current[0];
                var row = page.Locator("#prodTbody tr").First;
                if (await RowCodeAsync(row) != old.ExternalProductId)
                    return new(mutated ? "UNKNOWN" : "FAILED", "CART_ROW_CHANGED", current);
                stage = "CART_DELETE_CLICK";
                mutated = true;
                bool action = await ClickMutationAsync(page,
                    () => row.Locator("._btn_delete").ClickAsync(new() { Timeout = 12000 }),
                    "/delete", "delProdCart", DeleteConfirm);
                stage = "CART_DELETE_READ";
                var after = await ReadAsync(page, login);
                if (!action || !SameContents(current.Skip(1).ToArray(), after))
                    return new("UNKNOWN", "CART_DELETE_UNVERIFIED", after);
                current = after;
            }
            if ((await ReadAsync(page, login)).Count != 0)
                return new("UNKNOWN", "CART_NOT_EMPTY");

            foreach (var target in attempt.Targets)
            {
                stage = "PRODUCT_OPEN";
                var parsed = await PieceCakeProductLookup.NavigateAndReadAsync(page,
                    new Uri(target.ProductUrl), target.ExternalProductId, login);
                if (!parsed.Available || parsed.Price != target.Price ||
                    PurchaseKey(parsed.Name) != target.OptionKey || parsed.InitialQuantity != 1 ||
                    !parsed.CartConfigured)
                    return new(mutated ? "UNKNOWN" : "FAILED", "PRODUCT_CHANGED_BEFORE_ADD");
                stage = "QUANTITY_SET";
                var plus = page.Locator("#prdView .selectNum a.plus");
                for (int quantity = 1; quantity < target.Quantity; quantity++)
                {
                    await plus.ClickAsync(new() { Timeout = 10000 });
                    if (await page.Locator("#txtSaleQty").InputValueAsync() != (quantity + 1).ToString(CultureInfo.InvariantCulture))
                        return new(mutated ? "UNKNOWN" : "FAILED", "PURCHASE_UNIT_CHANGED");
                }
                stage = "CART_ADD_CLICK";
                mutated = true;
                var action = await ClickMutationAsync(page, () => page.Locator("#btnCart").ClickAsync(new() { Timeout = 12000 }),
                    "/insert", "insProdCart", AddConfirm);
                stage = "CART_ADD_READ";
                var after = await ReadAsync(page, login);
                if (!action || !ContainsUnchanged(current, after) ||
                    !after.Any(item => SameTarget(item, target)))
                    return new("UNKNOWN", "CART_ADD_UNVERIFIED", after);
                current = after;
            }
            return Matches(await ReadAsync(page, login), attempt.Targets)
                ? new("READY", "VERIFIED") : new("UNKNOWN", "FINAL_CART_MISMATCH", current);
        }
        catch (MegaCoffeeLoginRequiredException)
        { return new(mutated ? "UNKNOWN" : "FAILED", "LOGIN_REQUIRED_" + stage); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException or FormatException or JsonException)
        { return new(mutated ? "UNKNOWN" : "FAILED", stage + "_" + ex.GetType().Name); }
    }

    private static bool SameTarget(SiteCartEntry item, SiteCartTarget target) =>
        item.ExternalProductId == target.ExternalProductId && item.OptionKey == target.OptionKey &&
        item.Quantity == target.Quantity && item.UnitPrice == target.Price;

    private static bool ContainsUnchanged(IReadOnlyList<SiteCartEntry> before, IReadOnlyList<SiteCartEntry> after) =>
        before.All(item => after.Any(next => next.ExternalProductId == item.ExternalProductId &&
            next.OptionKey == item.OptionKey && next.Quantity == item.Quantity && next.UnitPrice == item.UnitPrice));

    private static bool SameContents(IReadOnlyList<SiteCartEntry> expected, IReadOnlyList<SiteCartEntry> actual)
    {
        if (expected.Count != actual.Count) return false;
        var unmatched = actual.ToList();
        foreach (var item in expected)
        {
            int index = unmatched.FindIndex(other => other.ExternalProductId == item.ExternalProductId &&
                other.OptionKey == item.OptionKey && other.Quantity == item.Quantity && other.UnitPrice == item.UnitPrice);
            if (index < 0) return false;
            unmatched.RemoveAt(index);
        }
        return true;
    }

    private static Task<string> RowCodeAsync(ILocator row) => row.EvaluateAsync<string>(
        "row => String(window.jQuery(row).data('prod_no')||'')");

    private static async Task<bool> ClickMutationAsync(IPage page, Func<Task> click,
        string path, string queryId, string? expectedConfirm)
    {
        var response = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(object? _, IResponse item)
        {
            if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Host == CartUrl.Host &&
                uri.AbsolutePath == path && item.Request.PostData?.Contains(queryId, StringComparison.Ordinal) == true)
                response.TrySetResult(item);
        }
        using var dialogs = new PieceCartDialogMonitor(page, expectedConfirm);
        page.Response += Capture;
        try
        {
            try { await click(); }
            catch (PlaywrightException) { /* Read the site; never click a mutation twice. */ }
            bool confirmed = expectedConfirm == null || await dialogs.WaitForExpectedAsync(TimeSpan.FromSeconds(12));
            IResponse reply;
            try { reply = await response.Task.WaitAsync(TimeSpan.FromSeconds(12)); }
            catch (TimeoutException) { return false; }
            await reply.FinishedAsync();
            return confirmed && !dialogs.Unexpected && reply.Status == 200;
        }
        finally { page.Response -= Capture; }
    }

    private sealed class PieceCartDialogMonitor : IDisposable
    {
        private readonly IPage page;
        private readonly string? expected;
        private readonly TaskCompletionSource<bool> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Unexpected { get; private set; }

        internal PieceCartDialogMonitor(IPage page, string? expected)
        { this.page = page; this.expected = expected; page.Dialog += Handle; }

        private async void Handle(object? _, IDialog dialog)
        {
            bool matched = expected != null && dialog.Type == "confirm" &&
                NormalizeDialog(dialog.Message) == NormalizeDialog(expected);
            try
            {
                if (matched) await dialog.AcceptAsync();
                else { Unexpected = true; await dialog.DismissAsync(); }
                seen.TrySetResult(matched);
            }
            catch (PlaywrightException) { Unexpected = true; seen.TrySetResult(false); }
        }

        internal async Task<bool> WaitForExpectedAsync(TimeSpan timeout)
        {
            try { return await seen.Task.WaitAsync(timeout); }
            catch (TimeoutException) { return false; }
        }

        public void Dispose() => page.Dialog -= Handle;
    }

    private static string NormalizeDialog(string message) =>
        System.Text.RegularExpressions.Regex.Replace(message, @"\s+", " ").Trim();
}
