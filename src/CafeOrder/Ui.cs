namespace CafeOrder;

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(36, 49, 47);
    public static readonly Color Accent = Color.FromArgb(37, 103, 83);
    public static readonly Color Background = Color.FromArgb(244, 246, 243);
    public static readonly Color Danger = Color.FromArgb(173, 53, 53);

    public static TextBox CopyText(string text, string name, bool heading = false) => new ScrollCopyTextBox()
    {
        Name = name, Text = text, ReadOnly = true, BorderStyle = BorderStyle.None,
        Multiline = true, WordWrap = true, ScrollBars = ScrollBars.None, BackColor = Color.White,
        ForeColor = Ink, Font = new Font("Malgun Gothic", heading ? 12 : 11.5f, heading ? FontStyle.Bold : FontStyle.Regular),
        ShortcutsEnabled = true, TabStop = true
    };

    public static Label Text(string text, bool heading = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = Ink, Margin = new Padding(4, 5, 4, 5),
        Font = new Font("Malgun Gothic", heading ? 13 : 11.5f, heading ? FontStyle.Bold : FontStyle.Regular),
        Dock = DockStyle.Top, UseMnemonic = false
    };
    public static Button Button(string text, Action action, bool primary = false, string? name = null)
    {
        var b = new ActionButton { Text = text, Name = name ?? text, AutoSize = true,
            MinimumSize = new Size(44, 40), Padding = new Padding(8, 4, 8, 4),
            Margin = new Padding(4), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
            Cursor = Cursors.Hand, UseMnemonic = false };
        b.FlatAppearance.BorderColor = Color.FromArgb(201, 211, 205);
        b.Click += (_, _) => action();
        return b;
    }
    public static ComboBox Combo(IEnumerable<string> items, string name)
    {
        var c = new ComboBox { Name = name, DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill, Margin = new Padding(4, 6, 4, 6), IntegralHeight = true };
        c.Items.AddRange(items.Cast<object>().ToArray()); c.SelectedIndex = 0; return c;
    }
    public static TableLayoutPanel Column(params Control[] controls)
    {
        var panel = new CardTable { ColumnCount = 1, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            BackColor = Color.White, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 10) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.SizeChanged += (_, _) =>
        {
            // A finite wrapping width keeps the original long product name inside its card.
            foreach (var label in panel.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(60, panel.ClientSize.Width - panel.Padding.Horizontal - label.Margin.Horizontal), 0);
        };
        foreach (var control in controls) { panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); panel.Controls.Add(control, 0, panel.RowCount++); }
        return panel;
    }
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0) };
        row.Controls.AddRange(controls); return row;
    }
    public static FlowLayoutPanel List(string name)
    {
        var list = new BufferedFlowPanel { Name = name, Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Background, Padding = new Padding(8) };
        list.ClientSizeChanged += (_, _) => Fit(list);
        list.ControlAdded += (_, e) => { if (e.Control != null) FitItem(list, e.Control); };
        return list;
    }
    public static void Fit(FlowLayoutPanel list)
    {
        if (list is BufferedFlowPanel { Fitting: true }) return;
        if (list is BufferedFlowPanel buffered) buffered.Fitting = true;
        list.SuspendLayout();
        try { foreach (Control c in list.Controls) FitItem(list, c); }
        finally
        {
            // Complete the pending flow/scroll-extent layout on resize; do not mask it with Refresh.
            list.ResumeLayout(true);
            if (list is BufferedFlowPanel finished) finished.Fitting = false;
        }
    }
    private static void FitItem(FlowLayoutPanel list, Control c)
    {
        int width = Math.Max(120, list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        if (c.Width == width && c.MaximumSize.Width == width) return;
        c.MinimumSize = Size.Empty;
        c.MaximumSize = new Size(width, 0);
        c.Width = width;
        if (c.AutoSize) c.MinimumSize = new Size(width, 0);
    }
    public static void Clear(Control parent)
    {
        foreach (var c in parent.Controls.Cast<Control>().ToArray()) c.Dispose();
    }
    public static Control Placeholder(string category)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 110,
            MinimumSize = new Size(94, 110), Margin = new Padding(4, 4, 12, 4) };
        panel.Controls.Add(new Label { Text = $"◇\n{category}\n샘플 이미지", TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(232, 238, 230), ForeColor = Accent });
        return panel;
    }
    public static void Notice(IWin32Window owner, string text)
        => MessageBox.Show(owner, text, "CafeOrder · UI 목업", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static Label SectionHeading(string text)
    {
        var label = Text(text, true); label.AutoSize = false; label.Height = 36;
        label.Padding = new Padding(8, 4, 0, 0); label.Margin = Padding.Empty; return label;
    }
    public static Control SellerHeading(Supplier supplier)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new PictureBox { Image = SampleImages.Seller(supplier), SizeMode = PictureBoxSizeMode.Zoom, Width = 30, Height = 30, Margin = new Padding(3) }, 0, 0);
        row.Controls.Add(Text(supplier.Name, true), 1, 0); return row;
    }
    public static int ActionHeight(Control c) => Math.Max(40, c.Font.Height * 2);
}

internal class BufferedPanel : Panel
{
    public BufferedPanel() => DoubleBuffered = true;
}
internal sealed class BufferedFlowPanel : FlowLayoutPanel
{
    internal bool Fitting;
    private bool arranging;
    public BufferedFlowPanel() => DoubleBuffered = true;
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (arranging) return;
        arranging = true;
        try { Ui.Fit(this); base.OnLayout(e); AdjustFormScrollbars(true); }
        finally { arranging = false; }
    }
}

internal class SoftPanel : BufferedPanel
{
    public SoftPanel() { BackColor = Color.White; Padding = new Padding(6); }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); UiBorder.Draw(e.Graphics, ClientRectangle); }
}
internal sealed class CardTable : TableLayoutPanel
{
    public CardTable() => DoubleBuffered = true;
    public override Size GetPreferredSize(Size proposedSize)
    {
        if (ColumnCount != 1) return base.GetPreferredSize(proposedSize);
        int width = MaximumSize.Width > 0 ? MaximumSize.Width : proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, 200);
        int height = Padding.Vertical;
        foreach (Control child in Controls)
        {
            int inner = Math.Max(40, width - Padding.Horizontal - child.Margin.Horizontal);
            height += child.GetPreferredSize(new Size(inner, 0)).Height + child.Margin.Vertical;
        }
        return new Size(width, height);
    }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); UiBorder.Draw(e.Graphics, ClientRectangle); }
}
internal static class UiBorder
{
    public static void Draw(Graphics graphics, Rectangle rect)
    {
        if (rect.Width < 2 || rect.Height < 2) return;
        using var pen = new Pen(Color.FromArgb(219, 226, 219));
        graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
    }
}
internal sealed class ScrollCopyTextBox : TextBox
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x020A) // Route the wheel to the list instead of scrolling the three-line text field.
        {
            for (Control? parent = Parent; parent != null; parent = parent.Parent)
                if (parent is ScrollableControl { AutoScroll: true })
                { NativeUi.SendMessage(parent.Handle, m.Msg, m.WParam, m.LParam); return; }
        }
        base.WndProc(ref m);
    }
}

// Disabled shortage actions retain an explicit red/white, readable treatment.
internal sealed class ActionButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, FlatAppearance.BorderColor, ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}
