using DupliFoto.Core;

namespace DupliFoto.Gui.Services;

/// <summary>Gli errori imprevisti (e gli avvii lenti) finiscono qui, per poterli segnalare: errori.log nella cartella dei file di lavoro.</summary>
public static class ErrorLog
{
    public static string FilePath { get; } = Path.Combine(AppFiles.Folder, "errori.log");

    public static void Write(Exception ex, string where) => Write(ex.ToString(), where);

    public static void Write(string message, string where)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {AppInfo.Name} {AppInfo.Version}{Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* se non si può scrivere il registro degli errori, non c'è altro da fare */ }
    }
}
