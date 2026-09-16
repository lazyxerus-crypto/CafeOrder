using System.Globalization;

namespace CafeOrder;

public enum TypographyKey { Tab, SectionTitle, General, ProductName, ProductPrice, Category, Button, CartProductName, CartPrice, OrderProductName, OrderInfo, HistoryProductName, HistoryInfo, SupplierManagement, Log }

public sealed class Typography : IDisposable
{
    private readonly LocalState store;
    private readonly Dictionary<Control, (TypographyKey Key, FontStyle Style)> controls = [];
    private readonly Dictionary<(float Size, FontStyle Style), Font> fonts = [];
    public event Action? Changed;
    public Typography(LocalState store) => this.store = store;
    public float Size(TypographyKey key) => store.Preferences.FontSizes.TryGetValue(key.ToString(), out float size) && float.IsFinite(size)
        ? Math.Clamp(size, 9, 24) : key switch { TypographyKey.ProductPrice => 13, TypographyKey.SectionTitle => 13, TypographyKey.General or TypographyKey.Category or TypographyKey.CartProductName or TypographyKey.CartPrice or TypographyKey.OrderProductName or TypographyKey.OrderInfo or TypographyKey.HistoryProductName or TypographyKey.HistoryInfo or TypographyKey.SupplierManagement => 11.5f, _ => 12 };
    public T Bind<T>(T control, TypographyKey key, FontStyle? style = null) where T : Control
    {
        bool exists = controls.ContainsKey(control);
        controls[control] = (key, style ?? control.Font.Style); Apply(control);
        if (!exists) control.Disposed += (_, _) => controls.Remove(control);
        return control;
    }
    public Font Font(TypographyKey role, FontStyle style = FontStyle.Regular)
    {
        var key = (Size(role), style);
        if (!fonts.TryGetValue(key, out var font)) fonts[key] = font = new Font("Malgun Gothic", key.Item1, style);
        return font;
    }
    private void Apply(Control control)
    {
        var role = controls[control]; var key = (Size(role.Key), role.Style);
        if (!fonts.TryGetValue(key, out var font)) fonts[key] = font = new Font("Malgun Gothic", key.Item1, key.Style);
        if (!control.Font.Equals(font)) control.Font = font;
    }
    public void Set(TypographyKey key, float value)
    {
        float size = Math.Clamp(value, 9, 24);
        var old = store.Preferences.FontSizes.GetValueOrDefault(key.ToString(), Size(key));
        store.Preferences.FontSizes[key.ToString()] = size;
        try { store.SavePreferences(); } catch { store.Preferences.FontSizes[key.ToString()] = old; throw; }
        var roots = Application.OpenForms.Cast<Form>().ToArray(); foreach (var form in roots) form.SuspendLayout();
        try { foreach (var control in controls.Keys.ToArray()) if (!control.IsDisposed) Apply(control); Changed?.Invoke(); }
        finally { foreach (var form in roots) if (!form.IsDisposed) { form.ResumeLayout(true); form.PerformLayout(); } }
    }
    public string CopyText() => "CafeOrder 글자크기 설정" + Environment.NewLine + Environment.NewLine +
        string.Join(Environment.NewLine, Enum.GetValues<TypographyKey>().Select(k => $"{k}={Size(k).ToString("0.##", CultureInfo.InvariantCulture)}"));
    public void Dispose() { controls.Clear(); foreach (var font in fonts.Values) font.Dispose(); fonts.Clear(); }
}

internal sealed class TypographyForm : Form
{
    public TypographyForm(Typography fonts)
    {
        Name = "TypographySettings"; Text = "글자 크기 상세 설정"; Size = new Size(440, 620); MinimumSize = new Size(400, 400);
        StartPosition = FormStartPosition.CenterParent; var list = Ui.List("TypographyFields");
        var copy = Ui.Button("전체 설정 복사", () => { }, name: "CopyTypography");
        copy.Click += (_, _) => { try { Clipboard.SetText(fonts.CopyText()); copy.Text = "복사됨"; } catch (System.Runtime.InteropServices.ExternalException) { copy.Text = "복사 실패 · 다시 시도"; } };
        foreach (var key in Enum.GetValues<TypographyKey>())
        {
            var input = Ui.Role(new NumericUpDown { Name = "Font_" + key, Minimum = 9, Maximum = 24, DecimalPlaces = 1, Increment = .5m, Value = (decimal)fonts.Size(key), Width = 110 }, TypographyKey.General);
            var label = Ui.Text(key.ToString()); label.MaximumSize = new Size(240, 0); label.MinimumSize = new Size(200, 0);
            input.ValueChanged += (_, _) => { try { fonts.Set(key, (float)input.Value); copy.Text = "전체 설정 복사"; } catch (IOException) { copy.Text = "설정 저장 실패 · 다시 시도"; } catch (UnauthorizedAccessException) { copy.Text = "설정 저장 권한 없음"; } };
            list.Controls.Add(Ui.Row(label, input));
        }
        copy.Dock = DockStyle.Bottom; Controls.Add(list); Controls.Add(copy);
    }
}
