using System.Text.RegularExpressions;

namespace DupliFoto.Core.Matching;

/// <summary>
/// Riconosce i nomi "da copia" creati da Windows, macOS, browser e app di messaggistica,
/// così che "foto (1).jpg", "foto - Copia.jpg" e "Copy of foto.jpg" diventino tutti "foto".
/// </summary>
public static partial class NameNormalizer
{
    // Suffissi di copia, applicati ripetutamente (es. "foto - Copia (2) (1)").
    [GeneratedRegex(@"(\s*\(\d{1,3}\))$")]                                      // foto (1)
    private static partial Regex ParenNumber();
    [GeneratedRegex(@"\s*[-–]\s*(copia|copy|kopie|copie|kopia|cópia)(\s*\(\d{1,3}\))?$", RegexOptions.IgnoreCase)]
    private static partial Regex DashCopy();                                     // foto - Copia (2)
    [GeneratedRegex(@"[\s_]+(copy|copia|kopie)(\s*\d{1,3})?$", RegexOptions.IgnoreCase)]
    private static partial Regex SpaceCopy();                                    // foto copy 2, foto_copy
    [GeneratedRegex(@"^(copia\s+di|copy\s+of|kopie\s+von|copie\s+de)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixCopy();                                   // Copia di foto
    [GeneratedRegex(@"(?<=\d)[_-]\d{1,2}$")]
    private static partial Regex NumericDup();                                   // IMG_1234-2, IMG_1234_1
    [GeneratedRegex(@"[\s_\-(]*(edited|modificat[ao]|bearbeitet|modifi[ée]e?)\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex EditSuffix();                                   // IMG_1234-edited, foto (modificata)

    public readonly record struct Result(string Normalized, bool HasCopyMarker, bool HasEditMarker);

    public static Result Normalize(string baseName)
    {
        string s = baseName.Trim();
        bool copy = false, edit = false;

        if (EditSuffix().IsMatch(s))
        {
            s = EditSuffix().Replace(s, "");
            edit = true;
        }

        for (int guard = 0; guard < 6; guard++)
        {
            string before = s;
            s = PrefixCopy().Replace(s, "");
            s = DashCopy().Replace(s, "");
            s = SpaceCopy().Replace(s, "");
            s = ParenNumber().Replace(s, "");
            s = s.Trim();
            if (s == before) break;
            copy = true;
        }

        // Il suffisso numerico "_1" è ambiguo (può far parte del nome originale):
        // lo togliamo solo per il confronto, ma non lo consideriamo un segno di copia certo.
        string withoutNumeric = NumericDup().Replace(s, "");

        if (s.Length == 0) s = baseName; // non ridurre mai un nome a stringa vuota
        return new Result(
            (withoutNumeric.Length > 0 ? withoutNumeric : s).ToLowerInvariant(),
            copy,
            edit);
    }

    /// <summary>Somiglianza 0..1 tra due nomi già normalizzati (1 - distanza di Levenshtein relativa).</summary>
    public static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;
        int d = Levenshtein(a, b);
        return 1.0 - (double)d / Math.Max(a.Length, b.Length);
    }

    internal static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
