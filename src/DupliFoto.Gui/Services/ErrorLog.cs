using DupliFoto.Core;

namespace DupliFoto.Gui.Services;

/// <summary>Gli errori imprevisti finiscono qui, per poterli segnalare: %LOCALAPPDATA%\DupliFoto\errori.log.</summary>
public static class ErrorLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DupliFoto", "errori.log");

    public static void Write(Exception ex, string where)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] {AppInfo.Name} {AppInfo.Version}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* se non si può scrivere il registro degli errori, non c'è altro da fare */ }
    }
}
