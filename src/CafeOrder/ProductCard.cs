namespace CafeOrder;

// One painted surface: every normal area shares the same click semantics, with no selectable text children.
public sealed class ProductCard : Panel
{
    public Product? Product;
    private readonly SampleData data;
    private readonly Func<string, Task<MegaProductLookupResult>>? megaLookup;
    private readonly Func<string, Task<MegaProductLookupResult>>? pieceLookup;
    private readonly Func<string, Task<MegaProductLookupResult>>? nuldamLookup;
    private string draftCategory;
    internal event Action? Registered;
    internal event Action? DeleteDraft;
    private TextBox? url;
    private readonly Button undo;
    private Image? selectedImage;
    private bool ownsImage, manualImage;
    private string feedback = "";
    private bool pressed, dragging, flashing;
    private Point pressedAt;
    private readonly System.Windows.Forms.Timer highlight = new() { Interval = 160 };
    private readonly ToolTip hint = new() { ShowAlways = true };
    private readonly System.Windows.Forms.Timer hover = new() { Interval = 450 };
    private Point hoverPoint;
    private bool checking;
    private ContextMenuStrip? menu;
    public bool IsDraft => Product == null;
    internal Rectangle ImageBounds { get; private set; }
    internal Rectangle NameBounds { get; private set; }
    internal Rectangle PriceBounds { get; private set; }
    private Font Role(TypographyKey key, FontStyle style = FontStyle.Regular) => Ui.Fonts!.Font(key, style);
    private int InnerWidth(int width) => Math.Max(24, width - 20);
    private int HeaderHeight(int width) => Math.Max(30, Role(TypographyKey.Category, FontStyle.Bold).Height + 2);
    private int NameHeight => TextRenderer.MeasureText("가\n가\n가", Role(TypographyKey.ProductName, FontStyle.Bold),
        new Size(10000, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + 4;
    private int PriceHeight(int width) => Role(TypographyKey.ProductPrice).Height + 2;
    internal string PriceDisplayText => Product?.PriceText ?? "";
    internal Rectangle SellerBounds => new(10, 10, 28, 28);
    internal Rectangle CategoryBounds => new(48, 10, Math.Max(24, Width - 58), HeaderHeight(Width));
    internal string TooltipAt(Point location) => Product == null ? (CategoryBounds.Contains(location) ? draftCategory : "상품 링크 입력 후 Enter") :
        SellerBounds.Contains(location) ? SellerLinks.Hint(Product.Supplier, SellerLinks.ProductHome(Product)) :
        CategoryBounds.Contains(location) ? Product.Category : PriceBounds.Contains(location) ? Product.PriceText : Product.Name;
    internal static System.Diagnostics.ProcessStartInfo LinkStartInfo(string url) => new(url) { UseShellExecute = true };
    private void ArmHint(Point point)
    {
        hoverPoint = point; hint.SetToolTip(this, TooltipAt(point)); hover.Stop(); hover.Start();
    }
    private void ShowMenu(Point point)
    {
        hover.Stop(); hint.Hide(this); menu?.Dispose(); menu = BuildMenu();
        menu.Closed += (_, _) => ArmHint(point); menu.Show(this, point);
    }
    public ProductCard(Product? product, SampleData data, string draftCategory = "기타")
        : this(product, data, draftCategory, null) { }

    internal ProductCard(Product? product, SampleData data, string draftCategory,
        Func<string, Task<MegaProductLookupResult>>? megaLookup)
        : this(product, data, draftCategory, megaLookup, null) { }

    internal ProductCard(Product? product, SampleData data, string draftCategory,
        Func<string, Task<MegaProductLookupResult>>? megaLookup,
        Func<string, Task<MegaProductLookupResult>>? pieceLookup)
        : this(product, data, draftCategory, megaLookup, pieceLookup, null) { }

    internal ProductCard(Product? product, SampleData data, string draftCategory,
        Func<string, Task<MegaProductLookupResult>>? megaLookup,
        Func<string, Task<MegaProductLookupResult>>? pieceLookup,
        Func<string, Task<MegaProductLookupResult>>? nuldamLookup)
    {
        this.data = data; this.megaLookup = megaLookup; this.pieceLookup = pieceLookup;
        this.nuldamLookup = nuldamLookup;
        Product = product; this.draftCategory = draftCategory;
        Name = product == null ? "Draft_" + Guid.NewGuid().ToString("N") : $"Product_{product.Id}";
        DoubleBuffered = true; ResizeRedraw = true; BackColor = Color.White; AllowDrop = true; Margin = Padding.Empty; TabStop = true;
        undo = Ui.Button("되돌리기", () => Run(() => data.SetActive(Product!, true)), name: "UndoProduct");
        undo.AutoSize = false; undo.Visible = false; Controls.Add(undo);
        if (IsDraft)
        {
            url = new TextBox { Name = "DraftUrl", PlaceholderText = "https://", MaxLength = 2048 };
            Ui.Role(url, TypographyKey.General); Controls.Add(url); url.ContextMenuStrip = BuildMenu();
            url.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RegisterDraft(); } };
        }
        hover.Tick += (_, _) => { hover.Stop(); if (!IsDisposed && Visible && menu?.Visible != true) hint.Show(TooltipAt(hoverPoint), this, hoverPoint.X, hoverPoint.Y + 22, 5000); };
        highlight.Tick += (_, _) => { highlight.Stop(); flashing = false; Invalidate(); };
        data.ProductChanged += Changed;
        if (Product != null) LoadImages();
        SyncState();
    }
    private void Changed(Product product) { if (Product?.Id == product.Id) { Product = product; LoadImages(); SyncState(); PerformLayout(); Invalidate(); } }
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
        if (MegaCoffeeProductLookup.IsMegaHost(url.Text)) { _ = RegisterMegaDraftAsync(); return; }
        if (PieceCakeProductLookup.IsPieceHost(url.Text)) { _ = RegisterSiteDraftAsync("piece"); return; }
        if (NuldamProductLookup.IsNuldamHost(url.Text)) { _ = RegisterSiteDraftAsync("nuldam"); return; }
        void Register()
        {
            Product = data.RegisterMock(url.Text, draftCategory); Name = $"Product_{Product.Id}";
            url.ContextMenuStrip?.Dispose(); url.Dispose(); url = null; SyncState(); PerformLayout(); Registered?.Invoke();
        }
        Run(() => { if (Parent is ProductGrid grid) grid.UpdateInPlace(Register); else Register(); });
    }
    internal Task RegisterMegaDraftAsync() => RegisterSiteDraftAsync("mega");

    private async Task RegisterSiteDraftAsync(string supplierId)
    {
        if (url == null || !url.Enabled || string.IsNullOrWhiteSpace(url.Text)) return;
        var lookup = supplierId switch { "mega" => megaLookup, "piece" => pieceLookup, _ => nuldamLookup };
        string supplierName = supplierId switch { "mega" => "메가커피", "piece" => "파미유", _ => "널담" };
        if (lookup == null) { feedback = supplierName + " 조회를 사용할 수 없습니다"; Invalidate(); return; }
        string requestedUrl = url.Text.Trim();
        url.Enabled = false; feedback = "조회 중"; Invalidate();
        string? imagePath = null;
        bool lookupSucceeded = false, imageSaved = false;
        try
        {
            var result = await lookup(requestedUrl);
            if (IsDisposed || url == null) return;
            if (result.Status != MegaProductLookupStatus.Success || result.Product == null)
            {
                feedback = result.Status switch
                {
                    MegaProductLookupStatus.InvalidUrl => supplierName + " 상품 URL을 확인해주세요",
                    MegaProductLookupStatus.LoginRequired => supplierName + " 로그인이 필요합니다",
                    _ => "상품을 확인하지 못했습니다. 다시 시도해주세요"
                };
                return;
            }
            lookupSucceeded = true;
            imagePath = data.Store.NewWebImagePath();
            await Task.Run(() => ManualImages.Save(result.Product.ImageBytes, imagePath));
            imageSaved = true;
            if (IsDisposed || url == null) return;
            void Register()
            {
                Product = data.RegisterLookupProduct(result.Product, supplierId, draftCategory, imagePath);
                imagePath = null; Name = $"Product_{Product.Id}";
                url.ContextMenuStrip?.Dispose(); url.Dispose(); url = null;
                LoadImages(); SyncState(); PerformLayout(); Registered?.Invoke();
            }
            if (Parent is ProductGrid grid) grid.UpdateInPlace(Register); else Register();
            feedback = "";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR,
                !lookupSucceeded ? "PRODUCT_LOOKUP_EXCEPTION" : imageSaved ? "PRODUCT_SAVE_FAILED" : "IMAGE_SAVE_FAILED",
                !lookupSucceeded ? "상품조회 도중 예외가 발생했습니다." : imageSaved ? "조회한 상품을 DB에 등록하지 못했습니다." : "조회한 상품 이미지를 WebP로 저장하지 못했습니다.",
                supplier: supplierId, result: "FAILED", error: ex);
            feedback = "상품 저장에 실패했습니다. 다시 시도해주세요";
        }
        finally
        {
            if (imagePath != null) { try { File.Delete(imagePath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            if (!IsDisposed && url != null) url.Enabled = true;
            if (!IsDisposed) Invalidate();
        }
    }
    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width,
        10 + HeaderHeight(proposedSize.Width) + 6 + InnerWidth(proposedSize.Width) + 6 + NameHeight + 3 + PriceHeight(proposedSize.Width) + 10);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); if (undo == null || Ui.Fonts == null) return;
        Invalidate(); // Owner-painted content must redraw after geometry/font changes, including shrinking.
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
        if (Product is { Available: false, IsActive: true }) { using var tint = new SolidBrush(Color.FromArgb(255, 230, 230)); g.FillRectangle(tint, ClientRectangle); }
        int header = HeaderHeight(Width);
        if (Product != null) g.DrawImage(SampleImages.Seller(Product.Supplier), SellerBounds);
        TextRenderer.DrawText(g, Product?.Category ?? draftCategory, Role(TypographyKey.Category, FontStyle.Bold), CategoryBounds, Ui.Ink,
            TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Product == null)
        {
            using var brush = new SolidBrush(Color.FromArgb(232, 238, 230)); g.FillRectangle(brush, ImageBounds);
            TextRenderer.DrawText(g, "상품 링크", Role(TypographyKey.General), NameBounds, Ui.Ink, TextFormatFlags.Top | TextFormatFlags.Left);
        }
        else
        {
            var image = selectedImage ?? SampleImages.Catalog(Product.Category);
            // Both sources are square; manual files are center-cropped on import.
            g.DrawImage(image, ImageBounds);
            if (manualImage)
            {
                int badgeSize = Math.Max(22, Role(TypographyKey.Category, FontStyle.Bold).Height + 2);
                var badge = new Rectangle(ImageBounds.Right - badgeSize - 4, ImageBounds.Top + 4, badgeSize, badgeSize);
                using var brush = new SolidBrush(Ui.Accent); g.FillRectangle(brush, badge);
                TextRenderer.DrawText(g, "M", Role(TypographyKey.Category, FontStyle.Bold), badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            TextRenderer.DrawText(g, Product.Name, Role(TypographyKey.ProductName, FontStyle.Bold), NameBounds, Ui.Ink, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            TextRenderer.DrawText(g, PriceDisplayText, Role(TypographyKey.ProductPrice), PriceBounds, Ui.Ink, TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            if (!Product.Available)
            {
                using var tint = new SolidBrush(Color.FromArgb(155, 255, 215, 215)); g.FillRectangle(tint, ImageBounds);
                using var soldFont = new Font("Malgun Gothic", Math.Max(20, Role(TypographyKey.SectionTitle).Size), FontStyle.Bold);
                TextRenderer.DrawText(g, checking ? "확인 중" : "품절", soldFont, ImageBounds, Ui.Danger, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            }
            if (!Product.IsActive)
            {
                using var red = new SolidBrush(Color.FromArgb(150, 190, 45, 45)); g.FillRectangle(red, ClientRectangle);
                int textHeight = Math.Max(40, Role(TypographyKey.SectionTitle, FontStyle.Bold).Height + 4);
                TextRenderer.DrawText(g, "삭제됨", Role(TypographyKey.SectionTitle, FontStyle.Bold), new Rectangle(0, Height / 2 - textHeight, Width, textHeight), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        if (feedback.Length > 0) TextRenderer.DrawText(g, feedback, Role(TypographyKey.General), new Rectangle(10, ImageBounds.Bottom - Role(TypographyKey.General).Height * 2, Width - 20, Role(TypographyKey.General).Height * 2), Ui.Danger, TextFormatFlags.WordBreak);
        UiBorder.Draw(g, ClientRectangle);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    { base.OnMouseDown(e); hover.Stop(); hint.Hide(this); pressed = e.Button == MouseButtons.Left && Product is { IsActive: true }; dragging = false; pressedAt = e.Location; Invalidate(); }
    protected override void OnMouseMove(MouseEventArgs e)
    { base.OnMouseMove(e); if (e.Button == MouseButtons.None) { ArmHint(e.Location); Cursor = Product != null && SellerBounds.Contains(e.Location) ? (SellerLinks.ProductHome(Product) == null ? Cursors.Default : Cursors.Hand) : Product is { IsActive: true, Available: true } ? Cursors.Hand : Cursors.Default; } if (pressed && (Math.Abs(e.X - pressedAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - pressedAt.Y) > SystemInformation.DragSize.Height)) dragging = true; }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e); bool activate = pressed && !dragging && ClientRectangle.Contains(e.Location); pressed = false;
        if (e.Button == MouseButtons.Right && (IsDraft || Product is { IsActive: true })) ShowMenu(e.Location);
        else if (e.Button == MouseButtons.Left && Product != null && SellerBounds.Contains(pressedAt) && SellerBounds.Contains(e.Location) && !dragging)
            SellerLinks.Open(SellerLinks.ProductHome(Product));
        else if (e.Button == MouseButtons.Left && !SellerBounds.Contains(pressedAt) && activate && Product is { IsActive: true } product)
        {
            if (!product.Available && !product.Supplier.Manual && !checking)
            {
                checking = true; Invalidate(); Update();
                BeginInvoke(() => { if (IsDisposed) return; Run(() => data.Recheck(product)); checking = false; Invalidate(); });
            }
            else if (product.Available && !data.IsLocked(product)) Run(() => { data.AddToCart(product); flashing = true; highlight.Stop(); highlight.Start(); });
        }
        ArmHint(e.Location); Invalidate();
    }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover.Stop(); hint.Hide(this); pressed = false; Invalidate(); }
    internal ContextMenuStrip BuildMenu()
    {
        var context = new ContextMenuStrip(); var product = Product;
        if (product is { IsActive: false }) return context;
        context.Items.Add("상품 삭제", null, (_, _) => { if (product == null) DeleteDraft?.Invoke(); else Run(() => data.SetActive(product, false)); });
        var imageDelete = context.Items.Add("이미지 삭제", null, (_, _) => Run(RemoveManual)); imageDelete.Enabled = manualImage;
        var categories = new ToolStripMenuItem("카테고리 변경");
        foreach (var category in SampleData.Categories.Skip(1))
        {
            var item = new ToolStripMenuItem(category) { Checked = category == (product?.Category ?? draftCategory) };
            item.Click += (_, _) => { if (product == null) { draftCategory = category; Invalidate(); } else Run(() => data.SetCategory(product, category)); };
            categories.DropDownItems.Add(item);
        }
        categories.DropDownOpening += (_, _) => { foreach (ToolStripMenuItem item in categories.DropDownItems) item.Checked = item.Text == (Product?.Category ?? draftCategory); };
        context.Items.Add(categories);
        var link = context.Items.Add("상품 링크", null, (_, _) =>
        {
            try { System.Diagnostics.Process.Start(LinkStartInfo(product!.Url)); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { feedback = "상품 링크를 열지 못했습니다"; Invalidate(); }
        });
        link.Enabled = !string.IsNullOrWhiteSpace(product?.Url);
        context.Items.Add(new ToolStripSeparator());
        var name = context.Items.Add("이름 복사", null, (_, _) => Copy(product!.Name)); name.Enabled = !string.IsNullOrEmpty(product?.Name);
        var price = context.Items.Add("가격 복사", null, (_, _) => Copy(product!.PriceText)); price.Enabled = !string.IsNullOrEmpty(product?.PriceText);
        var both = context.Items.Add("이름 + 가격 복사", null, (_, _) => Copy(product!.Name + Environment.NewLine + product.PriceText)); both.Enabled = name.Enabled && price.Enabled;
        return context;
    }
    private void Copy(string text) { try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { feedback = "복사하지 못했습니다"; Invalidate(); } }
    private void LoadImages()
    {
        if (ownsImage) selectedImage?.Dispose();
        var selection = ProductImages.Resolve(Product!);
        selectedImage = selection.Image; ownsImage = selection.Owned; manualImage = selection.Manual;
    }
    internal void SetManual(string source)
    {
        if (Product is not { IsActive: true }) return;
        string path = data.Store.NewManualImagePath(Product.Id);
        bool converted = false;
        try { ManualImages.Save(source, path); converted = true; data.SetManualImage(Product, path); }
        catch (Exception ex)
        {
            if (File.Exists(path)) File.Delete(path);
            data.Store.Log.Write(LogLevel.ERROR, converted ? "PRODUCT_IMAGE_LINK_FAILED" : "IMAGE_SAVE_FAILED",
                converted ? "수동 이미지 경로를 DB에 저장하지 못했습니다." : "수동 이미지를 변환하거나 저장하지 못했습니다.",
                supplier: Product.Supplier.Id, productId: Product.Id, result: "FAILED", error: ex);
            throw;
        }
        Invalidate();
    }
    internal void RemoveManual()
    {
        if (Product == null) return;
        string? previous = Product.ManualImagePath;
        data.SetManualImage(Product, null);
        Invalidate();
        if (previous != null && File.Exists(previous)) File.Delete(previous);
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
    { if (disposing) { data.ProductChanged -= Changed; menu?.Dispose(); url?.ContextMenuStrip?.Dispose(); highlight.Dispose(); hover.Dispose(); hint.Dispose(); if (ownsImage) selectedImage?.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ProductGrid : BufferedPanel
{
    private ProductCard[] ordered = [];
    internal IReadOnlyList<ProductCard> Items => ordered;
    internal void Prepend(ProductCard card) => SetItems([card, .. ordered]);
    internal void RemoveItem(ProductCard card) => SetItems(ordered.Where(c => c != card).ToArray(), false);
    private bool arranging;
    private bool keepViewport;
    internal int Columns = 3;
    public ProductGrid() { Name = "ProductList"; Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Background; }
    internal void UpdateInPlace(Action update)
    {
        // Removing the focused URL textbox otherwise auto-scrolls the next focused card into view.
        var scroll = AutoScrollPosition; keepViewport = true; SuspendLayout();
        try { update(); }
        finally { AutoScrollPosition = new Point(-scroll.X, -scroll.Y); ResumeLayout(false); keepViewport = false; PerformLayout(); }
    }
    protected override Point ScrollToControl(Control activeControl) => keepViewport ? AutoScrollPosition : base.ScrollToControl(activeControl);
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
            base.OnLayout(e); int gap = 10, width = Math.Max(40, (Ui.ReservedClientWidth(this) - gap * (Columns + 1)) / Columns);
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
