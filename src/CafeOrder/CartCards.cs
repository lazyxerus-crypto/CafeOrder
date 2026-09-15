namespace CafeOrder;

internal sealed class CartProductRow : BufferedPanel
{
    private readonly PictureBox image;
    private readonly TextBox title, price;
    private readonly Label quantity;
    private readonly Button? minus, plus, order;
    public CartLine Line { get; }
    public CartProductRow(SampleData data, CartLine line)
    {
        SuspendLayout();
        Line = line; Name = $"CartRow_{line.Product.Id}"; BackColor = Color.White;
        image = SampleImages.Picture(line.Product.Category, $"CartImage_{line.Product.Id}");
        title = Ui.CopyText(line.Product.Name, $"CartName_{line.Product.Id}");
        price = Ui.CopyText($"{line.Product.Price:N0}원", $"CartPrice_{line.Product.Id}");
        quantity = new Label { Name = $"Quantity_{line.Product.Id}", TextAlign = ContentAlignment.MiddleCenter };
        Controls.AddRange([image, title, price]);
        if (line.Product.Supplier.Manual)
        {
            order = Ui.Button("사이트에서 주문하기", () => { using var form = new OrderForm(data, [line], true); form.ShowDialog(FindForm()); }, true, $"Manual_{line.Product.Id}");
            order.AutoSize = false; Controls.Add(order);
        }
        else
        {
            minus = Ui.Button("−", () => data.ChangeQuantity(line, -1), name: $"Minus_{line.Product.Id}");
            plus = Ui.Button("+", () => data.ChangeQuantity(line, 1), name: $"Plus_{line.Product.Id}");
            foreach (var button in new[] { minus, plus }) { button.AutoSize = false; button.MinimumSize = new Size(36, 36); button.Padding = Padding.Empty; }
            Controls.AddRange([minus, quantity, plus]);
        }
        RefreshQuantity(); ResumeLayout(false);
    }
    public void RefreshQuantity() { if (quantity.Text != Line.Quantity.ToString()) quantity.Text = Line.Quantity.ToString(); }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Math.Max(19, Font.Height) * 5 + 34);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (image == null) return;
        int unit = Math.Max(19, Font.Height), thumb = Math.Max(58, unit * 3), x = thumb + 8, width = Math.Max(70, Width - x);
        image.SetBounds(0, 4, thumb, thumb);
        title.SetBounds(x, 2, width, unit * 3 + 2);
        price.SetBounds(x, title.Bottom + 2, width, unit + 3);
        int y = price.Bottom + 4, buttonSize = Math.Max(36, unit + 14);
        if (order != null) order.SetBounds(x, y, width, buttonSize);
        else
        {
            minus!.SetBounds(x, y, buttonSize, buttonSize); quantity.SetBounds(x + buttonSize, y, buttonSize, buttonSize);
            plus!.SetBounds(x + buttonSize * 2, y, buttonSize, buttonSize);
        }
    }
}

internal sealed class SupplierCartCard : BufferedPanel
{
    private readonly SampleData data;
    private readonly Supplier supplier;
    private readonly Action<CartLine[]> open;
    private readonly Label heading, subtotal;
    private readonly Button order;
    private readonly Dictionary<int, CartProductRow> rows = [];
    private CartLine[] lines = [];
    public SupplierCartCard(SampleData data, Supplier supplier, Action<CartLine[]> open)
    {
        this.data = data; this.supplier = supplier; this.open = open;
        Name = $"Cart_{supplier.Id}"; BackColor = Color.White; Margin = new Padding(0, 0, 0, 10);
        heading = Ui.Text(supplier.Name, true); heading.Dock = DockStyle.None; heading.AutoSize = false;
        subtotal = Ui.Text("", true); subtotal.Dock = DockStyle.None; subtotal.AutoSize = false;
        order = Ui.Button("주문하기", () => { if (data.Shortfall(supplier, lines) == 0) open(lines); }, true, $"SupplierOrder_{supplier.Id}");
        order.AutoSize = false; Controls.Add(heading);
        if (!supplier.Manual) Controls.AddRange([subtotal, order]);
    }
    public void Sync(CartLine[] current)
    {
        bool structural = !lines.Select(x => x.Product.Id).SequenceEqual(current.Select(x => x.Product.Id));
        if (structural) SuspendLayout();
        lines = current;
        foreach (int id in rows.Keys.Except(current.Select(x => x.Product.Id)).ToArray()) { rows[id].Dispose(); rows.Remove(id); }
        foreach (var line in lines)
        {
            if (!rows.TryGetValue(line.Product.Id, out var row))
            { row = new CartProductRow(data, line); rows[line.Product.Id] = row; Controls.Add(row); }
            row.RefreshQuantity();
        }
        if (!supplier.Manual)
        {
            string text = $"소계 {data.Subtotal(lines):N0}원"; if (subtotal.Text != text) subtotal.Text = text;
            decimal shortage = data.Shortfall(supplier, lines);
            string action = shortage > 0 ? $"{shortage:N0}원 부족" : "주문하기";
            if (order.Text != action) order.Text = action;
            order.BackColor = shortage > 0 ? Ui.Danger : Ui.Accent; order.ForeColor = Color.White;
            order.Enabled = shortage == 0;
        }
        if (structural) ResumeLayout(true);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (heading == null) return;
        int unit = Math.Max(19, Font.Height), x = 10, width = Math.Max(100, Width - 20), y = 10;
        heading.SetBounds(x, y, width, unit + 8); y += heading.Height + 6;
        foreach (var line in lines)
        {
            var row = rows[line.Product.Id]; int height = row.GetPreferredSize(new Size(width, 0)).Height;
            row.SetBounds(x, y, width, height); y += height + 10;
        }
        if (!supplier.Manual)
        {
            subtotal.SetBounds(x, y, width, unit + 8); y += subtotal.Height + 6;
            order.SetBounds(x, y, width, Math.Max(40, unit * 2)); y += order.Height + 10;
        }
        if (Height != y + 4) Height = y + 4;
    }
}
