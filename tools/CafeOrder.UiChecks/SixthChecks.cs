using CafeOrder;

internal static partial class Program
{
    private static async Task CheckSix(MainForm main)
    {
        var data = Data(main); var cart = Find<FlowLayoutPanel>(main, "CartList"); var grid = Find<ProductGrid>(main, "ProductList");
        var search = Find<TextBox>(main, "Search"); var oldCart = data.Cart.ToArray();
        Require(!All(main).Any(c => c.Name == "ProductColumnsSetting"), "No columns selector in settings");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            foreach (var line in data.Cart.ToArray()) data.Remove(line);
            foreach (int id in new[] { 7, 10, 6, 1, 7, 9, 10 })
            {
                var unchanged = All(cart).OfType<CartProductRow>().ToDictionary(r => r.Line.Product.Id);
                cart.AutoScrollPosition = new Point(0, 700);
                var product = data.Products.Single(p => p.Id == id); data.AddToCart(product); Pump();
                var seller = cart.Controls[0]; var row = Find<CartProductRow>(seller, $"CartRow_{id}");
                Require(data.Cart[0].Product == product && seller.Name == $"Cart_{product.Supplier.Id}" && seller.Height > row.Bottom, "Added row and seller have actual allocated height");
                Require(All(seller).OfType<CartProductRow>().All(r => r.Top >= row.Top), "Latest row first");
                Require(cart.RectangleToScreen(cart.ClientRectangle).Contains(row.RectangleToScreen(row.ClientRectangle)), "Newly added row actually visible after scrolling");
                Require(unchanged.All(item => ReferenceEquals(Find<CartProductRow>(cart, $"CartRow_{item.Key}"), item.Value)), "Adding reuses all existing rows");
                Require(row.BackColor == Color.FromArgb(221, 238, 225) && seller.BackColor == Color.White, "Highlight belongs only to new row");
            }
        }
        Capture(main, "15-cart-repro-fixed");
        var plusLine = data.Cart.Single(l => l.Product.Id == 7); var before = All(cart).ToArray(); var orderBefore = data.Cart.ToArray(); cart.AutoScrollPosition = new Point(0, 60); var beforeScroll = cart.AutoScrollPosition;
        data.ChangeQuantity(plusLine, 1); data.ChangeQuantity(plusLine, -1);
        Require(before.SequenceEqual(All(cart)) && orderBefore.SequenceEqual(data.Cart) && cart.AutoScrollPosition == beforeScroll, "Plus/minus retain rows, seller order and scroll");
        foreach (int columns in new[] { 3, 4, 5 })
        {
            Find<Button>(main, $"ViewColumns{columns}").PerformClick(); search.Clear(); Pump();
            int width = Find<ProductCard>(grid, "Product_7").Width;
            Capture(Find<ProductCard>(grid, "Product_1"), $"22-price-{columns}");
            search.Text = "카페 블렌드 원두"; Pump();
            Require(Find<ProductCard>(grid, "Product_7").Width == width, "One/many products have identical width with scrollbar reservation");
            foreach (var size in new[] { new Size(1000, 600), new Size(1280, 720), new Size(1500, 900) })
            {
                main.Size = size; Pump(); var filters = Find<TableLayoutPanel>(main, "Filters"); var left = Find<Panel>(main, "ProductRegion"); var right = Find<Panel>(main, "CartRegion");
                Require(filters.Left == left.Left && filters.Right == left.Right && filters.Right < right.Left, "Toolbar aligns with product column on resize");
                foreach (var name in new[] { "CategoryFilter", "SupplierFilter", "Search", "RefreshProducts", "AddProduct", "ExportProducts", "ImportProducts", "ViewColumns3", "ViewColumns4", "ViewColumns5" })
                { var c = Find<Control>(main, name); Require(filters.RectangleToScreen(filters.ClientRectangle).Contains(c.RectangleToScreen(c.ClientRectangle)), "Toolbar child within left region: " + name); }
            }
            main.Size = new Size(1280, 720); search.Clear();
        }
        Find<Button>(main, "ViewColumns3").PerformClick();
        var sold = Find<ProductCard>(grid, "Product_17");
        search.Text = sold.Product!.Name; Capture(sold, "16-sold-out");
        var soldOrder = grid.Items.ToArray(); Mouse(sold, MouseButtons.Left, sold.ImageBounds.Location + new Size(5, 5)); Pump();
        Require(!sold.Product.Available && soldOrder.SequenceEqual(grid.Items), "Still sold out without sorting");
        var restock = Find<ProductCard>(grid, "Product_12"); search.Text = restock.Product!.Name;
        Mouse(restock, MouseButtons.Left, new Point(15, 15)); Pump(); Require(restock.Product.Available, "Sample recheck returns stocked card"); Capture(restock, "17-restocked");
        search.Clear();
        var sample = Find<ProductCard>(grid, "Product_7"); var cardOrder = grid.Items.ToArray(); grid.AutoScrollPosition = new Point(0, 80); var productScroll = grid.AutoScrollPosition;
        data.SetActive(sample.Product!, false); Require(sample.Visible && cardOrder.SequenceEqual(grid.Items) && grid.AutoScrollPosition == productScroll, "Soft delete stays in place until explicit refresh");
        Find<Button>(main, "RefreshProducts").PerformClick(); Require(!sample.Visible && grid.AutoScrollPosition == Point.Empty, "Refresh hides deletion and scrolls top");
        data.SetActive(sample.Product!, true); Find<Button>(main, "RefreshProducts").PerformClick();
        string previousUrl = sample.Product!.Url; sample.Product.Url = ""; using (var menu = sample.BuildMenu()) Require(!menu.Items[3].Enabled, "Absent URL disables link"); sample.Product.Url = previousUrl;
        var launch = ProductCard.LinkStartInfo(sample.Product.Url); Require(launch.UseShellExecute && launch.FileName == sample.Product.Url && launch.Arguments == "", "URL delegates to Windows default handler without browser-specific arguments");
        using (var menu = sample.BuildMenu()) Require(menu.Items[3].Enabled && !menu.Items[1].Enabled, "URL enabled, absent manual image disabled");
        Find<Button>(main, "AddProduct").PerformClick(); var draft = grid.Items[0]; int draftCount = grid.Items.Count;
        using (var menu = draft.BuildMenu())
        { Require(menu.Items[0].Enabled && new[] { 1, 3, 5, 6, 7 }.All(i => !menu.Items[i].Enabled), "Draft shared menu disabled states"); menu.Items[0].PerformClick(); }
        Require(draft.IsDisposed && grid.Items.Count == draftCount - 1 && !grid.Controls.Contains(draft), "Draft delete removes temporary card");
        search.Text = "카페 블렌드 원두"; sample.Focus();
        var hint = (ToolTip)typeof(ProductCard).GetField("hint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sample)!;
        int pops = 0; hint.Popup += (_, _) => pops++;
        foreach (var point in new[] { new Point(15, 15), sample.ImageBounds.Location + new Size(5, 5), sample.NameBounds.Location + new Size(5, 5), sample.PriceBounds.Location + new Size(5, 5) })
        {
            Mouse(sample, MouseButtons.Left, point);
            int previousPops = pops; await Task.Delay(650);
            Require(pops > previousPops, "Native tooltip actually opens after click");
            Require(sample.TooltipAt(point) == (sample.PriceBounds.Contains(point) ? sample.Product.PriceText : sample.Product.Name), "Full tooltip text survives click/focus on each surface");
        }
        Mouse(sample, MouseButtons.Right, new Point(15, 15));
        var popup = (ContextMenuStrip?)typeof(ProductCard).GetField("menu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sample);
        popup!.Close(); Require(sample.TooltipAt(new Point(15, 15)) == sample.Product.Name, "Tooltip remains available after menu closes");
        int closedPops = pops; await Task.Delay(650); Require(pops > closedPops, "Native tooltip opens after context menu closes");
        search.Clear();
        using (var order = new OrderForm(data, data.Cart.ToArray()))
        {
            order.Show(main); Pump();
            foreach (var size in new[] { new Size(650, 520), new Size(900, 700) })
            {
                order.Size = size; Pump();
                foreach (var row in All(order).OfType<CartProductRow>()) CheckX(row);
                Capture(order, $"18-order-{size.Width}");
            }
            var orderList = Find<FlowLayoutPanel>(order, "OrderCards"); var keep = data.Cart.Single(l => l.Product.Id == 7); var keepCard = Find<Control>(order, "Order_mega_7"); int manyWidth = keepCard.Width;
            foreach (var line in data.Cart.Where(l => l != keep).ToArray()) data.Remove(line); Pump();
            Require(keepCard.Width == manyWidth, "Order card width stays stable with only one product");
            CheckX(Find<CartProductRow>(order, "CartRow_7")); Capture(order, "19-order-single"); order.Close();
        }
        int singleCartWidth = cart.Controls[0].Width; data.AddToCart(data.Products.Single(p => p.Id == 10)); Pump();
        Require(Find<Control>(cart, "Cart_mega").Width == singleCartWidth, "Cart width unchanged as scrollbar appears");
        foreach (var line in data.Cart.ToArray()) data.Remove(line); data.Cart.AddRange(oldCart); data.Notify();
        await Task.Delay(240);
    }
    private static void CheckX(CartProductRow row)
    {
        var remove = row.Controls.OfType<Button>().Single(b => b.Name.StartsWith("Remove_"));
        Require(row.ClientRectangle.Contains(remove.Bounds), "X fits parent");
        Require(row.Controls.Cast<Control>().Where(c => c != remove && c.Visible).All(c => !c.Bounds.IntersectsWith(remove.Bounds)), "X has no sibling overlap");
        using var bitmap = new Bitmap(row.Width, row.Height); row.DrawToBitmap(bitmap, row.ClientRectangle);
        var bounds = remove.Bounds; var color = remove.FlatAppearance.BorderColor.ToArgb();
        foreach (var point in new[] { new Point(bounds.Left, bounds.Top + bounds.Height / 2), new Point(bounds.Right - 1, bounds.Top + bounds.Height / 2), new Point(bounds.Left + bounds.Width / 2, bounds.Top), new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 1) })
            Require(bitmap.GetPixel(point.X, point.Y).ToArgb() == color, $"Rendered X border pixel exists: {row.Name} {point}");
        Capture(row, "20-x-" + row.Name);
    }
    private static void CheckReset(MainForm main)
    {
        var tabs = Find<TabControl>(main, "MainTabs"); tabs.SelectedIndex = 4; Find<Button>(main, "OpenTypography").PerformClick();
        var window = Application.OpenForms.Cast<Form>().Single(f => f.Name == "TypographySettings");
        Find<Button>(window, "ResetTypography").PerformClick();
        var saved = new Typography(new LocalState(statePath));
        foreach (var key in Enum.GetValues<TypographyKey>()) Require(Ui.Fonts!.Size(key) == Typography.DefaultSize(key) && saved.Size(key) == Typography.DefaultSize(key) && Find<NumericUpDown>(window, "Font_" + key).Value == (decimal)Typography.DefaultSize(key), "Every typography key reset/applied/saved");
        Require(Find<TextBox>(main, "CartName_1").Font.Size == Typography.DefaultSize(TypographyKey.CartProductName), "Reset updates main immediately");
        Find<Button>(window, "CopyTypography").PerformClick(); Require(Clipboard.GetText() == Ui.Fonts!.CopyText(), "Copy all works after reset");
        Capture(window, "21-reset-defaults"); saved.Dispose(); window.Close(); tabs.SelectedIndex = 0;
    }
    private static Task ReproSix(MainForm main)
    {
        main.Size = new Size(1280, 720);
        var data = Data(main); var cart = Find<FlowLayoutPanel>(main, "CartList");
        foreach (var line in data.Cart.ToArray()) data.Remove(line);
        foreach (int id in new[] { 7, 10 })
        {
            data.AddToCart(data.Products.Single(p => p.Id == id)); Pump();
            results.Add($"After add {id}: data={string.Join(',', data.Cart.Select(l => l.Product.Id))}; client={cart.ClientRectangle}; scroll={cart.AutoScrollPosition}");
            foreach (Control seller in cart.Controls)
            {
                results.Add($"{seller.Name}: {seller.Bounds}, visible={seller.Visible}, index={cart.Controls.GetChildIndex(seller)}");
                foreach (var row in All(seller).OfType<CartProductRow>()) results.Add($" {row.Name}: {row.Bounds}, visible={row.Visible}, parent={row.Parent?.Name}");
            }
            Capture(main, $"repro-cart-{id}");
        }
        using var order = new OrderForm(data, data.Cart.ToArray()); order.Show(main); Pump();
        foreach (var row in All(order).OfType<CartProductRow>())
        {
            var remove = row.Controls.OfType<Button>().Single(b => b.Name.StartsWith("Remove_"));
            foreach (Control child in row.Controls) if (child != remove && child.Bounds.IntersectsWith(remove.Bounds)) results.Add($"X overlap: {row.Name} {remove.Bounds} with {child.Name} {child.Bounds}; z={row.Controls.GetChildIndex(child)} vs X={row.Controls.GetChildIndex(remove)}");
            Capture(row, "repro-order-" + row.Name);
        }
        Capture(order, "repro-order"); order.Close(); return Task.CompletedTask;
    }
}
