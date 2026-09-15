namespace CafeOrder;

// Shared by the catalog and the add-product result; all information remains selectable.
public sealed class ProductCard : Panel
{
    public Product Product { get; }
    private readonly PictureBox picture, seller;
    private readonly TextBox title, price;
    private readonly SampleData data;
    private readonly Button add;
    private readonly ToolTip tooltip = new();
    public ProductCard(Product product, SampleData data)
    {
        this.data = data;
        Product = product; Name = $"Product_{product.Id}"; BackColor = Color.White;
        DoubleBuffered = true; Margin = Padding.Empty;
        picture = SampleImages.Picture(product.Category, $"Image_{product.Id}");
        title = Ui.CopyText(product.Name, $"Name_{product.Id}", true);
        seller = new PictureBox { Name = $"Seller_{product.Id}", Image = SampleImages.Seller(product.Supplier), SizeMode = PictureBoxSizeMode.Zoom };
        price = Ui.CopyText(product.PriceText, $"Price_{product.Id}");
        price.TextAlign = HorizontalAlignment.Right;
        add = Ui.Button("장바구니에 담기", ActivateProduct, true, $"Add_{product.Id}"); add.AutoSize = false;
        tooltip.SetToolTip(title, product.Name);
        tooltip.SetToolTip(seller, product.Supplier.Name);
        Controls.AddRange([picture, title, seller, price, add]);
        data.ProductChanged += OnProductChanged; UpdateAvailability();
    }
    private void OnProductChanged(Product product) { if (product.Id == Product.Id) { Product.Available = product.Available; UpdateAvailability(); } }
    private void UpdateAvailability()
    { add.Text = Product.Available ? "장바구니에 담기" : "품절"; add.BackColor = Product.Available ? Ui.Accent : Ui.Danger; add.Enabled = Product.Available || !Product.Supplier.Manual; }
    private void ActivateProduct()
    {
        if (Product.Available) { data.AddToCart(Product); return; }
        if (Product.Supplier.Manual) return;
        add.Text = "확인 중..."; add.Enabled = false;
        BeginInvoke(() => { if (!IsDisposed) data.Recheck(Product); });
    }
    private int Unit => Math.Max(Font.Height, 19);
    public override Size GetPreferredSize(Size proposedSize)
        => new(proposedSize.Width, Math.Max(40, proposedSize.Width - 20) + Unit * 7 + 50);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (picture == null) return;
        int gap = Math.Max(6, Unit / 3), inset = 10, width = Math.Max(40, ClientSize.Width - inset * 2), y = inset;
        picture.SetBounds(inset, y, width, width); y += width + gap;
        title.SetBounds(inset, y, width, Unit * 3 + 2); y += title.Height + gap;
        int icon = Math.Max(28, Unit + 6);
        seller.SetBounds(inset, y, icon, icon);
        price.SetBounds(inset + icon + 6, y, width - icon - 6, Unit * 2 + 2); y += price.Height + gap;
        add.SetBounds(inset, y, width, Math.Max(40, Unit * 2));
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); UiBorder.Draw(e.Graphics, ClientRectangle); }
    protected override void Dispose(bool disposing) { if (disposing) { data.ProductChanged -= OnProductChanged; tooltip.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ProductGrid : BufferedPanel
{
    private ProductCard[] ordered = [];
    private bool arranging;
    public ProductGrid() { Name = "ProductList"; Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Background; }
    public void SetItems(ProductCard[] cards)
    {
        SuspendLayout(); ordered = cards;
        var visible = cards.ToHashSet();
        foreach (ProductCard card in Controls) card.Visible = visible.Contains(card);
        AutoScrollPosition = Point.Empty; ResumeLayout(false); PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging) return;
        arranging = true;
        try
        {
            base.OnLayout(e);
            int gap = 10, width = Math.Max(100, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * 4) / 3);
            int height = ordered.Length == 0 ? 0 : ordered.Max(x => x.GetPreferredSize(new Size(width, 0)).Height);
            for (int i = 0; i < ordered.Length; i++)
            {
                var bounds = new Rectangle(gap + i % 3 * (width + gap) + AutoScrollPosition.X,
                    gap + i / 3 * (height + gap) + AutoScrollPosition.Y, width, height);
                if (ordered[i].Bounds != bounds) ordered[i].Bounds = bounds;
            }
            var extent = new Size(0, gap + ((ordered.Length + 2) / 3) * (height + gap));
            if (AutoScrollMinSize != extent) AutoScrollMinSize = extent;
        }
        finally { arranging = false; }
    }
}

internal static class SampleImages
{
    private static readonly Dictionary<string, Image> cache = [];
    static SampleImages() { Application.ApplicationExit += (_, _) => { foreach (var image in cache.Values) image.Dispose(); cache.Clear(); }; }
    public static PictureBox Picture(string category, string name) => new()
    { Name = name, Image = Get(category), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(232, 238, 230), TabStop = false };
    public static Image Seller(Supplier supplier)
    {
        string key = "seller:" + supplier.Id;
        if (cache.TryGetValue(key, out var image)) return image;
        var bitmap = new Bitmap(48, 48); using var g = Graphics.FromImage(bitmap);
        Color color = supplier.Id switch { "mega" => Color.FromArgb(237, 183, 62), "naver" => Color.FromArgb(38, 154, 90),
            "coupang" => Color.FromArgb(57, 133, 181), "piece" => Color.FromArgb(154, 105, 132), _ => Ui.Accent };
        g.Clear(color); using var f = new Font("Malgun Gothic", 19, FontStyle.Bold);
        TextRenderer.DrawText(g, supplier.Id == "naver" ? "N" : supplier.Name[..1], f, new Rectangle(0, 0, 48, 48), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        cache[key] = bitmap; return bitmap;
    }
    private static Image Get(string category)
    {
        if (cache.TryGetValue(category, out var image)) return image;
        var bitmap = new Bitmap(256, 256);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(232, 238, 230));
        using var pen = new Pen(Ui.Accent, 5);
        graphics.DrawRectangle(pen, 84, 52, 88, 114);
        graphics.DrawLine(pen, 84, 70, 172, 70);
        using var font = new Font("Malgun Gothic", 16, FontStyle.Bold);
        using var format = new StringFormat { Alignment = StringAlignment.Center };
        using var brush = new SolidBrush(Ui.Accent);
        graphics.DrawString(category, font, brush, new RectangleF(8, 182, 240, 60), format);
        cache[category] = bitmap; return bitmap;
    }
}
