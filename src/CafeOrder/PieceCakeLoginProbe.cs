using Microsoft.Playwright;

namespace CafeOrder;

internal sealed class PieceCakeLoginProbe : ISupplierLoginProbe
{
    public string HomeUrl => "https://www.piececake.co.kr/";
    public string LoginUrl => "https://www.piececake.co.kr/member/login";

    public async Task<SupplierLoginState> CheckAsync(IPage page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ||
            uri.Host is not ("www.piececake.co.kr" or "piececake.co.kr"))
            return SupplierLoginState.WaitingForUser;
        // Both links are hidden from guests; a URL or stored cookie alone is not proof.
        if (await page.Locator("a[href='/logout']:visible").CountAsync() > 0 &&
            await page.Locator("a[href='/affiliate/join_change']:visible").CountAsync() > 0)
            return SupplierLoginState.LoggedIn;
        if (await page.Locator("#loginFrm input[name='loginId']:visible").CountAsync() > 0 ||
            await page.Locator("a[href='/member/login']:visible").CountAsync() > 0)
            return SupplierLoginState.LoginRequired;
        return SupplierLoginState.WaitingForUser;
    }

    public async Task SubmitAsync(IPage page, LoginCredentials credentials)
    {
        if (page.Url != LoginUrl)
            await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        await page.Locator("#loginFrm input[name='loginId']").FillAsync(credentials.Id);
        await page.Locator("#loginFrm input[name='loginPw']").FillAsync(credentials.Password);
        await page.Locator("#loginFrm #btnLoginLarge").ClickAsync(new() { Timeout = 15000 });
    }
}
