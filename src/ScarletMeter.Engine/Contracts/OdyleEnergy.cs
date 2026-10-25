namespace ScarletMeter.Engine;

/// <summary>
/// The player's Odyle energy as the game last sent it: <see cref="Basic"/>, which raids spend, and <see cref="Charged"/>,
/// the reserve recharge items add to. The game's maximum (840 basic with a membership, 560 without, 2000 charged) is
/// not in the records, so none is given.
/// </summary>
public readonly record struct OdyleEnergy(int Basic, int Charged);
