using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal static partial class NuldamSiteCart
{
    private static readonly Uri CartUrl = new("https://nuldampartners.com/order/basket.html");
    private static readonly Regex Won = new(@"^([0-9]{1,3}(?:,[0-9]{3})*|[0-9]+)원$", RegexOptions.CultureInvariant);

    internal static bool Matches(IReadOnlyList<SiteCartEntry> actual, IReadOnlyList<SiteCartTarget> targets)
    {
        if (actual.Count != targets.Count || actual.Select(item => item.ExternalProductId).Distinct().Count() != actual.Count)
            return false;
        return targets.All(target => actual.Any(item => item.ExternalProductId == target.ExternalProductId &&
            item.OptionKey == target.OptionKey && item.Name == target.Name && item.Quantity == target.Quantity &&
            item.UnitPrice == target.Price));
    }

    internal static async Task<IReadOnlyList<SiteCartEntry>> ReadAsync(IPage page, NuldamLoginProbe login)
    {
        var response = await page.GotoAsync(CartUrl.ToString(), new()
        { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
            throw new MegaCoffeeLoginRequiredException();
        if (response?.Status != 200 || new Uri(page.Url).AbsolutePath != CartUrl.AbsolutePath ||
            await page.Locator(".xans-order-basketpackage").CountAsync() == 0)
            throw new InvalidDataException("늘담 장바구니 페이지를 확인할 수 없습니다.");
        var quantityInputs = page.Locator("input[id^='quantity_id_']");
        int count = await quantityInputs.CountAsync();
        if (count == 0)
        {
            // The site's explicit empty message is required; a temporarily missing row is not enough.
            var empty = page.Locator(".xans-order-empty:visible, .xans-order-basketpackage .empty:visible");
            if (await empty.CountAsync() == 0 ||
                !(await empty.First.InnerTextAsync()).Contains("장바구니", StringComparison.Ordinal))
                throw new InvalidDataException("늘담 빈 장바구니 상태를 확인할 수 없습니다.");
            return [];
        }
        var result = new List<SiteCartEntry>(count);
        for (int index = 0; index < count; index++)
        {
            var row = quantityInputs.Nth(index).Locator("xpath=ancestor::tr[1]");
            if (await row.CountAsync() != 1 || await row.Locator(".option:visible").CountAsync() != 0)
                throw new InvalidDataException("늘담 장바구니 상품의 선택 옵션을 확인할 수 없습니다.");
            var links = row.Locator("a[href*='product_no=']");
            if (await links.CountAsync() == 0)
                throw new InvalidDataException("늘담 장바구니 상품 링크를 확인할 수 없습니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int linkIndex = 0; linkIndex < await links.CountAsync(); linkIndex++)
            {
                string? href = await links.Nth(linkIndex).GetAttributeAsync("href");
                if (href == null || !NuldamProductLookup.TryProductUrl(new Uri(CartUrl, href).ToString(), out _, out string id))
                    throw new InvalidDataException("늘담 장바구니 상품 코드를 확인할 수 없습니다.");
                ids.Add(id);
            }
            if (ids.Count != 1 || await row.Locator("a.ec-product-name").CountAsync() != 1 ||
                await row.Locator("div[id^='product_price_div']").CountAsync() != 1)
                throw new InvalidDataException("늘담 장바구니 상품명 또는 단가를 확인할 수 없습니다.");
            string name = Normalize(await row.Locator("a.ec-product-name").InnerTextAsync());
            string priceText = Normalize(await row.Locator("div[id^='product_price_div']").InnerTextAsync());
            var priceMatch = Won.Match(priceText);
            if (name.Length == 0 || !priceMatch.Success ||
                !decimal.TryParse(priceMatch.Groups[1].Value.Replace(",", ""), NumberStyles.None,
                    CultureInfo.InvariantCulture, out decimal unitPrice) ||
                !int.TryParse(await quantityInputs.Nth(index).InputValueAsync(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int quantity) || quantity <= 0)
                throw new InvalidDataException("늘담 장바구니 상품 단가 또는 주문 수량을 확인할 수 없습니다.");
            result.Add(new(ids.Single(), quantity, NuldamProductLookup.PackageKey(name), name, unitPrice));
        }
        return result;
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
