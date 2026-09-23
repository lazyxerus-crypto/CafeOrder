namespace CafeOrder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var log = OperationalLog.Default;
        log.Write(LogLevel.INFO, "APP_START", "CafeOrder 실행을 시작했습니다.", result: "STARTED");
        Application.ThreadException += (_, e) =>
            log.Write(LogLevel.ERROR, "UNHANDLED_EXCEPTION", "UI 처리 중 예외가 발생했습니다.", error: e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Write(LogLevel.ERROR, "UNHANDLED_EXCEPTION", "처리되지 않은 예외가 발생했습니다.", error: e.ExceptionObject as Exception);
            log.FlushAsync().GetAwaiter().GetResult();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
            log.Write(LogLevel.ERROR, "UNHANDLED_TASK_EXCEPTION", "비동기 작업 예외가 관찰되지 않았습니다.", error: e.Exception);
        try { Application.Run(new MainForm()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log.Write(LogLevel.ERROR, "APP_START_FAILED", "로컬 데이터를 열거나 이전하지 못했습니다.", error: ex);
            MessageBox.Show("로컬 데이터를 열거나 이전하지 못했습니다. 기존 파일은 유지했습니다.\n\n" + ex.Message,
                "CafeOrder · 저장소 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            log.Write(LogLevel.INFO, "APP_EXIT", "CafeOrder 실행을 종료했습니다.", result: "CLOSED");
            log.FlushAsync().GetAwaiter().GetResult();
        }
    }
}
