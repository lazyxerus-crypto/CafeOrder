using Microsoft.Playwright;

namespace CafeOrder;

internal sealed record MegaCartDialogObservation(string Type, string Message,
    bool IsClearConfirmation, bool Accepted, bool Handled);

internal sealed class MegaCartDialogMonitor : IDisposable
{
    internal const string ClearMessage = "장바구니를 비우시겠습니까?";
    private IPage page;
    private readonly Action<MegaCartDialogObservation>? onObserved;
    private readonly TaskCompletionSource<MegaCartDialogObservation> first =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<MegaCartDialogObservation> observations = [];
    private readonly object gate = new();
    private int clearConfirmationSeen;

    internal MegaCartDialogMonitor(IPage page, Action<MegaCartDialogObservation>? onObserved = null)
    {
        this.page = page;
        this.onObserved = onObserved;
        page.Dialog += Handle;
    }

    internal Task<MegaCartDialogObservation> First => first.Task;
    internal void Attach(IPage next)
    {
        if (ReferenceEquals(page, next)) return;
        page.Dialog -= Handle;
        page = next;
        page.Dialog += Handle;
    }
    internal bool HasUnexpected
    {
        get { lock (gate) return observations.Any(x => !x.IsClearConfirmation || !x.Accepted || !x.Handled); }
    }

    private async void Handle(object? sender, IDialog dialog)
    {
        string type = dialog.Type;
        string message = dialog.Message;
        bool expected = type == "confirm" && message == ClearMessage &&
            Interlocked.CompareExchange(ref clearConfirmationSeen, 1, 0) == 0;
        bool accepted = false, handled = false;
        try
        {
            if (expected) { await dialog.AcceptAsync(); accepted = handled = true; }
            else { await dialog.DismissAsync(); handled = true; }
        }
        catch (PlaywrightException) { /* The caller will reread the site cart before deciding. */ }
        var observation = new MegaCartDialogObservation(type, message, expected, accepted, handled);
        lock (gate) observations.Add(observation);
        try { onObserved?.Invoke(observation); } catch (Exception) { /* Logging cannot control a dialog. */ }
        first.TrySetResult(observation);
    }

    public void Dispose() => page.Dialog -= Handle;
}
