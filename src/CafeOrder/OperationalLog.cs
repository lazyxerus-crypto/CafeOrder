using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace CafeOrder;

internal enum LogLevel { INFO, WARN, ERROR }

// Only event codes, known identifiers and fixed Korean descriptions enter the log. Never pass URL or exception messages.
internal sealed class OperationalLog
{
    private const long MaxFileBytes = 1_000_000;
    internal static OperationalLog Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CafeOrder", "Logs"));
    private readonly ConcurrentQueue<string> pending = new();
    private readonly object fileGate = new();
    private int draining;
    internal string FilePath { get; }
    internal event Action<string>? EntryAdded;

    internal OperationalLog(string directory) => FilePath = Path.Combine(directory, "CafeOrder.log");

    internal void Write(LogLevel level, string code, string description, string? supplier = null,
        int? productId = null, int? row = null, string? result = null, string? reason = null, Exception? error = null,
        string? goodsNo = null, decimal? oldPrice = null, decimal? newPrice = null)
    {
        static string Token(string value) => Regex.Replace(value, "[^A-Za-z0-9_-]", "_");
        // Call sites use fixed descriptions. This second guard removes accidental secrets if a caller regresses.
        string safeDescription = Regex.Replace(description, @"(?i)https?://\S+|\b(?:password|cookie|token|authorization|session)\s*[:=]\s*\S+", "[비공개]")
            .Replace('\r', ' ').Replace('\n', ' ');
        var line = new StringBuilder().Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [").Append(level).Append("] ").Append(Token(code));
        if (supplier != null) line.Append(" supplier=").Append(Token(supplier));
        if (productId != null) line.Append(" productId=").Append(productId.Value);
        if (goodsNo != null) line.Append(" goodsNo=").Append(Token(goodsNo));
        if (row != null) line.Append(" row=").Append(row.Value);
        if (oldPrice != null) line.Append(" oldPrice=").Append(oldPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (newPrice != null) line.Append(" newPrice=").Append(newPrice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (result != null) line.Append(" result=").Append(Token(result));
        if (reason != null) line.Append(" reason=").Append(Token(reason));
        if (error != null) line.Append(" errorType=").Append(Token(error.GetType().Name));
        line.Append(" · ").Append(safeDescription);
        string entry = line.ToString();
        pending.Enqueue(entry);
        foreach (Action<string> handler in EntryAdded?.GetInvocationList().Cast<Action<string>>() ?? [])
            try { handler(entry); } catch (Exception) { /* A log viewer cannot fail the operation. */ }
        StartDrain();
    }

    private void StartDrain()
    {
        if (Interlocked.CompareExchange(ref draining, 1, 0) == 0) _ = Task.Run(Drain);
    }

    private void Drain()
    {
        try
        {
            while (pending.TryDequeue(out var entry))
            {
                try
                {
                    lock (fileGate)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                        byte[] bytes = Encoding.UTF8.GetBytes(entry + Environment.NewLine);
                        if (File.Exists(FilePath) && new FileInfo(FilePath).Length + bytes.Length > MaxFileBytes)
                            Rotate();
                        using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                        stream.Write(bytes);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        finally
        {
            Interlocked.Exchange(ref draining, 0);
            if (!pending.IsEmpty) StartDrain();
        }
    }

    private void Rotate()
    {
        string old3 = FilePath + ".3";
        if (File.Exists(old3)) File.Delete(old3);
        for (int index = 2; index >= 1; index--)
        {
            string from = FilePath + "." + index;
            if (File.Exists(from)) File.Move(from, FilePath + "." + (index + 1));
        }
        File.Move(FilePath, FilePath + ".1");
    }

    internal async Task FlushAsync()
    {
        while (!pending.IsEmpty || Volatile.Read(ref draining) != 0) await Task.Delay(10);
    }

    internal async Task<string> ReadRecentAsync()
    {
        await FlushAsync();
        return await Task.Run(() =>
        {
        var text = new StringBuilder();
        foreach (var path in new[] { FilePath + ".3", FilePath + ".2", FilePath + ".1", FilePath })
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text.Append(reader.ReadToEnd());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        string result = text.ToString();
        if (result.Length <= 250_000) return result;
        int start = result.IndexOf('\n', result.Length - 250_000);
        return start < 0 ? result[^250_000..] : result[(start + 1)..];
        });
    }
}
