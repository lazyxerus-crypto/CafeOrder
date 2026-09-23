using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal enum MegaProductLookupStatus { Success, InvalidUrl, LoginRequired, Failed }
internal sealed record MegaCoffeeProductSnapshot(string Name, decimal Price, string DisplayPrice,
    string ProductUrl, string ImageUrl, byte[] ImageBytes, bool Available);
internal sealed record MegaProductLookupResult(MegaProductLookupStatus Status, MegaCoffeeProductSnapshot? Product = null);
internal sealed record MegaCoffeePageProduct(string Name, decimal Price, string DisplayPrice,
    string ProductUrl, Uri ImageUri, bool Available);

internal static class MegaCoffeeProductLookup
{
    private static readonly Regex GoodsQuery = new(@"^\?goodsNo=([0-9]{1,20})$", RegexOptions.CultureInvariant);
    private static readonly Regex Money = new(@"^((?:[0-9]{1,3}(?:,[0-9]{3})+)|(?:[0-9]+))\s*원$", RegexOptions.CultureInvariant);

    internal static bool IsMegaHost(string text) => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        uri.Host is "www.megacoffee.co.kr" or "megacoffee.co.kr";

    internal static bool TryProductUrl(string text, out Uri? uri, out string goodsNo)
    {
        uri = null; goodsNo = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.Host != "www.megacoffee.co.kr" ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0 ||
            parsed.AbsolutePath != "/goods/goods_view.php") return false;
        var match = GoodsQuery.Match(parsed.Query);
        if (!match.Success) return false;
        uri = parsed; goodsNo = match.Groups[1].Value; return true;
    }

    internal static async Task<MegaCoffeePageProduct> ReadPageAsync(IPage page, string expectedGoodsNo)
    {
        if (!TryProductUrl(page.Url, out var productUrl, out var actualGoodsNo) ||
            actualGoodsNo != expectedGoodsNo) throw new InvalidDataException("상품 페이지가 일치하지 않습니다.");
        var box = page.Locator(".item_info_box");
        if (await box.CountAsync() != 1) throw new InvalidDataException("상품 정보가 확인되지 않습니다.");
        string? code = await box.EvaluateAsync<string?>("""
            box => [...box.querySelectorAll('div')]
              .filter(node => node.children.length === 0)
              .map(node => node.textContent.trim())
              .find(text => /^상품코드\s*:\s*[0-9]+$/.test(text)) || null
            """);
        if (code == null || Regex.Match(code, @"[0-9]+$").Value != expectedGoodsNo)
            throw new InvalidDataException("상품 코드가 일치하지 않습니다.");

        var title = box.Locator(".item_detail_tit > span");
        var price = box.Locator("dl.item_price dd");
        if (await title.CountAsync() != 1 || await price.CountAsync() != 1)
            throw new InvalidDataException("상품명 또는 판매가를 확인할 수 없습니다.");
        string name = Normalize(await title.InnerTextAsync());
        string displayPrice = Normalize(await price.InnerTextAsync());
        var amount = Money.Match(displayPrice);
        if (name.Length == 0 || name.Length > 500 || !amount.Success ||
            !decimal.TryParse(amount.Groups[1].Value.Replace(",", ""), NumberStyles.None,
                CultureInfo.InvariantCulture, out decimal numericPrice))
            throw new InvalidDataException("상품명 또는 숫자 판매가를 확인할 수 없습니다.");

        var cartButton = box.Locator("#cartBtn:visible");
        var orderButton = box.Locator("button.btn_add_order:visible");
        bool cartReady = await cartButton.CountAsync() == 1 && await cartButton.IsEnabledAsync();
        bool orderReady = await orderButton.CountAsync() == 1 && await orderButton.IsEnabledAsync();
        bool soldOutLabel = await box.EvaluateAsync<bool>("""
            box => [...box.querySelectorAll('*')].some(node =>
              node.children.length === 0 && node.offsetParent !== null &&
              node.textContent.trim() === '품절')
            """);
        bool available;
        if (cartReady && orderReady && !soldOutLabel) available = true;
        else if (!cartReady && !orderReady && soldOutLabel) available = false;
        else throw new InvalidDataException("품절 상태를 확인할 수 없습니다.");

        var photo = page.Locator(".item_photo_big img");
        if (await photo.CountAsync() == 0) throw new InvalidDataException("상품 이미지를 확인할 수 없습니다.");
        string? source = await photo.First.GetAttributeAsync("src");
        if (source == null || !Uri.TryCreate(productUrl, source, out var imageUri) ||
            imageUri.Scheme != Uri.UriSchemeHttps || imageUri.Host != "megacotr3116.cdn-nhncommerce.com" ||
            imageUri.UserInfo.Length != 0 || imageUri.Query.Length != 0 || imageUri.Fragment.Length != 0 ||
            !imageUri.AbsolutePath.Contains("/" + expectedGoodsNo + "/", StringComparison.Ordinal))
            throw new InvalidDataException("상품 이미지 주소를 확인할 수 없습니다.");
        return new(name, numericPrice, displayPrice, productUrl!.ToString(), imageUri, available);
    }

    internal static async Task<MegaCoffeeProductSnapshot> FetchAsync(IPage page, IBrowserContext context,
        Uri productUrl, string goodsNo, MegaCoffeeLoginProbe login)
    {
        var response = await page.GotoAsync(productUrl.ToString(), new()
        { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        var loginState = await login.CheckAsync(page);
        if (loginState == SupplierLoginState.LoginRequired) throw new MegaCoffeeLoginRequiredException();
        if (loginState != SupplierLoginState.LoggedIn || response?.Status != 200)
            throw new InvalidDataException("상품 페이지를 확인할 수 없습니다.");
        var parsed = await ReadPageAsync(page, goodsNo);
        var imageResponse = await context.APIRequest.GetAsync(parsed.ImageUri.ToString(),
            new() { Timeout = 15000, MaxRedirects = 0 });
        if (imageResponse.Status != 200 || !imageResponse.Headers.TryGetValue("content-type", out var contentType) ||
            !new[] { "image/jpeg", "image/png", "image/webp" }.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("상품 이미지 파일을 확인할 수 없습니다.");
        byte[] bytes = await imageResponse.BodyAsync();
        if (bytes.Length is < 100 or > 8_000_000) throw new InvalidDataException("상품 이미지 크기가 올바르지 않습니다.");
        return new(parsed.Name, parsed.Price, parsed.DisplayPrice, parsed.ProductUrl,
            parsed.ImageUri.ToString(), bytes, parsed.Available);
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}

internal sealed class MegaCoffeeLoginRequiredException : Exception;
