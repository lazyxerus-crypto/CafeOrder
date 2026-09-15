using System.Runtime.InteropServices;

namespace CafeOrder;

// One owned overlay for the application. Layered transparency and hit-test transparency
// keep clicks/wheel/focus on the underlying application; there are no child HWNDs.
internal sealed class ToastHost : Form
{
    private readonly List<(string Text, long Expires)> messages = [];
    private readonly System.Windows.Forms.Timer expiry = new();
    private Form? anchor;
    public ToastHost()
    {
        Name = "ToastHost"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; BackColor = Ui.Accent; ForeColor = Color.White;
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
    public void Notify(Form owner, string text)
    {
        if (owner.IsDisposed || !owner.Visible) return;
        anchor = owner; Owner = owner.Owner ?? owner; messages.Insert(0, (text, Environment.TickCount64 + 2800));
        if (messages.Count > 3) messages.RemoveAt(3); Present();
    }
    public void Reposition()
    {
        if (Owner == null || !Visible) return;
        var target = anchor is { IsDisposed: false, Visible: true } ? anchor : Owner;
        Location = target.PointToScreen(new Point(Math.Max(8, target.ClientSize.Width - Width - 12), 6));
    }
    private void Present()
    {
        expiry.Stop();
        if (messages.Count == 0) { Hide(); return; }
        Size = new Size(360, messages.Count * 36 + 8);
        if (!Visible && Owner != null) Show(Owner);
        Reposition(); Invalidate(); // Only message changes repaint; no animation/render loop.
        expiry.Interval = (int)Math.Max(1, messages.Min(x => x.Expires) - Environment.TickCount64); expiry.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        for (int i = 0; i < messages.Count; i++)
            TextRenderer.DrawText(e.Graphics, messages[i].Text, Font, new Rectangle(10, 4 + i * 36, Width - 20, 36), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    protected override void Dispose(bool disposing) { if (disposing) expiry.Dispose(); base.Dispose(disposing); }
}

internal static class NativeUi
{
    [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
