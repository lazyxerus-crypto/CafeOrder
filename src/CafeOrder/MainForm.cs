namespace CafeOrder;

public partial class MainForm : Form
{
    private readonly SampleData sample = new();
    public MainForm()
    {
        InitializeComponent();
        tabs.TabPages[0].Controls.Add(new ProductsView(sample));
        tabs.TabPages[1].Controls.Add(OtherPages.History());
        tabs.TabPages[2].Controls.Add(OtherPages.Suppliers(sample));
        tabs.TabPages[3].Controls.Add(OtherPages.AddProduct(sample));
        tabs.TabPages[4].Controls.Add(OtherPages.Logs());
        tabs.TabPages[5].Controls.Add(OtherPages.Settings());
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitWorkingArea();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitWorkingArea();
    }
    private void FitWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1000, area.Width), Math.Min(600, area.Height));
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
}
