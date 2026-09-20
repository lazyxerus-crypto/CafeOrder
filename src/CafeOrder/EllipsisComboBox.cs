namespace CafeOrder;

internal sealed class EllipsisComboBox : ComboBox
{
    private readonly ToolTip tip = new() { ShowAlways = true };
    internal EllipsisComboBox() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Font.Height + 4; }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = Font.Height + 4; }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); tip?.SetToolTip(this, Text); }
    protected override void OnDropDown(EventArgs e)
    {
        // The popup can show complete choices even when the closed field is narrow.
        DropDownWidth = Math.Max(Width, Items.Cast<object>().Select(item => TextRenderer.MeasureText(GetItemText(item), Font).Width + 30).DefaultIfEmpty(Width).Max());
        base.OnDropDown(e);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground(); string? text = e.Index >= 0 ? GetItemText(Items[e.Index]) : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -2, 0), e.ForeColor,
            TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
