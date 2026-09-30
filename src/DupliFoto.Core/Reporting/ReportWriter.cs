using System.Globalization;
using System.Net;
using System.Text;

namespace DupliFoto.Core.Reporting;

/// <summary>Report in sola lettura: HTML con anteprime (si apre nel browser) e CSV per Excel.</summary>
public static class ReportWriter
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public static string KindLabel(MatchKind k) => k switch
    {
        MatchKind.ExactBytes => "Identici al byte",
        MatchKind.IdenticalPixels => "Stessi pixel",
        MatchKind.Perceptual => "Stessa immagine",
        MatchKind.Burst => "Scatti multipli",
        _ => k.ToString(),
    };

    public static string FormatBytes(long b) => b switch
    {
        >= 1L << 30 => (b / (double)(1L << 30)).ToString("0.00 'GB'", It),
        >= 1L << 20 => (b / (double)(1L << 20)).ToString("0.0 'MB'", It),
        >= 1L << 10 => (b / 1024.0).ToString("0 'KB'", It),
        _ => $"{b} B",
    };

    public static void WriteCsv(ScanResult r, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("gruppo;ruolo;tipo;affidabilita;motivo;percorso;peso_byte;larghezza;altezza;data_scatto;nitidezza");
        foreach (var g in r.Groups)
        {
            Row(g.Id, "TENERE", KindLabel(g.Kind), "", g.KeeperReason, g.Keeper);
            foreach (var d in g.Duplicates)
                Row(g.Id, "doppione", KindLabel(d.Kind), d.Confidence.ToString("0", It), d.Reason, d.File);
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)); // BOM: Excel legge gli accenti

        void Row(int id, string role, string kind, string conf, string reason, PhotoFile f) =>
            sb.AppendLine(string.Join(';', id, role, kind, conf, Csv(reason), Csv(f.Path), f.Size, f.Width, f.Height,
                f.TakenAt?.ToString("yyyy-MM-dd HH:mm:ss.ff", CultureInfo.InvariantCulture) ?? "",
                f.Sharpness.ToString("0", CultureInfo.InvariantCulture)));
    }

    private static string Csv(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

    public static void WriteHtml(ScanResult r, string path)
    {
        long reclaim = r.Groups.Sum(g => g.ReclaimableBytes);
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html lang="it"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>DupliFoto 2026 – report</title>
            <style>
            :root{--bg:#f6f5f1;--card:#fff;--fg:#222;--mut:#6b6a66;--line:#ddd9cf;--ok:#0f6e56;--mid:#854f0b;--low:#993c1d}
            @media (prefers-color-scheme:dark){:root{--bg:#1d1d1b;--card:#282826;--fg:#eee;--mut:#aaa89f;--line:#3c3b37;--ok:#5dcaa5;--mid:#ef9f27;--low:#f0997b}}
            body{font:15px/1.5 system-ui,"Segoe UI",sans-serif;background:var(--bg);color:var(--fg);margin:0;padding:24px;max-width:1200px;margin:auto}
            h1{font-weight:600;margin:0 0 4px} .mut{color:var(--mut)}
            .stats{display:flex;gap:24px;flex-wrap:wrap;margin:16px 0 24px}.stats div{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:10px 16px}
            .stats b{display:block;font-size:22px}
            .g{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:14px;margin:0 0 14px}
            .gh{display:flex;gap:12px;align-items:baseline;flex-wrap:wrap;margin-bottom:10px}
            .pill{border-radius:99px;padding:2px 10px;font-size:13px;border:1px solid currentColor}
            .c100{color:var(--ok)}.c90{color:var(--mid)}.c60{color:var(--low)}
            .row{display:flex;gap:12px;overflow-x:auto}
            .it{flex:0 0 200px;font-size:12.5px;word-break:break-all}
            .it img{width:200px;height:150px;object-fit:cover;border-radius:8px;background:var(--line);display:block;margin-bottom:6px}
            .keep img{outline:3px solid var(--ok);outline-offset:-3px}
            .tag{font-weight:600}
            </style></head><body>
            """);
        sb.Append($"<h1>DupliFoto 2026</h1><div class=mut>Analisi del {DateTime.Now.ToString("f", It)} · durata {r.Elapsed:mm\\:ss} · {Enc(r.AcceleratorDescription)}</div>");
        sb.Append("<div class=stats>");
        Stat("Foto analizzate", r.Files.Count.ToString("N0", It));
        Stat("Gruppi di doppioni", r.Groups.Count.ToString("N0", It));
        Stat("Spazio recuperabile", FormatBytes(reclaim));
        foreach (MatchKind k in Enum.GetValues<MatchKind>())
            Stat(KindLabel(k), r.Groups.Count(g => g.Kind == k).ToString("N0", It));
        if (r.UnreadableFiles > 0) Stat("File illeggibili", r.UnreadableFiles.ToString("N0", It));
        sb.Append("</div>");

        foreach (var g in r.Groups)
        {
            string cls = g.Confidence >= 99 ? "c100" : g.Confidence >= 90 ? "c90" : "c60";
            sb.Append($"<section class=g><div class=gh><b>Gruppo {g.Id}</b><span class=\"pill {cls}\">{KindLabel(g.Kind)} · {g.Confidence:0}%</span>");
            sb.Append($"<span class=mut>recuperabili {FormatBytes(g.ReclaimableBytes)}</span></div><div class=row>");
            Item(g.Keeper, "DA TENERE", g.KeeperReason, keep: true);
            foreach (var d in g.Duplicates) Item(d.File, $"{d.Confidence:0}%", d.Reason, keep: false);
            sb.Append("</div></section>");
        }
        sb.Append("</body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        void Stat(string label, string value) => sb.Append($"<div><b>{Enc(value)}</b><span class=mut>{Enc(label)}</span></div>");

        void Item(PhotoFile f, string tag, string reason, bool keep)
        {
            string uri = new Uri(f.Path).AbsoluteUri;
            string dims = f.Width > 0 ? $"{f.Width}×{f.Height} · " : "";
            string when = f.TakenAt is { } t ? $"<br>{t.ToString("g", It)}" : "";
            sb.Append($"<div class=\"it{(keep ? " keep" : "")}\"><a href=\"{Enc(uri)}\"><img loading=lazy src=\"{Enc(uri)}\" alt=\"\"></a>");
            sb.Append($"<span class=tag>{Enc(tag)}</span> <span class=mut>{Enc(reason)}</span><br>{Enc(f.Path)}<br>");
            sb.Append($"<span class=mut>{dims}{FormatBytes(f.Size)} · nitidezza {f.Sharpness:0}{when}</span></div>");
        }
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
