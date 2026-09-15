using CafeOrder;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class Program
{
    private static string output = "";
    private static readonly List<string> results = [];
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/ui-checks" : args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var clock = Stopwatch.StartNew(); using var main = new MainForm(); clock.Stop(); results.Add($"Main construction: {clock.Elapsed.TotalMilliseconds:F2}ms");
        Exception? failure = null;
        main.Shown += async (_, _) =>
        {
            try { MeasureScreens(main); await Check(main); }
            catch (Exception ex) { failure = ex; }
            finally { main.Close(); }
        };
        Application.Run(main);
        string result = failure?.ToString() ?? "PASS: revision 3 UI, resize, editing locks, wheel/copy, toast input and performance checks.";
        File.WriteAllText(Path.Combine(output, "result.txt"), result); File.WriteAllLines(Path.Combine(output, "performance.txt"), results);
        Console.WriteLine(result); return failure == null ? 0 : 1;
    }
    private static IEnumerable<Control> All(Control root)
    { foreach (Control c in root.Controls) { yield return c; foreach (var child in All(c)) yield return child; } }
    private static T Find<T>(Control root, string name) where T : Control => All(root).OfType<T>().Single(c => c.Name == name);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pump() { Application.DoEvents(); }
    private static void Capture(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }
    private static void MeasureScreens(MainForm main)
    {
        main.Size = new Size(1280, 720); var tabs = Find<TabControl>(main, "MainTabs"); var controls = All(main).ToArray();
        for (int i = 0; i < tabs.TabCount; i++)
        {
            var timer = Stopwatch.StartNew(); tabs.SelectedIndex = i; main.Update(); timer.Stop(); results.Add($"{tabs.TabPages[i].Text} first show: {timer.Elapsed.TotalMilliseconds:F2}ms");
            Capture(main, $"01-tab-{i}");
        }
        for (int i = 1; i < tabs.TabCount; i++)
        {
            var values = new List<double>();
            for (int pass = 0; pass < 3; pass++)
            {
                tabs.SelectedIndex = 0; main.Update(); var timer = Stopwatch.StartNew(); tabs.SelectedIndex = i; main.Update(); tabs.SelectedIndex = 0; main.Update(); timer.Stop(); values.Add(timer.Elapsed.TotalMilliseconds);
            }
            results.Add($"상품 ↔ {tabs.TabPages[i].Text} round trip: {string.Join(", ", values.Select(x => x.ToString("F2")))}ms");
        }
        Require(controls.SequenceEqual(All(main)), "Tab transitions must preserve every control");
    }
    private static async Task Check(MainForm main)
    {
        Require(Environment.Is64BitProcess, "x64 process");
        var data = (SampleData)typeof(MainForm).GetField("sample", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        var tabs = Find<TabControl>(main, "MainTabs"); var products = Find<Panel>(main, "ProductList");
        Require(!All(main).Any(c => c is DataGridView || c.Name == "Sort" || c.Name.StartsWith("Payment_")), "Removed sorting, grids, payment preferences");
        Require(!All(main).Any(c => c.Text.Contains("사이트") || c.Text.Contains("발주처")), "User terminology uses 판매처");
        var cached = products.Controls.OfType<ProductCard>().ToArray(); Require(cached.Length == 24, "Cached sample cards");
        Require(cached.Count(c => c.Top == cached.Min(x => x.Top)) == 3, "Three catalog columns");
        var image = Find<PictureBox>(products, "Image_1"); Require(image.Width == image.Height && image.SizeMode == PictureBoxSizeMode.Zoom, "Square zoom image");
        Require(Find<Control>(products, "Seller_1") is PictureBox, "Seller icon instead of text");
        Require(Find<TextBox>(products, "Price_1").TextAlign == HorizontalAlignment.Right, "Right aligned price");
        var search = Find<TextBox>(main, "Search"); var category = Find<ComboBox>(main, "CategoryFilter"); var supplier = Find<ComboBox>(main, "SupplierFilter");
        Require(search.Top > category.Top && search.Width > category.Width, "Wide search in second row");
        var headers = All(main).OfType<Label>().Where(c => c.Text is "상품 24개" or "장바구니").ToArray();
        Require(headers.Length == 2 && headers[0].Height == headers[1].Height && headers[0].Font.Equals(headers[1].Font), "Matching region headers");
        var ordered = cached.OrderBy(c => c.Top).ThenBy(c => c.Left).Select(c => c.Product.Id);
        var expected = data.Products.OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category)).ThenBy(p => p.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false)).Select(p => p.Id);
        Require(ordered.SequenceEqual(expected), "Fixed Korean automatic ordering");
        search.Text = "포모나"; Require(cached.Count(c => c.Visible) == 1, "Search filter"); Capture(main, "02-long-name"); search.Clear();
        category.SelectedItem = "유제품"; var sold = Find<Button>(products, "Add_12"); Require(sold.Enabled && sold.Text == "품절", "AUTO sold out recheck action");
        sold.PerformClick(); Require(sold.Text == "확인 중...", "Recheck state without artificial delay"); Pump(); Require(sold.Text == "장바구니에 담기", "Sample cream restocked");
        category.SelectedItem = "과일"; var unavailable = Find<Button>(products, "Add_17"); unavailable.PerformClick(); Pump(); Require(unavailable.Text == "품절", "Sample mango still unavailable"); category.SelectedIndex = 0;
        supplier.SelectedItem = "메가커피"; Require(cached.Where(c => c.Visible).All(c => c.Product.Supplier.Id == "mega"), "Seller filter"); supplier.SelectedIndex = 0;
        var name = Find<TextBox>(products, "Name_1"); name.SelectAll(); Require(name.ReadOnly && name.SelectedText == data.Products[0].Name, "Full selectable name");
        var productScroll = (ScrollableControl)products; productScroll.AutoScrollPosition = Point.Empty;
        var screenPoint = name.PointToScreen(new Point(5, 5)); Native.SendMessage(name.Handle, 0x020A, new IntPtr(-120 << 16), Pack(screenPoint));
        Require(productScroll.AutoScrollPosition.Y < 0, "Wheel over selectable product text scrolls catalog"); productScroll.AutoScrollPosition = Point.Empty;
        var cart = Find<FlowLayoutPanel>(main, "CartList");
        Find<Button>(products, "Add_21").PerformClick(); Require(cart.Controls[0].Name == "Cart_piece", "Added seller moves first");
        var refs = All(cart).ToArray(); var sellerOrder = cart.Controls.Cast<Control>().ToArray();
        cart.AutoScrollPosition = new Point(0, 80); var scroll = cart.AutoScrollPosition;
        var watch = Stopwatch.StartNew(); Find<Button>(cart, "Plus_21").PerformClick(); Find<Button>(cart, "Minus_21").PerformClick(); watch.Stop();
        results.Add($"Quantity + / -: {watch.Elapsed.TotalMilliseconds:F2}ms");
        Require(refs.SequenceEqual(All(cart)) && sellerOrder.SequenceEqual(cart.Controls.Cast<Control>()) && scroll == cart.AutoScrollPosition, "Quantity retains tree, order and scroll");
        Find<Button>(products, "Add_1").PerformClick(); Require(cart.Controls[0].Name == "Cart_mega", "Repeat add also moves seller first");
        Find<Button>(cart, "Minus_1").PerformClick(); // Restore quantity two.
        Find<Button>(cart, "Minus_1").PerformClick(); Require(!Find<Button>(cart, "SupplierOrder_mega").Enabled, "Shortage blocks order"); Find<Button>(cart, "Plus_1").PerformClick();
        Find<Button>(cart, "Remove_21").PerformClick(); Require(data.Cart.All(x => x.Product.Id != 21), "X removes without confirmation");
        var sellerIcon = Find<PictureBox>(products, "Seller_1").Image;
        Require(All(Find<Control>(cart, "Cart_mega")).OfType<PictureBox>().Any(p => ReferenceEquals(p.Image, sellerIcon)), "Shared seller icon cache");
        cart.AutoScrollPosition = Point.Empty; Capture(main, "03-cart");
        // All seven settings cards survive resize, maximize/restore and retain entered values.
        tabs.SelectedIndex = 2; var settings = Find<FlowLayoutPanel>(main, "Suppliers"); var settingsRefs = All(settings).ToArray();
        Find<TextBox>(settings, "LoginId_mega").Text = "ui-test";
        Require(Find<TextBox>(settings, "LoginPassword_mega").UseSystemPasswordChar, "Password masked");
        Require(!All(settings).OfType<CheckBox>().Any(), "No unverified linked-login sellers shown");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            foreach (var size in new[] { new Size(1000, 600), new Size(1500, 900), new Size(1280, 720) })
            {
                var timer = Stopwatch.StartNew(); main.Size = size; main.Update(); timer.Stop();
                Require(settingsRefs.SequenceEqual(All(settings)) && settings.Controls.Count == 7, "Settings resize retains controls");
                Require(settings.DisplayRectangle.Width <= settings.ClientSize.Width, "No stale horizontal scroll range");
                Require(settings.Controls.Cast<Control>().Any(c => settings.ClientRectangle.IntersectsWith(c.Bounds)), "Settings visible immediately after resize");
                Require(settings.Controls[0].Height < 220, "No inflated auto-height"); results.Add($"Settings resize {size.Width}: {timer.Elapsed.TotalMilliseconds:F2}ms");
            }
            main.WindowState = FormWindowState.Maximized; main.Update(); main.WindowState = FormWindowState.Normal; main.Size = new Size(1280,720); main.Update();
        }
        Require(Find<TextBox>(settings, "LoginId_mega").Text == "ui-test", "Resize preserves field contents"); Find<TextBox>(settings, "LoginId_mega").Clear();
        Capture(main, "04-settings-resize");
        tabs.SelectedIndex = 1; Require(Find<FlowLayoutPanel>(main, "HistoryCards").Controls.Count == 3, "One card per order");
        Require(All(tabs.TabPages[1]).Any(c => c.Text.Contains("2026-09-15 14:20")) && All(tabs.TabPages[1]).Any(c => c.Text == "총 결제 53,000원"), "Full date and payment total");
        tabs.SelectedIndex = 3; Find<TextBox>(main, "ProductUrl").Text = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613";
        Find<Button>(main, "AddProductButton").PerformClick(); Require(Find<Panel>(main, "ProductPreview").Controls.OfType<ProductCard>().Count() == 1, "Shared result card"); Capture(main, "05-add-product");
        var clipboard = Clipboard.GetDataObject();
        try
        {
            tabs.SelectedIndex = 4; var log = Find<RichTextBox>(main, "LogText"); Require(log.ReadOnly && log.ShortcutsEnabled, "Readonly diagnostic log");
            Find<Button>(main, "CopyAllLogs").PerformClick(); Require(Clipboard.GetText() == log.Text, "Copy all diagnostic log");
            tabs.SelectedIndex = 0; name.SelectAll(); name.Copy(); Require(Clipboard.GetText() == name.Text, "Copy selected product name");
        }
        finally { if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
        await CheckToast(main, data);
        // Shared cart references and immutable in-flight targets.
        using (var order = new OrderForm(data, data.Cart.ToArray()))
        {
            var timer = Stopwatch.StartNew(); order.Show(main); order.Update(); timer.Stop(); results.Add($"Order window first show: {timer.Elapsed.TotalMilliseconds:F2}ms");
            int before = data.Cart.Single(x => x.Product.Id == 1).Quantity;
            Find<Button>(order, "Plus_1").PerformClick(); Require(data.Cart.Single(x => x.Product.Id == 1).Quantity == before + 1, "Order edits shared cart immediately");
            Find<Button>(order, "Minus_1").PerformClick();
            Find<Button>(order, "Remove_9").PerformClick(); Require(data.Cart.All(x => x.Product.Id != 9), "Order X edits original cart");
            Require(!All(order).Any(c => c.Name == "Plus_6"), "Manual has no quantity controls");
            Require(Find<Button>(order, "CloseOrders").Size == Find<Button>(order, "StartAllOrders").Size, "Equal compact footer buttons");
            Capture(order, "06-order-editable");
            Find<Button>(order, "StartAllOrders").PerformClick();
            Require(All(order).OfType<Button>().Where(b => b.Name.StartsWith("Plus_") || b.Name.StartsWith("Minus_") || b.Name.StartsWith("Remove_")).All(b => !b.Enabled), "Batch locks all edits immediately");
            var lockedLine = data.Cart.Single(x => x.Product.Id == 1); int lockedQty = lockedLine.Quantity;
            data.ChangeQuantity(lockedLine, 1); data.Remove(lockedLine); Require(data.Cart.Contains(lockedLine) && lockedLine.Quantity == lockedQty, "Model enforces lock");
            Capture(order, "07-order-locked"); Pump(); Require(data.Cart.All(x => x.Product.Supplier.Id != "mega"), "Successful AUTO removed");
            Find<Button>(order, "OrderAction_6").PerformClick(); Pump(); Require(data.Cart.All(x => x.Product.Id != 6), "Confirmed manual URL removed");
            Find<Button>(order, "OrderAction_10").PerformClick(); Pump(); Require(data.Cart.Any(x => x.Product.Id == 10) && Find<Label>(order, "OrderStatus_10").Text.StartsWith("확인필요"), "Unknown manual order retained");
            Capture(order, "08-order-unknown"); order.Close();
        }
        Require(data.Cart.All(x => !data.IsLocked(x.Product)), "Closing mock flow releases edit locks");
        data.Toast("장바구니에서 삭제했습니다"); Require(Application.OpenForms.Cast<Form>().Count(f => f.Name == "ToastHost") == 1, "Toast survives dialog close as one shared host");
        // Repeat order open/close and all-tab resize checks; no artificial UI delay.
        for (int i = 0; i < 3; i++) { using var f = new OrderForm(data, data.Cart.ToArray()); var timer = Stopwatch.StartNew(); f.Show(main); f.Update(); f.Size = new Size(700, 520); f.Close(); timer.Stop(); results.Add($"Order reopen/resize/close: {timer.Elapsed.TotalMilliseconds:F2}ms"); }
        for (int i = 0; i < tabs.TabCount; i++) { tabs.SelectedIndex = i; main.Size = new Size(1100, 650); main.Update(); main.Size = new Size(1280, 720); main.Update(); Require(tabs.TabPages[i].Controls.Count == 1, "Tab resize retained view"); }
        tabs.SelectedIndex = 0;
    }
    private static async Task CheckToast(MainForm main, SampleData data)
    {
        using var probe = new Button { Name = "InputProbe", Text = "입력 검사", Bounds = new Rectangle(main.ClientSize.Width - 180, 12, 130, 32) };
        main.Controls.Add(probe); probe.BringToFront(); probe.Focus(); IntPtr focus = Native.GetFocus(); int clicked = 0; probe.Click += (_, _) => clicked++;
        data.Toast("장바구니에 추가했습니다"); data.Toast("상품 정보를 다시 확인했습니다"); data.Toast("로그 전체를 복사했습니다"); data.Toast("장바구니에서 삭제했습니다");
        var toast = Application.OpenForms.Cast<Form>().Single(f => f.Name == "ToastHost");
        Require(Native.GetFocus() == focus, "Toast never steals focus");
        long style = Native.GetWindowLongPtr(toast.Handle, -20).ToInt64(); Require((style & 0x08000020) == 0x08000020, "No activate and transparent styles");
        Require(Native.SendMessage(toast.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero).ToInt64() == -1, "Toast hit-test transparent");
        Require(Native.SendMessage(toast.Handle, 0x0021, IntPtr.Zero, IntPtr.Zero).ToInt64() == 3, "Toast does not activate");
        var point = probe.PointToScreen(new Point(60, 15)); var hit = Native.WindowFromPoint(point);
        Require(hit == probe.Handle, "Pointer passes through toast to underlying button");
        Native.SendMessage(hit, 0x0201, new IntPtr(1), new IntPtr((15 << 16) | 60)); Native.SendMessage(hit, 0x0202, IntPtr.Zero, new IntPtr((15 << 16) | 60));
        Require(clicked == 1, "Button behind toast receives click"); Capture(toast, "09-toast-stack");
        var catalog = Find<Panel>(main, "ProductList"); var catalogScroll = (ScrollableControl)catalog;
        catalogScroll.ScrollControlIntoView(Find<ProductCard>(catalog, "Product_1"));
        var text = Find<TextBox>(catalog, "Name_1"); var textPoint = text.PointToScreen(new Point(8, 8));
        var originalLocation = toast.Location; toast.Location = text.PointToScreen(Point.Empty);
        Require(Native.WindowFromPoint(textPoint) == text.Handle, "Toast passes pointer hit to underlying selectable text");
        int scrollY = catalogScroll.AutoScrollPosition.Y;
        Native.SendMessage(text.Handle, 0x020A, new IntPtr(-120 << 16), Pack(textPoint));
        Require(catalogScroll.AutoScrollPosition.Y != scrollY, "Wheel behind toast reaches catalog");
        toast.Location = originalLocation; catalogScroll.AutoScrollPosition = Point.Empty;
        Require(toast.Height == 116, "At most three toasts");
        var hidden = new TaskCompletionSource(); toast.VisibleChanged += OnVisible;
        void OnVisible(object? sender, EventArgs e) { if (!toast.Visible) hidden.TrySetResult(); }
        var clock = Stopwatch.StartNew(); await hidden.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Stop(); toast.VisibleChanged -= OnVisible;
        Require(clock.Elapsed.TotalMilliseconds is > 2200 and < 4000, "Toast expires near 2.8 seconds"); results.Add($"Toast expiry: {clock.Elapsed.TotalMilliseconds:F0}ms; input transparent; one timer, no animation loop");
    }
    private static IntPtr Pack(Point p) => new((p.Y << 16) | (p.X & 0xffff));
    private static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    }
}
