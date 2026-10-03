using System.Diagnostics;
using DupliFoto.Core;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Reporting;

namespace DupliFoto.Cli;

/// <summary>
/// Chiede conferma in console, gruppo per gruppo. I tasti seguono la lingua e non si mescolano: in italiano
/// s (sposta), n, t (tutti), a (apri); in inglese y (yes, move), n, a (all), o (open). Una "s" in inglese, che
/// qualcuno potrebbe intendere come "skip", non sposta niente: è una scelta non valida.
/// </summary>
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
            Ui.Color(ConsoleColor.White, Lang.T($"Gruppo {g.Id} · {ReportWriter.KindLabel(g.Kind)} · affidabilità {g.Confidence:0}%",
                                                $"Group {g.Id} · {ReportWriter.KindLabel(g.Kind)} · confidence {g.Confidence:0}%"));
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                var member = g.Duplicates.FirstOrDefault(d => ReferenceEquals(d.File, f));
                bool isKeeper = ReferenceEquals(f, keeper);
                string tag = isKeeper ? Lang.T("TIENI", "KEEP")
                    : asked.Contains(f) || !ReferenceEquals(keeper, g.Keeper) ? Lang.T("sposta", "move")
                    : "  —  ";
                var color = isKeeper ? ConsoleColor.Green : ConsoleColor.Yellow;
                Ui.Color(color, $"  [{i}] {tag,-6} {f.Path}");
                string detail = $"{f.Width}×{f.Height}  {ReportWriter.FormatBytes(f.Size)}  {Lang.T("nitidezza", "sharpness")} {f.Sharpness:0}"
                                + (f.TakenAt is { } t ? $"  {t.ToString(Lang.T("dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss"))}" : "");
                Console.WriteLine($"             {detail}");
                if (isKeeper && ReferenceEquals(f, g.Keeper) && !g.KeeperReason.IsEmpty)
                    Console.WriteLine($"             {Lang.T("perché", "because")}: {g.KeeperReason}");
                else if (member is not null)
                    Console.WriteLine($"             {member.Confidence:0}% — {member.Reason}");
            }

            Console.Write(Lang.T(
                "  [s] sposta  [n] salta  [t] sì a tutti i gruppi di questo tipo  [k N] tieni il file N  [a] apri  [q] esci > ",
                "  [y] move  [n] skip  [a] yes to all groups of this kind  [k N] keep file N  [o] open  [q] quit > "));
            var input = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();

            var choice = Lang.IsEnglish
                ? input switch { "y" => "move", "" or "n" => "skip", "a" => "all", "o" => "open", "q" => "quit", _ => null }
                : input switch { "s" => "move", "" or "n" => "skip", "t" => "all", "a" => "open", "q" => "quit", _ => null };
            switch (choice)
            {
                case "move": return new PromptAnswer(UserChoice.Apply, keeper);
                case "skip": return new PromptAnswer(UserChoice.Skip);
                case "all": return new PromptAnswer(UserChoice.ApplyToAllOfThisKind, keeper);
                case "quit": return new PromptAnswer(UserChoice.Quit);
                case "open":
                    foreach (var f in files) Open(f.Path);
                    continue;
            }

            if (input.StartsWith('k') && int.TryParse(input[1..].Trim(), out int n) && n >= 0 && n < files.Count)
            {
                keeper = files[n];
                continue;
            }
            Console.WriteLine(Lang.T("  Scelta non valida.", "  Invalid choice."));
        }
    }

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine(Lang.T($"  Impossibile aprire {path}: {ex.Message}", $"  Could not open {path}: {ex.Message}")); }
    }
}

internal static class Ui
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    /// <summary>Vero se la console è stata creata apposta per questo processo (doppio clic da Esplora risorse).</summary>
    public static bool OwnsConsole()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected) return false;
        try { return GetConsoleProcessList(new uint[2], 2) == 1; }
        catch (Exception) { return false; }
    }

    public static void Color(ConsoleColor c, string text)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(text);
        Console.ForegroundColor = old;
    }
}
