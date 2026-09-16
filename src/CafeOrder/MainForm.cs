namespace CafeOrder;

public partial class MainForm : Form
{
    private readonly SampleData sample = new();
    private readonly ToastHost toast = new();
    public MainForm()
    {
        InitializeComponent();
        var products = new ProductsView(sample);
        tabs.TabPages[0].Controls.Add(products);
        toast.AnchorRegion = products.ToastRegion;
        products.ToastRegion.SizeChanged += (_, _) => toast.Reposition();
        products.ToastRegion.LocationChanged += (_, _) => toast.Reposition();
        tabs.TabPages[1].Controls.Add(OtherPages.History(sample));
        tabs.TabPages[2].Controls.Add(OtherPages.Suppliers(sample));
        tabs.TabPages[3].Controls.Add(OtherPages.AddProduct(sample));
        tabs.TabPages[4].Controls.Add(OtherPages.Logs(sample));
        tabs.TabPages[5].Controls.Add(OtherPages.Settings());
        sample.ToastRequested += ShowToast;
        LocationChanged += (_, _) => toast.Reposition(); SizeChanged += (_, _) => toast.Reposition();
    }
    private void ShowToast(string message, string? key)
    {
        var owner = Application.OpenForms.OfType<OrderForm>().LastOrDefault(f => f.Visible) as Form ?? this;
        toast.Notify(owner, message, key);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { sample.ToastRequested -= ShowToast; toast.Dispose(); base.OnFormClosed(e); }
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
