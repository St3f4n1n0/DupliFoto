namespace DupliFoto.Core;

/// <summary>Dove può lavorare la rete neurale. Gli algoritmi classici (lettura, hash, confronto) usano sempre la CPU.</summary>
public enum ComputeEngine { Cpu, Gpu, Npu }

/// <summary>
/// Cosa offre questo PC per un motore di calcolo: se la rete neurale lo può usare e perché sì o no (nelle due lingue),
/// per i pallini nella finestra e per il comando "hardware".
/// </summary>
/// <param name="Usable">C'è, con driver e componenti di Windows ML compatibili (o che Windows può scaricare).</param>
public sealed record EngineAvailability(ComputeEngine Engine, bool Usable, Text Detail);
