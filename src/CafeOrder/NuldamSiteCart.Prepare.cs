using System.Globalization;
using Microsoft.Playwright;

namespace CafeOrder;

internal static partial class NuldamSiteCart
{
    private const string ClearConfirm = "장바구니를 비우시겠습니까?";

    internal static async Task<SiteCartPreparationResult> PrepareAsync(IPage page, SiteCartAttempt attempt,
        NuldamLoginProbe login)
    {
        if (attempt.SupplierId != "nuldam" || attempt.Targets.Count == 0 ||
            attempt.Targets.Select(x => x.ExternalProductId).Distinct().Count() != attempt.Targets.Count)
            return new("FAILED", "INVALID_SNAPSHOT");

        IReadOnlyList<SiteCartEntry> current;
        try
        {
            // Verify every product and its purchasable unit before changing the site cart.
            foreach (var target in attempt.Targets)
            {
                if (!NuldamProductLookup.TryProductUrl(target.ProductUrl, out var url, out string number) ||
                    number != target.ExternalProductId || target.Quantity <= 0)
                    return new("FAILED", "INVALID_PRODUCT_NUMBER");
                var product = await NuldamProductLookup.NavigateAndReadAsync(page, url!, number, login);
                if (!product.Available) return new("FAILED", "SOLD_OUT");
                if (product.Price != target.Price) return new("FAILED", "PRICE_CHANGED");
                if (product.Name != target.Name || product.PackageKey != target.OptionKey ||
                    product.InitialQuantity != 1 || product.HasSelectableOptions)
                    return new("WAITING_FOR_USER", "OPTION_OR_PURCHASE_UNIT_REQUIRES_REVIEW");
            }
            current = await ReadAsync(page, login);
        }
        catch (MegaCoffeeLoginRequiredException) { return new("FAILED", "LOGIN_REQUIRED_BEFORE_MUTATION"); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException or FormatException)
        { return new("FAILED", "PRECHECK_" + ex.GetType().Name); }

        bool mutated = false;
        string stage = "CART_CLEAR";
        try
        {
            if (current.Count > 0)
            {
                var clear = page.Locator("a.btnNormal[onclick='Basket.emptyBasket()']:visible");
                if (await clear.CountAsync() != 1)
                    return new("FAILED", "CART_CLEAR_CONTROL_UNVERIFIED", current);
                using var dialog = new NuldamClearDialog(page);
                stage = "CART_CLEAR_CLICK";
                mutated = true;
                try { await clear.ClickAsync(new() { Timeout = 12000 }); }
                catch (PlaywrightException) { /* The site may have changed; read it before any further action. */ }
                bool confirmed = await dialog.WaitAsync(TimeSpan.FromSeconds(12));
                stage = "CART_CLEAR_READ";
                var after = await ReadAfterMutationAsync(page, login);
                if (!confirmed || dialog.Unexpected || after.Count != 0)
                    return new("UNKNOWN", "CART_CLEAR_UNVERIFIED", after);
                current = after;
            }
            else if ((await ReadAsync(page, login)).Count != 0)
                return new("FAILED", "CART_CHANGED_BEFORE_ADD");

            foreach (var target in attempt.Targets)
            {
                stage = "PRODUCT_OPEN";
                var product = await NuldamProductLookup.NavigateAndReadAsync(page,
                    new Uri(target.ProductUrl), target.ExternalProductId, login);
                if (!product.Available || product.Price != target.Price || product.Name != target.Name ||
                    product.PackageKey != target.OptionKey || product.InitialQuantity != 1 ||
                    product.HasSelectableOptions)
                    return new(mutated ? "UNKNOWN" : "FAILED", "PRODUCT_CHANGED_BEFORE_ADD", current);

                stage = "QUANTITY_SET";
                var quantityInput = page.Locator("#quantity[name='quantity_opt[]']");
                var plus = page.Locator("a.up.QuantityUp:visible");
                if (await plus.CountAsync() != 1) return new(mutated ? "UNKNOWN" : "FAILED", "QUANTITY_CONTROL_UNVERIFIED", current);
                for (int number = 1; number < target.Quantity; number++)
                {
                    await plus.ClickAsync(new() { Timeout = 10000 });
                    if (await quantityInput.InputValueAsync() != (number + 1).ToString(CultureInfo.InvariantCulture))
                        return new(mutated ? "UNKNOWN" : "FAILED", "QUANTITY_SET_UNVERIFIED", current);
                }

                var add = page.Locator("a.sub_cart.move:visible");
                if (await add.CountAsync() != 1 || Normalize(await add.InnerTextAsync()) != "ADD TO CART")
                    return new(mutated ? "UNKNOWN" : "FAILED", "CART_ADD_CONTROL_UNVERIFIED", current);
                stage = "CART_ADD_CLICK";
                mutated = true;
                using var dialogs = new NuldamUnexpectedDialog(page);
                try { await add.ClickAsync(new() { Timeout = 12000 }); }
                catch (PlaywrightException) { /* Never retry an uncertain add. */ }
                stage = "CART_ADD_POPUP";
                if (dialogs.Unexpected) return new("UNKNOWN", "UNEXPECTED_CART_ADD_DIALOG", current);
                var move = page.GetByText("장바구니 이동", new() { Exact = true });
                try { await move.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 }); }
                catch (PlaywrightException) { /* Re-read the site cart to determine whether the add took effect. */ }
                if (await move.IsVisibleAsync())
                    await move.ClickAsync(new() { Timeout = 10000 });
                if (dialogs.Unexpected) return new("UNKNOWN", "UNEXPECTED_CART_ADD_DIALOG", current);
                stage = "CART_ADD_READ";
                var after = await ReadAfterMutationAsync(page, login);
                if (after.Count != current.Count + 1 ||
                    !current.All(old => after.Any(item => item.ExternalProductId == old.ExternalProductId &&
                        item.OptionKey == old.OptionKey && item.Quantity == old.Quantity &&
                        item.UnitPrice == old.UnitPrice && item.Name == old.Name)) ||
                    !after.Any(item => item.ExternalProductId == target.ExternalProductId &&
                        item.OptionKey == target.OptionKey && item.Quantity == target.Quantity &&
                        item.UnitPrice == target.Price && item.Name == target.Name))
                    return new("UNKNOWN", "CART_ADD_UNVERIFIED", after);
                current = after;
            }
            var final = await ReadAfterMutationAsync(page, login);
            return Matches(final, attempt.Targets)
                ? new("READY", "VERIFIED") : new("UNKNOWN", "FINAL_CART_MISMATCH", final);
        }
        catch (MegaCoffeeLoginRequiredException)
        { return new(mutated ? "UNKNOWN" : "FAILED", "LOGIN_REQUIRED_" + stage); }
        catch (Exception ex) when (ex is PlaywrightException or InvalidDataException or TimeoutException or FormatException)
        { return new(mutated ? "UNKNOWN" : "FAILED", stage + "_" + ex.GetType().Name); }
    }

    private static async Task<IReadOnlyList<SiteCartEntry>> ReadAfterMutationAsync(IPage page,
        NuldamLoginProbe login)
    {
        try { return await ReadAsync(page, login); }
        catch (PlaywrightException)
        {
            // A site redirect can overtake the first read. Repeat only the read, never the write.
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 12000 });
            return await ReadAsync(page, login);
        }
    }

    private sealed class NuldamClearDialog : IDisposable
    {
        private readonly IPage page;
        private readonly TaskCompletionSource<bool> seen = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Unexpected { get; private set; }

        internal NuldamClearDialog(IPage page) { this.page = page; page.Dialog += Handle; }

        private async void Handle(object? _, IDialog dialog)
        {
            bool expected = dialog.Type == "confirm" && Normalize(dialog.Message) == ClearConfirm;
            try
            {
                if (expected) await dialog.AcceptAsync();
                else { Unexpected = true; await dialog.DismissAsync(); }
                seen.TrySetResult(expected);
            }
            catch (PlaywrightException) { Unexpected = true; seen.TrySetResult(false); }
        }

        internal async Task<bool> WaitAsync(TimeSpan timeout)
        {
            try { return await seen.Task.WaitAsync(timeout); }
            catch (TimeoutException) { return false; }
        }

        public void Dispose() => page.Dialog -= Handle;
    }

    private sealed class NuldamUnexpectedDialog : IDisposable
    {
        private readonly IPage page;
        internal bool Unexpected { get; private set; }
        internal NuldamUnexpectedDialog(IPage page) { this.page = page; page.Dialog += Handle; }
        private async void Handle(object? _, IDialog dialog)
        {
            Unexpected = true;
            try { await dialog.DismissAsync(); } catch (PlaywrightException) { }
        }
        public void Dispose() => page.Dialog -= Handle;
    }
}
