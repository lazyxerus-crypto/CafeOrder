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
    private SupplierCartCard? highlighted;
    private readonly Dictionary<int, ProductCard> productCards = [];
    private readonly Dictionary<string, SupplierCartCard> supplierCards = [];
    private readonly Label emptyCart = Ui.Text("장바구니가 비었습니다.");
    private readonly Button orderAll;

    public ProductsView(SampleData data)
    {
        this.data = data; Dock = DockStyle.Fill; DoubleBuffered = true;
        supplier = Ui.Combo(new[] { "전체 판매처" }.Concat(data.Suppliers.Select(x => x.Name)), "SupplierFilter");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var filters = new TableLayoutPanel { Name = "Filters", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 2 };
        foreach (float percent in new[] { 50f, 50f })
        { filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent)); }
        Control[] filterControls = [Ui.Text("카테고리"), category, Ui.Text("판매처"), supplier];
        for (int i = 0; i < filterControls.Length; i++) filters.Controls.Add(filterControls[i], i, 0);
        filters.Controls.Add(Ui.Text("검색"), 0, 1); filters.Controls.Add(search, 1, 1); filters.SetColumnSpan(search, 3);
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        var left = new SoftPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(237, 243, 239) };
        products.BackColor = left.BackColor;
        left.Controls.Add(products); left.Controls.Add(count);
        var right = new SoftPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 241, 235) };
        cart.BackColor = right.BackColor;
        orderAll = Ui.Button("전체 주문하기", () => OpenOrders(data.Cart.ToArray()), true, "OrderAll");
        orderAll.Dock = DockStyle.Bottom; orderAll.AutoSize = false; orderAll.Height = 50;
        right.Controls.Add(cart); right.Controls.Add(Ui.SectionHeading("장바구니")); right.Controls.Add(orderAll);
        split.Controls.Add(left, 0, 0); split.Controls.Add(right, 1, 0);
        root.Controls.Add(filters, 0, 0); root.Controls.Add(split, 0, 1); Controls.Add(root);
        products.SuspendLayout();
        foreach (var p in data.Products)
        {
            var card = new ProductCard(p, data); productCards[p.Id] = card; products.Controls.Add(card);
        }
        products.ResumeLayout(false);
        search.TextChanged += (_, _) => FilterProducts(); category.SelectedIndexChanged += (_, _) => FilterProducts();
        supplier.SelectedIndexChanged += (_, _) => FilterProducts();
        data.CartChanged += SyncCart;
        data.ProductAdded += BringSellerFirst;
        emphasis.Tick += (_, _) => { emphasis.Stop(); if (highlighted is { IsDisposed: false }) highlighted.BackColor = Color.White; highlighted = null; };
        FilterProducts(); SyncCart();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { data.CartChanged -= SyncCart; data.ProductAdded -= BringSellerFirst; emphasis.Dispose(); emptyCart.Dispose(); }
        base.Dispose(disposing);
    }
    private void FilterProducts()
    {
        IEnumerable<Product> list = data.Products.Where(p => p.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && (category.SelectedIndex == 0 || p.Category == category.Text)
            && (supplier.SelectedIndex == 0 || p.Supplier.Name == supplier.Text));
        list = list.OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category)).ThenBy(p => p.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false));
        var visible = list.Select(p => productCards[p.Id]).ToArray();
        count.Text = $"상품 {visible.Length}개";
        products.SetItems(visible);
    }
    private void BringSellerFirst(string id)
    {
        if (!supplierCards.TryGetValue(id, out var card)) return;
        if (highlighted is { IsDisposed: false }) highlighted.BackColor = Color.White;
        cart.Controls.SetChildIndex(card, 0); cart.AutoScrollPosition = Point.Empty;
        highlighted = card; card.BackColor = Color.FromArgb(221, 238, 225); emphasis.Stop(); emphasis.Start();
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
