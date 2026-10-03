using System.ComponentModel;
using System.Reflection;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DupliFoto.Core;

namespace DupliFoto.Gui.Views;

/// <summary>
/// <c>{l:T Chiave}</c> nel XAML: il testo <see cref="Strings"/>.Chiave nella lingua in uso. È un collegamento, non un
/// valore fisso: quando si cambia lingua la finestra si aggiorna senza riaprirsi. Una chiave che non esiste ferma
/// subito il caricamento della finestra (e i test), invece di mostrare un testo vuoto.
/// </summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = LiveStrings.Check(key);

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBinding($"[{Key}]") { Source = LiveStrings.Instance, Mode = BindingMode.OneWay };
}

/// <summary>I testi di <see cref="Strings"/> per nome, con l'avviso di cambio quando cambia la lingua.</summary>
public sealed class LiveStrings : INotifyPropertyChanged
{
    public static LiveStrings Instance { get; } = new();

    private static readonly Dictionary<string, Func<string>> All = typeof(Strings)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(p => p.Name, p => p.GetMethod!.CreateDelegate<Func<string>>());

    // Avalonia riconosce il cambio di un indicizzatore dal nome "Item" (WPF usa "Item[]": si mandano entrambi).
    // Il binding ascolta in modo debole: i controlli che spariscono non restano agganciati qui.
    private LiveStrings() => Lang.Changed += () =>
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    };

    public string this[string key] => All.TryGetValue(key, out var text) ? text() : key;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal static string Check(string key) =>
        All.ContainsKey(key) ? key : throw new ArgumentException($"Testo sconosciuto nella finestra: {key}", nameof(key));
}
