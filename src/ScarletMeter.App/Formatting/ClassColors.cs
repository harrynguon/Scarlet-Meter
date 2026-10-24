namespace ScarletMeter.App.Formatting;

/// <summary>
/// The CSS class naming a player's class gradient (app.css, ".fill.class-N"), shared by the overlay's rows and the
/// saved fight's share bars so both colour a class the same way. Class ids 10 to 19 have their own gradient;
/// anything else (an unknown or unnamed player) gets the neutral class-0.
/// </summary>
public static class ClassColors
{
    public static string CssClass(int classId) => $"class-{(classId is >= 10 and <= 19 ? classId : 0)}";
}
