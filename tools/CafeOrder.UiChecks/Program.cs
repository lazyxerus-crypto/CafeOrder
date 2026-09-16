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
        string result = failure?.ToString() ?? "PASS: revision 4 columns, toast stack/deduplication/input, supplied icons/fallback, cart order/scroll/row highlight, ASCII credentials/Caps Lock, compact order cards; x64 and existing UI regressions.";
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
        await CheckCartAndColumns(main, data);
        CheckIcons(main, data);
        // All seven settings cards survive resize, maximize/restore and retain entered values.
        tabs.SelectedIndex = 2; var settings = Find<FlowLayoutPanel>(main, "Suppliers"); var settingsRefs = All(settings).ToArray();
        Find<TextBox>(settings, "LoginId_mega").Text = "ui-test";
        Require(Find<TextBox>(settings, "LoginPassword_mega").UseSystemPasswordChar, "Password masked");
        Require(!All(settings).OfType<CheckBox>().Any(), "No unverified linked-login sellers shown");
        CheckCredentials(main, data);
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
            CheckOrderPadding(order);
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
        var region = Find<Panel>(main, "ToastRegion");
        using var probe = new Button { Name = "InputProbe", Text = "입력 검사", Bounds = new Rectangle(20, 10, 130, 32) };
        region.Controls.Add(probe); probe.BringToFront(); probe.Focus(); IntPtr focus = Native.GetFocus(); int clicked = 0; probe.Click += (_, _) => clicked++;
        data.Toast("장바구니에 추가했습니다"); data.Toast("상품 정보를 다시 확인했습니다"); data.Toast("로그 전체를 복사했습니다"); data.Toast("장바구니에서 삭제했습니다");
        var toast = Application.OpenForms.Cast<Form>().Single(f => f.Name == "ToastHost");
        Require(Native.GetFocus() == focus, "Toast never steals focus");
        long style = Native.GetWindowLongPtr(toast.Handle, -20).ToInt64(); Require((style & 0x08000020) == 0x08000020, "No activate and transparent styles");
        Require(Native.SendMessage(toast.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero).ToInt64() == -1, "Toast hit-test transparent");
        Require(Native.SendMessage(toast.Handle, 0x0021, IntPtr.Zero, IntPtr.Zero).ToInt64() == 3, "Toast does not activate");
        var point = probe.PointToScreen(new Point(60, 15)); var hit = Native.WindowFromPoint(point);
        Require(toast.Bounds.Contains(point), "Input probe is actually underneath the toast");
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
        int cardHeight = toast.Font.Height + 12;
        Require(toast.Height == cardHeight * 3 + 16 + 20 && toast.Opacity is > 0 and < 1, "Three content-height cards with gaps and translucent surface");
        using (var bitmap = new Bitmap(toast.Width, toast.Height))
        {
            toast.DrawToBitmap(bitmap, toast.ClientRectangle);
            Require(bitmap.GetPixel(10, 10).ToArgb() != toast.TransparencyKey.ToArgb() && bitmap.GetPixel(10, 10 + cardHeight + 3).ToArgb() == toast.TransparencyKey.ToArgb() && bitmap.GetPixel(5, 5).ToArgb() == toast.TransparencyKey.ToArgb(), "8px transparent gaps and 10px outer padding");
        }
        foreach (var size in new[] { new Size(1000, 600), new Size(1500, 900), new Size(1280, 720) })
        {
            main.Size = size; main.Update(); CheckColumns(main);
            Require(region.RectangleToScreen(region.ClientRectangle).Contains(toast.Bounds), "Toast stays entirely in reserved right column after resize");
            Require(!catalogScroll.HorizontalScroll.Visible, "Catalog has no stale horizontal scrollbar after wheel and resize");
        }
        data.Toast(data.Products[0].Name + "를 장바구니에 추가했습니다", "add:1");
        data.Toast(data.Products[0].Name + "를 장바구니에 추가했습니다", "add:1");
        var entries = ((System.Collections.IEnumerable)toast.GetType().GetField("messages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(toast)!).Cast<object>().ToArray();
        Require(entries.Length == 3 && entries.Count(e => (string?)e.GetType().GetField("Item2")!.GetValue(e) == "add:1") == 1, "Repeated product toast deduplicated");
        Require((string)entries[0].GetType().GetField("Item1")!.GetValue(entries[0])! == data.Products[0].Name + "를 장바구니에 추가했습니다", "Latest toast retains original full name; ellipsis is paint only");
        Capture(toast, "09-toast-stack");
        // DrawToBitmap renders the layer's transparency key literally. Composite only
        // this review artifact; native hit-testing and opacity are checked separately.
        using (var preview = new Bitmap(main.Width, main.Height))
        {
            main.DrawToBitmap(preview, new Rectangle(Point.Empty, main.Size));
            using var overlay = new Bitmap(toast.Width, toast.Height); toast.DrawToBitmap(overlay, toast.ClientRectangle);
            overlay.MakeTransparent(toast.TransparencyKey);
            using var attributes = new ImageAttributes(); attributes.SetColorMatrix(new ColorMatrix { Matrix33 = (float)toast.Opacity });
            using var graphics = Graphics.FromImage(preview);
            graphics.DrawImage(overlay, new Rectangle(toast.Left - main.Left, toast.Top - main.Top, toast.Width, toast.Height), 0, 0, toast.Width, toast.Height, GraphicsUnit.Pixel, attributes);
            preview.Save(Path.Combine(output, "15-toast-layout-preview.png"), ImageFormat.Png);
        }
        var hidden = new TaskCompletionSource(); toast.VisibleChanged += OnVisible;
        void OnVisible(object? sender, EventArgs e) { if (!toast.Visible) hidden.TrySetResult(); }
        var clock = Stopwatch.StartNew(); await hidden.Task.WaitAsync(TimeSpan.FromSeconds(5)); clock.Stop(); toast.VisibleChanged -= OnVisible;
        Require(clock.Elapsed.TotalMilliseconds is > 2200 and < 4000, "Toast expires near 2.8 seconds"); results.Add($"Toast expiry: {clock.Elapsed.TotalMilliseconds:F0}ms; input transparent; one timer, no animation loop");
    }
    private static void CheckColumns(MainForm main)
    {
        var filter = Find<Panel>(main, "FilterRegion").Bounds; var toast = Find<Panel>(main, "ToastRegion").Bounds;
        var product = Find<Panel>(main, "ProductRegion").Bounds; var cart = Find<Panel>(main, "CartRegion").Bounds;
        Require(filter.Left == product.Left && filter.Right == product.Right && toast.Left == cart.Left && toast.Right == cart.Right, "Top/bottom column edges match exactly");
        Require(toast.Left - filter.Right == cart.Left - product.Right, "Matching horizontal column gap");
        Require(toast.Bottom <= cart.Top && filter.Bottom <= product.Top, "Reserved top regions cannot intrude into catalog/cart");
    }
    private static async Task CheckCartAndColumns(MainForm main, SampleData data)
    {
        var cart = Find<FlowLayoutPanel>(main, "CartList"); var catalog = Find<Panel>(main, "ProductList");
        var quantities = data.Cart.ToDictionary(x => x.Product.Id, x => x.Quantity);
        foreach (int id in new[] { 13, 7, 1 })
        {
            cart.AutoScrollPosition = new Point(0, 600); data.AddToCart(data.Products.Single(p => p.Id == id));
            var seller = cart.Controls[0]; var row = Find<Control>(seller, $"CartRow_{id}");
            Require(seller.Name == "Cart_mega" && row.Top == All(seller).Where(c => c.Name.StartsWith("CartRow_")).Min(c => c.Top), "Added/new/repeated item is first row of first seller");
            Require(cart.RectangleToScreen(cart.ClientRectangle).Contains(row.RectangleToScreen(row.ClientRectangle)), "Added row fully visible after scroll reset");
            Require(row.BackColor == Color.FromArgb(221, 238, 225) && seller.BackColor == Color.White && All(cart).Where(c => c.Name.StartsWith("CartRow_") && c != row).All(c => c.BackColor == Color.White), "Only most recently added row highlighted");
            Require(Find<TextBox>(row, $"CartName_{id}").BackColor == row.BackColor, "Highlight includes text background");
            if (id == 1) Capture(main, "10-added-row");
        }
        var order = data.Cart.ToArray(); var positions = All(cart).Where(c => c.Name.StartsWith("CartRow_")).ToDictionary(c => c, c => c.Bounds);
        cart.AutoScrollPosition = new Point(0, 80); var scroll = cart.AutoScrollPosition;
        data.ChangeQuantity(data.Cart.Single(x => x.Product.Id == 7), 1); data.ChangeQuantity(data.Cart.Single(x => x.Product.Id == 7), -1);
        Require(order.SequenceEqual(data.Cart) && positions.All(x => x.Key.Bounds == x.Value) && scroll == cart.AutoScrollPosition, "Quantity buttons preserve row/seller order and scroll");
        await Task.Delay(260);
        Require(All(cart).Where(c => c.Name.StartsWith("CartRow_")).All(c => c.BackColor == Color.White), "Row highlight expires");
        data.Remove(data.Cart.Single(x => x.Product.Id == 13));
        foreach (var line in data.Cart) if (quantities.TryGetValue(line.Product.Id, out int quantity)) line.Quantity = quantity;
        data.Notify(); cart.AutoScrollPosition = Point.Empty;
        foreach (var size in new[] { new Size(1000, 600), new Size(1500, 900), new Size(1280, 720) })
        {
            main.Size = size; main.Update(); CheckColumns(main);
            var cards = catalog.Controls.OfType<ProductCard>().ToArray();
            Require(cards.Count(c => c.Top == cards.Min(c => c.Top)) == 3, "Three columns survive resize");
            foreach (var card in cards)
            {
                var image = Find<PictureBox>(card, $"Image_{card.Product.Id}"); var title = Find<TextBox>(card, $"Name_{card.Product.Id}");
                var price = Find<TextBox>(card, $"Price_{card.Product.Id}"); var icon = Find<PictureBox>(card, $"Seller_{card.Product.Id}"); var add = Find<Button>(card, $"Add_{card.Product.Id}");
                Require(image.Width == image.Height && title.Height >= card.Font.Height * 3, "Square image and three name lines retained");
                Require(add.Top - price.Bottom == 4 && add.Top - icon.Bottom == 4 && card.Height - add.Bottom == 10, "Compact aligned icon/price/button spacing");
            }
            Require(cards.Select(c => Find<Button>(c, $"Add_{c.Product.Id}").Top).Distinct().Count() == 1, "Buttons align for short and long names");
        }
        Capture(main, "11-columns-resize");
    }
    private static void CheckIcons(MainForm main, SampleData data)
    {
        foreach (var supplier in data.Suppliers)
        {
            var product = data.Products.First(p => p.Supplier.Id == supplier.Id);
            var icon = Find<PictureBox>(Find<Panel>(main, "ProductList"), $"Seller_{product.Id}");
            using var supplied = new Bitmap(Path.Combine(AppContext.BaseDirectory, "Assets", "Sellers", supplier.Id + ".png"));
            Require(icon.Image!.Size == supplied.Size && icon.SizeMode == PictureBoxSizeMode.Zoom, "Provided seller icon with preserved aspect ratio");
            using var loaded = (Bitmap)icon.Image.Clone();
            Require(Enumerable.Range(0, 32).All(y => Enumerable.Range(0, 32).All(x => loaded.GetPixel(x, y) == supplied.GetPixel(x, y))), "Exact supplied icon pixels: " + supplier.Id);
        }
        var loader = typeof(MainForm).Assembly.GetType("CafeOrder.SampleImages")!.GetMethod("LoadSeller", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var fallback = (Image)loader.Invoke(null, [Path.Combine(output, "missing-icon.png")])!;
        string invalid = Path.Combine(output, "invalid-icon.tmp"); File.WriteAllText(invalid, "invalid");
        using var bad = (Image)loader.Invoke(null, [invalid])!; File.Delete(invalid);
        Require(fallback.Size == new Size(32, 32) && bad.Size == fallback.Size, "Missing/corrupt icon has fixed-size fallback without crash");
    }
    private static void CheckCredentials(MainForm main, SampleData data)
    {
        var settings = Find<FlowLayoutPanel>(main, "Suppliers");
        foreach (var supplier in data.Suppliers)
        {
            var id = Find<TextBox>(settings, $"LoginId_{supplier.Id}"); var password = Find<TextBox>(settings, $"LoginPassword_{supplier.Id}");
            Require(id.Width > 160 && password.Width > 160 && password.UseSystemPasswordChar && id.ImeMode == ImeMode.Disable, "Wider English-mode masked login inputs for every seller");
            Require(Find<Button>(settings, $"Login_{supplier.Id}").Text is "로그인" or "다시 로그인", "Common provisional login UI");
            Require(!supplier.Manual || !All(settings).Any(c => c.Name == $"Shipping_{supplier.Id}"), "Manual login UI does not enable automatic shipping rules");
        }
        var field = Find<TextBox>(settings, "LoginPassword_mega"); var caps = Find<Label>(settings, "CapsLock_mega");
        var saved = Clipboard.GetDataObject(); var keyboard = new byte[256]; Native.GetKeyboardState(keyboard);
        try
        {
            field.Focus(); string ascii = new(Enumerable.Range(32, 95).Select(x => (char)x).ToArray());
            Clipboard.SetText("한글" + ascii + "日本😀"); field.Paste(); Require(field.Text == ascii, "Paste permits printable ASCII only, including all ordinary special characters");
            field.Clear(); Native.SendMessage(field.Handle, 0x0102, new IntPtr('한'), IntPtr.Zero); Require(field.Text.Length == 0, "Korean typing rejected");
            Native.SendMessage(field.Handle, 0x0102, new IntPtr('A'), IntPtr.Zero); Require(field.Text == "A", "English typing retained");
            var state = (byte[])keyboard.Clone(); state[0x14] |= 1; Native.SetKeyboardState(state); Native.SendMessage(field.Handle, 0x0101, new IntPtr(0x14), IntPtr.Zero);
            Require(caps.Visible && caps.Text == "⚠ Caps Lock 켜짐", "Caps Lock ON shown immediately on key event"); Capture(main, "12-caps-lock");
            state[0x14] &= 0xfe; Native.SetKeyboardState(state); Native.SendMessage(field.Handle, 0x0101, new IntPtr(0x14), IntPtr.Zero);
            Require(!caps.Visible, "Caps Lock OFF hidden immediately without timer"); field.Clear();
        }
        finally { Native.SetKeyboardState(keyboard); if (saved != null) Clipboard.SetDataObject(saved, true); else Clipboard.Clear(); }
        settings.ScrollControlIntoView(Find<TextBox>(settings, "LoginId_naver").Parent!.Parent!.Parent!); Capture(main, "13-manual-login"); settings.AutoScrollPosition = Point.Empty;
    }
    private static void CheckOrderPadding(OrderForm order)
    {
        foreach (var size in new[] { new Size(796, 639), new Size(650, 520), new Size(900, 700) })
        {
            order.Size = size; order.Update();
            var cards = Find<FlowLayoutPanel>(order, "OrderCards").Controls.Cast<Control>().ToArray();
            foreach (var card in cards)
            {
                var action = card.Controls.OfType<Button>().Single();
                Require(card.Height - action.Bottom == card.Padding.Bottom + action.Margin.Bottom, "AUTO/manual card ends at content plus identical padding");
                foreach (var row in All(card).Where(c => c.Name.StartsWith("CartRow_")))
                {
                    int bottom = row.Controls.Cast<Control>().Max(c => c.Bottom);
                    Require(row.Height - bottom == 4, "No unused action row below manual product content");
                }
            }
        }
        var list = Find<FlowLayoutPanel>(order, "OrderCards");
        list.ScrollControlIntoView(list.Controls.Cast<Control>().First(c => c.Name.StartsWith("Order_naver"))); Capture(order, "14-manual-order-padding");
        list.AutoScrollPosition = Point.Empty;
    }
    private static IntPtr Pack(Point p) => new((p.Y << 16) | (p.X & 0xffff));
    private static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] internal static extern IntPtr GetFocus();
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern bool GetKeyboardState(byte[] state);
        [DllImport("user32.dll")] internal static extern bool SetKeyboardState(byte[] state);
    }
}
