namespace CafeOrder;

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(36, 49, 47);
    public static readonly Color Accent = Color.FromArgb(37, 103, 83);
    public static readonly Color Background = Color.FromArgb(244, 246, 243);

    public static Label Text(string text, bool heading = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = Ink, Margin = new Padding(4, 5, 4, 5),
        Font = new Font("Malgun Gothic", heading ? 13 : 11.5f, heading ? FontStyle.Bold : FontStyle.Regular),
        Dock = DockStyle.Top, UseMnemonic = false
    };
    public static Button Button(string text, Action action, bool primary = false, string? name = null)
    {
        var b = new Button { Text = text, Name = name ?? text, AutoSize = true,
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
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true,
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
        var row = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0) };
        row.Controls.AddRange(controls); return row;
    }
    public static FlowLayoutPanel List(string name)
    {
        var list = new FlowLayoutPanel { Name = name, Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Background, Padding = new Padding(8) };
        list.ClientSizeChanged += (_, _) => Fit(list);
        list.ControlAdded += (_, _) => Fit(list);
        return list;
    }
    public static void Fit(FlowLayoutPanel list)
    {
        int width = Math.Max(120, list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        foreach (Control c in list.Controls)
        {
            c.Width = width;
            c.MinimumSize = new Size(width, 0);
            c.MaximumSize = new Size(width, 0);
        }
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
}
