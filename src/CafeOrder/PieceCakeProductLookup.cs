using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal sealed record PieceCakePageProduct(string Name, decimal Price, string DisplayPrice,
    string ProductUrl, Uri ImageUri, bool Available, string PurchaseUnit, int InitialQuantity)
{
    internal bool CartConfigured { get; init; }
}

internal static class PieceCakeProductLookup
{
    private static readonly Regex ProductNumber = new(@"^PD[0-9]{1,12}$", RegexOptions.CultureInvariant);
    private static readonly Regex Money = new(@"^[0-9]{1,3}(?:,[0-9]{3})*$|^[0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex PurchaseUnit = new(@"^\((BOX|EA) [1-9][0-9]*개입\)$", RegexOptions.CultureInvariant);
    private static readonly Regex LegacyPrice = new(@"^([0-9]{1,3}(?:,[0-9]{3})*|[0-9]+) (\((?:BOX|EA) [1-9][0-9]*개입\))$",
        RegexOptions.CultureInvariant);

    internal static string DisplayName(string rawName, string unit)
    {
        string name = Normalize(rawName);
        unit = Normalize(unit);
        if (name.Length == 0 || !PurchaseUnit.IsMatch(unit))
            throw new InvalidDataException("파미유 상품명 또는 판매 단위를 확인할 수 없습니다.");
        if (name.EndsWith(unit, StringComparison.Ordinal)) return name;
        if (Regex.IsMatch(name, @"\((?:BOX|EA) [1-9][0-9]*개입\)$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("파미유 상품명과 판매 단위가 다릅니다.");
        return name + " " + unit;
    }

    internal static bool TryNormalizeLegacy(Product product, out string name, out string priceText)
    {
        name = ""; priceText = "";
        if (product.Supplier.Id != "piece" || !TryProductUrl(product.Url, out _, out _) ||
            product.DisplayPrice == null) return false;
        var match = LegacyPrice.Match(product.DisplayPrice.Trim());
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(",", ""),
            NumberStyles.None, CultureInfo.InvariantCulture, out decimal amount) || amount != product.Price)
            return false;
        name = DisplayName(product.Name, match.Groups[2].Value);
        priceText = match.Groups[1].Value + "원";
        return true;
    }

    internal static bool IsPieceHost(string text) => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        uri.Host is "www.piececake.co.kr" or "piececake.co.kr";

    internal static bool TryProductUrl(string text, out Uri? uri, out string productNumber)
    {
        uri = null; productNumber = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps || parsed.Host is not ("www.piececake.co.kr" or "piececake.co.kr") ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0 ||
            parsed.AbsolutePath != "/product/product_view") return false;
        var numbers = parsed.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)).Where(pair => pair[0] == "prodNo").ToArray();
        if (numbers.Length != 1 || numbers[0].Length != 2 || !ProductNumber.IsMatch(numbers[0][1])) return false;
        productNumber = numbers[0][1];
        uri = new Uri($"https://www.piececake.co.kr/product/product_view?prodNo={productNumber}");
        return true;
    }

    internal static async Task<PieceCakePageProduct> ReadPageAsync(IPage page, string expectedProductNumber)
    {
        if (!TryProductUrl(page.Url, out var url, out var actual) || actual != expectedProductNumber)
            throw new InvalidDataException("파미유 상품 페이지가 일치하지 않습니다.");
        await page.WaitForFunctionAsync("""
            () => !!document.querySelector('#prdView .infoBox .name')?.textContent.trim() &&
              !!document.querySelector('#prdView .infoBox .price .value')?.textContent.trim() &&
              !!document.querySelector('#prdView img.prdImage')?.getAttribute('src')
            """, null, new() { Timeout = 15000 });
        var box = page.Locator("#prdView");
        if (await box.CountAsync() != 1) throw new InvalidDataException("파미유 상품 정보를 확인할 수 없습니다.");
        string name = Normalize(await box.Locator(".infoBox .name").InnerTextAsync());
        string priceText = Normalize(await box.Locator(".infoBox .price .value").InnerTextAsync());
        string unit = Normalize(await box.Locator(".infoBox .price .box").InnerTextAsync());
        if (name.Length is 0 or > 500) throw new InvalidDataException("파미유 상품명을 확인할 수 없습니다.");
        if (!Money.IsMatch(priceText) || !decimal.TryParse(priceText.Replace(",", ""),
            NumberStyles.None, CultureInfo.InvariantCulture, out decimal price))
            throw new InvalidDataException("파미유 숫자 판매가를 확인할 수 없습니다.");
        if (unit.Length is 0 or > 100) throw new InvalidDataException("파미유 구매 단위를 확인할 수 없습니다.");
        name = DisplayName(name, unit);
        string display = priceText + "원";
        var sold = box.Locator(".sold_overlay");
        bool soldVisible = await sold.CountAsync() == 1 && await sold.IsVisibleAsync();
        bool cartVisible = await box.Locator("#btnCart:visible").CountAsync() == 1;
        if (soldVisible == cartVisible)
            throw new InvalidDataException("파미유 품절 상태를 확인할 수 없습니다.");
        var quantity = box.Locator("#txtSaleQty");
        if (await quantity.CountAsync() != 1 || !int.TryParse(await quantity.InputValueAsync(),
            NumberStyles.None, CultureInfo.InvariantCulture, out int initialQuantity) || initialQuantity <= 0)
            throw new InvalidDataException("파미유 구매 수량 단위를 확인할 수 없습니다.");
        string? source = await box.Locator("img.prdImage").GetAttributeAsync("src");
        if (source == null || !Uri.TryCreate(url, source, out var imageUri) ||
            imageUri.Scheme != Uri.UriSchemeHttps || imageUri.Host != "www.piececake.co.kr" ||
            imageUri.AbsolutePath != "/prodImg" || imageUri.UserInfo.Length != 0 ||
            imageUri.Fragment.Length != 0 ||
            !imageUri.Query.StartsWith("?p=", StringComparison.Ordinal) || imageUri.Query.Length > 260)
            throw new InvalidDataException("파미유 상품 이미지 주소를 확인할 수 없습니다.");
        return new(name, price, display, url!.ToString(), imageUri, !soldVisible, unit, initialQuantity);
    }

    internal static async Task<MegaCoffeeProductSnapshot> FetchAsync(IPage page, IBrowserContext context,
        Uri url, string productNumber, PieceCakeLoginProbe login)
    {
        var parsed = await NavigateAndReadAsync(page, url, productNumber, login);
        var imageResponse = await context.APIRequest.GetAsync(parsed.ImageUri.ToString(),
            new() { Timeout = 15000, MaxRedirects = 0 });
        if (imageResponse.Status != 200 || !imageResponse.Headers.TryGetValue("content-type", out var contentType) ||
            !new[] { "image/jpeg", "image/png", "image/webp" }.Any(type =>
                contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("파미유 상품 이미지 파일을 확인할 수 없습니다.");
        byte[] bytes = await imageResponse.BodyAsync();
        if (bytes.Length is < 100 or > 8_000_000)
            throw new InvalidDataException("파미유 상품 이미지 크기가 올바르지 않습니다.");
        return new(parsed.Name, parsed.Price, parsed.DisplayPrice, parsed.ProductUrl,
            parsed.ImageUri.ToString(), bytes, parsed.Available);
    }

    internal static async Task<PieceCakePageProduct> NavigateAndReadAsync(IPage page, Uri url,
        string productNumber, PieceCakeLoginProbe login)
    {
        if (!TryProductUrl(url.ToString(), out _, out var requested) || requested != productNumber)
            throw new InvalidDataException("파미유 상품 URL과 상품 코드가 일치하지 않습니다.");
        var detailResponse = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(object? _, IResponse response)
        {
            if (Uri.TryCreate(response.Url, UriKind.Absolute, out var uri) &&
                uri.Host == "www.piececake.co.kr" && uri.AbsolutePath == "/selectList" &&
                response.Request.PostData?.Contains("getProdDtpt", StringComparison.Ordinal) == true)
                detailResponse.TrySetResult(response);
        }
        page.Response += Capture;
        try
        {
            var navigation = await page.GotoAsync(url.ToString(), new()
            { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            var state = await login.CheckAsync(page);
            if (state == SupplierLoginState.LoginRequired) throw new MegaCoffeeLoginRequiredException();
            if (navigation?.Status != 200 || state != SupplierLoginState.LoggedIn)
                throw new InvalidDataException("파미유 상품 페이지 또는 로그인을 확인할 수 없습니다.");
            var reply = await detailResponse.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (reply.Status != 200) throw new InvalidDataException("파미유 상품 상세 응답이 실패했습니다.");
            await reply.FinishedAsync();
            using var json = JsonDocument.Parse(await reply.TextAsync());
            if (!json.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array ||
                list.GetArrayLength() != 1)
                throw new InvalidDataException("파미유 상품 상세 응답을 확인할 수 없습니다.");
            var item = list[0];
            if (item.GetProperty("PROD_NO").GetString() != productNumber)
                throw new InvalidDataException("파미유 상품 코드가 페이지와 다릅니다.");
            var parsed = await ReadPageAsync(page, productNumber);
            if (DisplayName(item.GetProperty("PROD_KNM").GetString() ?? "", parsed.PurchaseUnit) != parsed.Name ||
                !decimal.TryParse(item.GetProperty("UNIT_PRICE").ToString(), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal responsePrice) || responsePrice != parsed.Price ||
                item.GetProperty("SOLDOUT").GetString() is not ("Y" or "N") ||
                (item.GetProperty("SOLDOUT").GetString() == "N") != parsed.Available ||
                !int.TryParse(item.GetProperty("DEFAULT_CNT").ToString(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int defaultCount) || defaultCount != parsed.InitialQuantity)
                throw new InvalidDataException("파미유 상품 상세 응답과 화면의 이름·가격·품절·구매 수량이 다릅니다.");
            bool configured = item.TryGetProperty("CUST_PROD_NO", out var customerProduct) &&
                customerProduct.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
                !string.IsNullOrWhiteSpace(customerProduct.ToString());
            return parsed with { CartConfigured = configured };
        }
        finally { page.Response -= Capture; }
    }

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
