using CafeOrder;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static partial class Program
{
    private static string output = "";
    private static readonly List<string> results = [];
    private static readonly string statePath = Path.Combine(Path.GetTempPath(), "CafeOrderChecks", Guid.NewGuid().ToString("N"));
    private static int registeredId;
    private static WindowPlacement? savedWindow;
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/ui-checks" : args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Exception? failure = null;
        var clipboard = Clipboard.GetDataObject();
        try
        {
            if (args.Contains("--repro-six")) { Run(ReproSix); File.WriteAllLines(Path.Combine(output, "repro.txt"), results); return 0; }
            if (args.Contains("--repro-seven")) { Run(ReproSeven); File.WriteAllLines(Path.Combine(output, "repro.txt"), results); return 0; }
            Run(Check);
            Run(CheckRestart);
            var store = new LocalState(statePath); Require(store.Preferences.Window?.Maximized == true, "Closing a maximized window saves its state"); store.Preferences.Window = new(-30000, -30000, 10, 10, true); store.SavePreferences();
            Run(CheckOffscreen);
        }
        catch (Exception ex) { failure = ex; }
        finally { if (clipboard != null) Clipboard.SetDataObject(clipboard, true); else Clipboard.Clear(); }
        string result = failure?.ToString() ?? "PASS: revision 7 column repaint, two-column toolbar, category check, ellipsis/original tooltips, seller homepage routing; revision 6 regressions: toolbar/grid buttons, single-line prices, cart visibility/order/reuse, unified draft/product menu, local refresh/in-place registration, sold-out/tooltip, stable scroll widths, X borders, typography reset/persistence, x64.";
        File.WriteAllText(Path.Combine(output, "result.txt"), result); File.WriteAllLines(Path.Combine(output, "performance.txt"), results);
        Console.WriteLine(result); return failure == null ? 0 : 1;
    }
    private static void Run(Func<MainForm, Task> check)
    {
        Exception? failure = null; var watch = Stopwatch.StartNew(); using var main = new MainForm(statePath); watch.Stop();
        results.Add($"Main construction: {watch.Elapsed.TotalMilliseconds:F2}ms");
        main.Shown += async (_, _) => { try { await check(main); } catch (Exception ex) { failure = ex; } finally { main.Close(); } };
        Application.Run(main); if (failure != null) throw failure;
    }
    private static IEnumerable<Control> All(Control root) { foreach (Control c in root.Controls) { yield return c; foreach (var child in All(c)) yield return child; } }
    private static T Find<T>(Control root, string name) where T : Control => All(root).OfType<T>().Single(c => c.Name == name);
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static SampleData Data(MainForm main) => (SampleData)typeof(MainForm).GetField("sample", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
    private static void Capture(Control control, string name)
    { using var bitmap = new Bitmap(control.Width, control.Height); control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size)); bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png); }
    private static void Pump() => Application.DoEvents();
    private static void Mouse(ProductCard card, MouseButtons button, Point point, bool move = false)
    {
        typeof(ProductCard).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, [new MouseEventArgs(button, 1, point.X, point.Y, 0)]);
        if (move) typeof(ProductCard).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, [new MouseEventArgs(button, 1, point.X + 20, point.Y + 20, 0)]);
        typeof(ProductCard).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, [new MouseEventArgs(button, 1, point.X, point.Y, 0)]);
    }
    private static void Measure(MainForm main)
    {
        main.Size = new Size(1280, 720); var tabs = Find<TabControl>(main, "MainTabs"); var refs = All(main).ToArray();
        for (int i = 0; i < tabs.TabCount; i++)
        { var sw = Stopwatch.StartNew(); tabs.SelectedIndex = i; main.Update(); sw.Stop(); results.Add($"{tabs.TabPages[i].Text} first show: {sw.Elapsed.TotalMilliseconds:F2}ms"); Capture(main, $"01-tab-{i}"); }
        for (int i = 1; i < tabs.TabCount; i++)
        {
            var timings = new List<double>();
            for (int j = 0; j < 3; j++) { tabs.SelectedIndex = 0; main.Update(); var sw = Stopwatch.StartNew(); tabs.SelectedIndex = i; main.Update(); tabs.SelectedIndex = 0; main.Update(); sw.Stop(); timings.Add(sw.Elapsed.TotalMilliseconds); }
            results.Add($"상품 ↔ {tabs.TabPages[i].Text} round trip: {string.Join(", ", timings.Select(v => v.ToString("F2")))}ms");
        }
        Require(refs.SequenceEqual(All(main)), "Tabs reuse all controls including supplier fields/icons");
    }
    private static async Task Check(MainForm main)
    {
        Require(Environment.Is64BitProcess, "x64 process"); Measure(main);
        var data = Data(main); var tabs = Find<TabControl>(main, "MainTabs"); var grid = Find<ProductGrid>(main, "ProductList");
        Require(tabs.TabPages.Cast<TabPage>().Select(p => p.Text).SequenceEqual(new[] { "상품", "주문기록", "판매처관리", "로그", "설정" }), "Five tabs without add-product tab");
        Require(!typeof(MainForm).Assembly.GetTypes().Any(t => t.Name.Contains("Toast")), "Removed notification implementation");
        Require(!All(main).Any(c => c.Name.Contains("Toast")), "Removed notification space/controls");
        var search = Find<TextBox>(main, "Search"); var filters = Find<TableLayoutPanel>(main, "Filters"); var productRegion = Find<Panel>(main, "ProductRegion");
        Require(productRegion.Top - filters.Bottom <= 8, "Catalog starts immediately below search toolbar");
        Find<Button>(main, "ExportProducts").PerformClick(); Require(Find<Label>(main, "TransferStatus").Text.Contains("저장소 연결 후"), "XLSX boundary is honestly unavailable without adapter");
        Find<Label>(main, "TransferStatus").Visible = false; Find<Label>(main, "TransferStatus").Text = "";
        var card = Find<ProductCard>(grid, "Product_1"); search.Text = "포모나";
        Require(!All(card).OfType<TextBox>().Any() && !All(card).OfType<Button>().Any(b => b.Text.Contains("장바구니")), "No copy textboxes or add button in normal cards");
        int qty = data.Cart.Single(l => l.Product.Id == 1).Quantity;
        foreach (var point in new[] { new Point(45, 40), new Point(card.ImageBounds.Left + 10, card.ImageBounds.Top + 10), card.NameBounds.Location + new Size(5, 5), card.PriceBounds.Location + new Size(5, 5) }) Mouse(card, MouseButtons.Left, point);
        Require(data.Cart.Single(l => l.Product.Id == 1).Quantity == qty + 4, "Exactly one increment per click across every surface");
        Mouse(card, MouseButtons.Left, card.ImageBounds.Location + new Size(5, 5), true);
        Require(data.Cart.Single(l => l.Product.Id == 1).Quantity == qty + 4, "Image drag gesture never adds");
        using (var menu = card.BuildMenu())
        {
            Require(menu.Items.Cast<ToolStripItem>().Select(x => x.Text).SequenceEqual(new[] { "상품 삭제", "이미지 삭제", "카테고리 변경", "상품 링크", "", "이름 복사", "가격 복사", "이름 + 가격 복사" }), "Context menu ordering");
            Require(((ToolStripMenuItem)menu.Items[2]).DropDownItems.Cast<ToolStripItem>().Select(x => x.Text).SequenceEqual(SampleData.Categories.Skip(1)), "Category submenu");
            menu.Items[5].PerformClick(); Require(Clipboard.GetText() == card.Product!.Name, "Copy full name");
            menu.Items[6].PerformClick(); Require(Clipboard.GetText() == card.Product!.PriceText, "Copy price including note");
            menu.Items[7].PerformClick(); Require(Clipboard.GetText() == card.Product!.Name + Environment.NewLine + card.Product.PriceText, "Copy name newline price");
            Mouse(card, MouseButtons.Right, new Point(45, 40));
            Require(data.Cart.Single(l => l.Product.Id == 1).Quantity == qty + 4, "Right click never adds");
            foreach (var popup in Application.OpenForms.Cast<Form>().Where(f => f != main).ToArray()) Require(popup.Name != "ToastHost", "No floating feedback");
            menu.Items[0].PerformClick(); Require(!card.Product!.IsActive && grid.Controls.Contains(card) && card.Visible, "Soft deletion keeps visible card");
            Mouse(card, MouseButtons.Left, new Point(45, 40)); Require(data.Cart.Single(l => l.Product.Id == 1).Quantity == qty + 4, "Deleted product does not add");
            Capture(card, "02-deleted-card"); Find<Button>(card, "UndoProduct").PerformClick(); Require(card.Product.IsActive, "Immediate undo");
        }
        // Close any context popup before driving more controls.
        Native.SendMessage(card.Handle, 0x001F, IntPtr.Zero, IntPtr.Zero); Pump();
        await Task.Delay(240);
        var cart = Find<FlowLayoutPanel>(main, "CartList");
        Require(Find<Control>(cart, "CartRow_1").BackColor == Color.White, "Row highlight expires");
        search.Clear();
        var lineOrder = data.Cart.ToArray(); var sellerOrder = cart.Controls.Cast<Control>().ToArray(); cart.AutoScrollPosition = new Point(0, 80); var scroll = cart.AutoScrollPosition;
        data.ChangeQuantity(data.Cart.Single(l => l.Product.Id == 1), 1); data.ChangeQuantity(data.Cart.Single(l => l.Product.Id == 1), -1);
        Require(lineOrder.SequenceEqual(data.Cart) && sellerOrder.SequenceEqual(cart.Controls.Cast<Control>()) && scroll == cart.AutoScrollPosition, "Quantity retains order and scroll");
        cart.AutoScrollPosition = new Point(0, 700); Mouse(Find<ProductCard>(grid, "Product_7"), MouseButtons.Left, new Point(45, 40));
        var row7 = Find<Control>(cart, "CartRow_7");
        Require(cart.Controls[0].Name == "Cart_mega" && row7.Top < Find<Control>(cart, "CartRow_1").Top && cart.RectangleToScreen(cart.ClientRectangle).Contains(row7.RectangleToScreen(row7.ClientRectangle)), "Latest seller/item first and fully visible");
        Require(row7.BackColor == Color.FromArgb(221, 238, 225) && cart.Controls[0].BackColor == Color.White, "Only added row highlights");
        await CheckSeven(main);
        await CheckSix(main);
        CheckImages(main, data, grid, card);
        CheckDrafts(main, data, grid);
        CheckColumnsAndFonts(main, data, grid);
        CheckLargeFonts(main, grid);
        CheckSuppliers(main);
        tabs.SelectedIndex = 1; CheckHistory(main); Capture(main, "09-history-wrap");
        tabs.SelectedIndex = 0;
        using (var order = new OrderForm(data, data.Cart.ToArray()))
        {
            order.Show(main); Pump();
            foreach (var size in new[] { new Size(650, 520), new Size(900, 700) })
            {
                order.Size = size; order.Update();
                foreach (var remove in All(order).OfType<Button>().Where(b => b.Name.StartsWith("Remove_"))) Require(remove.Parent!.ClientRectangle.Contains(remove.Bounds) && remove.Right <= remove.Parent.Width - 4, "Complete X border with right padding");
                foreach (var orderCard in Find<FlowLayoutPanel>(order, "OrderCards").Controls.Cast<Control>())
                { var action = orderCard.Controls.OfType<Button>().Single(); Require(orderCard.Height - action.Bottom == orderCard.Padding.Bottom + action.Margin.Bottom, "Content-fit card padding"); }
            }
            Capture(order, "10-order-x-padding");
            var target = data.Cart.Single(l => l.Product.Id == 1); var before = target.Quantity; Find<Button>(order, "Plus_1").PerformClick(); Require(target.Quantity == before + 1, "Shared order quantity");
            Find<Button>(order, "StartAllOrders").PerformClick(); Require(data.IsLocked(target.Product), "Order start still locks edits"); order.Close();
        }
        Require(data.Cart.All(l => !data.IsLocked(l.Product)), "Order close releases locks");
        tabs.SelectedIndex = 3; Find<Button>(main, "CopyAllLogs").PerformClick(); Require(Clipboard.GetText() == Find<RichTextBox>(main, "LogText").Text, "Log copy local feedback");
        CheckReset(main);
        // Persist a tombstone while leaving the history sample untouched; drafts are intentionally not serialized.
        data.SetActive(data.Products.Single(p => p.Id == 21), false);
        Require(Find<FlowLayoutPanel>(main, "HistoryCards").Controls.Count == 3, "Deleting catalog never deletes order history");
        main.WindowState = FormWindowState.Normal; main.Size = new Size(1120, 680);
        main.Location = Screen.FromControl(main).WorkingArea.Location + new Size(25, 25);
        savedWindow = new(main.Left, main.Top, main.Width, main.Height, false);
        Capture(main, "11-final-state");
    }
    private static void CheckImages(MainForm main, SampleData data, ProductGrid grid, ProductCard card)
    {
        string source = Path.Combine(statePath, "source.png"); Directory.CreateDirectory(statePath);
        using (var image = new Bitmap(200, 100)) { using var g = Graphics.FromImage(image); g.Clear(Color.Blue); g.FillRectangle(Brushes.Red, 0, 0, 50, 100); g.FillRectangle(Brushes.Green, 150, 0, 50, 100); image.Save(source); }
        grid.AutoScrollPosition = new Point(0, 60); var scroll = grid.AutoScrollPosition; var refs = grid.Controls.Cast<Control>().ToArray(); var bounds = card.Bounds;
        int qty = data.Cart.Single(l => l.Product.Id == 1).Quantity;
        var point = card.PointToScreen(card.ImageBounds.Location + new Size(5, 5)); var drop = new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { source }), 0, point.X, point.Y, DragDropEffects.Copy, DragDropEffects.None);
        typeof(ProductCard).GetMethod("OnDragEnter", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, [drop]);
        typeof(ProductCard).GetMethod("OnDragDrop", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(card, [drop]);
        string manual = data.Store.ManualImagePath(1); Require(File.Exists(manual), "Drop writes manual WebP");
        Require(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(manual), 8, 4) == "WEBP", "Real WebP output");
        using (var cropped = (Bitmap)ManualImages.Load(manual)) Require(cropped.Width == 100 && cropped.Height == 100 && cropped.GetPixel(3, 3).B > 200 && cropped.GetPixel(3, 3).R < 30, "Center square crop retains middle rather than stretching");
        Capture(card, "03-manual-image");
        using var menu = card.BuildMenu(); Require(menu.Items[1].Text == "이미지 삭제" && menu.Items[1].Enabled, "Shared menu enables manual image removal");
        menu.Items[1].PerformClick();
        Require(!File.Exists(manual) && grid.Controls.Cast<Control>().SequenceEqual(refs) && grid.AutoScrollPosition == scroll && card.Bounds == bounds, "Manual removal swaps only image, preserving cards/scroll/position");
        using var withoutManual = card.BuildMenu(); Require(!withoutManual.Items[1].Enabled, "Image removal disabled without manual image");
        Require(data.Cart.Single(l => l.Product.Id == 1).Quantity == qty, "Image editing never adds to cart");
        card.SetManual(source); grid.AutoScrollPosition = Point.Empty;
    }
    private static void CheckDrafts(MainForm main, SampleData data, ProductGrid grid)
    {
        var category = Find<ComboBox>(main, "CategoryFilter"); var supplier = Find<ComboBox>(main, "SupplierFilter"); var search = Find<TextBox>(main, "Search");
        int count = data.Products.Count, export = data.ExportRows().Length;
        category.SelectedItem = "티백"; Find<Button>(main, "AddProduct").PerformClick(); Find<Button>(main, "AddProduct").PerformClick();
        var drafts = grid.Controls.OfType<ProductCard>().Where(c => c.IsDraft).OrderBy(c => c.Top).ThenBy(c => c.Left).ToArray(); Require(drafts.Length == 2, "Multiple drafts");
        search.Text = "no-match"; supplier.SelectedItem = "쿠팡";
        Require(drafts.All(d => d.Visible) && data.Products.Count == count && data.ExportRows().Length == export, "Drafts excluded from normal count/search/seller/export/sorting");
        var draft = drafts[0]; var url = Find<TextBox>(draft, "DraftUrl"); url.Text = "https://unsupported.invalid/item/1"; draft.RegisterDraft(); Require(draft.IsDraft && url.Text.Contains("unsupported"), "Failed registration retains editable draft");
        var previousSize = main.Size; main.Size = new Size(1500, 600); Pump();
        grid.AutoScrollPosition = new Point(0, 35); Pump();
        var registrationOrder = grid.Items.ToArray(); var registrationBounds = draft.Bounds; var registrationScroll = grid.AutoScrollPosition;
        Require(registrationScroll.Y < 0, "Registration also tested with nonzero scroll");
        url.Text = "https://www.megacoffee.co.kr/goods/goods_view.php?goodsNo=1000002613"; draft.RegisterDraft();
        Require(draft.Visible && registrationOrder.SequenceEqual(grid.Items) && draft.Bounds == registrationBounds && grid.AutoScrollPosition == registrationScroll, $"URL position: visible={draft.Visible} order={registrationOrder.SequenceEqual(grid.Items)} bounds={registrationBounds} -> {draft.Bounds} scroll={registrationScroll} -> {grid.AutoScrollPosition}");
        main.Size = previousSize;
        Require(!draft.IsDraft && draft.Product!.Category == "티백" && grid.Controls.Contains(draft) && data.Products.Count == count + 1, "Mock URL success converts same card and persists category"); registeredId = draft.Product!.Id;
        Require(data.Store.LoadProducts().Any(p => p.Id == registeredId && p.Url == draft.Product.Url), "Registered sample is saved");
        Find<Button>(main, "RefreshProducts").PerformClick(); Require(!draft.Visible && drafts[1].Visible, "Explicit refresh applies filters to registered card while retaining unfinished draft");
        category.SelectedIndex = 0; supplier.SelectedIndex = 0; search.Clear();
        Find<Button>(main, "AddProduct").PerformClick();
        Require(grid.Controls.OfType<ProductCard>().Count(c => c.IsDraft) == 2 && grid.AutoScrollPosition == Point.Empty, "Add draft scrolls top"); Capture(main, "04-drafts");
        var product = Find<ProductCard>(grid, "Product_7"); using var menu = product.BuildMenu(); ((ToolStripMenuItem)menu.Items[2]).DropDownItems[3].PerformClick(); Require(product.Product!.Category == "파우더", "Category update saved and same card reused");
        void CheckList()
        {
            var expected = data.Products.Where(p => p.IsActive && (category.SelectedIndex == 0 || p.Category == category.Text) && (supplier.SelectedIndex == 0 || p.Supplier.Name == supplier.Text) && p.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(p => Array.IndexOf(SampleData.Categories, p.Category)).ThenBy(p => p.Name, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), false)).ToArray();
            Require(grid.Items.Where(c => !c.IsDraft).Select(c => c.Product).SequenceEqual(expected) && grid.AutoScrollPosition == Point.Empty, "Common refresh applies filters/order and resets scroll");
            Require(grid.Items.Take(2).All(c => c.IsDraft), "Unfinished drafts retained separately at top");
        }
        CheckList();
        grid.AutoScrollPosition = new Point(0, 35); category.SelectedItem = "파우더"; CheckList();
        grid.AutoScrollPosition = new Point(0, 35); supplier.SelectedItem = "메가커피"; CheckList();
        grid.AutoScrollPosition = new Point(0, 35); search.Text = "포모나"; CheckList();
        grid.AutoScrollPosition = new Point(0, 35); Find<Button>(main, "RefreshProducts").PerformClick(); CheckList();
        category.SelectedIndex = 0; supplier.SelectedIndex = 0; search.Clear();
    }
    private static void CheckColumnsAndFonts(MainForm main, SampleData data, ProductGrid grid)
    {
        var tabs = Find<TabControl>(main, "MainTabs"); var cartRegion = Find<Panel>(main, "CartRegion"); var originalWidth = cartRegion.Width;
        foreach (int columns in new[] { 3, 4, 5 })
        {
            tabs.SelectedIndex = 0; Find<Button>(main, $"ViewColumns{columns}").PerformClick(); main.Update();
            Require(grid.Columns == columns && cartRegion.Width == originalWidth, "Columns change only product area");
            var visible = grid.Controls.OfType<ProductCard>().Where(c => c.Visible).ToArray(); Require(visible.Count(c => c.Top == visible.Min(c => c.Top)) == columns, "Requested column count");
            foreach (var card in visible)
            { Require(card.ImageBounds.Width == card.ImageBounds.Height && card.NameBounds.Height >= Ui.Fonts!.Font(TypographyKey.ProductName, FontStyle.Bold).Height * 3, "Square image and three lines"); Require(card.PriceBounds.Bottom <= card.Height - 10, "Price row inside card"); }
            Capture(main, $"05-columns-{columns}");
        }
        tabs.SelectedIndex = 4; Find<Button>(main, "OpenTypography").PerformClick(); var window = Application.OpenForms.Cast<Form>().Single(f => f.Name == "TypographySettings");
        Require(!window.Modal && main.Enabled, "Typography window modeless"); tabs.SelectedIndex = 0;
        foreach (var key in Enum.GetValues<TypographyKey>()) Find<NumericUpDown>(window, "Font_" + key).Value = key is TypographyKey.ProductName or TypographyKey.ProductPrice or TypographyKey.HistoryProductName ? 16 : 14;
        Require(Find<RichTextBox>(main, "LogText").Font.Size == 14, "Log font live update");
        Require(Find<TextBox>(main, "CartName_1").Font.Size == 14 && Find<TabControl>(main, "MainTabs").Font.Size == 14, "Shared roles applied throughout main");
        tabs.SelectedIndex = 1; CheckHistory(main); tabs.SelectedIndex = 0; grid.AutoScrollPosition = new Point(0, 60); Require(grid.AutoScrollPosition.Y < 0, "Main scroll works while settings open"); grid.AutoScrollPosition = Point.Empty;
        Find<Button>(window, "CopyTypography").PerformClick(); string text = Clipboard.GetText();
        Require(text.StartsWith("CafeOrder 글자크기 설정") && Enum.GetValues<TypographyKey>().All(k => text.Contains(k + "=")), "Copies all typography keys");
        Require(new LocalState(statePath).Preferences.FontSizes["ProductName"] == 16, "Font settings save immediately");
        Capture(main, "06-live-fonts"); Capture(window, "07-font-settings"); window.Close();
        tabs.SelectedIndex = 0; Find<Button>(main, "ViewColumns4").PerformClick();
    }
    private static void CheckHistory(MainForm main)
    {
        foreach (var table in All(main).OfType<HistoryItemsTable>())
        {
            foreach (var label in table.Controls.OfType<Label>())
            { int needed = TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(20, label.Width - 8), 0), (label is EllipsisLabel { SingleLine: true } ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak) | TextFormatFlags.NoPrefix).Height; Require(label.Height >= needed + 4, "History names fully wrap; other fields stay on one line"); }
            Require(table.Height == table.Controls.Cast<Control>().Max(c => c.Bottom), "History table height matches row sum");
        }
    }
    private static void CheckLargeFonts(MainForm main, ProductGrid grid)
    {
        var fonts = Ui.Fonts!; var before = Enum.GetValues<TypographyKey>().ToDictionary(k => k, fonts.Size);
        foreach (var key in before.Keys) fonts.Set(key, 24);
        var tabs = Find<TabControl>(main, "MainTabs");
        foreach (int columns in new[] { 3, 4, 5 })
        {
            tabs.SelectedIndex = 0; Find<Button>(main, $"ViewColumns{columns}").PerformClick();
            foreach (var size in new[] { new Size(1000, 600), new Size(1280, 720) })
            {
                main.Size = size; main.Update();
                var filters = Find<TableLayoutPanel>(main, "Filters");
                foreach (var name in new[] { "AddProduct", "ExportProducts", "ImportProducts", "Search" })
                { var control = Find<Control>(main, name); var toolbar = name == "Search" ? filters : Find<TableLayoutPanel>(main, "Management"); if (!toolbar.RectangleToScreen(toolbar.ClientRectangle).Contains(control.RectangleToScreen(control.ClientRectangle))) { Capture(main, "toolbar-failure"); throw new Exception($"Toolbar {name}: filter={filters.Bounds} control={control.Bounds} parent={control.Parent!.Bounds}"); } }
                foreach (var card in grid.Controls.OfType<ProductCard>().Where(c => c.Visible && c.Product != null))
                {
                    var needed = TextRenderer.MeasureText(card.PriceDisplayText, fonts.Font(TypographyKey.ProductPrice), new Size(card.PriceBounds.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    Require(!card.PriceDisplayText.Contains(Environment.NewLine) && card.PriceBounds.Height == fonts.Font(TypographyKey.ProductPrice).Height + 2, "Price uses one line with ellipsis at every column/window width");
                    Require(card.ImageBounds.Width == card.ImageBounds.Height && card.ClientRectangle.Contains(card.PriceBounds), "Large fonts preserve square images and content bounds");
                }
                foreach (var row in All(Find<FlowLayoutPanel>(main, "CartList")).OfType<CartProductRow>())
                    foreach (Control control in row.Controls) Require(row.ClientRectangle.Contains(control.Bounds), "Large fonts keep cart controls inside rows");
            }
        }
        tabs.SelectedIndex = 1; CheckHistory(main);
        tabs.SelectedIndex = 0; Capture(main, "14-large-font-catalog");
        using (var order = new OrderForm(Data(main), Data(main).Cart.ToArray()))
        {
            order.Show(main); order.Size = new Size(650, 520); order.Update();
            foreach (var row in All(order).OfType<CartProductRow>())
                foreach (Control c in row.Controls) Require(row.ClientRectangle.Contains(c.Bounds), "Large fonts keep order controls inside rows");
            Capture(order, "13-large-font-order"); order.Close();
        }
        foreach (var item in before) fonts.Set(item.Key, item.Value);
        tabs.SelectedIndex = 0; Find<Button>(main, "ViewColumns4").PerformClick();
    }
    private static void CheckSuppliers(MainForm main)
    {
        var tabs = Find<TabControl>(main, "MainTabs"); tabs.SelectedIndex = 2; var list = Find<FlowLayoutPanel>(main, "Suppliers"); var refs = All(list).ToArray();
        foreach (var size in new[] { new Size(1000, 600), new Size(1500, 900), new Size(1280, 720) })
        { main.Size = size; main.Update(); Require(refs.SequenceEqual(All(list)) && !list.HorizontalScroll.Visible, "Supplier resize preserves fields without horizontal overflow"); }
        var password = Find<TextBox>(main, "LoginPassword_mega"); password.Focus(); password.Text = "ABC한글123!"; Require(password.Text == "ABC123!" && password.UseSystemPasswordChar, "ASCII and masking retained"); password.Clear();
        Capture(main, "08-suppliers-resize"); tabs.SelectedIndex = 0;
    }
    private static Task CheckRestart(MainForm main)
    {
        var data = Data(main); var grid = Find<ProductGrid>(main, "ProductList");
        Require(!grid.Controls.OfType<ProductCard>().Any(c => c.IsDraft), "Drafts never restored");
        Require(data.Products.Single(p => p.Id == 21).IsActive == false && !grid.Controls.OfType<ProductCard>().Any(c => c.Product?.Id == 21), "Tombstone survives and is hidden after restart");
        Require(grid.Controls.OfType<ProductCard>().Any(c => c.Product?.Id == registeredId), "Registered mock product restored");
        Require(data.Store.Preferences.Columns == 4 && grid.Columns == 4 && Enum.GetValues<TypographyKey>().All(k => Ui.Fonts!.Size(k) == Typography.DefaultSize(k)), "Columns and typography restored");
        Require(main.Bounds == new Rectangle(savedWindow!.X, savedWindow.Y, savedWindow.Width, savedWindow.Height), "Window position and size restored exactly");
        using var menu = Find<ProductCard>(grid, "Product_1").BuildMenu(); Require(menu.Items[1].Enabled, "Manual image restored");
        Capture(main, "12-restarted"); main.WindowState = FormWindowState.Maximized; return Task.CompletedTask;
    }
    private static Task CheckOffscreen(MainForm main)
    { Require(main.WindowState == FormWindowState.Maximized, "Maximized state restored"); main.WindowState = FormWindowState.Normal; Require(Screen.AllScreens.Any(s => s.WorkingArea.Contains(main.Bounds)) && main.Width >= main.MinimumSize.Width, "Offscreen/undersized bounds rejected safely"); return Task.CompletedTask; }
    private static class Native
    { [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp); }
}
