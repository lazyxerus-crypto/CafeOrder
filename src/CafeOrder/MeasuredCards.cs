namespace CafeOrder;

internal sealed class FilterTable : TableLayoutPanel
{
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int height = Controls.Cast<Control>().Where(c => c.Name != "TransferStatus" || c.Text.Length > 0)
            .GroupBy(GetRow).Sum(row => row.Max(c => c.GetPreferredSize(new Size(Math.Max(40, GetColumnSpan(c) > 1 ? width - c.Margin.Horizontal : c.Width), 0)).Height + c.Margin.Vertical));
        return new Size(width, height + Padding.Vertical);
    }
}

// Width/font changes determine row height; tab visibility alone never remeasures these cards.
internal sealed class SupplierSettingsCard : SoftPanel
{
    private readonly FlowLayoutPanel first, second;
    private int measuredWidth = -1;
    private bool arranging;
    public SupplierSettingsCard(FlowLayoutPanel first, FlowLayoutPanel second)
    {
        this.first = first; this.second = second; Margin = new Padding(0, 0, 0, 10);
        foreach (var row in new[] { first, second })
        {
            row.Dock = DockStyle.None; row.AutoSize = false; Controls.Add(row);
            foreach (var control in Descendants(row))
            {
                if (control is Label or TextBox) Ui.Role(control, TypographyKey.SupplierManagement);
                control.FontChanged += (_, _) => Remeasure();
                control.TextChanged += (_, _) => { if (control is Label or Button) Remeasure(); };
            }
        }
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control c in parent.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; } }
    internal void Remeasure() { measuredWidth = -1; PerformLayout(); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging || first == null) return;
        arranging = true;
        try
        {
            base.OnLayout(e); int width = Math.Max(80, Width - 20); if (width == measuredWidth) return; measuredWidth = width;
            int top = 10;
            foreach (var row in new[] { first, second }) { int height = row.GetPreferredSize(new Size(width, 0)).Height; row.SetBounds(10, top, width, height); top += height; }
            Height = top + 10;
        }
        finally { arranging = false; }
    }
}

internal sealed class HistoryItemsTable : BufferedPanel
{
    private readonly List<Label[]> rows = [];
    private readonly List<HistoryThumbnail?> images = [];
    private readonly SampleData data;
    private readonly float[] portions = [.54f, .16f, .10f, .20f];
    private readonly Dictionary<int, int[]> heights = [];
    private bool arranging;
    internal HistoryItemsTable(SampleData data, (Product Product, string Price, string Qty, string Amount)[] items)
    {
        this.data = data;
        Name = "HistoryItems"; Dock = DockStyle.Top;
        Add(["상품명", "단가", "수량", "금액"], true);
        foreach (var item in items) Add([item.Product.Name, item.Price, item.Qty, item.Amount], false, item.Product);
        data.ProductChanged += ProductChanged;
    }
    private void Add(string[] values, bool header, Product? product = null)
    {
        HistoryThumbnail? thumbnail = product == null ? null : new HistoryThumbnail(product, rows.Count);
        var row = values.Select((value, index) =>
        {
            var label = Ui.Role(Ui.Text(value, header), !header && index == 0 ? TypographyKey.HistoryProductName : TypographyKey.HistoryInfo);
            if (label is EllipsisLabel fullText) { fullText.SingleLine = index != 0; fullText.AutoEllipsis = index != 0; }
            label.Name = $"HistoryCell_{rows.Count}_{index}"; label.AutoSize = false; label.Dock = DockStyle.None; label.Padding = new Padding(4, 2, 4, 2);
            label.FontChanged += (_, _) => { heights.Clear(); PerformLayout(); Parent?.PerformLayout(); };
            return label;
        }).ToArray(); rows.Add(row); images.Add(thumbnail); heights.Clear(); Controls.AddRange(row);
        if (thumbnail != null) Controls.Add(thumbnail);
    }
    private void ProductChanged(Product product)
    { foreach (var image in images) if (image?.Product.Id == product.Id) image.RefreshImage(); }
    protected override void Dispose(bool disposing)
    { if (disposing) data.ProductChanged -= ProductChanged; base.Dispose(disposing); }
    private int RowHeight(Label[] row, int width, int rowIndex) => row.Select((label, index) => index == 0 ? Math.Max(images[rowIndex] == null ? 0 : 44,
        TextRenderer.MeasureText(label.Text, label.Font,
        new Size(Math.Max(20, (int)(width * portions[index]) - (images[rowIndex] == null ? 8 : 52)), 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8) : label.Font.Height + 8).Max();
    private int[] Heights(int width)
    { if (!heights.TryGetValue(width, out var value)) { if (heights.Count > 64) heights.Clear(); heights[width] = value = rows.Select((row, index) => RowHeight(row, width, index)).ToArray(); } return value; }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Heights(proposedSize.Width).Sum());
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging) return; arranging = true;
        try
        {
            base.OnLayout(e); int y = 0;
            var sizes = Heights(Width);
            for (int r = 0; r < rows.Count; r++)
            {
                var row = rows[r]; int height = sizes[r], x = 0;
                for (int c = 0; c < 4; c++)
                {
                    int width = c == 3 ? Width - x : (int)(Width * portions[c]);
                    if (c == 0 && images[r] is { } image)
                    { image.SetBounds(x + 4, y + 4, 36, 36); row[c].SetBounds(x + 44, y, Math.Max(20, width - 44), height); }
                    else row[c].SetBounds(x, y, width, height);
                    x += width;
                }
                y += height;
            }
            if (Height != y) Height = y;
        }
        finally { arranging = false; }
    }
}

internal sealed class HistoryThumbnail : PictureBox
{
    internal Product Product { get; }
    private string identity = "";
    private bool owned;
    internal HistoryThumbnail(Product product, int row)
    { Product = product; Name = $"HistoryImage_{row}"; SizeMode = PictureBoxSizeMode.Zoom; TabStop = false; RefreshImage(); }
    internal void RefreshImage()
    {
        string next = ProductImages.Identity(Product);
        if (identity == next) return;
        var selected = ProductImages.Resolve(Product);
        if (owned) Image?.Dispose();
        Image = selected.Image; owned = selected.Owned; identity = next;
    }
    protected override void Dispose(bool disposing)
    { if (disposing && owned) Image?.Dispose(); base.Dispose(disposing); }
}
