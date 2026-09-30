using System.Diagnostics;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Reporting;

namespace DupliFoto.Cli;

/// <summary>Chiede conferma in console, gruppo per gruppo.</summary>
internal sealed class ConsolePrompt : IDecisionPrompt
{
    public PromptAnswer Ask(PromptRequest req)
    {
        var g = req.Group;
        var files = new List<PhotoFile> { g.Keeper };
        files.AddRange(g.Duplicates.Select(d => d.File));
        var asked = req.Members.Select(m => m.File).ToHashSet(ReferenceEqualityComparer.Instance);
        var keeper = g.Keeper;

        while (true)
        {
            Console.WriteLine();
            Ui.Color(ConsoleColor.White, $"Gruppo {g.Id} · {ReportWriter.KindLabel(g.Kind)} · affidabilità {g.Confidence:0}%");
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var member = g.Duplicates.FirstOrDefault(d => ReferenceEquals(d.File, f));
                bool isKeeper = ReferenceEquals(f, keeper);
                string tag = isKeeper ? "TIENI" : asked.Contains(f) || !ReferenceEquals(keeper, g.Keeper) ? "sposta" : "  —  ";
                var color = isKeeper ? ConsoleColor.Green : ConsoleColor.Yellow;
                Ui.Color(color, $"  [{i}] {tag,-6} {f.Path}");
                string detail = $"{f.Width}×{f.Height}  {ReportWriter.FormatBytes(f.Size)}  nitidezza {f.Sharpness:0}"
                                + (f.TakenAt is { } t ? $"  {t:dd/MM/yyyy HH:mm:ss}" : "");
                Console.WriteLine($"             {detail}");
                if (isKeeper && ReferenceEquals(f, g.Keeper) && g.KeeperReason.Length > 0)
                    Console.WriteLine($"             perché: {g.KeeperReason}");
                else if (member is not null)
                    Console.WriteLine($"             {member.Confidence:0}% — {member.Reason}");
            }

            Console.Write("  [s] sposta  [n] salta  [t] sì a tutti i gruppi di questo tipo  [k N] tieni il file N  [a] apri  [q] esci > ");
            var input = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();

            switch (input)
            {
                case "s": return new PromptAnswer(UserChoice.Apply, keeper);
                case "" or "n": return new PromptAnswer(UserChoice.Skip);
                case "t": return new PromptAnswer(UserChoice.ApplyToAllOfThisKind, keeper);
                case "q": return new PromptAnswer(UserChoice.Quit);
                case "a":
                    foreach (var f in files) Open(f.Path);
                    continue;
            }

            if (input.StartsWith('k') && int.TryParse(input[1..].Trim(), out int n) && n >= 0 && n < files.Count)
            {
                keeper = files[n];
                continue;
            }
            Console.WriteLine("  Scelta non valida.");
        }
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine($"  Impossibile aprire {path}: {ex.Message}"); }
    }
}

internal static class Ui
{
    public static void Color(ConsoleColor c, string text)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}
