namespace CafeOrder;

internal sealed class EllipsisLabel : Label
{
    internal bool SingleLine = true;
    private readonly ToolTip tip = new() { ShowAlways = true };
    internal EllipsisLabel() { AutoEllipsis = true; }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); tip?.SetToolTip(this, Text); }
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (!SingleLine) return base.GetPreferredSize(proposedSize);
        var measured = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        return new Size(proposedSize.Width > 0 ? Math.Min(proposedSize.Width, measured.Width + Padding.Horizontal) : measured.Width + Padding.Horizontal, Font.Height + Padding.Vertical + 2);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (!SingleLine) { base.OnPaint(e); return; }
        var rect = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        var align = TextAlign is ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight ? TextFormatFlags.Right :
            TextAlign is ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, Text, Font, rect, ForeColor, align | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
