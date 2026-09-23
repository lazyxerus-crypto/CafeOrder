using Microsoft.Playwright;

namespace CafeOrder;

internal sealed class NuldamLoginProbe : ISupplierLoginProbe
{
    public string HomeUrl => "https://nuldampartners.com/";
    public string LoginUrl => "https://nuldampartners.com/member/login.html";

    public async Task<SupplierLoginState> CheckAsync(IPage page)
    {
        if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var uri) ||
            uri.Host is not ("nuldampartners.com" or "www.nuldampartners.com"))
            return SupplierLoginState.WaitingForUser;
        if (await page.Locator(".xans-layout-statelogon a[href='/exec/front/Member/logout/']:visible").CountAsync() > 0 &&
            await page.Locator(".xans-layout-statelogon a[href='/member/modify.html']:visible").CountAsync() > 0)
            return SupplierLoginState.LoggedIn;
        if (await page.Locator("#member_id:visible").CountAsync() > 0 ||
            await page.Locator(".xans-layout-statelogoff a[href='/member/login.html']:visible").CountAsync() > 0)
            return SupplierLoginState.LoginRequired;
        return SupplierLoginState.WaitingForUser;
    }

    public async Task SubmitAsync(IPage page, LoginCredentials credentials)
    {
        if (new Uri(page.Url).AbsolutePath != "/member/login.html")
            await page.GotoAsync(LoginUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 20000 });
        await page.Locator("#member_id").FillAsync(credentials.Id);
        await page.Locator("#member_passwd").FillAsync(credentials.Password);
        await page.Locator("#member_passwd").PressAsync("Enter");
    }
}
