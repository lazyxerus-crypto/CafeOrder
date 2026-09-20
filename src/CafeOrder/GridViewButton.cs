namespace CafeOrder;

internal sealed class GridViewButton : Button
{
    internal int Columns { get; }
    private readonly ToolTip tip = new() { ShowAlways = true };
    private bool selected;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get => selected; set { selected = value; Invalidate(); } }
    internal GridViewButton(int columns)
    {
        Columns = columns; Name = $"ViewColumns{columns}"; AccessibleName = $"{columns}열";
        Size = new Size(60, 34); Margin = new Padding(4); FlatStyle = FlatStyle.Flat;
        tip.SetToolTip(this, AccessibleName);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(selected ? Color.FromArgb(221, 238, 225) : Color.White);
        using var border = new Pen(selected ? Ui.Accent : Color.FromArgb(201, 211, 205), selected ? 2 : 1);
        e.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        int gap = 3, side = Math.Min(10, (Width - 14 - gap * (Columns - 1)) / Columns);
        int start = (Width - Columns * side - (Columns - 1) * gap) / 2;
        using var brush = new SolidBrush(Ui.Accent);
        for (int row = 0; row < 2; row++) for (int col = 0; col < Columns; col++)
            e.Graphics.FillRectangle(brush, start + col * (side + gap), (Height - side * 2 - gap) / 2 + row * (side + gap), side, side);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
