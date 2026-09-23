namespace CafeOrder;

internal sealed class ProductImportProgressForm : Form
{
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "XLSX 검증 중..." };
    private readonly Button cancelButton;
    private bool finished, applying;

    internal ProductImportProgressForm(Action cancel)
    {
        Text = "상품 XLSX 가져오기"; ClientSize = new Size(390, 110);
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        cancelButton = new Button { Text = "취소", Dock = DockStyle.Bottom, Height = 38 };
        cancelButton.Click += (_, _) => { cancelButton.Enabled = false; status.Text = "취소 중..."; cancel(); };
        FormClosing += (_, e) =>
        { if (applying && !finished) e.Cancel = true; else if (!finished) cancel(); };
        Controls.Add(status); Controls.Add(cancelButton);
    }

    internal void SetStatus(string text) { if (!IsDisposed && !finished) status.Text = text; }
    internal void SetApplying()
    { applying = true; cancelButton.Enabled = false; ControlBox = false; status.Text = "SQLite에 적용 중..."; }
    internal void Finish() { if (IsDisposed || finished) return; finished = true; Close(); }
}
