using System.Reflection;

namespace DupliFoto.Core;

/// <summary>Informazioni sul programma in esecuzione.</summary>
public static class AppInfo
{
    public const string Name = "DupliFoto 2026";

    /// <summary>La versione pubblicata (es. "0.1.0"), senza il commit che la compilazione aggiunge dopo il "+".</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
