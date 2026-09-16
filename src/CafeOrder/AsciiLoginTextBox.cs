namespace CafeOrder;

// UI-only credentials: printable ASCII for typing, paste and replacement; never saved.
internal sealed class AsciiLoginTextBox : TextBox
{
    internal event Action<bool>? CapsLockChanged;
    public AsciiLoginTextBox()
    { Width = 200; MaxLength = 100; Margin = new Padding(4, 5, 4, 5); ImeMode = ImeMode.Disable; }
    protected override void OnTextChanged(EventArgs e)
    {
        static bool Allowed(char c) => c is >= ' ' and <= '~';
        string filtered = new(Text.Where(Allowed).ToArray());
        if (Text != filtered)
        {
            int start = Text.Take(SelectionStart).Count(Allowed);
            Text = filtered; SelectionStart = start; return;
        }
        base.OnTextChanged(e);
    }
    protected override void OnKeyPress(KeyPressEventArgs e)
    { if (!char.IsControl(e.KeyChar) && e.KeyChar is not (>= ' ' and <= '~')) e.Handled = true; base.OnKeyPress(e); }
    protected override void OnGotFocus(EventArgs e)
    { ImeMode = ImeMode.Disable; base.OnGotFocus(e); UpdateCapsLock(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); CapsLockChanged?.Invoke(false); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); UpdateCapsLock(); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); UpdateCapsLock(); }
    private void UpdateCapsLock() => CapsLockChanged?.Invoke(Focused && UseSystemPasswordChar && IsKeyLocked(Keys.CapsLock));
}
