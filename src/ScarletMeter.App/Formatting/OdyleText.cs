using System.Globalization;
using ScarletMeter.Engine;

namespace ScarletMeter.App.Formatting;

/// <summary>Odyle energy as the overlay writes it, the way the game does: basic, then charged in brackets ("420 (+1,200)").</summary>
public static class OdyleText
{
    /// <summary>The basic energy, with a thousands separator.</summary>
    public static string Basic(OdyleEnergy energy) => energy.Basic.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The charged energy in brackets, signed: "(+1,200)".</summary>
    public static string Charged(OdyleEnergy energy) => "(+" + energy.Charged.ToString("N0", CultureInfo.InvariantCulture) + ")";
}
