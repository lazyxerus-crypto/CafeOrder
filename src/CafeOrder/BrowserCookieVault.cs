using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace CafeOrder;

// Chromium persists durable cookies itself. Its session-only cookies need a separate protected snapshot.
internal sealed class BrowserCookieVault(string profilePath, string supplierId)
{
    private readonly string path = Path.Combine(profilePath, "session-cookies.dpapi");
    private readonly byte[] entropy = Encoding.UTF8.GetBytes("CafeOrder/browser/" + supplierId);

    internal async Task RestoreAsync(IBrowserContext context)
    {
        if (!File.Exists(path)) return;
        byte[] protectedBytes = await File.ReadAllBytesAsync(path);
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
            var cookies = JsonSerializer.Deserialize<Cookie[]>(plain);
            if (cookies is { Length: > 0 }) await context.AddCookiesAsync(cookies);
        }
        catch (CryptographicException) { } // Old/foreign Windows user: use the browser profile if possible.
        catch (JsonException) { }
        finally { if (plain != null) CryptographicOperations.ZeroMemory(plain); }
    }

    internal async Task SaveAsync(IBrowserContext context, string siteUrl)
    {
        var cookies = await context.CookiesAsync([siteUrl]);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(cookies);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            string pending = path + ".tmp";
            await File.WriteAllBytesAsync(pending, protectedBytes);
            File.Move(pending, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal void Clear()
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
