using System.Diagnostics;

namespace CafeOrder;

internal static class SellerLinks
{
    // Only user-confirmed homepages belong here; product detail URLs remain on Product.
    internal static readonly Dictionary<string, string?> Homepages = new()
    { ["mega"] = null, ["piece"] = "https://www.piececake.co.kr/", ["food"] = null, ["wym"] = null, ["nuldam"] = null, ["coupang"] = null, ["naver"] = null };
    internal static Action<ProcessStartInfo> Launch = info => Process.Start(info);
    internal static string? Valid(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
    internal static string? Home(Supplier supplier) => Valid(Homepages.GetValueOrDefault(supplier.Id));
    internal static string? ProductHome(Product product)
    {
        if (product.Supplier.Id != "naver") return Home(product.Supplier);
        if (!Uri.TryCreate(product.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Host != "smartstore.naver.com") return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 3 || parts[1] != "products" || !parts[2].All(char.IsAsciiDigit) || parts[2].Length == 0 ||
            parts[0].Length == 0 || !parts[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return null;
        return $"https://smartstore.naver.com/{parts[0]}";
    }
    internal static string Hint(Supplier supplier, string? url) => supplier.Name + (url == null ? " · 홈페이지 미설정" : " · 홈페이지 열기");
    internal static void Open(string? url)
    {
        if (Valid(url) is not { } valid) return;
        try { Launch(new ProcessStartInfo(valid) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* An unavailable OS handler must not mutate catalog/cart state. */ }
    }
}

internal sealed class SellerIcon : PictureBox
{
    private readonly Supplier supplier;
    private readonly ToolTip tip = new() { ShowAlways = true };
    internal SellerIcon(Supplier supplier)
    {
        this.supplier = supplier; Name = "SellerIcon_" + supplier.Id;
        Image = SampleImages.Seller(supplier); SizeMode = PictureBoxSizeMode.Zoom; Size = new Size(30, 30); Margin = new Padding(3);
        UpdateHint();
    }
    private void UpdateHint() { var url = SellerLinks.Home(supplier); tip.SetToolTip(this, SellerLinks.Hint(supplier, url)); Cursor = url == null ? Cursors.Default : Cursors.Hand; }
    protected override void OnMouseEnter(EventArgs e) { UpdateHint(); base.OnMouseEnter(e); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) SellerLinks.Open(SellerLinks.Home(supplier)); }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
