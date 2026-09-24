using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

// MANUAL_BROWSER suppliers are read only. This never uses a login profile or site cart.
internal static class ManualStoreProductLookup
{
    private static readonly Regex Digits = new("^[0-9]{1,20}$", RegexOptions.CultureInvariant);
    private static readonly Regex Money = new(@"(?<amount>[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)\s*원", RegexOptions.CultureInvariant);

    internal static bool IsManualHost(string text) => Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Host == "coupang.com" || uri.Host.EndsWith(".coupang.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host is "brand.naver.com" or "smartstore.naver.com");

    internal static bool TryProductUrl(string text, out string supplierId, out string identity)
    {
        supplierId = identity = "";
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var path = uri.AbsolutePath.Trim('/').Split('/');
        var query = ParseQuery(uri.Query);
        if (uri.Host == "mc.coupang.com" && path.SequenceEqual(["ssr", "sdp", "link"]) &&
            SingleNumber(query, "vendorItemId", out var shortVendor))
        { supplierId = "coupang"; identity = "coupang|vendor:" + shortVendor; return true; }
        if (uri.Host == "www.coupang.com" && path.Length == 3 && path[0] == "vp" && path[1] == "products" &&
            Digits.IsMatch(path[2]) && SingleNumber(query, "vendorItemId", out var vendor))
        { supplierId = "coupang"; identity = "coupang|vendor:" + vendor; return true; }
        if (uri.Host is "brand.naver.com" or "smartstore.naver.com" && path.Length == 3 &&
            path[1] == "products" && Digits.IsMatch(path[2]) && path[0].Length is > 0 and <= 80 &&
            path[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        { supplierId = "naver"; identity = "naver|" + path[0].ToLowerInvariant() + "|" + path[2]; return true; }
        return false;
    }

    private static Dictionary<string, List<string>> ParseQuery(string query)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0]);
            if (!values.TryGetValue(key, out var entries)) values[key] = entries = [];
            entries.Add(pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        }
        return values;
    }
    private static bool SingleNumber(Dictionary<string, List<string>> query, string key, out string number)
    {
        number = "";
        if (!query.TryGetValue(key, out var entries) || entries.Count != 1 || !Digits.IsMatch(entries[0])) return false;
        number = entries[0]; return true;
    }

    internal static async Task<MegaProductLookupResult> LookupAsync(string originalUrl)
    {
        if (!TryProductUrl(originalUrl, out string supplierId, out _))
            return new(MegaProductLookupStatus.InvalidUrl, Reason: "쿠팡·네이버 상품 URL 형식을 확인해주세요.");
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new()
                { Channel = "msedge", Headless = true, ChromiumSandbox = true, Timeout = 20000 });
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(originalUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            string? resolvedUrl = supplierId == "coupang" ? ResolvedCoupangUrl(originalUrl, page.Url) : null;
            if (response?.Status != 200)
                return new MegaProductLookupResult(MegaProductLookupStatus.Failed,
                    Reason: $"상품 페이지 접근 실패 (HTTP {(response == null ? "응답 없음" : response.Status.ToString(CultureInfo.InvariantCulture))}).")
                    { ResolvedProductUrl = resolvedUrl };
            if (!TryProductUrl(page.Url, out string actualSupplier, out string actualIdentity) || actualSupplier != supplierId)
                return new(MegaProductLookupStatus.Failed, Reason: "이동한 페이지가 요청한 판매처의 상품 페이지가 아닙니다.");
            if (supplierId == "naver" && !string.Equals(actualIdentity,
                    ProductUrlIdentity.Key(supplierId, originalUrl), StringComparison.OrdinalIgnoreCase))
                return new(MegaProductLookupStatus.Failed, Reason: "이동한 네이버 상품 ID가 입력한 URL과 다릅니다.");
            var product = supplierId == "naver"
                ? await ReadNaverPageAsync(page, originalUrl)
                : await ReadCoupangPageAsync(page, originalUrl);
            if (product == null)
                return new(MegaProductLookupStatus.Failed, Reason: "상품명·화면 가격·대표 이미지 또는 상품 ID를 안정적으로 확인할 수 없습니다.");
            var imageUri = new Uri(product.ImageUrl);
            bool trustedImage = supplierId == "naver"
                ? imageUri.Host is "shop-phinf.pstatic.net" or "phinf.pstatic.net"
                : imageUri.Host.EndsWith(".coupangcdn.com", StringComparison.OrdinalIgnoreCase);
            if (imageUri.Scheme != Uri.UriSchemeHttps || imageUri.UserInfo.Length != 0 || !trustedImage)
                return new(MegaProductLookupStatus.Failed, Reason: "대표 이미지 출처를 확인할 수 없습니다.");
            var imageResponse = await context.APIRequest.GetAsync(product.ImageUrl, new() { Timeout = 15000, MaxRedirects = 0 });
            if (imageResponse.Status != 200 || !imageResponse.Headers.TryGetValue("content-type", out var contentType) ||
                !new[] { "image/jpeg", "image/png", "image/webp" }.Any(type => contentType.StartsWith(type, StringComparison.OrdinalIgnoreCase)))
                return new(MegaProductLookupStatus.Failed, Reason: $"대표 이미지 다운로드 실패 (HTTP {imageResponse.Status}).");
            var bytes = await imageResponse.BodyAsync();
            if (bytes.Length is < 100 or > 8_000_000)
                return new(MegaProductLookupStatus.Failed, Reason: "대표 이미지 크기를 확인할 수 없습니다.");
            return new(MegaProductLookupStatus.Success, new MegaCoffeeProductSnapshot(product.Name, product.Price,
                product.DisplayPrice, originalUrl.Trim(), product.ImageUrl, bytes, product.Available)
                { ResolvedProductUrl = resolvedUrl }) { ResolvedProductUrl = resolvedUrl };
        }
        catch (TimeoutException)
        { return new(MegaProductLookupStatus.Failed, Reason: "상품 페이지 응답 시간이 초과됐습니다.", ErrorType: "TimeoutException"); }
        catch (InvalidDataException ex)
        { return new(MegaProductLookupStatus.Failed, Reason: ex.Message, ErrorType: "InvalidDataException"); }
        catch (PlaywrightException)
        { return new(MegaProductLookupStatus.Failed, Reason: "Edge에서 상품 페이지를 읽지 못했습니다.", ErrorType: "PlaywrightException"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return new(MegaProductLookupStatus.Failed, Reason: "상품 정보를 읽는 중 오류가 발생했습니다.", ErrorType: ex.GetType().Name); }
    }

    private static string? ResolvedCoupangUrl(string originalUrl, string destination)
    {
        if (!TryProductUrl(originalUrl, out var supplier, out var key) || supplier != "coupang" ||
            !Uri.TryCreate(destination, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            uri.Host != "www.coupang.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || parts[0] != "vp" || parts[1] != "products" || !Digits.IsMatch(parts[2])) return null;
        string vendor = key["coupang|vendor:".Length..];
        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("vendorItemId", out var actual) && (actual.Count != 1 || actual[0] != vendor)) return null;
        if (ProductUrlIdentity.CoupangProductConflict(originalUrl, null, destination)) return null;
        return $"https://www.coupang.com/vp/products/{parts[2]}?vendorItemId={vendor}";
    }

    private sealed record PageProduct(string Name, decimal Price, string DisplayPrice, string ImageUrl, bool Available);

    private static async Task<PageProduct?> ReadNaverPageAsync(IPage page, string originalUrl)
    {
        string expectedId = new Uri(originalUrl).AbsolutePath.Trim('/').Split('/')[2];
        var scripts = page.Locator("script[type='application/ld+json']");
        await scripts.First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15000 });
        JsonElement product = default;
        for (int i = 0; i < await scripts.CountAsync(); i++)
        {
            using var doc = JsonDocument.Parse(await scripts.Nth(i).TextContentAsync() ?? "{}");
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("productID", out var id) && id.GetString() == expectedId)
            { product = doc.RootElement.Clone(); break; }
        }
        if (product.ValueKind != JsonValueKind.Object || !product.TryGetProperty("name", out var nameValue) ||
            !product.TryGetProperty("image", out var imageValue) ||
            !product.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Object ||
            !offers.TryGetProperty("priceCurrency", out var currency) || currency.GetString() != "KRW" ||
            !offers.TryGetProperty("availability", out var availability)) return null;
        string name = nameValue.GetString()?.Trim() ?? "";
        string image = imageValue.GetString() ?? "";
        if (name.Length is < 1 or > 500 || !Uri.TryCreate(image, UriKind.Absolute, out _)) return null;
        var priceLabel = page.GetByText("상품 가격", new() { Exact = true });
        await priceLabel.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        if (await priceLabel.CountAsync() != 1) return null;
        string visiblePrice = await priceLabel.First.EvaluateAsync<string>("element => element.parentElement?.innerText || ''");
        var amount = Money.Match(visiblePrice);
        if (!amount.Success || !decimal.TryParse(amount.Groups["amount"].Value.Replace(",", ""),
                NumberStyles.None, CultureInfo.InvariantCulture, out decimal price)) return null;
        string stock = availability.GetString() ?? "";
        if (stock is not ("http://schema.org/InStock" or "https://schema.org/InStock" or
            "http://schema.org/OutOfStock" or "https://schema.org/OutOfStock")) return null;
        bool options = await page.GetByText("옵션 선택 (필수)", new() { Exact = true }).CountAsync() > 0;
        string display = $"{price:N0}원" + (options ? " (옵션 선택 전 표시가)" : "");
        return new(name, price, display, image, stock.EndsWith("/InStock", StringComparison.Ordinal));
    }

    private static async Task<PageProduct?> ReadCoupangPageAsync(IPage page, string originalUrl)
    {
        // Do not infer a price from page titles, thumbnails or unrelated options.
        var json = page.Locator("script[type='application/ld+json']");
        for (int i = 0; i < await json.CountAsync(); i++)
        {
            using var doc = JsonDocument.Parse(await json.Nth(i).TextContentAsync() ?? "{}");
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("@type", out var type) || type.GetString() != "Product" ||
                !root.TryGetProperty("name", out var name) || !root.TryGetProperty("image", out var image) ||
                !root.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Object ||
                !offers.TryGetProperty("price", out var priceNode) || !offers.TryGetProperty("priceCurrency", out var currency) ||
                currency.GetString() != "KRW") continue;
            string productName = name.GetString()?.Trim() ?? "";
            string imageUrl = image.ValueKind == JsonValueKind.String ? image.GetString() ?? "" :
                image.ValueKind == JsonValueKind.Array && image.GetArrayLength() == 1 ? image[0].GetString() ?? "" : "";
            if (productName.Length is < 1 or > 500 || !decimal.TryParse(priceNode.ToString(), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var price) || price < 0 || !Uri.TryCreate(imageUrl, UriKind.Absolute, out _)) continue;
            string? expectedVendor = ProductUrlIdentity.Key("coupang", originalUrl)?["coupang|vendor:".Length..];
            bool matchingOffer = offers.TryGetProperty("url", out var offerUrl) && offerUrl.ValueKind == JsonValueKind.String &&
                ProductUrlIdentity.Key("coupang", offerUrl.GetString() ?? "") == ProductUrlIdentity.Key("coupang", originalUrl) ||
                offers.TryGetProperty("sku", out var offerSku) && offerSku.ValueKind == JsonValueKind.String &&
                offerSku.GetString() == expectedVendor;
            if (!matchingOffer || ProductUrlIdentity.CoupangProductConflict(originalUrl, null, page.Url)) continue;
            string? stock = offers.TryGetProperty("availability", out var status) ? status.GetString() : null;
            if (stock is not ("http://schema.org/InStock" or "https://schema.org/InStock" or
                "http://schema.org/OutOfStock" or "https://schema.org/OutOfStock")) continue;
            // The selected vendor option must be proved by the destination URL, not just a shared product title.
            if (!TryProductUrl(page.Url, out _, out string actualIdentity) ||
                !string.Equals(actualIdentity, ProductUrlIdentity.Key("coupang", originalUrl), StringComparison.OrdinalIgnoreCase)) continue;
            return new(productName, price, $"{price:N0}원 (옵션 확인 필요)", imageUrl,
                stock.EndsWith("/InStock", StringComparison.Ordinal));
        }
        return null;
    }
}
