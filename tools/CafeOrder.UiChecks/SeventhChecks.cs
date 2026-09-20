using CafeOrder;

internal static partial class Program
{
    private static Task CheckSeven(MainForm main)
    {
        var data = Data(main); var grid = Find<ProductGrid>(main, "ProductList"); var refs = grid.Controls.Cast<Control>().ToArray();
        var previousFonts = Enum.GetValues<TypographyKey>().ToDictionary(k => k, Ui.Fonts!.Size);
        foreach (int font in new[] { 12, 24 })
        {
            foreach (var key in previousFonts.Keys) Ui.Fonts!.Set(key, font);
            foreach (var size in new[] { new Size(1280, 720), new Size(1000, 600), new Size(1500, 900) })
            {
                main.Size = size; Pump();
                foreach (int columns in new[] { 4, 5, 3, 4, 3, 5 })
                {
                    main.Update(); foreach (var c in grid.Items) c.Update(); int previous = grid.Columns;
                    Find<Button>(main, $"ViewColumns{columns}").PerformClick();
                    var first = grid.Items[0];
                    if (previous != columns)
                    {
                        Require(GetUpdateRect(first.Handle, out var invalid, false), "Column change requests repaint before any tab switch or capture");
                        Require(invalid.Left == 0 && invalid.Top == 0 && invalid.Right == first.Width, "Shrinking redraws full card width");
                    }
                    foreach (var card in grid.Items)
                    {
                        Require(card.ImageBounds.Width == card.Width - 20 && card.ImageBounds.Width == card.ImageBounds.Height, "Immediate square image geometry");
                        Require(card.CategoryBounds.Bottom <= card.ImageBounds.Top && card.ImageBounds.Bottom < card.NameBounds.Top && card.NameBounds.Bottom < card.PriceBounds.Top && card.PriceBounds.Bottom <= card.Height - 10, "Immediate non-overlapping card regions");
                        Require(card.CategoryBounds.Height == Math.Max(30, Ui.Fonts!.Font(TypographyKey.Category, FontStyle.Bold).Height + 2), "Category one-line height independent of width");
                    }
                    var left = Find<Panel>(main, "ProductRegion"); var right = Find<Panel>(main, "CartRegion"); var filters = Find<TableLayoutPanel>(main, "Filters"); var management = Find<TableLayoutPanel>(main, "Management");
                    Require(filters.Left == left.Left && filters.Right == left.Right && management.Left == right.Left && management.Right == right.Right && filters.Padding == management.Padding, "Top and body share column edges/gap/padding");
                    foreach (var names in new[] { new[] { "RefreshProducts", "AddProduct", "ExportProducts", "ImportProducts" }, new[] { "ViewColumns3", "ViewColumns4", "ViewColumns5" } })
                    {
                        var bounds = names.Select(n => { var c = Find<Control>(main, n); return c.RectangleToScreen(c.ClientRectangle); }).ToArray();
                        Require(bounds.Select(b => b.Top).Distinct().Count() == 1 && bounds.All(b => management.RectangleToScreen(management.ClientRectangle).Contains(b)), "Management/view buttons stay on one line inside right column");
                        Require(bounds.Zip(bounds.Skip(1)).All(pair => pair.First.Right < pair.Second.Left), "Management controls do not overlap");
                    }
                    if (size.Width == 1280) Capture(main, $"seven-{font}pt-{columns}columns");
                }
            }
        }
        foreach (var item in previousFonts) Ui.Fonts!.Set(item.Key, item.Value);
        main.Size = new Size(1280, 720); Find<Button>(main, "ViewColumns3").PerformClick();
        Require(refs.SequenceEqual(grid.Controls.Cast<Control>()), "Column/size/font changes reuse every card");
        var product = data.Products.Single(p => p.Id == 7); var sample = Find<ProductCard>(grid, "Product_7"); string category = product.Category;
        foreach (string selected in new[] { "유제품", "베이스/농축액", category })
        {
            data.SetCategory(product, selected);
            using var menu = sample.BuildMenu(); var items = ((ToolStripMenuItem)menu.Items[2]).DropDownItems.Cast<ToolStripMenuItem>().ToArray();
            Require(items.Count(i => i.Checked) == 1 && items.Single(i => i.Checked).Text == selected, "Exactly current category checked on each opening");
            Require(sample.TooltipAt(sample.CategoryBounds.Location + new Size(2, 2)) == selected, "Category tooltip preserves original");
        }
        var snapshot = data.Cart.Select(l => (l.Product.Id, l.Quantity)).ToArray(); var order = grid.Items.ToArray(); var scroll = grid.AutoScrollPosition;
        var launches = new List<System.Diagnostics.ProcessStartInfo>(); var originalLauncher = SellerLinks.Launch;
        try
        {
            SellerLinks.Launch = launches.Add;
            Require(SellerLinks.Homepages.Values.All(v => v == null), "No guessed real homepage configured");
            Mouse(sample, MouseButtons.Left, new Point(20, 20)); Require(launches.Count == 0, "Unset homepage does nothing");
            Require(sample.TooltipAt(new Point(20, 20)).Contains("홈페이지 미설정"), "Unset homepage tooltip");
            SellerLinks.Homepages["mega"] = "https://home.example.invalid/"; // Test-only fixture; never launches a browser.
            Mouse(sample, MouseButtons.Left, new Point(20, 20)); Require(launches.Count == 1 && launches[0].UseShellExecute && launches[0].FileName == "https://home.example.invalid/", "Product icon dispatches one default-browser homepage command");
            Mouse(sample, MouseButtons.Right, new Point(20, 20));
            var popup = (ContextMenuStrip)typeof(ProductCard).GetField("menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sample)!;
            Require(popup.Items.Count == 8, "Icon right click shares card menu"); popup.Close();
            var naver = data.Products.Single(p => p.Id == 10); string oldUrl = naver.Url;
            naver.Url = "https://smartstore.naver.com/placer_mall/products/7419367541?query=test";
            Require(SellerLinks.ProductHome(naver) == "https://smartstore.naver.com/placer_mall", "Exact Naver store derived from known product path");
            naver.Url = "https://smartstore.naver.com.evil.invalid/placer_mall/products/7419367541"; Require(SellerLinks.ProductHome(naver) == null, "Lookalike host rejected");
            naver.Url = "https://smartstore.naver.com/products/7419367541"; Require(SellerLinks.ProductHome(naver) == null, "Ambiguous Naver path disabled"); naver.Url = oldUrl;
            var tabs = Find<TabControl>(main, "MainTabs");
            foreach (int tab in new[] { 0, 1, 2 })
            {
                tabs.SelectedIndex = tab;
                foreach (var icon in All(tabs.TabPages[tab]).OfType<SellerIcon>())
                {
                    int before = launches.Count;
                    typeof(SellerIcon).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(icon, [new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)]);
                    Require(launches.Count == before + (icon.Name == "SellerIcon_mega" ? 1 : 0), "Shared header icon obeys per-seller configuration");
                }
            }
            tabs.SelectedIndex = 0;
            using var dialog = new OrderForm(data, data.Cart.ToArray()); dialog.Show(main); Pump();
            foreach (var icon in All(dialog).OfType<SellerIcon>())
            {
                int before = launches.Count;
                typeof(SellerIcon).GetMethod("OnMouseUp", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(icon, [new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0)]);
                Require(launches.Count == before + (icon.Name == "SellerIcon_mega" ? 1 : 0), "Order icon uses shared configuration");
            }
            dialog.Close();
            Require(snapshot.SequenceEqual(data.Cart.Select(l => (l.Product.Id, l.Quantity))) && order.SequenceEqual(grid.Items) && scroll == grid.AutoScrollPosition, "Homepage clicks preserve quantity/order/scroll");
        }
        finally { SellerLinks.Launch = originalLauncher; SellerLinks.Homepages["mega"] = null; }
        return Task.CompletedTask;
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct UpdateRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr hwnd, out UpdateRect rect, bool erase);
    private static Task ReproSeven(MainForm main)
    {
        main.Size = new Size(1280, 720); var grid = Find<ProductGrid>(main, "ProductList");
        foreach (int columns in new[] { 3, 4, 5, 3, 5, 4 })
        {
            main.Update(); foreach (var c in grid.Items) c.Update();
            Find<Button>(main, $"ViewColumns{columns}").PerformClick();
            var card = grid.Items.First();
            results.Add($"{columns}: card={card.Bounds}; image={card.ImageBounds}; name={card.NameBounds}; price={card.PriceBounds}");
            bool invalid = GetUpdateRect(card.Handle, out var rect, false); results.Add($"  repaint={invalid}, region={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
            Capture(main, $"columns-{columns}");
        }
        return Task.CompletedTask;
    }
}
