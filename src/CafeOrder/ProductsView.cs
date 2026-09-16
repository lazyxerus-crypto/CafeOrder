namespace CafeOrder;

public sealed class ProductsView : UserControl
{
    private readonly SampleData data;
    private readonly ProductGrid products = new();
    private readonly FlowLayoutPanel cart = Ui.List("CartList");
    private readonly TextBox search = new() { Name = "Search", Dock = DockStyle.Fill, PlaceholderText = "상품명 검색", Margin = new Padding(4, 6, 8, 6) };
    private readonly ComboBox category = Ui.Combo(SampleData.Categories, "CategoryFilter");
    private readonly ComboBox supplier;
    private readonly Label count = Ui.SectionHeading("상품 0개");
    private readonly System.Windows.Forms.Timer emphasis = new() { Interval = 200 };
    private CartProductRow? highlighted;
    private readonly List<ProductCard> drafts = [];
    private readonly Dictionary<int, ProductCard> productCards = [];
    private readonly Dictionary<string, SupplierCartCard> supplierCards = [];
    private readonly Label emptyCart = Ui.Text("장바구니가 비었습니다.");
    private readonly Button orderAll;

    public ProductsView(SampleData data)
    {
        this.data = data; Dock = DockStyle.Fill; DoubleBuffered = true;
        supplier = Ui.Combo(new[] { "전체 판매처" }.Concat(data.Suppliers.Select(x => x.Name)), "SupplierFilter");
        var root = new TableLayoutPanel { Name = "ProductColumns", Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        var filters = new FilterTable { Name = "Filters", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, RowCount = 4 };
        foreach (float percent in new[] { 50f, 50f })
        { filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent)); }
        Control[] filterControls = [Ui.Text("카테고리"), category, Ui.Text("판매처"), supplier];
        for (int i = 0; i < filterControls.Length; i++) filters.Controls.Add(filterControls[i], i, 0);
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (int i = 0; i < 4; i++) filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var feedback = Ui.Text(""); feedback.Name = "TransferStatus"; feedback.Visible = false;
        void Transfer(string action) { feedback.Text = $"xlsx {action}는 저장소 연결 후 사용할 수 있습니다."; feedback.Visible = true; }
        var actions = Ui.Row(Ui.Button("상품 추가", AddDraft, name: "AddProduct"), Ui.Button("내보내기", () => Transfer("내보내기"), name: "ExportProducts"), Ui.Button("가져오기", () => Transfer("가져오기"), name: "ImportProducts"));
        actions.WrapContents = false; filters.Controls.Add(actions, 4, 0);
        var searchLabel = Ui.Text("검색"); filters.Controls.Add(searchLabel, 0, 1); filters.Controls.Add(Ui.Role(search, TypographyKey.General), 1, 1); filters.SetColumnSpan(search, 4);
        filters.Controls.Add(feedback, 0, 3); filters.SetColumnSpan(feedback, 5);
        bool arrangingFilters = false, stackedActions = false;
        void FitToolbar()
        {
            if (arrangingFilters) return;
            int dropdownWidth = Math.Max(Math.Max(180, TextRenderer.MeasureText(category.Text, category.Font).Width + 32), TextRenderer.MeasureText(supplier.Text, supplier.Font).Width + 32);
            int needed = actions.GetPreferredSize(Size.Empty).Width + filterControls[0].PreferredSize.Width + filterControls[2].PreferredSize.Width + dropdownWidth * 2 + 32;
            bool stacked = filters.Width < needed;
            if (stacked == stackedActions) return;
            arrangingFilters = true; filters.SuspendLayout();
            try
            {
                stackedActions = stacked;
                filters.SetCellPosition(actions, new TableLayoutPanelCellPosition(stacked ? 0 : 4, stacked ? 1 : 0)); filters.SetColumnSpan(actions, stacked ? 5 : 1);
                filters.ColumnStyles[4].SizeType = stacked ? SizeType.Absolute : SizeType.AutoSize; filters.ColumnStyles[4].Width = 0;
                filters.SetRow(searchLabel, stacked ? 2 : 1); filters.SetRow(search, stacked ? 2 : 1);
            }
            finally { filters.ResumeLayout(true); arrangingFilters = false; }
        }
        filters.SizeChanged += (_, _) => FitToolbar(); actions.FontChanged += (_, _) => FitToolbar();
        category.FontChanged += (_, _) => FitToolbar(); supplier.FontChanged += (_, _) => FitToolbar();
        category.SelectedIndexChanged += (_, _) => FitToolbar(); supplier.SelectedIndexChanged += (_, _) => FitToolbar();
        foreach (Control action in actions.Controls) action.FontChanged += (_, _) => FitToolbar();
        var left = new SoftPanel { Name = "ProductRegion", Dock = DockStyle.Fill, BackColor = Color.FromArgb(237, 243, 239) };
        products.BackColor = left.BackColor;
        left.Controls.Add(products); left.Controls.Add(count);
        var right = new SoftPanel { Name = "CartRegion", Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 241, 235) };
        cart.BackColor = right.BackColor;
        orderAll = Ui.Button("전체 주문하기", () => OpenOrders(data.Cart.ToArray()), true, "OrderAll");
        orderAll.Dock = DockStyle.Bottom; orderAll.AutoSize = false; orderAll.Height = Math.Max(50, Ui.ActionHeight(orderAll, 350));
        right.SizeChanged += (_, _) => orderAll.Height = Math.Max(50, Ui.ActionHeight(orderAll, right.ClientSize.Width - right.Padding.Horizontal));
        right.Controls.Add(cart); right.Controls.Add(Ui.SectionHeading("장바구니")); right.Controls.Add(orderAll);
        root.Controls.Add(filters, 0, 0); root.SetColumnSpan(filters, 2);
        root.Controls.Add(left, 0, 1); root.Controls.Add(right, 1, 1); Controls.Add(root);
        products.SuspendLayout();
        foreach (var p in data.Products.Where(p => p.IsActive))
        {
            var card = new ProductCard(p, data); productCards[p.Id] = card; products.Controls.Add(card);
        }
        products.ResumeLayout(false);
        search.TextChanged += (_, _) => FilterProducts(); category.SelectedIndexChanged += (_, _) => FilterProducts();
        supplier.SelectedIndexChanged += (_, _) => FilterProducts();
        data.CartChanged += SyncCart;
        data.ProductAdded += BringSellerFirst;
        data.CatalogChanged += CatalogChanged; data.ProductChanged += ProductChanged;
        emphasis.Tick += (_, _) => { emphasis.Stop(); if (highlighted is { IsDisposed: false }) highlighted.Highlight(false); highlighted = null; };
        ApplyColumns(); FilterProducts(); SyncCart();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { data.CartChanged -= SyncCart; data.ProductAdded -= BringSellerFirst; data.CatalogChanged -= CatalogChanged; data.ProductChanged -= ProductChanged; emphasis.Dispose(); emptyCart.Dispose(); }
        base.Dispose(disposing);
    }
    public void ApplyColumns() { products.Columns = data.Store.Preferences.Columns; products.PerformLayout(); }
    private void AddDraft()
    {
        var draft = new ProductCard(null, data, category.SelectedIndex > 0 ? category.Text : "기타");
        drafts.Insert(0, draft); products.Controls.Add(draft); FilterProducts(); products.AutoScrollPosition = Point.Empty;
        draft.Controls.OfType<TextBox>().Single().Focus();
    }
    private void CatalogChanged()
    {
        foreach (var card in drafts.Where(d => !d.IsDraft).ToArray()) { drafts.Remove(card); productCards[card.Product!.Id] = card; }
        FilterProducts();
    }
    private void ProductChanged(Product _) => FilterProducts(false);
    private void FilterProducts(bool resetScroll = true)
    {
        IEnumerable<Product> list = productCards.Values.Select(c => c.Product!).Where(p => p.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && (category.SelectedIndex == 0 || p.Category == category.Text)
            && (supplier.SelectedIndex == 0 || p.Supplier.Name == supplier.Text));
        list = list.OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category)).ThenBy(p => p.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false));
        var visible = list.Select(p => productCards[p.Id]).ToArray();
        count.Text = $"상품 {visible.Count(c => c.Product!.IsActive)}개";
        products.SetItems(drafts.Concat(visible).ToArray(), resetScroll);
    }
    private void BringSellerFirst(Product product)
    {
        if (!supplierCards.TryGetValue(product.Supplier.Id, out var card)) return;
        if (highlighted is { IsDisposed: false }) highlighted.Highlight(false);
        cart.Controls.SetChildIndex(card, 0); cart.PerformLayout(); cart.AutoScrollPosition = Point.Empty;
        highlighted = card.Row(product.Id); highlighted.Highlight(true); emphasis.Stop(); emphasis.Start();
    }
    private void SyncCart()
    {
        // Keep each supplier and row alive. Quantity-only changes do not touch the control tree.
        var groups = data.Cart.GroupBy(x => x.Product.Supplier).ToArray();
        foreach (string id in supplierCards.Keys.Except(groups.Select(x => x.Key.Id)).ToArray())
        { supplierCards[id].Dispose(); supplierCards.Remove(id); }
        foreach (var group in groups)
        {
            if (!supplierCards.TryGetValue(group.Key.Id, out var card))
            {
                card = new SupplierCartCard(data, group.Key, OpenOrders); supplierCards[group.Key.Id] = card;
                cart.Controls.Add(card);
            }
            card.Sync(group.ToArray());
        }
        if (groups.Length == 0 && emptyCart.Parent == null) cart.Controls.Add(emptyCart);
        else if (groups.Length != 0 && emptyCart.Parent != null) cart.Controls.Remove(emptyCart);
        orderAll.Enabled = data.Cart.Count != 0;
    }
    internal static void AddRow(TableLayoutPanel card, Control control)
    { card.RowStyles.Add(new RowStyle(SizeType.AutoSize)); card.Controls.Add(control, 0, card.RowCount++); }
    private void OpenOrders(CartLine[] lines)
    {
        if (lines.Length == 0) return;
        using var dialog = new OrderForm(data, lines); dialog.ShowDialog(FindForm());
    }
}
