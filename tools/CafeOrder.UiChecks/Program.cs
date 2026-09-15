using CafeOrder;
using System.Drawing.Imaging;

internal static class Program
{
    private static string output = "";
    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/ui-checks" : args[0]);
        Directory.CreateDirectory(output);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using var main = new MainForm();
        Exception? failure = null;
        main.Shown += async (_, _) =>
        {
            try { await Check(main); }
            catch (Exception ex) { failure = ex; }
            finally { main.Close(); }
        };
        Application.Run(main);
        File.WriteAllText(Path.Combine(output, "result.txt"), failure?.ToString() ?? "PASS: x64, six tabs, 24 products, filters, AUTO/MANUAL cart, order dialog, completion removal, FAILED/UNKNOWN retention, preview and layout captures.");
        Console.WriteLine(failure?.ToString() ?? "All UI checks passed.");
        return failure == null ? 0 : 1;
    }
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls) { yield return c; foreach (var child in All(c)) yield return child; }
    }
    private static T Find<T>(Control root, string name) where T : Control => All(root).OfType<T>().Single(c => c.Name == name);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Capture(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }
    private static async Task Check(MainForm main)
    {
        Require(Environment.Is64BitProcess, "Must run x64");
        main.Size = new Size(1280, 720); await Task.Delay(150);
        var tabs = Find<TabControl>(main, "MainTabs"); Require(tabs.TabCount == 6, "Six tabs");
        var products = Find<FlowLayoutPanel>(main, "ProductList");
        Require(products.Controls.Count == 24, "24 initial sample products");
        Capture(main, "01-products-1280x720");
        var search = Find<TextBox>(main, "Search"); search.Text = "포모나"; await Task.Delay(100);
        Require(products.Controls.Count == 1 && products.Controls[0].Name == "Product_1", "Sample name filtering");
        Capture(main, "02-long-product-name");
        main.Size = new Size(1000, 720); await Task.Delay(80);
        Capture(main, "02-long-product-name-narrow");
        main.Size = new Size(1280, 720); search.Clear();
        Require(!Find<Button>(main, "Add_12").Enabled, "Sold out product cannot be added");
        foreach (var id in new[] { "naver", "coupang" })
            Require(!All(Find<TableLayoutPanel>(main, "Cart_" + id)).OfType<Button>().Any(b => b.Name.StartsWith("Plus_") || b.Name.StartsWith("Minus_")), "Manual has no quantity controls");
        Find<Button>(main, "Plus_1").PerformClick();
        Require(All(Find<TableLayoutPanel>(main, "Cart_mega")).OfType<Label>().Any(x => x.Text == "3"), "AUTO increment");
        Find<Button>(main, "Minus_1").PerformClick();
        var cart = Find<FlowLayoutPanel>(main, "CartList");
        cart.ScrollControlIntoView(Find<TableLayoutPanel>(main, "Cart_naver"));
        Capture(main, "02-manual-cart"); cart.AutoScrollPosition = Point.Empty;
        for (int i = 1; i < tabs.TabCount; i++)
        { tabs.SelectedIndex = i; await Task.Delay(80); Capture(main, $"0{i + 2}-tab-{i}"); }
        tabs.SelectedIndex = 3;
        Find<TextBox>(main, "ProductUrl").Text = "https://example.invalid/sample";
        Find<Button>(main, "CheckProduct").PerformClick(); await Task.Delay(80);
        Require(Find<Button>(main, "RegisterPreview").Visible, "Sample preview opens");
        Capture(main, "08-product-preview");
        tabs.SelectedIndex = 0;
        // Exercise the real OrderAll click while the nested modal message loop is active.
        using (var closeDialog = new System.Windows.Forms.Timer { Interval = 150 })
        {
            bool opened = false;
            closeDialog.Tick += (_, _) => { var f = Application.OpenForms.OfType<OrderForm>().FirstOrDefault(); if (f != null) { opened = true; Capture(f, "09-order-all"); f.Close(); } };
            closeDialog.Start(); Find<Button>(main, "OrderAll").PerformClick(); closeDialog.Stop();
            Require(opened, "OrderAll opens a separate form");
        }
        var data = new SampleData();
        using (var order = new OrderForm(data, data.Cart.ToArray()))
        {
            order.Show(main); await Task.Delay(100);
            var list = Find<FlowLayoutPanel>(order, "OrderCards");
            Require(list.Controls.Count == 6, "Three AUTO cards and three URL cards");
            Find<Button>(order, "Complete_1").PerformClick(); await Task.Delay(100);
            Require(All(order).OfType<Label>().Any(x => x.Text == "✓ 주문 완료"), "Visible success feedback");
            Capture(order, "10-order-completed-feedback"); await Task.Delay(700);
            Require(list.Controls.Count == 5 && data.Cart.All(x => x.Product.Supplier.Id != "mega"), "Only completed supplier removed from both views");
            Require(list.Controls.Cast<Control>().Any(c => c.Name.StartsWith("Order_piece")) && list.Controls.Cast<Control>().Any(c => c.Name.StartsWith("Order_nuldam")), "FAILED and UNKNOWN retained");
            Find<Button>(order, "Complete_6").PerformClick(); await Task.Delay(750);
            Require(data.Cart.Any(x => x.Product.Id == 10) && data.Cart.All(x => x.Product.Id != 6), "Manual completion only removes its URL");
            Capture(order, "11-order-failed-unknown-retained"); order.Close();
        }
        // Layout stress only: does not alter Windows display settings or claim real monitor DPI validation.
        foreach (float scale in new[] { 1.25f, 1.5f })
        {
            using var scaled = new MainForm(); scaled.Show();
            scaled.AutoScaleMode = AutoScaleMode.None;
            foreach (var container in All(scaled).OfType<ContainerControl>()) container.AutoScaleMode = AutoScaleMode.None;
            var fonts = All(scaled).Prepend(scaled).Select(c => (Control: c, Size: c.Font.Size, Style: c.Font.Style)).ToArray();
            foreach (var entry in fonts) entry.Control.SuspendLayout();
            scaled.Scale(new SizeF(scale, scale));
            foreach (var entry in fonts)
                entry.Control.Font = new Font("Malgun Gothic", entry.Size * scale, entry.Style);
            foreach (var entry in fonts.Reverse()) entry.Control.ResumeLayout(true);
            scaled.MinimumSize = new Size(1000, 600); scaled.Size = new Size(1280, 720);
            await Task.Delay(100);
            var allButton = Find<Button>(scaled, "OrderAll");
            Capture(scaled, $"12-layout-scale-{scale:0.00}");
            var bounds = new List<string>();
            for (Control? c = allButton; c != null; c = c.Parent) bounds.Add($"{c.GetType().Name} {c.Name} bounds={c.Bounds} min={c.MinimumSize} max={c.MaximumSize} dock={c.Dock}");
            File.WriteAllLines(Path.Combine(output, "layout-bounds.txt"), bounds);
            Require(scaled.ClientRectangle.Contains(scaled.PointToClient(allButton.PointToScreen(new Point(allButton.Width / 2, allButton.Height / 2)))), "OrderAll remains within enlarged window");
            Find<TextBox>(scaled, "Search").Text = "포모나"; await Task.Delay(80);
            Capture(scaled, $"13-long-name-scale-{scale:0.00}"); scaled.Close();
        }
    }
}
