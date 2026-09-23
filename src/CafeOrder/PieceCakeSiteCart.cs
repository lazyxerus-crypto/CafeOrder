using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace CafeOrder;

internal static partial class PieceCakeSiteCart
{
    private static readonly Uri CartUrl = new("https://www.piececake.co.kr/affiliate/prdorder");
    private static readonly Regex ProductUnit = new(@"\((BOX|EA) ([1-9][0-9]*)개입\)$", RegexOptions.CultureInvariant);
    private static readonly Regex CartPrice = new(@"^([0-9]{1,3}(?:,[0-9]{3})*|[0-9]+)원\s*/\s*(BOX|EA)\(([1-9][0-9]*)\)개입$",
        RegexOptions.CultureInvariant);

    internal static string? PurchaseKey(string displayPrice)
    {
        var match = ProductUnit.Match(displayPrice);
        return match.Success ? match.Groups[1].Value + ":" + match.Groups[2].Value : null;
    }

    internal static bool Matches(IReadOnlyList<SiteCartEntry> actual, IReadOnlyList<SiteCartTarget> targets)
    {
        if (actual.Count != targets.Count || actual.Select(x => (x.ExternalProductId, x.OptionKey)).Distinct().Count() != actual.Count)
            return false;
        return targets.All(target => actual.Any(item => item.ExternalProductId == target.ExternalProductId &&
            item.OptionKey == target.OptionKey && item.Quantity == target.Quantity && item.UnitPrice == target.Price));
    }

    internal static async Task<IReadOnlyList<SiteCartEntry>> ReadAsync(IPage page, PieceCakeLoginProbe login)
    {
        var listResponse = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Capture(object? _, IResponse response)
        {
            if (Uri.TryCreate(response.Url, UriKind.Absolute, out var uri) &&
                uri.Host == CartUrl.Host && uri.AbsolutePath == "/selectList" &&
                response.Request.PostData?.Contains("getProdCartList", StringComparison.Ordinal) == true)
                listResponse.TrySetResult(response);
        }
        page.Response += Capture;
        try
        {
            var navigation = await page.GotoAsync(CartUrl.ToString(), new()
            { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
            if (await login.CheckAsync(page) != SupplierLoginState.LoggedIn)
                throw new MegaCoffeeLoginRequiredException();
            if (navigation?.Status != 200 || page.Url != CartUrl.ToString() ||
                await page.Locator("#prodTbody").CountAsync() != 1)
                throw new InvalidDataException("파미유 장바구니 페이지를 확인할 수 없습니다.");
            var response = await listResponse.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (response.Status != 200) throw new InvalidDataException("파미유 장바구니 목록 응답이 실패했습니다.");
            await response.FinishedAsync();
            using var json = JsonDocument.Parse(await response.TextAsync());
            if (!json.RootElement.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("파미유 장바구니 목록을 확인할 수 없습니다.");
            var expected = list.EnumerateArray().Select(item =>
            {
                string id = item.GetProperty("prod_no").GetString() ?? "";
                int quantity = item.GetProperty("sale_qty").ValueKind == JsonValueKind.Number
                    ? item.GetProperty("sale_qty").GetInt32()
                    : int.Parse(item.GetProperty("sale_qty").GetString() ?? "", CultureInfo.InvariantCulture);
                if (!Regex.IsMatch(id, @"^PD[0-9]{1,12}$") || quantity <= 0)
                    throw new InvalidDataException("파미유 장바구니 상품 코드 또는 수량이 올바르지 않습니다.");
                return (id, quantity);
            }).ToArray();
            await page.WaitForFunctionAsync("""
                expected => {const rows=[...document.querySelectorAll('#prodTbody tr')];
                  return rows.length===expected.length && rows.every((row,index)=>
                    window.jQuery(row).data('prod_no')===expected[index].id &&
                    Number(row.querySelector('input[name=volum]')?.value)===expected[index].quantity);}
                """, expected.Select(x => new { id = x.id, quantity = x.quantity }).ToArray(),
                new() { Timeout = 12000 });
            string rowsJson = await page.EvaluateAsync<string>("""
                () => JSON.stringify([...document.querySelectorAll('#prodTbody tr')].map(row=>({
                  code:String(window.jQuery(row).data('prod_no')||''),
                  quantity:Number(row.querySelector('input[name=volum]')?.value),
                  price:(row.querySelector('._unitPrice')?.textContent||'').trim().replace(/\s+/g,' '),
                  name:(row.querySelector('._prodKnm')?.textContent||'').trim().replace(/\s+/g,' ')
                })))
                """);
            using var rows = JsonDocument.Parse(rowsJson);
            var result = new List<SiteCartEntry>();
            foreach (var row in rows.RootElement.EnumerateArray())
            {
                string code = row.GetProperty("code").GetString() ?? "";
                int quantity = row.GetProperty("quantity").GetInt32();
                string priceText = row.GetProperty("price").GetString() ?? "";
                string name = row.GetProperty("name").GetString() ?? "";
                var match = CartPrice.Match(priceText);
                if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(",", ""),
                    NumberStyles.None, CultureInfo.InvariantCulture, out decimal unitPrice) || name.Length == 0)
                    throw new InvalidDataException("파미유 장바구니 상품의 구매 단위 또는 가격을 확인할 수 없습니다.");
                result.Add(new(code, quantity, match.Groups[2].Value + ":" + match.Groups[3].Value,
                    name, unitPrice));
            }
            return result;
        }
        finally { page.Response -= Capture; }
    }
}
