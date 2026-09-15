namespace CafeOrder;

// Shared by the catalog and the add-product result; all information remains selectable.
public sealed class ProductCard : Panel
{
    public Product Product { get; }
    private readonly PictureBox picture;
    private readonly TextBox title, seller, price;
    private readonly Label status;
    private readonly Button add;
    private readonly ToolTip tooltip = new();
    public ProductCard(Product product, Action<Product> onAdd)
    {
        Product = product; Name = $"Product_{product.Id}"; BackColor = Color.White;
        DoubleBuffered = true; Margin = Padding.Empty;
        picture = SampleImages.Picture(product.Category, $"Image_{product.Id}");
        title = Ui.CopyText(product.Name, $"Name_{product.Id}", true);
        seller = Ui.CopyText(product.Supplier.Name, $"Seller_{product.Id}");
        price = Ui.CopyText(product.PriceText, $"Price_{product.Id}");
        status = new Label { Text = product.Available ? "주문가능" : "품절", ForeColor = product.Available ? Ui.Accent : Ui.Danger };
        add = Ui.Button("장바구니에 담기", () => onAdd(product), true, $"Add_{product.Id}");
        add.AutoSize = false; add.Enabled = product.Available;
        if (!product.Available) { add.BackColor = Color.FromArgb(226, 230, 227); add.ForeColor = Color.DimGray; }
        tooltip.SetToolTip(title, product.Name);
        Controls.AddRange([picture, title, seller, price, status, add]);
    }
    private int Unit => Math.Max(Font.Height, 19);
    public override Size GetPreferredSize(Size proposedSize)
        => new(proposedSize.Width, Math.Max(40, proposedSize.Width - 20) + Unit * 9 + 70);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (picture == null) return;
        int gap = Math.Max(6, Unit / 3), inset = 10, width = Math.Max(40, ClientSize.Width - inset * 2), y = inset;
        picture.SetBounds(inset, y, width, width); y += width + gap;
        title.SetBounds(inset, y, width, Unit * 3 + 2); y += title.Height + gap;
        seller.SetBounds(inset, y, width, Unit + 3); y += seller.Height + 2;
        price.SetBounds(inset, y, width, Unit * 2 + 2); y += price.Height + 2;
        status.SetBounds(inset, y, width, Unit + 3); y += status.Height + gap;
        add.SetBounds(inset, y, width, Math.Max(40, Unit * 2));
    }
    protected override void Dispose(bool disposing) { if (disposing) tooltip.Dispose(); base.Dispose(disposing); }
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
