using System.Globalization;
using System.Runtime.CompilerServices;
using DupliFoto.Core;
using Xunit;

namespace DupliFoto.Gui.Tests;

/// <summary>
/// I test girano in italiano qualunque sia la lingua del PC (sui server di GitHub è l'inglese): anche la scelta
/// "automatica" deve dare l'italiano. Quelli in inglese stanno nella raccolta <see cref="InEnglish"/>, che gira da sola.
/// </summary>
internal static class Italiano
{
    [ModuleInitializer]
    internal static void Start()
    {
        var it = CultureInfo.GetCultureInfo("it-IT");
        CultureInfo.DefaultThreadCurrentUICulture = it;
        CultureInfo.CurrentUICulture = it;
        Lang.Set(Lang.Italian);
    }
}

/// <summary>I test che passano all'inglese: girano da soli, senza altri test in parallelo, e alla fine tornano all'italiano.</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class InEnglish
{
}
