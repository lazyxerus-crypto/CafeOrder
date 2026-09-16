using System.Runtime.InteropServices;

namespace CafeOrder;

// One owned overlay for the application. Layered transparency and hit-test transparency
// keep clicks/wheel/focus on the underlying application; there are no child HWNDs.
internal sealed class ToastHost : Form
{
    private readonly List<(string Text, string? Key, long Expires)> messages = [];
    private readonly System.Windows.Forms.Timer expiry = new();
    internal Control? AnchorRegion;
    private int Inset => 10;
    private int Gap => 8;
    private int CardHeight => Font.Height + 12;
    public ToastHost()
    {
        Name = "ToastHost"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; BackColor = Color.Magenta; TransparencyKey = BackColor; ForeColor = Color.White;
        Font = new Font("Malgun Gothic", 11); Opacity = .92; DoubleBuffered = true;
        expiry.Tick += (_, _) => { messages.RemoveAll(x => x.Expires <= Environment.TickCount64); Present(); };
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; return p; }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
        if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    public void Notify(Form owner, string text, string? key = null)
    {
        if (owner.IsDisposed || !owner.Visible) return;
        Owner = owner.Owner ?? owner;
        if (key != null) messages.RemoveAll(x => x.Key == key);
        messages.Insert(0, (text, key, Environment.TickCount64 + 2800));
        if (messages.Count > 3) messages.RemoveAt(3); Present();
    }
    public void Reposition()
    {
        if (AnchorRegion is not { IsDisposed: false } region || messages.Count == 0) return;
        int height = messages.Count * CardHeight + (messages.Count - 1) * Gap + Inset * 2;
        // The same table columns own the filters/catalog and toast/cart, including resize.
        region.MinimumSize = new Size(0, CardHeight * 3 + Gap * 2 + Inset * 2);
        Bounds = new Rectangle(region.PointToScreen(Point.Empty), new Size(region.Width, height));
        Invalidate();
    }
    private void Present()
    {
        expiry.Stop();
        if (messages.Count == 0) { Hide(); return; }
        Reposition();
        if (!Visible && Owner != null) Show(Owner);
        Reposition(); Invalidate(); // Only message changes repaint; no animation/render loop.
        expiry.Interval = (int)Math.Max(1, messages.Min(x => x.Expires) - Environment.TickCount64); expiry.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new SolidBrush(Ui.Accent);
        for (int i = 0; i < messages.Count; i++)
        {
            var card = new Rectangle(Inset, Inset + i * (CardHeight + Gap), Math.Max(1, Width - Inset * 2), CardHeight);
            e.Graphics.FillRectangle(brush, card);
            card.Inflate(-8, 0);
            string text = messages[i].Text;
            const string suffix = "를 장바구니에 추가했습니다";
            if (messages[i].Key?.StartsWith("add:", StringComparison.Ordinal) == true && text.EndsWith(suffix, StringComparison.Ordinal))
            {
                int suffixWidth = TextRenderer.MeasureText(suffix, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                var ending = new Rectangle(card.Right - suffixWidth, card.Top, suffixWidth, card.Height);
                TextRenderer.DrawText(e.Graphics, suffix, Font, ending, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                card.Width = Math.Max(1, card.Width - suffixWidth); text = text[..^suffix.Length];
            }
            TextRenderer.DrawText(e.Graphics, text, Font, card, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) expiry.Dispose(); base.Dispose(disposing); }
}

internal static class NativeUi
{
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
