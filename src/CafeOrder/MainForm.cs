namespace CafeOrder;

public partial class MainForm : Form
{
    private readonly SampleData sample;
    private readonly Typography typography;
    private readonly SupplierSessionManager sessions;
    private readonly bool checkLoginOnShown;
    private bool sessionsClosed;
    private TypographyForm? typographyWindow;
    public MainForm() : this(null) { }
    public MainForm(string? stateDirectory)
    {
        sessions = new SupplierSessionManager(stateDirectory == null ? null : Path.Combine(stateDirectory, "browser-profiles"));
        checkLoginOnShown = stateDirectory == null;
        sample = new SampleData(new LocalState(stateDirectory));
        typography = new Typography(sample.Store); Ui.Fonts = typography;
        InitializeComponent(); Ui.Role(this, TypographyKey.General); Ui.Role(tabs, TypographyKey.Tab);
        tabs.FontChanged += (_, _) => tabs.ItemSize = new Size(0, tabs.Font.Height + 22);
        var products = new ProductsView(sample, sessions);
        tabs.TabPages[0].Controls.Add(products);
        tabs.TabPages[1].Controls.Add(OtherPages.History(sample));
        tabs.TabPages[2].Controls.Add(OtherPages.Suppliers(sample, sessions, this));
        tabs.TabPages[3].Controls.Add(OtherPages.Logs(sample));
        tabs.TabPages[4].Controls.Add(OtherPages.Settings(sample, OpenTypography));
        typography.Changed += () =>
        {
            products.ApplyColumns();
            foreach (var card in Descendants(this).OfType<SupplierSettingsCard>()) card.Remeasure();
            foreach (var control in Descendants(this).Reverse()) control.PerformLayout();
            foreach (var form in Application.OpenForms.OfType<OrderForm>()) foreach (var control in Descendants(form).Reverse()) control.PerformLayout();
            Invalidate(true);
        };
    }
    private static IEnumerable<Control> Descendants(Control parent)
    { foreach (Control c in parent.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; } }
    private void OpenTypography()
    {
        if (typographyWindow is { IsDisposed: false }) { typographyWindow.Activate(); return; }
        typographyWindow = new TypographyForm(typography); typographyWindow.Show(this);
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e); if (e.Cancel) return;
        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        sample.Store.Preferences.Window = new(bounds.X, bounds.Y, bounds.Width, bounds.Height, WindowState == FormWindowState.Maximized);
        try { sample.Store.SavePreferences(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { e.Cancel = MessageBox.Show(this, "창 설정을 저장하지 못했습니다. 저장하지 않고 닫을까요?", "설정 저장", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes; }
        if (e.Cancel || sessionsClosed) return;
        e.Cancel = true;
        try { await sessions.DisposeAsync(); }
        finally { sessionsClosed = true; if (!IsDisposed) BeginInvoke(Close); }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { typographyWindow?.Close(); base.OnFormClosed(e); }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e); var saved = sample.Store.Preferences.Window;
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1000, area.Width), Math.Min(600, area.Height));
        if (saved != null)
        {
            var desired = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
            var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(desired));
            if (screen != null && saved.Width >= MinimumSize.Width && saved.Height >= MinimumSize.Height) { StartPosition = FormStartPosition.Manual; Bounds = desired; }
            else FitWorkingArea();
            if (saved.Maximized) WindowState = FormWindowState.Maximized;
        }
        else FitWorkingArea();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (checkLoginOnShown) sessions.StartBackgroundChecks();
    }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); FitWorkingArea(); }
    private void FitWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(1000, area.Width), Math.Min(600, area.Height));
        Size = new Size(Math.Min(Math.Max(Width, MinimumSize.Width), area.Width), Math.Min(Math.Max(Height, MinimumSize.Height), area.Height));
        Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
    }
}
