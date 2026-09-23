namespace CafeOrder;

public sealed class ProductsView : UserControl
{
    private readonly SampleData data;
    private readonly SupplierSessionManager? sessions;
    internal Func<string, Task<MegaProductLookupResult>>? MegaLookup;
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
    internal bool importing;
    private readonly List<GridViewButton> columnButtons = [];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IProductTransferDialogs TransferDialogs { get; set; } = new WinFormsProductTransferDialogs();

    public ProductsView(SampleData data) : this(data, null) { }

    internal ProductsView(SampleData data, SupplierSessionManager? sessions)
    {
        this.data = data; this.sessions = sessions;
        MegaLookup = sessions == null ? null : sessions.LookupMegaProductAsync; Dock = DockStyle.Fill; DoubleBuffered = true;
        supplier = Ui.Combo(new[] { "전체 판매처" }.Concat(data.Suppliers.Select(x => x.Name)), "SupplierFilter");
        var root = new TableLayoutPanel { Name = "ProductColumns", Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        var filters = new TableLayoutPanel { Name = "Filters", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 2, Padding = new Padding(6) };
        foreach (float percent in new[] { 50f, 50f })
        { filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent)); }
        Control[] filterControls = [Ui.Text("카테고리"), category, Ui.Text("판매처"), supplier];
        for (int i = 0; i < filterControls.Length; i++) filters.Controls.Add(filterControls[i], i, 0);
        for (int i = 0; i < 2; i++) filters.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var feedback = Ui.Text(""); feedback.Name = "TransferStatus"; feedback.Visible = false;
        var actions = Ui.Row(Ui.Button("새로고침", () => FilterProducts(), name: "RefreshProducts"), Ui.Button("상품 추가", AddDraft, name: "AddProduct"), Ui.Button("내보내기", ExportProducts, name: "ExportProducts"), Ui.Button("가져오기", ImportProducts, name: "ImportProducts"));
        var management = new TableLayoutPanel { Name = "Management", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3, Padding = new Padding(6) };
        management.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 3; row++) management.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var buttonRow = new TableLayoutPanel { Name = "ManagementButtons", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = Padding.Empty };
        foreach (Control button in actions.Controls.Cast<Control>().ToArray())
        {
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            button.AutoSize = false; button.Dock = DockStyle.Fill; button.MinimumSize = Size.Empty; button.Padding = new Padding(2);
            button.Height = Ui.ActionHeight(button); buttonRow.Controls.Add(button);
        }
        actions.Dispose(); management.Controls.Add(buttonRow, 0, 0);
        var views = Ui.Row();
        foreach (int columns in new[] { 3, 4, 5 })
        {
            var button = new GridViewButton(columns); columnButtons.Add(button);
            button.Click += (_, _) =>
            {
                int previous = data.Store.Preferences.Columns;
                data.Store.Preferences.Columns = columns;
                try { data.Store.SavePreferences(); ApplyColumns(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { data.Store.Preferences.Columns = previous; feedback.Text = "설정 저장 실패 · 다시 시도해주세요"; feedback.Visible = true; }
            };
            views.Controls.Add(button);
        }
        views.WrapContents = false; management.Controls.Add(views, 0, 1);
        filters.Controls.Add(Ui.Text("검색"), 0, 1); filters.Controls.Add(Ui.Role(search, TypographyKey.General), 1, 1); filters.SetColumnSpan(search, 3);
        management.Controls.Add(feedback, 0, 2);
        bool fitting = false;
        void FitRows()
        {
            if (fitting) return; fitting = true;
            try
            {
                int first = Math.Max(buttonRow.GetPreferredSize(new Size(management.Width - 12, 0)).Height, category.PreferredSize.Height + category.Margin.Vertical);
                first = Math.Max(first, supplier.PreferredSize.Height + supplier.Margin.Vertical);
                int second = Math.Max(views.GetPreferredSize(Size.Empty).Height, search.PreferredSize.Height + search.Margin.Vertical);
                filters.RowStyles[0] = new RowStyle(SizeType.Absolute, first); filters.RowStyles[1] = new RowStyle(SizeType.Absolute, second);
                management.RowStyles[0] = new RowStyle(SizeType.Absolute, first); management.RowStyles[1] = new RowStyle(SizeType.Absolute, second);
                filters.PerformLayout(); management.PerformLayout();
            }
            finally { fitting = false; }
        }
        foreach (Control button in buttonRow.Controls) button.FontChanged += (_, _) => FitRows();
        category.FontChanged += (_, _) => FitRows(); supplier.FontChanged += (_, _) => FitRows(); search.FontChanged += (_, _) => FitRows();
        management.SizeChanged += (_, _) => FitRows(); FitRows();
        var left = new SoftPanel { Name = "ProductRegion", Dock = DockStyle.Fill, BackColor = Color.FromArgb(237, 243, 239) };
        products.BackColor = left.BackColor;
        left.Controls.Add(products); left.Controls.Add(count);
        var right = new SoftPanel { Name = "CartRegion", Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 241, 235) };
        cart.BackColor = right.BackColor;
        orderAll = Ui.Button("전체 주문하기", () => OpenOrders(data.Cart.ToArray()), true, "OrderAll");
        orderAll.Dock = DockStyle.Bottom; orderAll.AutoSize = false; orderAll.Height = Math.Max(50, Ui.ActionHeight(orderAll, 350));
        right.SizeChanged += (_, _) => orderAll.Height = Math.Max(50, Ui.ActionHeight(orderAll, right.ClientSize.Width - right.Padding.Horizontal));
        right.Controls.Add(cart); right.Controls.Add(Ui.SectionHeading("장바구니")); right.Controls.Add(orderAll);
        root.Controls.Add(filters, 0, 0); root.Controls.Add(management, 1, 0);
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
        data.FirstMegaCartAdded += FirstMegaCartAdded;
        data.CatalogChanged += CatalogChanged; data.ProductChanged += ProductChanged;
        emphasis.Tick += (_, _) => { emphasis.Stop(); if (highlighted is { IsDisposed: false }) highlighted.Highlight(false); highlighted = null; };
        ApplyColumns(); FilterProducts(); SyncCart();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { data.CartChanged -= SyncCart; data.ProductAdded -= BringSellerFirst; data.FirstMegaCartAdded -= FirstMegaCartAdded; data.CatalogChanged -= CatalogChanged; data.ProductChanged -= ProductChanged; emphasis.Dispose(); emptyCart.Dispose(); }
        base.Dispose(disposing);
    }
    public void ApplyColumns() { products.Columns = data.Store.Preferences.Columns; foreach (var button in columnButtons) button.Selected = button.Columns == products.Columns; products.PerformLayout(); }
    private void AddDraft()
    {
        var draft = new ProductCard(null, data, category.SelectedIndex > 0 ? category.Text : "기타", MegaLookup);
        draft.Registered += () => { drafts.Remove(draft); productCards[draft.Product!.Id] = draft; UpdateCount(); };
        draft.DeleteDraft += () => { drafts.Remove(draft); products.RemoveItem(draft); draft.Dispose(); };
        drafts.Insert(0, draft); products.Controls.Add(draft); products.Prepend(draft); products.AutoScrollPosition = Point.Empty;
        draft.Controls.OfType<TextBox>().Single().Focus();
    }
    private void CatalogChanged()
    {
        foreach (var product in data.Products.Where(p => p.IsActive && !productCards.ContainsKey(p.Id)))
        { var card = new ProductCard(product, data); productCards.Add(product.Id, card); products.Controls.Add(card); }
        FilterProducts();
    }
    private void ExportProducts()
    {
        var dialogs = TransferDialogs; IWin32Window owner = (IWin32Window?)FindForm() ?? this;
        string? path = dialogs.ChooseExport(owner); if (path == null) return;
        try { int count = data.ExportWorkbook(path); dialogs.Show(owner, $"상품 {count}개를 내보냈습니다.", false); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { dialogs.Show(owner, "내보내기 실패: " + ex.Message, true); }
    }
    private async void ImportProducts()
    {
        if (importing) return;
        var dialogs = TransferDialogs; IWin32Window owner = (IWin32Window?)FindForm() ?? this;
        string? path = dialogs.ChooseImport(owner); if (path == null) return;
        importing = true;
        using var cancellation = new CancellationTokenSource();
        using var progress = new ProductImportProgressForm(cancellation.Cancel);
        progress.Show(FindForm());
        try
        {
            var lookup = MegaLookup ?? (_ => Task.FromResult(new MegaProductLookupResult(MegaProductLookupStatus.Failed)));
            using var plan = await data.PrepareImportAsync(path, lookup,
                new Progress<string>(progress.SetStatus), cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            if (plan.Issues.Count != 0)
            {
                progress.Finish();
                dialogs.ShowIssues(owner, plan);
                return;
            }
            if (plan.Changes.Count == 0)
            {
                progress.Finish();
                data.Store.Log.Write(LogLevel.INFO, "XLSX_IMPORT_NO_CHANGES", "가져올 변경이 없어 DB를 수정하지 않았습니다.",
                    result: "SKIPPED");
                dialogs.Show(owner, $"가져오기 완료 · 추가 0개, 수정 0개, 건너뜀 {plan.Skipped}개, 실패 0개 · DB 변경 없음{plan.DuplicateDetails}", false);
                return;
            }
            progress.Hide();
            if (!dialogs.Confirm(owner, plan))
            {
                data.Store.Log.Write(LogLevel.INFO, "XLSX_IMPORT_CANCELLED", "사용자가 XLSX 적용을 취소했습니다.", result: "CANCELLED");
                return;
            }
            progress.SetApplying(); progress.Show(FindForm());
            await data.ApplyImportAsync(plan);
            if (IsDisposed) return;
            dialogs.Show(owner, $"가져오기 완료 · 추가 {plan.Added}개, 수정 {plan.Updated}개, 건너뜀 {plan.Skipped}개, 실패 0개, 비활성 변경 {plan.Deactivated}개{plan.DuplicateDetails}", false);
        }
        catch (OperationCanceledException)
        {
            data.Store.Log.Write(LogLevel.INFO, "XLSX_IMPORT_CANCELLED", "상품조회 또는 파일 검증 중 가져오기를 취소했습니다.", result: "CANCELLED");
            if (!IsDisposed) dialogs.Show(owner, "가져오기 취소 · DB 변경 없음", false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            data.Store.Log.Write(LogLevel.ERROR, "XLSX_IMPORT_FAILED", "XLSX 가져오기 중 파일·조회·DB 처리에 실패했습니다.", result: "FAILED", error: ex);
            dialogs.Show(owner, "가져오기 실패 · DB 반영을 완료하지 않았습니다.\n" + ex.Message, true);
        }
        finally { progress.Finish(); importing = false; }
    }
    private void ProductChanged(Product _) => UpdateCount();
    private void UpdateCount() => count.Text = $"상품 {products.Items.Count(c => c.Product is { IsActive: true })}개";
    private void FilterProducts(bool resetScroll = true)
    {
        IEnumerable<Product> list = productCards.Values.Select(c => c.Product!).Where(p => p.IsActive && p.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)
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
    private void FirstMegaCartAdded(Product product, CartLine line)
    {
        if (MegaLookup is { } lookup) _ = data.RefreshFirstMegaCartAsync(product, line, lookup);
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
        orderAll.Enabled = data.Cart.Count != 0 && data.Cart.All(data.CanStartLocalOrder);
    }
    internal static void AddRow(TableLayoutPanel card, Control control)
    { card.RowStyles.Add(new RowStyle(SizeType.AutoSize)); card.Controls.Add(control, 0, card.RowCount++); }
    private void OpenOrders(CartLine[] lines)
    {
        if (lines.Length == 0 || lines.Any(line => !data.CanStartLocalOrder(line))) return;
        using var dialog = new OrderForm(data, lines) { Sessions = sessions }; dialog.ShowDialog(FindForm());
    }
}
