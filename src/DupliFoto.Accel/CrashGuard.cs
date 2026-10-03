using DupliFoto.Core;
using Microsoft.ML.OnnxRuntime;

namespace DupliFoto.Accel;

/// <summary>
/// Un dispositivo che fa chiudere il programma non si riprova. Un errore dentro un driver (DirectML, OpenVINO...) non è
/// un'eccezione che si possa intercettare: il processo finisce e basta. Prima di provare un dispositivo se ne annota il
/// nome in DupliFoto-dati, e l'annotazione si toglie appena il dispositivo ha funzionato o ha dato un errore normale.
/// Se al giro dopo è ancora lì, il programma si era chiuso: il dispositivo passa tra gli esclusi, finché l'utente non
/// cancella il file degli esclusi.
/// </summary>
internal static class CrashGuard
{
    private static string Attempt => Path.Combine(AppFiles.Folder, "rete-neurale-in-prova.txt");

    public static string BlockedFile => Path.Combine(AppFiles.Folder, "rete-neurale-esclusi.txt");

    public static string Key(OrtEpDevice d) =>
        $"{d.EpName} {d.HardwareDevice.Type} {d.HardwareDevice.VendorId:x4}:{d.HardwareDevice.DeviceId:x4}";

    /// <summary>
    /// Gli esclusi. Con <paramref name="afterCrash"/> prima vi aggiunge il dispositivo rimasto "in prova": va chiamato solo
    /// quando nessuna prova è in corso (all'inizio della creazione della rete neurale), mai dai pallini, che girano in parallelo.
    /// </summary>
    public static HashSet<string> Blocked(bool afterCrash)
    {
        try
        {
            if (afterCrash && File.Exists(Attempt))
            {
                File.AppendAllText(BlockedFile, File.ReadAllText(Attempt).Trim() + Environment.NewLine);
                File.Delete(Attempt);
            }
            return File.Exists(BlockedFile)
                ? File.ReadAllLines(BlockedFile).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet()
                : [];
        }
        catch (Exception) { return []; }
    }

    public static void Begin(OrtEpDevice device)
    {
        try { File.WriteAllText(Attempt, Key(device)); } catch (Exception) { /* senza annotazione si prova lo stesso */ }
    }

    public static void End()
    {
        try { File.Delete(Attempt); } catch (Exception) { /* resta: al giro dopo il dispositivo verrà escluso */ }
    }
}
