using System.Runtime.InteropServices;
using System.Text;

namespace DupliFoto.Accel;

/// <summary>
/// I dispositivi presenti di una classe di Gestione dispositivi, per nome: "Display" (schede video) e
/// "ComputeAccelerator" (i «Processori neurali»: Intel AI Boost, Qualcomm Hexagon, AMD NPU).
/// Serve a dire "c'è una NPU" anche quando Windows ML non ha ancora il componente per usarla.
/// </summary>
internal static class PnpDevices
{
    public static IReadOnlyList<string> Present(string className)
    {
        var names = new List<string>();
        if (!OperatingSystem.IsWindows()) return names;
        try
        {
            SetupDiClassGuidsFromNameW(className, null, 0, out int count);
            if (count <= 0) return names;
            var guids = new Guid[count];
            if (!SetupDiClassGuidsFromNameW(className, guids, count, out count)) return names;
            foreach (var classGuid in guids.Take(count))
            {
                var guid = classGuid;
                IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DIGCF_PRESENT);
                if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;
                try
                {
                    var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                    for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                    {
                        if ((Property(set, ref data, SPDRP_FRIENDLYNAME) ?? Property(set, ref data, SPDRP_DEVICEDESC)) is { Length: > 0 } name)
                            names.Add(name);
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(set); }
            }
        }
        catch (Exception) { /* senza l'elenco dei dispositivi si sa comunque cosa vede Windows ML */ }
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? Property(IntPtr set, ref SP_DEVINFO_DATA data, int property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, buffer.Length, out int size)) return null;
        return Encoding.Unicode.GetString(buffer, 0, Math.Clamp(size, 0, buffer.Length)).TrimEnd('\0').Trim();
    }

    private const int DIGCF_PRESENT = 0x2;
    private const int SPDRP_DEVICEDESC = 0x0;
    private const int SPDRP_FRIENDLYNAME = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiClassGuidsFromNameW(string className, [Out] Guid[]? classGuids, int size, out int requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, int property,
        out int registryType, byte[] buffer, int size, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
