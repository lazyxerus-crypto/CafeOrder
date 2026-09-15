using CafeOrder;
using System.Drawing.Imaging;
using System.Diagnostics;

internal static class Program
{
    private static string output = "";
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/ui-checks" : args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using var main = new MainForm(); Exception? failure = null;
        main.Shown += async (_, _) =>
        {
            try { if (args.Contains("--perf")) await Benchmark(main); else { await Check(main); await Benchmark(main); } }
            catch (Exception ex) { failure = ex; }
            finally { main.Close(); }
        };
        Application.Run(main);
        string result = failure?.ToString() ?? (args.Contains("--perf") ? "PASS: performance probe completed." : "PASS: UI revision 2 behavior and layout checks.");
        File.WriteAllText(Path.Combine(output, "result.txt"), result); Console.WriteLine(result); return failure == null ? 0 : 1;
    }
    private static IEnumerable<Control> All(Control root)
    { foreach (Control c in root.Controls) { yield return c; foreach (var child in All(c)) yield return child; } }
    private static T Find<T>(Control root, string name) where T : Control => All(root).OfType<T>().Single(c => c.Name == name);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }
    private static async Task Benchmark(MainForm main)
    {
        main.Size = new Size(1280, 720); var tabs = Find<TabControl>(main, "MainTabs");
        int layouts = 0, additions = 0, removals = 0;
        foreach (var c in All(main)) { c.Layout += (_, _) => layouts++; c.ControlAdded += (_, _) => additions++; c.ControlRemoved += (_, _) => removals++; }
        var timings = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            tabs.SelectedIndex = 1; await Task.Delay(30); layouts = additions = removals = 0;
            var timer = Stopwatch.StartNew(); tabs.SelectedIndex = 0; main.Update(); timer.Stop(); timings.Add(timer.Elapsed.TotalMilliseconds);
            Require(additions == 0 && removals == 0, "Tab switch must retain controls");
        }
        int before = All(main).Count(); layouts = additions = removals = 0;
        var quantity = Stopwatch.StartNew(); Find<Button>(main, "Plus_1").PerformClick(); main.Update(); quantity.Stop();
        File.WriteAllText(Path.Combine(output, "performance.txt"), $"Tab switch ms: {string.Join(", ", timings.Select(x => x.ToString("F2")))}\nQuantity ms: {quantity.Elapsed.TotalMilliseconds:F2}\nQuantity layouts: {layouts}, added: {additions}, removed: {removals}; controls before {before}, after {All(main).Count()}");
        Require(additions == 0 && removals == 0, "Quantity-only update must retain controls");
    }
    private static async Task Check(MainForm main)
    {
        Require(Environment.Is64BitProcess, "Must run x64"); main.Size = new Size(1280, 720); await Task.Delay(100);
        var tabs = Find<TabControl>(main, "MainTabs"); Require(tabs.TabCount == 6 && tabs.Top == 0, "Six tabs without banner");
        var products = Find<Panel>(main, "ProductList"); var initialCards = products.Controls.OfType<ProductCard>().ToArray();
        Require(initialCards.Length == 24, "24 cached product cards"); Capture(main, "01-products-1280x720");
        Require(initialCards.Count(c => c.Top == initialCards.Min(x => x.Top)) == 3, "Three cards per row");
        foreach (var card in initialCards)
        {
            var picture = card.Controls.OfType<PictureBox>().Single(); Require(picture.Width == picture.Height && picture.SizeMode == PictureBoxSizeMode.Zoom, "Square images with Zoom");
            Require(card.Controls.OfType<Button>().Single().Text == "장바구니에 담기", "Product action text");
        }
        int[] filterY = new[] { "Search", "CategoryFilter", "SupplierFilter", "Sort" }.Select(n => main.PointToClient(Find<Control>(main, n).PointToScreen(Point.Empty)).Y).ToArray();
        Require(filterY.Max() - filterY.Min() < 12, "Four filters in one row");
        var search = Find<TextBox>(main, "Search"); search.Text = "포모나"; await Task.Delay(30);
        Require(initialCards.Count(c => c.Visible) == 1 && ReferenceEquals(initialCards.Single(c => c.Product.Id == 1), Find<ProductCard>(main, "Product_1")), "Filtering reuses cards");
        Capture(main, "02-long-name"); search.Clear();
        var category = Find<ComboBox>(main, "CategoryFilter"); category.SelectedItem = "일회용품";
        Require(initialCards.Where(c => c.Visible).All(c => c.Product.Category == "일회용품"), "Category filtering"); category.SelectedIndex = 0;
        var supplier = Find<ComboBox>(main, "SupplierFilter"); supplier.SelectedItem = "메가커피";
        Require(initialCards.Where(c => c.Visible).All(c => c.Product.Supplier.Id == "mega"), "Supplier filtering"); supplier.SelectedIndex = 0;
        Find<ComboBox>(main, "Sort").SelectedIndex = 2;
        var sorted = initialCards.Where(c => c.Visible).OrderBy(c => c.Top).ThenBy(c => c.Left).Select(c => c.Product.Price).ToArray();
        Require(sorted.SequenceEqual(sorted.Order()), "Price sorting"); Find<ComboBox>(main, "Sort").SelectedIndex = 0;
        Require(!Find<Button>(main, "Add_12").Enabled, "Sold out disabled");
        var cart = Find<FlowLayoutPanel>(main, "CartList");
        foreach (string id in new[] { "naver", "coupang" })
        {
            var card = Find<Control>(main, "Cart_" + id);
            Require(!All(card).OfType<Button>().Any(b => b.Name.StartsWith("Plus_") || b.Name.StartsWith("Minus_")), "Manual quantity hidden");
            Require(All(card).OfType<Button>().All(b => b.BackColor.G > b.BackColor.R), "Manual green actions");
        }
        var cartBefore = All(cart).ToArray(); var scrollBefore = cart.AutoScrollPosition;
        Find<Button>(main, "Minus_1").PerformClick();
        var supplierAction = Find<Button>(main, "SupplierOrder_mega");
        Require(supplierAction.Text == "11,500원 부족" && !supplierAction.Enabled && supplierAction.BackColor.R > supplierAction.BackColor.G, "Insufficient shipping threshold blocks order");
        Capture(main, "03-shipping-shortfall"); Find<Button>(main, "Plus_1").PerformClick();
        Require(supplierAction.Text == "주문하기" && supplierAction.Enabled, "Meeting threshold enables order");
        Require(cartBefore.SequenceEqual(All(cart)) && cart.AutoScrollPosition == scrollBefore, "Quantity preserves controls and scroll");
        cart.ScrollControlIntoView(Find<Control>(main, "Cart_naver"));
        var scrolled = cart.AutoScrollPosition;
        Find<Button>(main, "Plus_1").PerformClick(); Find<Button>(main, "Minus_1").PerformClick();
        Require(cart.AutoScrollPosition == scrolled && cartBefore.SequenceEqual(All(cart)), "Offscreen quantity change preserves nonzero scroll and controls");
        Capture(main, "04-manual-cart"); cart.AutoScrollPosition = Point.Empty;
        Find<Button>(main, "Add_3").PerformClick(); Require(Find<Button>(main, "SupplierOrder_food").Enabled, "FoodRain is free shipping from first product");
        for (int i = 1; i < tabs.TabCount; i++) { tabs.SelectedIndex = i; await Task.Delay(50); Capture(main, $"05-tab-{i}"); }
        var grid = Find<DataGridView>(main, "HistoryGrid");
        Require(grid.ColumnHeadersVisible && grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText).SequenceEqual(new[] { "주문일시", "판매처", "상품", "결제금액", "주문번호", "상태" }), "Visible history headers");
        Require(!All(main).Any(c => new[] { "AUTO", "MANUAL", "MANUAL_BROWSER", "Adapter version", "주문완료로 기록" }.Any(t => c.Text.Contains(t))), "No internal terms or manual completion button");
        tabs.SelectedIndex = 2;
        Require(Find<TextBox>(main, "LoginPassword_mega").UseSystemPasswordChar, "Password masked");
        Require(!All(main).Any(c => c.Name == "LoginId_naver"), "No direct login settings for manual suppliers");
        var linked = Find<CheckBox>(main, "NaverLogin_mega"); linked.Checked = true;
        Require(!Find<TextBox>(main, "LoginId_mega").Enabled, "Linked login disables direct fields"); linked.Checked = false;
        var shipping = Find<TextBox>(main, "Shipping_mega"); shipping.Text = "60abc000";
        Require(shipping.Text == "60000", "Digits only including pasted text"); shipping.Text = "50000";
        tabs.SelectedIndex = 3;
        var url = Find<TextBox>(main, "ProductUrl"); url.Text = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613";
        Require(Find<Label>(main, "DetectedSupplier").Text == "메가커피", "URL supplier detection");
        Find<Button>(main, "AddProductButton").PerformClick();
        Require(Find<Panel>(main, "ProductPreview").Controls.OfType<ProductCard>().Count() == 1, "Reuses product card for preview"); Capture(main, "06-add-result");
        url.Text = "https://www.megacoffee.co.kr.evil.invalid/product";
        Require(!Find<Button>(main, "AddProductButton").Enabled, "Reject lookalike domain");
        var data = new SampleData();
        foreach (var pair in new[] { ("https://www.piececake.co.kr/product/product_view?prodNo=PD2637", "piece"), ("https://foodrain.com/items/H000001104", "food"),
            ("https://nuldampartners.com/product/detail.html?product_no=250", "nuldam"), ("https://smartstore.naver.com/placer_mall/products/7419367541?nl-query=20", "naver"), ("https://www.coupang.com/vp/products/7281521260", "coupang") })
            Require(data.DetectSupplier(pair.Item1)?.Id == pair.Item2, "User supplied URL mapping");
        Require(data.DetectSupplier("https://evil.invalid/?next=https://coupang.com") == null && data.DetectSupplier("https://coupang.com@evil.invalid/") == null, "Reject URL spoofing");
        tabs.SelectedIndex = 4; var log = Find<RichTextBox>(main, "LogText");
        Require(log.ReadOnly && log.ShortcutsEnabled, "Selectable readonly log"); log.Select(0, 5); Require(log.SelectedText == "02:31", "Log selection");
        tabs.SelectedIndex = 0; var name = Find<TextBox>(products, "Name_1"); name.SelectAll();
        Require(name.ReadOnly && name.ShortcutsEnabled && name.SelectedText == data.Products[0].Name, "Selectable full product name");
        // Exercise standard copying while restoring the user's existing clipboard contents.
        var savedClipboard = Clipboard.GetDataObject();
        try
        {
            name.Copy(); Require(Clipboard.GetText() == name.Text, "Product text copies exactly");
            log.Select(0, 5); log.Copy(); Require(Clipboard.GetText() == "02:31", "Log selection copies exactly");
        }
        finally { if (savedClipboard != null) Clipboard.SetDataObject(savedClipboard, true); else Clipboard.Clear(); }
        using (var closer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            bool opened = false; closer.Tick += (_, _) => { var form = Application.OpenForms.OfType<OrderForm>().FirstOrDefault(); if (form != null) { opened = true; Capture(form, "07-confirm-orders"); form.Close(); } };
            closer.Start(); Find<Button>(main, "OrderAll").PerformClick(); closer.Stop(); Require(opened, "Cart opens confirmation window only");
        }
        using (var order = new OrderForm(data, data.Cart.ToArray()))
        {
            order.Show(main); await Task.Delay(50);
            var list = Find<FlowLayoutPanel>(order, "OrderCards"); Require(list.Controls.Count == 6, "Supplier and URL card grouping");
            Require(All(order).Any(c => c.Text == "14,500원 × 2") && All(order).Any(c => c.Text == "합계 53,000원"), "Order unit price and total");
            Require(All(order).Any(c => c.Text == "수량/옵션: 사이트에서 선택"), "Manual quantity explanation");
            Find<Button>(order, "StartAllOrders").PerformClick(); await Task.Delay(1400);
            Require(data.Cart.All(x => x.Product.Supplier.Id != "mega"), "Batch removes successful AUTO only");
            Require(All(order).Any(c => c.Text.StartsWith("주문실패")) && All(order).Any(c => c.Text.StartsWith("확인필요")), "Failed/unknown remain in Korean");
            Find<Button>(order, "OrderAction_6").PerformClick(); await Task.Delay(100);
            Require(Find<Label>(order, "OrderStatus_6").Text == "주문 확인 중...", "Automatic mock confirmation pending");
            await Task.Delay(1300); Require(data.Cart.All(x => x.Product.Id != 6) && data.Cart.Any(x => x.Product.Id == 10), "Confirmed URL only removed");
            Find<Button>(order, "OrderAction_10").PerformClick(); await Task.Delay(800);
            Require(data.Cart.Any(x => x.Product.Id == 10) && Find<Label>(order, "OrderStatus_10").Text.StartsWith("확인필요"), "Uncertain manual order retained");
            Capture(order, "08-uncertain-orders"); order.Close();
        }
        // Layout stress; no Windows display settings are changed.
        foreach (float scale in new[] { 1.25f, 1.5f })
        {
            using var scaled = new MainForm(); scaled.Show();
            scaled.AutoScaleMode = AutoScaleMode.None; foreach (var c in All(scaled).OfType<ContainerControl>()) c.AutoScaleMode = AutoScaleMode.None;
            var fonts = All(scaled).Prepend(scaled).Select(c => (Control: c, Size: c.Font.Size, Style: c.Font.Style)).ToArray();
            foreach (var entry in fonts) entry.Control.SuspendLayout(); scaled.Scale(new SizeF(scale, scale));
            foreach (var entry in fonts) entry.Control.Font = new Font("Malgun Gothic", entry.Size * scale, entry.Style);
            foreach (var entry in fonts.Reverse()) entry.Control.ResumeLayout(true);
            scaled.MinimumSize = new Size(1000, 600); scaled.Size = new Size(1280, 720); await Task.Delay(80);
            Capture(scaled, $"09-scale-{scale:0.00}"); var action = Find<Button>(scaled, "OrderAll");
            Require(scaled.ClientRectangle.Contains(scaled.PointToClient(action.PointToScreen(new Point(action.Width / 2, action.Height / 2)))), "OrderAll within enlarged window");
            scaled.Close();
        }
    }
}
