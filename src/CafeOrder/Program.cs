namespace CafeOrder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { MessageBox.Show("로컬 데이터를 열거나 이전하지 못했습니다. 기존 파일은 유지했습니다.\n\n" + ex.Message,
            "CafeOrder · 저장소 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
