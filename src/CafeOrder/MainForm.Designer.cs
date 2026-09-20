#nullable enable
namespace CafeOrder;

partial class MainForm
{
    private System.ComponentModel.IContainer? components;
    private TabControl tabs = null!;
    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
        if (disposing) typography?.Dispose();
    }
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        tabs = new TabControl();
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Malgun Gothic", 12F);
        Text = "CafeOrder · UI 목업";
        ClientSize = new Size(1264, 681);
        MinimumSize = new Size(1000, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Ui.Background;
        tabs.Name = "MainTabs";
        tabs.Dock = DockStyle.Fill;
        tabs.Padding = new Point(20, 10);
        tabs.ShowToolTips = true;
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == tabs.SelectedIndex;
            using var brush = new SolidBrush(selected ? Ui.Background : SystemColors.Control);
            e.Graphics.FillRectangle(brush, e.Bounds);
            using var font = new Font(tabs.Font, selected ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, font, e.Bounds, selected ? Ui.Accent : Ui.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (selected) e.Graphics.FillRectangle(Brushes.SeaGreen, e.Bounds.Left + 8, e.Bounds.Bottom - 3, e.Bounds.Width - 16, 3);
        };
        foreach (var title in new[] { "상품", "주문기록", "판매처관리", "로그", "설정" })
            tabs.TabPages.Add(new TabPage(title) { ToolTipText = title, BackColor = Ui.Background, Padding = new Padding(8) });
        Controls.Add(tabs);
        ResumeLayout(false);
    }
}
