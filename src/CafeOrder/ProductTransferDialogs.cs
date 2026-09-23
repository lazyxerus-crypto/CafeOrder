namespace CafeOrder;

internal interface IProductTransferDialogs
{
    string? ChooseExport(IWin32Window owner);
    string? ChooseImport(IWin32Window owner);
    bool Confirm(IWin32Window owner, ProductImportPlan plan);
    void Show(IWin32Window owner, string message, bool error);
    void ShowIssues(IWin32Window owner, ProductImportPlan plan);
}

internal sealed class WinFormsProductTransferDialogs : IProductTransferDialogs
{
    public string? ChooseExport(IWin32Window owner)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "상품 XLSX 내보내기", Filter = "Excel 파일 (*.xlsx)|*.xlsx", DefaultExt = "xlsx",
            AddExtension = true, OverwritePrompt = true, FileName = $"CafeOrder_Products_{DateTime.Today:yyyy-MM-dd}.xlsx"
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
    public string? ChooseImport(IWin32Window owner)
    {
        using var dialog = new OpenFileDialog { Title = "상품 XLSX 가져오기", Filter = "Excel 파일 (*.xlsx)|*.xlsx", CheckFileExists = true };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
    public bool Confirm(IWin32Window owner, ProductImportPlan plan) =>
        MessageBox.Show(owner,
            $"추가 상품 {plan.Added}개\n수정 상품 {plan.Updated}개\n비활성 변경 {plan.Deactivated}개\n오류 0개\n\n적용하시겠습니까?",
            "상품 XLSX 가져오기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    public void Show(IWin32Window owner, string message, bool error) =>
        MessageBox.Show(owner, message, "상품 XLSX", MessageBoxButtons.OK, error ? MessageBoxIcon.Error : MessageBoxIcon.Information);
    public void ShowIssues(IWin32Window owner, ProductImportPlan plan)
    {
        using var dialog = new Form { Text = $"상품 XLSX · 추가 {plan.Added} · 수정 {plan.Updated} · 실패 {plan.Failed} · DB 변경 없음", StartPosition = FormStartPosition.CenterParent,
            Width = 680, Height = 420, MinimumSize = new Size(440, 260) };
        var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = false,
            ScrollBars = ScrollBars.Both, Text = string.Join(Environment.NewLine, plan.Issues) };
        var close = new Button { Text = "닫기", Dock = DockStyle.Bottom, Height = 40, DialogResult = DialogResult.OK };
        dialog.Controls.Add(details); dialog.Controls.Add(close); dialog.AcceptButton = close;
        dialog.ShowDialog(owner);
    }
}
