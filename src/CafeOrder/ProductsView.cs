namespace CafeOrder;

public sealed class ProductsView : UserControl
{
    private readonly SampleData data;
    private readonly FlowLayoutPanel products = Ui.List("ProductList");
    private readonly FlowLayoutPanel cart = Ui.List("CartList");
    private readonly TextBox search = new() { Name = "Search", Dock = DockStyle.Fill, PlaceholderText = "상품명 검색", Margin = new Padding(4, 6, 8, 6) };
    private readonly ComboBox supplier;
    private readonly ComboBox sort = Ui.Combo(["기본순", "이름순", "가격순", "자주주문한순"], "Sort");
    private readonly Label count = Ui.Text("");
    private string category = "전체";
    private readonly List<Button> categoryButtons = [];

    public ProductsView(SampleData data)
    {
        this.data = data; Dock = DockStyle.Fill;
        supplier = Ui.Combo(new[] { "전체 발주처" }.Concat(data.Suppliers.Select(x => x.Name)), "SupplierFilter");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var filters = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6 };
        foreach (var style in new[] { new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 50),
            new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 28),
            new ColumnStyle(SizeType.AutoSize), new ColumnStyle(SizeType.Percent, 22) }) filters.ColumnStyles.Add(style);
        filters.Controls.Add(Ui.Text("검색"), 0, 0); filters.Controls.Add(search, 1, 0);
        filters.Controls.Add(Ui.Text("발주처"), 2, 0); filters.Controls.Add(supplier, 3, 0);
        filters.Controls.Add(Ui.Text("정렬"), 4, 0); filters.Controls.Add(sort, 5, 0);
        var chips = Ui.Row();
        foreach (string name in SampleData.Categories)
        {
            var b = Ui.Button(name, () => { category = name; UpdateCategory(); RenderProducts(); });
            categoryButtons.Add(b); chips.Controls.Add(b);
        }
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        var left = new Panel { Dock = DockStyle.Fill };
        count.Dock = DockStyle.Top;
        left.Controls.Add(products); left.Controls.Add(count);
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 0, 0) };
        var orderAll = Ui.Button("전체 주문하기", () => OpenOrders(data.Cart.ToArray()), true, "OrderAll");
        orderAll.Dock = DockStyle.Bottom; orderAll.MinimumSize = new Size(0, 52);
        var heading = Ui.Text("통합 장바구니", true);
        right.Controls.Add(cart); right.Controls.Add(heading); right.Controls.Add(orderAll);
        split.Controls.Add(left, 0, 0); split.Controls.Add(right, 1, 0);
        root.Controls.Add(filters, 0, 0); root.Controls.Add(chips, 0, 1); root.Controls.Add(split, 0, 2);
        Controls.Add(root);
        search.TextChanged += (_, _) => RenderProducts(); supplier.SelectedIndexChanged += (_, _) => RenderProducts();
        sort.SelectedIndexChanged += (_, _) => RenderProducts(); data.CartChanged += RenderCart;
        UpdateCategory(); RenderProducts(); RenderCart();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) data.CartChanged -= RenderCart;
        base.Dispose(disposing);
    }
    private void UpdateCategory()
    {
        foreach (var b in categoryButtons)
        { b.BackColor = b.Text == category ? Ui.Accent : Color.White; b.ForeColor = b.Text == category ? Color.White : Ui.Ink; }
    }
    private void RenderProducts()
    {
        products.SuspendLayout(); Ui.Clear(products);
        IEnumerable<Product> list = data.Products.Where(p => p.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && (category == "전체" || p.Category == category)
            && (supplier.SelectedIndex == 0 || p.Supplier.Name == supplier.Text));
        list = sort.SelectedIndex switch
        {
            1 => list.OrderBy(p => p.Name), 2 => list.OrderBy(p => p.Price),
            3 => list.OrderByDescending(p => p.SampleOrderCount),
            _ => list.OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category)).ThenBy(p => p.Name)
        };
        var visible = list.ToArray(); count.Text = $"상품 {visible.Length}개 · 샘플";
        foreach (var p in visible)
        {
            var add = Ui.Button("＋ 담기", () => data.AddToCart(p), true, $"Add_{p.Id}"); add.Enabled = p.Available;
            var info = Ui.Column(Ui.Text(p.Name, true), Ui.Text(p.PriceText),
                Ui.Text($"{p.Supplier.Name} · {(p.Available ? "주문가능" : "품절")} · {(p.Supplier.Manual ? "사이트에서 직접 주문" : "AUTO")}"), add);
            var card = new TableLayoutPanel { Name = $"Product_{p.Id}", AutoSize = true, ColumnCount = 2, BackColor = Color.White, Margin = new Padding(0, 0, 0, 10) };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22)); card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
            card.Controls.Add(Ui.Placeholder(p.Category), 0, 0); card.Controls.Add(info, 1, 0);
            products.Controls.Add(card);
        }
        if (visible.Length == 0) products.Controls.Add(Ui.Text("조건에 맞는 샘플 상품이 없습니다."));
        products.ResumeLayout(true); Ui.Fit(products);
    }
    private void RenderCart()
    {
        cart.SuspendLayout(); var scroll = cart.AutoScrollPosition; Ui.Clear(cart);
        foreach (var group in data.Cart.GroupBy(x => x.Product.Supplier))
        {
            var s = group.Key; var lines = group.ToArray();
            var card = Ui.Column(Ui.Text($"{s.Name} · {(s.Manual ? "MANUAL" : "AUTO")}", true));
            card.Name = $"Cart_{s.Id}";
            foreach (var line in lines)
            {
                AddRow(card, Ui.Text(line.Product.Name));
                if (s.Manual) AddRow(card, Ui.Button("사이트에서 주문하기", () => Ui.Notice(this, "실제 사이트는 열지 않습니다.\n수량과 옵션은 실제 사이트에서 직접 선택하는 흐름입니다.")));
                else AddRow(card, Ui.Row(Ui.Button("−", () => data.ChangeQuantity(line, -1), name: $"Minus_{line.Product.Id}"),
                    Ui.Text($"{line.Quantity}"), Ui.Button("＋", () => data.ChangeQuantity(line, 1), name: $"Plus_{line.Product.Id}")));
            }
            if (!s.Manual)
            {
                decimal subtotal = lines.Sum(x => x.Product.Price * x.Quantity), threshold = data.Shipping[s.Id];
                AddRow(card, Ui.Text($"소계 {subtotal:N0}원", true));
                AddRow(card, Ui.Text(threshold == 0 ? "CafeOrder 설정 · 기본 무료배송" : subtotal >= threshold
                    ? "CafeOrder 설정 기준 무료배송 충족" : $"설정 기준 무료배송까지 {threshold - subtotal:N0}원"));
                AddRow(card, Ui.Button("주문하기", () => OpenOrders(lines), true));
            }
            cart.Controls.Add(card);
        }
        if (data.Cart.Count == 0) cart.Controls.Add(Ui.Text("장바구니가 비었습니다.\n왼쪽 상품의 [담기]를 눌러주세요."));
        cart.ResumeLayout(true); Ui.Fit(cart); cart.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }
    internal static void AddRow(TableLayoutPanel card, Control control)
    { card.RowStyles.Add(new RowStyle(SizeType.AutoSize)); card.Controls.Add(control, 0, card.RowCount++); }
    private void OpenOrders(CartLine[] lines)
    {
        if (lines.Length == 0) { Ui.Notice(this, "장바구니에 상품을 담아주세요."); return; }
        using var dialog = new OrderForm(data, lines); dialog.ShowDialog(FindForm());
    }
}
