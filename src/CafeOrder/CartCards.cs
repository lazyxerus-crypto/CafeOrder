namespace CafeOrder;

internal sealed class CartProductRow : BufferedPanel
{
    private readonly PictureBox image;
    private readonly TextBox title, price;
    private readonly Label quantity;
    private readonly Button? minus, plus, order;
    private readonly Button remove;
    private readonly SampleData data;
    private readonly bool compact;
    public CartLine Line { get; }
    public CartProductRow(SampleData data, CartLine line, bool inOrder = false)
    {
        SuspendLayout();
        this.data = data; compact = inOrder;
        Line = line; Name = $"CartRow_{line.Product.Id}"; BackColor = Color.White;
        image = SampleImages.Picture(line.Product.Category, $"CartImage_{line.Product.Id}");
        title = Ui.Role(Ui.CopyText(line.Product.Name, $"CartName_{line.Product.Id}"), inOrder ? TypographyKey.OrderProductName : TypographyKey.CartProductName);
        price = Ui.Role(Ui.CopyText($"{line.Product.Price:N0}원", $"CartPrice_{line.Product.Id}"), inOrder ? TypographyKey.OrderInfo : TypographyKey.CartPrice);
        quantity = Ui.Role(new Label { Name = $"Quantity_{line.Product.Id}", TextAlign = ContentAlignment.MiddleCenter }, TypographyKey.General);
        remove = Ui.Button("X", () => data.Remove(line), name: $"Remove_{line.Product.Id}");
        remove.AutoSize = false; remove.MinimumSize = new Size(30, 30); remove.Padding = Padding.Empty;
        Controls.AddRange([image, title, price, remove]);
        if (line.Product.Supplier.Manual)
        {
            if (!inOrder)
            {
                order = Ui.Button("판매처에서 주문하기", () => { using var form = new OrderForm(data, [line], true); form.ShowDialog(FindForm()); }, true, $"Manual_{line.Product.Id}");
                order.AutoSize = false; Controls.Add(order);
            }
        }
        else
        {
            minus = Ui.Button("−", () => data.ChangeQuantity(line, -1), name: $"Minus_{line.Product.Id}");
            plus = Ui.Button("+", () => data.ChangeQuantity(line, 1), name: $"Plus_{line.Product.Id}");
            foreach (var button in new[] { minus, plus }) { button.AutoSize = false; button.MinimumSize = new Size(36, 36); button.Padding = Padding.Empty; }
            Controls.AddRange([minus, quantity, plus]);
        }
        foreach (Control child in Controls) child.FontChanged += (_, _) => { PerformLayout(); Parent?.PerformLayout(); };
        RefreshQuantity(); ResumeLayout(false);
    }
    public bool RefreshQuantity()
    {
        bool details = false;
        if (title.Text != Line.Product.Name) { title.Text = Line.Product.Name; details = true; }
        string currentPrice = $"{Line.Product.Price:N0}원";
        if (price.Text != currentPrice) { price.Text = currentPrice; details = true; }
        Image currentImage = SampleImages.Catalog(Line.Product.Category);
        if (!ReferenceEquals(image.Image, currentImage)) { image.Image = currentImage; details = true; }
        if (quantity.Text != Line.Quantity.ToString()) quantity.Text = Line.Quantity.ToString();
        bool editable = !data.IsLocked(Line.Product);
        remove.Enabled = editable; if (minus != null) minus.Enabled = editable; if (plus != null) plus.Enabled = editable;
        if (order != null) order.Enabled = editable;
        if (details) PerformLayout();
        return details;
    }
    public void Highlight(bool active)
    { BackColor = title.BackColor = price.BackColor = active ? Color.FromArgb(221, 238, 225) : Color.White; }
    private int Thumb => Math.Max(58, 66 * DeviceDpi / 96);
    private int PriceHeight(int rowWidth)
    {
        int width = Math.Max(40, rowWidth - Thumb - 12);
        int lines = Math.Max(1, (int)Math.Ceiling((double)TextRenderer.MeasureText(price.Text, price.Font).Width / width));
        return price.Font.Height * lines + 3;
    }
    private int TitleHeight(int width)
    {
        int textWidth = Math.Max(40, width - Thumb - 8 - RemoveSize - 8);
        int measured = TextRenderer.MeasureText(title.Text, title.Font, new Size(textWidth, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + 2;
        return compact ? measured : Math.Max(title.Font.Height * 3 + 2, measured);
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int contentBottom = Math.Max(TitleHeight(proposedSize.Width), RemoveSize) + PriceHeight(proposedSize.Width) + 4;
        if (order != null) contentBottom += 4 + Ui.ActionHeight(order, Math.Max(40, proposedSize.Width - Thumb - 12));
        else if (minus != null) contentBottom += 4 + Math.Max(36, minus.Font.Height + 14);
        return new(proposedSize.Width, Math.Max(contentBottom, 4 + Thumb) + 4);
    }
    private int RemoveSize => Math.Max(30, remove.Font.Height + 6);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (image == null) return;
        int unit = Math.Max(19, Font.Height), thumb = Thumb, x = thumb + 8, width = Math.Max(40, Width - x - 4);
        image.SetBounds(0, 4, thumb, thumb);
        remove.SetBounds(Width - RemoveSize - 4, 2, RemoveSize, RemoveSize);
        title.SetBounds(x, 2, Math.Max(24, width - RemoveSize - 4), TitleHeight(Width));
        price.SetBounds(x, Math.Max(title.Bottom, remove.Bottom) + 2, width, PriceHeight(Width));
        int y = price.Bottom + 4, buttonSize = Math.Max(36, (minus?.Font.Height ?? unit) + 14);
        if (order != null) order.SetBounds(x, y, width, Ui.ActionHeight(order, width));
        else if (minus != null)
        {
            int actionX = Math.Max(0, Math.Min(x, Width - buttonSize * 3 - 4));
            minus!.SetBounds(actionX, y, buttonSize, buttonSize); quantity.SetBounds(actionX + buttonSize, y, buttonSize, buttonSize);
            plus!.SetBounds(actionX + buttonSize * 2, y, buttonSize, buttonSize);
        }
    }
}

internal sealed class SupplierCartCard : SoftPanel
{
    private readonly SampleData data;
    private readonly Supplier supplier;
    private readonly Action<CartLine[]> open;
    private readonly Control heading;
    private readonly Label subtotal;
    private readonly Button order;
    private readonly Dictionary<int, CartProductRow> rows = [];
    private CartLine[] lines = [];
    private int measureVersion, measuredVersion = -1, measuredWidth = -1, measuredHeight;
    private void InvalidateMeasure() { measureVersion++; PerformLayout(); Parent?.PerformLayout(); }
    internal CartProductRow Row(int id) => rows[id];
    public SupplierCartCard(SampleData data, Supplier supplier, Action<CartLine[]> open)
    {
        this.data = data; this.supplier = supplier; this.open = open;
        Name = $"Cart_{supplier.Id}"; BackColor = Color.White; Margin = new Padding(0, 0, 0, 10);
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        heading = Ui.SellerHeading(supplier); heading.Dock = DockStyle.None; heading.AutoSize = false;
        subtotal = Ui.Text("", true); subtotal.Dock = DockStyle.None; subtotal.AutoSize = false;
        order = Ui.Button("주문하기", () => { if (data.Shortfall(supplier, lines) == 0) open(lines); }, true, $"SupplierOrder_{supplier.Id}");
        order.AutoSize = false; Controls.Add(heading);
        if (!supplier.Manual) Controls.AddRange([subtotal, order]);
        foreach (Control child in heading.Controls) child.FontChanged += (_, _) => InvalidateMeasure();
        subtotal.FontChanged += (_, _) => InvalidateMeasure(); order.FontChanged += (_, _) => InvalidateMeasure();
    }
    public void Sync(CartLine[] current)
    {
        bool structural = !lines.Select(x => x.Product.Id).SequenceEqual(current.Select(x => x.Product.Id));
        if (structural) measureVersion++;
        if (structural) SuspendLayout();
        lines = current;
        foreach (int id in rows.Keys.Except(current.Select(x => x.Product.Id)).ToArray()) { rows[id].Dispose(); rows.Remove(id); }
        foreach (var line in lines)
        {
            if (!rows.TryGetValue(line.Product.Id, out var row))
            {
                row = new CartProductRow(data, line); rows[line.Product.Id] = row; Controls.Add(row);
                foreach (Control child in row.Controls) child.FontChanged += (_, _) => InvalidateMeasure();
            }
            if (row.RefreshQuantity()) measureVersion++;
        }
        if (!supplier.Manual)
        {
            string text = $"소계 {data.Subtotal(lines):N0}원"; if (subtotal.Text != text) subtotal.Text = text;
            decimal shortage = data.Shortfall(supplier, lines);
            string action = shortage > 0 ? $"{shortage:N0}원 부족" : "주문하기";
            if (order.Text != action) { measureVersion++; order.Text = action; }
            order.BackColor = shortage > 0 ? Ui.Danger : Ui.Accent; order.ForeColor = Color.White;
            order.Enabled = shortage == 0;
        }
        if (structural) { ResumeLayout(true); PerformLayout(); }
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = MaximumSize.Width > 0 ? MaximumSize.Width : Math.Max(120, proposedSize.Width);
        if (measuredWidth != width || measuredVersion != measureVersion)
        { measuredHeight = Arrange(width, false); measuredWidth = width; measuredVersion = measureVersion; }
        return new Size(width, measuredHeight);
    }
    private int Arrange(int outerWidth, bool apply)
    {
        if (heading == null) return 0;
        int x = 10, width = Math.Max(100, outerWidth - 20), y = 10;
        int headingHeight = Math.Max(36, heading.GetPreferredSize(new Size(width, 0)).Height);
        if (apply) heading.SetBounds(x, y, width, headingHeight); y += headingHeight + 6;
        foreach (var line in lines)
        {
            var row = rows[line.Product.Id]; int height = row.GetPreferredSize(new Size(width, 0)).Height;
            if (apply) row.SetBounds(x, y, width, height); y += height + 10;
        }
        if (!supplier.Manual)
        {
            int subtotalHeight = subtotal.Font.Height + 8, orderHeight = Ui.ActionHeight(order, width);
            if (apply) subtotal.SetBounds(x, y, width, subtotalHeight); y += subtotalHeight + 6;
            if (apply) order.SetBounds(x, y, width, orderHeight); y += orderHeight + 10;
        }
        return y + 4;
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); Arrange(Width, true);
    }
}
