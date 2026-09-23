using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal static partial class NuldamProductLookup
{
    private static readonly Regex ProductNumber = new(@"^[0-9]{1,12}$", RegexOptions.CultureInvariant);
    private static readonly Regex PriceText = new(@"^([0-9]{1,3}(?:,[0-9]{3})*|[0-9]+)원$", RegexOptions.CultureInvariant);
    private static readonly Regex PackageCount = new(@"\(([1-9][0-9]*)개입\)$", RegexOptions.CultureInvariant);

    internal static bool IsNuldamHost(string text) => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        uri.Host is "nuldampartners.com" or "www.nuldampartners.com";

    internal static bool TryProductUrl(string text, out Uri? uri, out string productNumber)
    {
        uri = null; productNumber = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.Host is not ("nuldampartners.com" or "www.nuldampartners.com") ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0 ||
            parsed.AbsolutePath != "/product/detail.html") return false;
        var numbers = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).Where(pair => pair[0] == "product_no").ToArray();
        if (numbers.Length != 1 || numbers[0].Length != 2 || !ProductNumber.IsMatch(numbers[0][1])) return false;
        productNumber = numbers[0][1].TrimStart('0');
        if (productNumber.Length == 0) return false;
        uri = new Uri("https://nuldampartners.com/product/detail.html?product_no=" + productNumber);
        return true;
    }

    // A pack's contents are descriptive; site cart quantity still counts purchasable packs.
    internal static string PackageKey(string name)
    {
        var match = PackageCount.Match(name);
        return match.Success ? "PACK:" + match.Groups[1].Value : "NO_PACK_LABEL";
    }

    internal static async Task<NuldamPageProduct> NavigateAndReadAsync(IPage page, Uri url,
        string productNumber, NuldamLoginProbe login)
    {
        if (!TryProductUrl(url.ToString(), out _, out string requested) || requested != productNumber)
            throw new InvalidDataException("널담 상품 URL과 상품 코드가 일치하지 않습니다.");
        var navigation = await page.GotoAsync(url.ToString(), new()
        { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        var state = await login.CheckAsync(page);
        if (state == SupplierLoginState.LoginRequired) throw new MegaCoffeeLoginRequiredException();
        if (navigation?.Status != 200 || state != SupplierLoginState.LoggedIn ||
            !TryProductUrl(page.Url, out _, out string actual) || actual != productNumber)
            throw new InvalidDataException("널담 상품 페이지 또는 로그인 상태를 확인할 수 없습니다.");
        await page.WaitForFunctionAsync("""
            () => !!document.querySelector('.headingArea h2')?.textContent.trim() &&
              !!document.querySelector('#span_product_price_text')?.textContent.trim() &&
              !!document.querySelector('img.BigImage')?.getAttribute('src')
            """, null, new() { Timeout = 15000 });
        if (await page.Locator(".headingArea h2").CountAsync() != 1 ||
            await page.Locator("#span_product_price_text").CountAsync() != 1 ||
            await page.Locator("img.BigImage").CountAsync() != 1)
            throw new InvalidDataException("널담 상품명·가격·이미지 영역을 확인할 수 없습니다.");
        string name = Normalize(await page.Locator(".headingArea h2").InnerTextAsync());
        string displayedPrice = Normalize(await page.Locator("#span_product_price_text").InnerTextAsync());
        string metaName = Normalize(await page.Locator("meta[property='og:title']").GetAttributeAsync("content") ?? "");
        if (name.Length is 0 or > 500 || metaName != name)
            throw new InvalidDataException("널담 상품명을 화면과 메타정보에서 함께 확인할 수 없습니다.");
        var priceMatch = PriceText.Match(displayedPrice);
        if (!priceMatch.Success || !decimal.TryParse(priceMatch.Groups[1].Value.Replace(",", ""),
            NumberStyles.None, CultureInfo.InvariantCulture, out decimal price))
            throw new InvalidDataException("널담 숫자 판매가를 확인할 수 없습니다.");
        bool addVisible = await page.Locator("a.sub_cart.move:visible").CountAsync() == 1;
        bool soldVisible = await page.Locator(".sub_soldout.move:visible").CountAsync() == 1;
        if (addVisible == soldVisible) throw new InvalidDataException("널담 품절 상태를 확인할 수 없습니다.");
        int initialQuantity = 0;
        if (addVisible)
        {
            if (await page.Locator("#quantity[name='quantity_opt[]']").CountAsync() != 1 ||
                !int.TryParse(await page.Locator("#quantity").InputValueAsync(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out initialQuantity) || initialQuantity <= 0)
                throw new InvalidDataException("널담 주문 수량 입력 상태를 확인할 수 없습니다.");
        }
        string? imageSource = await page.Locator("img.BigImage").GetAttributeAsync("src");
        if (imageSource == null || !Uri.TryCreate(url, imageSource, out var imageUri) ||
            imageUri.Scheme != Uri.UriSchemeHttps || imageUri.Host is not ("nuldampartners.com" or "www.nuldampartners.com") ||
            !imageUri.AbsolutePath.StartsWith("/web/product/big/", StringComparison.Ordinal) ||
            imageUri.UserInfo.Length != 0 || imageUri.Fragment.Length != 0)
            throw new InvalidDataException("널담 상품 이미지 주소를 확인할 수 없습니다.");
        return new(name, price, displayedPrice, url.ToString(), imageUri, addVisible,
            PackageKey(name), initialQuantity, await page.Locator("select:visible").CountAsync() != 0);
    }

    internal static async Task<MegaCoffeeProductSnapshot> FetchAsync(IPage page, IBrowserContext context,
        Uri url, string productNumber, NuldamLoginProbe login)
    {
        var product = await NavigateAndReadAsync(page, url, productNumber, login);
        var response = await context.APIRequest.GetAsync(product.ImageUri.ToString(),
            new() { Timeout = 15000, MaxRedirects = 0 });
        if (response.Status != 200 || !response.Headers.TryGetValue("content-type", out string? contentType) ||
            !new[] { "image/jpeg", "image/png", "image/webp" }.Any(type =>
                contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("널담 상품 이미지 파일을 확인할 수 없습니다.");
        byte[] image = await response.BodyAsync();
        if (image.Length is < 100 or > 8_000_000)
            throw new InvalidDataException("널담 상품 이미지 크기를 확인할 수 없습니다.");
        return new(product.Name, product.Price, product.DisplayPrice, product.ProductUrl,
            product.ImageUri.ToString(), image, product.Available);
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}

internal sealed record NuldamPageProduct(string Name, decimal Price, string DisplayPrice,
    string ProductUrl, Uri ImageUri, bool Available, string PackageKey, int InitialQuantity,
    bool HasSelectableOptions);
