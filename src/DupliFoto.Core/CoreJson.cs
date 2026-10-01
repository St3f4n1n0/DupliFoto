using System.Text.Json.Serialization;
using DupliFoto.Core.Actions;
using DupliFoto.Core.Scanning;

namespace DupliFoto.Core;

/// <summary>
/// Registro degli spostamenti e cache in JSON con il codice generato in compilazione, senza reflection:
/// funziona anche negli exe pubblicati con il trimming, dove la serializzazione via reflection è spenta.
/// </summary>
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(Dictionary<string, AnalysisCache.Entry>))]
internal sealed partial class CoreJson : JsonSerializerContext
{
}
