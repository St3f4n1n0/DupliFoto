using System.Diagnostics;

namespace DupliFoto.Gui.Services;

/// <summary>Apre file e cartelle con i programmi del sistema.</summary>
public static class Shell
{
    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception) { /* nessun programma associato: non è un errore bloccante */ }
    }

    /// <summary>Apre Esplora risorse con il file già selezionato.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Open(Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception) { /* come sopra */ }
    }
}
