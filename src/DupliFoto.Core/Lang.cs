using System.Globalization;

namespace DupliFoto.Core;

/// <summary>
/// La lingua dei testi: italiano o inglese. Ogni testo sta nel codice nelle due lingue, una accanto all'altra,
/// <c>Lang.T("Avvia ricerca", "Start search")</c>: una traduzione non può mancare.
/// "auto" segue la lingua di Windows: italiano se Windows è in italiano, altrimenti inglese.
/// </summary>
public static class Lang
{
    public const string Auto = "auto";
    public const string Italian = "it";
    public const string English = "en";

    private static volatile bool _english = Detect(CultureInfo.CurrentUICulture);

    /// <summary>La scelta dell'utente: "auto", "it" o "en".</summary>
    public static string Choice { get; private set; } = Auto;

    public static bool IsEnglish => _english;

    /// <summary>La lingua in uso, "it" o "en".</summary>
    public static string Code => IsEnglish ? English : Italian;

    /// <summary>Per date e numeri nei testi che non seguono le impostazioni di Windows (il report).</summary>
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-GB" : "it-IT");

    /// <summary>Quando la lingua cambia. Chi lo ascolta da un'interfaccia lo riceve sul thread che ha chiamato <see cref="Set"/>.</summary>
    public static event Action? Changed;

    /// <summary>Sceglie la lingua: "it", "en", oppure "auto" (o qualunque altro valore) per seguire Windows.</summary>
    public static void Set(string? choice)
    {
        Choice = choice is Italian or English ? choice : Auto;
        bool english = Choice switch
        {
            Italian => false,
            English => true,
            _ => Detect(CultureInfo.CurrentUICulture),
        };
        if (english == _english) return;
        _english = english;
        Changed?.Invoke();
    }

    /// <summary>Il testo nella lingua in uso.</summary>
    public static string T(string italian, string english) => IsEnglish ? english : italian;

    /// <summary>Inglese per tutte le lingue di Windows tranne l'italiano.</summary>
    internal static bool Detect(CultureInfo ui) => ui.TwoLetterISOLanguageName != "it";
}

/// <summary>
/// Un testo nelle due lingue, per i motivi che il motore calcola durante la ricerca: si mostra nella lingua in uso
/// nel momento in cui lo si legge, quindi cambiare lingua dopo una ricerca cambia anche i motivi già trovati.
/// </summary>
public readonly record struct Text(string It, string En)
{
    public static readonly Text Empty = new("", "");

    public bool IsEmpty => string.IsNullOrEmpty(It) && string.IsNullOrEmpty(En);

    public override string ToString() => Lang.IsEnglish ? En : It;

    public static implicit operator string(Text t) => t.ToString();

    public static Text operator +(Text a, Text b) => new(a.It + b.It, a.En + b.En);

    public static Text Join(string separator, IEnumerable<Text> parts)
    {
        var list = parts.ToList();
        return new(string.Join(separator, list.Select(p => p.It)), string.Join(separator, list.Select(p => p.En)));
    }
}
