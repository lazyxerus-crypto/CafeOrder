namespace CafeOrder;

// One painted surface: every normal area shares the same click semantics, with no selectable text children.
public sealed class ProductCard : Panel
{
    public Product? Product;
    private readonly SampleData data;
    private readonly string draftCategory;
    private TextBox? url;
    private readonly Button undo;
    private Image? manual;
    private string feedback = "";
    private (int Width, float FontSize, string Text) priceDisplayKey;
    private string priceDisplay = "";
    private bool pressed, dragging, flashing;
    private Point pressedAt;
    private readonly System.Windows.Forms.Timer highlight = new() { Interval = 160 };
    private readonly ToolTip hint = new();
    private ContextMenuStrip? menu;
    public bool IsDraft => Product == null;
    internal Rectangle ImageBounds { get; private set; }
    internal Rectangle NameBounds { get; private set; }
    internal Rectangle PriceBounds { get; private set; }
    private Font Role(TypographyKey key, FontStyle style = FontStyle.Regular) => Ui.Fonts!.Font(key, style);
    private int InnerWidth(int width) => Math.Max(24, width - 20);
    private static readonly Dictionary<(int Width, float Size), int> headerHeights = [], priceHeights = [];
    private int HeaderHeight(int width)
    {
        var font = Role(TypographyKey.Category); var key = (width, font.Size);
        if (!headerHeights.TryGetValue(key, out int height))
        { if (headerHeights.Count > 128) headerHeights.Clear(); headerHeights[key] = height = Math.Max(30, SampleData.Categories.Skip(1).Max(c => TextRenderer.MeasureText(c, font, new Size(Math.Max(30, InnerWidth(width) - 38), 0), TextFormatFlags.WordBreak).Height)); }
        return height;
    }
    private int NameHeight => Role(TypographyKey.ProductName, FontStyle.Bold).Height * 3 + 4;
    private int PriceHeight(int width)
    {
        var font = Role(TypographyKey.ProductPrice); var key = (width, font.Size);
        if (!priceHeights.TryGetValue(key, out int height))
        { if (priceHeights.Count > 128) priceHeights.Clear(); priceHeights[key] = height = new[] { "14,500원 (메가회원가)", "108,000원" }.Max(text => TextRenderer.MeasureText(WrapPrice(text, font, InnerWidth(width)), font, new Size(InnerWidth(width), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2); }
        return height;
    }
    private static string WrapPrice(string text, Font font, int width)
    {
        var lines = new List<string>(); string line = "";
        foreach (char c in text)
        {
            if (line.Length > 0 && TextRenderer.MeasureText(line + c, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width > width) { lines.Add(line.TrimEnd()); line = ""; }
            if (line.Length > 0 || c != ' ') line += c;
        }
        lines.Add(line); return string.Join(Environment.NewLine, lines);
    }
    internal string PriceDisplayText
    {
        get
        {
            if (Product == null) return "";
            var font = Role(TypographyKey.ProductPrice); var key = (PriceBounds.Width, font.Size, Product.PriceText);
            if (priceDisplayKey != key) { priceDisplayKey = key; priceDisplay = WrapPrice(Product.PriceText, font, PriceBounds.Width); }
            return priceDisplay;
        }
    }
    public ProductCard(Product? product, SampleData data, string draftCategory = "기타")
    {
        this.data = data; Product = product; this.draftCategory = draftCategory;
        Name = product == null ? "Draft_" + Guid.NewGuid().ToString("N") : $"Product_{product.Id}";
        DoubleBuffered = true; BackColor = Color.White; AllowDrop = true; Margin = Padding.Empty; TabStop = true;
        undo = Ui.Button("되돌리기", () => Run(() => data.SetActive(Product!, true)), name: "UndoProduct");
        undo.AutoSize = false; undo.Visible = false; Controls.Add(undo);
        if (IsDraft)
        {
            url = new TextBox { Name = "DraftUrl", PlaceholderText = "https://", MaxLength = 2048 };
            Ui.Role(url, TypographyKey.General); Controls.Add(url);
            url.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RegisterDraft(); } };
        }
        highlight.Tick += (_, _) => { highlight.Stop(); flashing = false; Invalidate(); };
        data.ProductChanged += Changed;
        if (Product != null) LoadManual();
        SyncState();
    }
    private void Changed(Product product) { if (Product?.Id == product.Id) { Product = product; SyncState(); PerformLayout(); Invalidate(); } }
    private void SyncState()
    {
        undo.Visible = Product is { IsActive: false };
        hint.SetToolTip(this, Product?.Name ?? "상품 링크 입력 후 Enter");
        Cursor = Product is { IsActive: true, Available: true } ? Cursors.Hand : Cursors.Default;
    }
    private void Run(Action action)
    {
        try { action(); feedback = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ImageMagick.MagickException)
        { feedback = ex is ArgumentException ? ex.Message : "처리하지 못했습니다. 다시 시도해주세요"; }
        Invalidate(); Parent?.PerformLayout();
    }
    internal void RegisterDraft()
    {
        if (url == null || string.IsNullOrWhiteSpace(url.Text)) return;
        Run(() =>
        {
            Product = data.RegisterMock(url.Text, draftCategory); Name = $"Product_{Product.Id}";
            url.Dispose(); url = null; SyncState(); PerformLayout(); data.CatalogUpdated();
        });
    }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width,
        10 + HeaderHeight(proposedSize.Width) + 6 + InnerWidth(proposedSize.Width) + 6 + NameHeight + 3 + PriceHeight(proposedSize.Width) + 10 + (feedback.Length > 0 ? Role(TypographyKey.General).Height * 2 : 0));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (undo == null || Ui.Fonts == null) return;
        int inner = InnerWidth(Width), y = 10 + HeaderHeight(Width) + 6;
        ImageBounds = new Rectangle(10, y, inner, inner); y += inner + 6;
        NameBounds = new Rectangle(10, y, inner, NameHeight); y += NameHeight + 3;
        PriceBounds = new Rectangle(10, y, inner, PriceHeight(Width));
        url?.SetBounds(10, NameBounds.Top + Role(TypographyKey.General).Height + 6, inner, Role(TypographyKey.General).Height + 10);
        int undoWidth = Math.Min(inner, Math.Max(100, undo.GetPreferredSize(Size.Empty).Width));
        undo.SetBounds((Width - undoWidth) / 2, Height / 2 + 4, undoWidth, Ui.ActionHeight(undo));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Ui.Fonts == null) return;
        var g = e.Graphics;
        if (pressed || flashing) { using var flash = new SolidBrush(Color.FromArgb(230, 241, 232)); g.FillRectangle(flash, ClientRectangle); }
        int header = HeaderHeight(Width);
        if (Product != null) g.DrawImage(SampleImages.Seller(Product.Supplier), new Rectangle(10, 10, 28, 28));
        TextRenderer.DrawText(g, Product?.Category ?? draftCategory, Role(TypographyKey.Category), new Rectangle(48, 10, Math.Max(24, Width - 58), header), Ui.Ink,
            TextFormatFlags.Right | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (Product == null)
        {
            using var brush = new SolidBrush(Color.FromArgb(232, 238, 230)); g.FillRectangle(brush, ImageBounds);
            TextRenderer.DrawText(g, "상품 링크", Role(TypographyKey.General), NameBounds, Ui.Ink, TextFormatFlags.Top | TextFormatFlags.Left);
        }
        else
        {
            var image = manual ?? SampleImages.Catalog(Product.Category);
            // Both sources are square; manual files are center-cropped on import.
            g.DrawImage(image, ImageBounds);
            if (manual != null)
            {
                int badgeSize = Math.Max(22, Role(TypographyKey.Category, FontStyle.Bold).Height + 2);
                var badge = new Rectangle(ImageBounds.Right - badgeSize - 4, ImageBounds.Top + 4, badgeSize, badgeSize);
                using var brush = new SolidBrush(Ui.Accent); g.FillRectangle(brush, badge);
                TextRenderer.DrawText(g, "M", Role(TypographyKey.Category, FontStyle.Bold), badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, Product.Name, Role(TypographyKey.ProductName, FontStyle.Bold), NameBounds, Ui.Ink, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            TextRenderer.DrawText(g, PriceDisplayText, Role(TypographyKey.ProductPrice), PriceBounds, Ui.Ink, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (!Product.Available) TextRenderer.DrawText(g, "품절 · 클릭하여 재확인", Role(TypographyKey.General), ImageBounds, Ui.Danger, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            if (!Product.IsActive)
            {
                using var red = new SolidBrush(Color.FromArgb(150, 190, 45, 45)); g.FillRectangle(red, ClientRectangle);
                int textHeight = Math.Max(40, Role(TypographyKey.SectionTitle, FontStyle.Bold).Height + 4);
                TextRenderer.DrawText(g, "삭제됨", Role(TypographyKey.SectionTitle, FontStyle.Bold), new Rectangle(0, Height / 2 - textHeight, Width, textHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        if (feedback.Length > 0) TextRenderer.DrawText(g, feedback, Role(TypographyKey.General), new Rectangle(10, PriceBounds.Bottom, Width - 20, Height - PriceBounds.Bottom), Ui.Danger, TextFormatFlags.WordBreak);
        UiBorder.Draw(g, ClientRectangle);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left && Product is { IsActive: true }; dragging = false; pressedAt = e.Location; Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); if (pressed && (Math.Abs(e.X - pressedAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - pressedAt.Y) > SystemInformation.DragSize.Height)) dragging = true; }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); bool activate = pressed && !dragging && ClientRectangle.Contains(e.Location); pressed = false;
        if (e.Button == MouseButtons.Right && Product is { IsActive: true }) { menu?.Dispose(); menu = BuildMenu(ImageBounds.Contains(e.Location)); menu.Show(this, e.Location); }
        else if (e.Button == MouseButtons.Left && activate && Product is { IsActive: true } product)
        {
            if (!product.Available) data.Recheck(product);
            else if (!data.IsLocked(product)) { data.AddToCart(product); flashing = true; highlight.Stop(); highlight.Start(); }
        }
        Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); pressed = false; Invalidate(); }
    internal ContextMenuStrip BuildMenu(bool imageArea)
    {
        var context = new ContextMenuStrip(); if (Product is not { IsActive: true } product) return context;
        if (imageArea && manual != null) { context.Items.Add("수동 이미지 삭제", null, (_, _) => Run(RemoveManual)); context.Items.Add(new ToolStripSeparator()); }
        context.Items.Add("상품 삭제", null, (_, _) => Run(() => data.SetActive(product, false)));
        var categories = new ToolStripMenuItem("카테고리 변경");
        foreach (var category in SampleData.Categories.Skip(1)) categories.DropDownItems.Add(category, null, (_, _) => Run(() => data.SetCategory(product, category)));
        context.Items.Add(categories); context.Items.Add(new ToolStripSeparator());
        context.Items.Add("이름 복사", null, (_, _) => Copy(product.Name));
        context.Items.Add("가격 복사", null, (_, _) => Copy(product.PriceText));
        context.Items.Add("이름 + 가격 복사", null, (_, _) => Copy(product.Name + Environment.NewLine + product.PriceText));
        return context;
    }
    private void Copy(string text) { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { feedback = "복사하지 못했습니다"; Invalidate(); } }
    private void LoadManual()
    {
        manual?.Dispose(); manual = null;
        string path = data.Store.ManualImagePath(Product!.Id);
        if (File.Exists(path)) Run(() => manual = ManualImages.Load(path));
    }
    internal void SetManual(string source)
    {
        if (Product is not { IsActive: true }) return;
        ManualImages.Save(source, data.Store.ManualImagePath(Product.Id)); LoadManual(); Invalidate();
    }
    internal void RemoveManual()
    {
        if (Product == null) return;
        File.Delete(data.Store.ManualImagePath(Product.Id)); manual?.Dispose(); manual = null; Invalidate();
    }
    protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); dragging = true; pressed = false; UpdateDrop(e); }
    protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); UpdateDrop(e); }
    private void UpdateDrop(DragEventArgs e) => e.Effect = Product is { IsActive: true } && ImageBounds.Contains(PointToClient(new Point(e.X, e.Y))) && e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } ? DragDropEffects.Copy : DragDropEffects.None;
    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e); UpdateDrop(e); pressed = false;
        if (e.Effect == DragDropEffects.Copy && e.Data?.GetData(DataFormats.FileDrop) is string[] files) Run(() => SetManual(files[0]));
        dragging = false;
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { data.ProductChanged -= Changed; highlight.Dispose(); hint.Dispose(); manual?.Dispose(); menu?.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ProductGrid : BufferedPanel
{
    private ProductCard[] ordered = [];
    private bool arranging;
    internal int Columns = 3;
    public ProductGrid() { Name = "ProductList"; Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Background; }
    public void SetItems(ProductCard[] cards, bool resetScroll = true)
    {
        SuspendLayout(); ordered = cards; var visible = cards.ToHashSet();
        foreach (ProductCard card in Controls) card.Visible = visible.Contains(card);
        if (resetScroll) AutoScrollPosition = Point.Empty; ResumeLayout(false); PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging || Ui.Fonts == null) return; arranging = true;
        try
        {
            base.OnLayout(e); int gap = 10, width = Math.Max(40, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth - gap * (Columns + 1)) / Columns);
            int height = ordered.Length == 0 ? 0 : ordered.Max(x => x.GetPreferredSize(new Size(width, 0)).Height);
            for (int i = 0; i < ordered.Length; i++) ordered[i].Bounds = new Rectangle(gap + i % Columns * (width + gap) + AutoScrollPosition.X, gap + i / Columns * (height + gap) + AutoScrollPosition.Y, width, height);
            var extent = new Size(0, gap + ((ordered.Length + Columns - 1) / Columns) * (height + gap));
            if (AutoScrollMinSize != extent) AutoScrollMinSize = extent; AdjustFormScrollbars(true);
        }
        finally { arranging = false; }
    }
}
internal static class SampleImages
{
    public static Image Catalog(string category) => Get(category);
    private static readonly Dictionary<string, Image> cache = [];
    static SampleImages() { Application.ApplicationExit += (_, _) => { foreach (var image in cache.Values) image.Dispose(); cache.Clear(); }; }
    public static PictureBox Picture(string category, string name) => new()
    { Name = name, Image = Get(category), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(232, 238, 230), TabStop = false };
    public static Image Seller(Supplier supplier)
    {
        string key = "seller:" + supplier.Id;
        if (cache.TryGetValue(key, out var image)) return image;
        var bitmap = LoadSeller(Path.Combine(AppContext.BaseDirectory, "Assets", "Sellers", supplier.Id + ".png"));
        cache[key] = bitmap; return bitmap;
    }
    internal static Image LoadSeller(string path)
    {
        try { using var source = Image.FromFile(path); return (Image)source.Clone(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            // Missing/invalid assets affect this seller only; keep the fixed icon slot.
            var placeholder = new Bitmap(32, 32); using var g = Graphics.FromImage(placeholder);
            g.Clear(Color.FromArgb(232, 238, 230)); using var pen = new Pen(Ui.Accent, 2);
            g.DrawRectangle(pen, 6, 11, 20, 16); g.DrawLine(pen, 5, 10, 27, 10); g.DrawRectangle(pen, 12, 18, 7, 9);
            return placeholder;
        }
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
