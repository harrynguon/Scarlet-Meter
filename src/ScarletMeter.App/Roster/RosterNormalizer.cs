namespace ScarletMeter.App.Roster;

/// <summary>
/// Checks a loaded roster, which is untrusted (a hand edit, a damaged write, another build): drops what cannot be used
/// and repairs what can, saying what it did in sentences that never name a character, since they are logged. Pure: the
/// current instant is a parameter.
/// </summary>
public static class RosterNormalizer
{
    /// <summary>The most characters kept; past it, the ones seen longest ago go.</summary>
    public const int MaxCharacters = 200;

    /// <summary>The longest name kept: upstream's own limit on a character name's bytes.</summary>
    public const int MaxNameLength = 72;

    /// <summary>The longest server name kept (upstream's are three letters).</summary>
    public const int MaxServerLength = 32;

    /// <summary>The longest class name kept.</summary>
    public const int MaxClassNameLength = 40;

    /// <summary>The largest combat power kept.</summary>
    public const int MaxCombatPower = 100_000_000;

    /// <summary>The largest count kept: the entry list's own bound.</summary>
    public const int MaxCount = 1_000_000;

    /// <summary>The largest Odyle energy kept: the entry list's bound for it.</summary>
    public const int MaxOdyle = 100_000;

    /// <summary>The earliest date taken: before AION 2's release anything is not the game's.</summary>
    public static readonly DateTimeOffset Earliest = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far after now a date may lie (a clock set back since): beyond that it is not one the meter wrote.</summary>
    public static readonly TimeSpan FutureSlack = TimeSpan.FromDays(1);

    /// <summary>The roster to use, and a sentence for each repair (none when it was usable as it was).</summary>
    public static (RosterFile File, IReadOnlyList<string> Repairs) Normalize(RosterFile? file, DateTimeOffset now)
    {
        List<string> repairs = [];
        var dropped = 0;
        var countsDropped = 0;
        var runsTrimmed = 0;
        var repaired = 0;
        List<RosterCharacter> kept = [];
        foreach (var character in file?.Characters ?? [])
        {
            if (character is null || Clean(character, now, ref countsDropped, ref runsTrimmed, ref repaired) is not { } clean)
            {
                dropped++;
                continue;
            }
            kept.Add(clean);
        }

        // One entry per character: the one seen last.
        var unique = kept.GroupBy(c => c.Key).Select(g => g.OrderByDescending(c => c.LastSeen).First()).ToList();
        var duplicates = kept.Count - unique.Count;
        var newest = unique.OrderByDescending(c => c.LastSeen).Take(MaxCharacters).ToList();
        var over = unique.Count - newest.Count;

        if (dropped > 0) repairs.Add($"{Characters(dropped)} without a usable name, server or date dropped.");
        if (duplicates > 0) repairs.Add($"{Characters(duplicates)} listed twice merged, the latest kept.");
        if (over > 0) repairs.Add($"{Characters(over)} seen longest ago dropped, past the {MaxCharacters} kept.");
        if (countsDropped > 0) repairs.Add($"{countsDropped} unusable {(countsDropped == 1 ? "count or corridor entry" : "counts or corridor entries")} dropped.");
        if (runsTrimmed > 0) repairs.Add($"{runsTrimmed} corridor {(runsTrimmed == 1 ? "entry" : "entries")} listed twice or past the {RosterRules.CorridorRunsKept} kept dropped.");
        if (repaired > 0) repairs.Add($"Class, combat power, cap or the order of corridor entries repaired on {Characters(repaired)}.");
        return (new RosterFile { Version = RosterFile.CurrentVersion, Characters = newest }, repairs);
    }

    private static string Characters(int count) => count == 1 ? "1 character" : $"{count} characters";

    // A character as it can be used, or null when it cannot: no usable name, server or date. Unusable counts go;
    // a class, combat power or cap out of range is reset.
    private static RosterCharacter? Clean(RosterCharacter character, DateTimeOffset now, ref int countsDropped, ref int runsTrimmed, ref int repaired)
    {
        if (!IsUsableText(character.Name, MaxNameLength, allowEmpty: false) || !IsUsableText(character.Server, MaxServerLength, allowEmpty: true)) return null;

        var odyle = CleanCount(character.Odyle, MaxOdyle, now, ref countsDropped);
        var nightmare = CleanCount(character.Nightmare, MaxCount, now, ref countsDropped);
        var ascension = CleanCount(character.Ascension, MaxCount, now, ref countsDropped);
        // Corridor entries: usable dates only (a null list reads as none), each once, oldest first, the most recent kept.
        var saved = character.CorridorRuns ?? [];
        List<DateTimeOffset> usable = [.. saved.Where(run => IsUsableDate(run, now))];
        List<DateTimeOffset> runs = [.. usable.Distinct().Order().TakeLast(RosterRules.CorridorRunsKept)];
        countsDropped += saved.Count - usable.Count;
        runsTrimmed += usable.Count - runs.Count;

        var lastSeen = character.LastSeen;
        if (!IsUsableDate(lastSeen, now))
        {
            DateTimeOffset?[] seen = [odyle?.SeenAt, nightmare?.SeenAt, ascension?.SeenAt];
            if (seen.Max() is not { } latest) return null;
            lastSeen = latest;
        }

        var classId = character.ClassId is >= 0 and <= 99 ? character.ClassId : 0;
        var className = IsUsableText(character.ClassName, MaxClassNameLength, allowEmpty: true) ? character.ClassName : string.Empty;
        var combatPower = character.CombatPower is >= 0 and <= MaxCombatPower ? character.CombatPower : 0;
        var cap = Enum.IsDefined(character.OdyleCap) ? character.OdyleCap : OdyleCapChoice.Automatic;
        if (classId != character.ClassId || className != character.ClassName || combatPower != character.CombatPower || cap != character.OdyleCap
            || (runs.Count == usable.Count && !runs.SequenceEqual(usable))) repaired++;

        return character with
        {
            ClassId = classId,
            ClassName = className,
            CombatPower = combatPower,
            OdyleCap = cap,
            LastSeen = lastSeen,
            Odyle = odyle,
            Nightmare = nightmare,
            Ascension = ascension,
            CorridorRuns = runs,
        };
    }

    // A count kept as it is, or null (counted) when a value or its date is out of range.
    private static RosterCount? CleanCount(RosterCount? count, int max, DateTimeOffset now, ref int dropped)
    {
        if (count is null) return null;
        if (count.First is >= 0 && count.First <= max && count.Second is >= 0 && count.Second <= max && IsUsableDate(count.SeenAt, now)) return count;
        dropped++;
        return null;
    }

    private static bool IsUsableDate(DateTimeOffset date, DateTimeOffset now) => date >= Earliest && date <= now + FutureSlack;

    // Text the page can show: within its length, and no control characters (a name upstream decodes has none).
    private static bool IsUsableText(string? text, int maxLength, bool allowEmpty) =>
        text is not null
        && (allowEmpty || !string.IsNullOrWhiteSpace(text))
        && text.Length <= maxLength
        && !text.Any(char.IsControl);
}
