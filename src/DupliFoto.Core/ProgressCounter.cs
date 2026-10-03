namespace DupliFoto.Core;

/// <summary>
/// "Fatti N di M" per le fasi lunghe, contati da più thread insieme: il messaggio esce al massimo quattro volte al secondo,
/// più una volta alla fine, così l'interfaccia resta leggera anche con decine di migliaia di file.
/// </summary>
/// <param name="message">Il testo: fatti, totale, byte letti (per le fasi che leggono i file).</param>
internal sealed class ProgressCounter(IProgress<string>? progress, int total, Func<int, int, long, string> message)
{
    private const long IntervalMs = 250;
    private int _done;
    private long _bytes;
    private long _last = Environment.TickCount64;

    public int Done => Volatile.Read(ref _done);

    public void Add(long bytes = 0)
    {
        int done = Interlocked.Increment(ref _done);
        long read = Interlocked.Add(ref _bytes, bytes);
        long now = Environment.TickCount64, last = Interlocked.Read(ref _last);
        if (now - last >= IntervalMs && Interlocked.CompareExchange(ref _last, now, last) == last)
            progress?.Report(message(done, total, read));
    }

    /// <summary>L'ultimo messaggio, con il conto completo (se c'era qualcosa da fare).</summary>
    public void Finish()
    {
        if (total > 0) progress?.Report(message(Done, total, Interlocked.Read(ref _bytes)));
    }
}
