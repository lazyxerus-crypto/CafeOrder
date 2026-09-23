using CafeOrder;

internal static class PieceLoginChecks
{
    internal static async Task RunAsync(bool interactive)
    {
        await using var manager = new SupplierSessionManager(log: OperationalLog.Default);
        SupplierLoginState last = SupplierLoginState.NotChecked;
        manager.StateChanged += (id, state) =>
        {
            if (id != "piece") return;
            last = state;
            Console.WriteLine("PieceCake login state=" + state);
        };
        if (interactive) await manager.OpenLoginAsync("piece");
        else await manager.CheckAsync("piece");
        if (last != SupplierLoginState.LoggedIn)
            throw new InvalidDataException("PieceCake login was not verified by authenticated page elements");
        Console.WriteLine(interactive ? "PASS: PieceCake manual login verified" :
            "PASS: PieceCake session reused without credentials");
    }
}
