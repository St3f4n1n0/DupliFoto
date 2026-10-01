using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DupliFoto.Core.Scanning;

/// <summary>
/// Riconosce lo stesso file del disco raggiunto da percorsi diversi: un'unità SUBST, la stessa cartella di rete come
/// Z:\Foto e come \\NAS\Foto, una cartella aggiunta tramite giunzione, un collegamento fisico. Due percorsi così
/// non sono due copie: "spostare il doppione" toglierebbe anche la copia da tenere.
/// </summary>
public static class FileIdentity
{
    /// <summary>
    /// Vero se i due percorsi portano allo stesso file. Se l'identità non si può leggere (fuori da Windows, o un
    /// file system che non la fornisce) risponde falso: l'ultima difesa resta in <see cref="Actions.ActionSession"/>,
    /// che dopo ogni spostamento controlla che la copia da tenere sia ancora al suo posto.
    /// </summary>
    public static bool AreSameFile(string a, string b)
    {
        if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
        return TryGetId(a) is { } ia && TryGetId(b) is { } ib && ia == ib;
    }

    /// <summary>Volume e numero del file su Windows; <c>null</c> altrove o se il file system non li fornisce.</summary>
    public static (ulong Volume, UInt128 File)? TryGetId(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Identificativo a 128 bit (NTFS, ReFS); altrimenti quello classico a 64 bit (FAT, exFAT, molte condivisioni).
            // Alcuni NAS rispondono 0 per tutti i file: vale come "non disponibile", non come "stesso file".
            if (Native.GetFileInformationByHandleEx(handle, Native.FileIdInfo, out var id, Marshal.SizeOf<Native.FileIdInfoData>())
                && (id.FileIdLow | id.FileIdHigh) != 0)
                return (id.VolumeSerialNumber, new UInt128(id.FileIdHigh, id.FileIdLow));
            if (Native.GetFileInformationByHandle(handle, out var info) && (info.FileIndexHigh | info.FileIndexLow) != 0)
                return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
        }
        catch (Exception) { /* file non leggibile: identità sconosciuta */ }
        return null;
    }

    private static class Native
    {
        public const int FileIdInfo = 18; // FILE_INFO_BY_HANDLE_CLASS

        [StructLayout(LayoutKind.Sequential)]
        public struct FileIdInfoData
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;   // FILE_ID_128, primi 8 byte
            public ulong FileIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public uint CreationTimeLow, CreationTimeHigh, LastAccessTimeLow, LastAccessTimeHigh, LastWriteTimeLow, LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh, FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh, FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileIdInfoData info, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);
    }
}
