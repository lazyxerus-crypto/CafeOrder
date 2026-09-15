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
        foreach (var title in new[] { "상품", "주문기록", "사이트관리", "상품추가", "로그", "설정" })
            tabs.TabPages.Add(new TabPage(title) { BackColor = Ui.Background, Padding = new Padding(8) });
        Controls.Add(tabs);
        ResumeLayout(false);
    }
}
