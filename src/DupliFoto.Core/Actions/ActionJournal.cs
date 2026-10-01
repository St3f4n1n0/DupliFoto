using System.Text.Json;

namespace DupliFoto.Core.Actions;

/// <summary>Una riga del registro: cosa è stato spostato, dove, e perché.</summary>
public sealed record JournalEntry(
    DateTime TimestampUtc,
    DisposalMethod Method,
    string OriginalPath,
    string? MovedTo,
    long Size,
    string KeptFile,
    MatchKind Kind,
    double Confidence,
    bool Automatic);

/// <summary>
/// Registro JSON Lines (una riga per file), scritto e svuotato su disco dopo ogni azione:
/// anche se il programma si interrompe, tutto ciò che è stato fatto resta annullabile.
/// </summary>
public sealed class ActionJournal : IDisposable
{
    private readonly StreamWriter _writer;
    public string Path { get; }

    public ActionJournal(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
    }

    public void Write(JournalEntry e) => _writer.WriteLine(JsonSerializer.Serialize(e, CoreJson.Default.JournalEntry));

    public void Dispose() => _writer.Dispose();

    public static IEnumerable<JournalEntry> Read(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JournalEntry? e;
            try { e = JsonSerializer.Deserialize(line, CoreJson.Default.JournalEntry); }
            catch (JsonException) { continue; }
            if (e is not null) yield return e;
        }
    }

    public sealed record UndoResult(int Restored, int Skipped, List<string> Messages);

    /// <summary>Riporta i file dalla quarantena alla posizione originale.</summary>
    public static UndoResult Undo(string journalPath)
    {
        int restored = 0, skipped = 0;
        var messages = new List<string>();
        foreach (var e in Read(journalPath).Reverse())
        {
            if (e.Method != DisposalMethod.Quarantine || e.MovedTo is null)
            {
                skipped++;
                messages.Add($"Nel Cestino di Windows, da ripristinare da lì: {e.OriginalPath}");
                continue;
            }
            if (!File.Exists(e.MovedTo))
            {
                skipped++;
                messages.Add($"Non più presente in quarantena: {e.MovedTo}");
                continue;
            }
            if (File.Exists(e.OriginalPath))
            {
                skipped++;
                messages.Add($"Esiste già un file nella posizione originale, non sovrascritto: {e.OriginalPath}");
                continue;
            }
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(e.OriginalPath)!);
                File.Move(e.MovedTo, e.OriginalPath);
                restored++;
            }
            catch (Exception ex)
            {
                skipped++;
                messages.Add($"Errore ripristinando {e.OriginalPath}: {ex.Message}");
            }
        }
        return new UndoResult(restored, skipped, messages);
    }
}
