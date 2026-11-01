using AionDpsMeter.Services.Models;

namespace ScarletMeter.Engine.Upstream;

/// <summary>
/// A player's class as their own skills say it. Upstream gives a player id the class of the first skill it sees from it
/// and never replaces it (EntityTracker's GetOrCreateSessionPlayer and ApplyIdentity, v1.11.5.341; the game's party
/// records carry no class), so when the game hands an id another player had used to someone new, their name and combat
/// power follow and the class stays the first player's: a Cleric showed with a Ranger's class and icon in the overlay
/// and in saved fights, every hit of theirs a Cleric skill (Harry, 2026-11-02). A class skill's id names its class, so
/// the skills are the evidence that cannot go stale. <see cref="ClassSkillTap"/> applies this to the live meter,
/// <see cref="UpstreamMapping"/> to details and saved fights. Pure.
/// </summary>
internal static class ClassSkills
{
    /// <summary>The first class id (the Gladiator's, 11) and how many there are, to the Brawler's (19).</summary>
    public const int FirstClass = 11;
    public const int Classes = 9;

    // Upstream's range of player skills (DataValidationHelper.IsReasonableSkillCode, and SkillRepository's first skill
    // range): the id's first two digits are its class (GetBySkillCode, Skill.ClassId). Raw codes and the ids upstream
    // normalises them to agree on it. Ids from 10,000,000 (the Spiritmaster's spirit, and effects any class has) are left
    // out, as upstream leaves them out of IsClassSkill, and so are theostones (3,000,000 up): they say nothing of the
    // player's own class.
    private const long FirstClassSkill = 11_000_000;
    private const long PastClassSkills = 20_000_000;
    private const long PerClass = 1_000_000;

    /// <summary>Whether a class id is a player's class (11 to 19): not the spirit's (10), nor none (0).</summary>
    public static bool IsClass(int classId) => classId is >= FirstClass and < FirstClass + Classes;

    /// <summary>The class (11 to 19) a skill id or raw skill code belongs to, or 0 for any outside upstream's player skills.</summary>
    public static int ClassOf(long skillId) =>
        skillId is >= FirstClassSkill and < PastClassSkills ? (int)(skillId / PerClass) : 0;

    /// <summary>
    /// The class to show for a player upstream tracks under <paramref name="upstreamClassId"/>: the class whose skills did
    /// the most of the player's damage in <paramref name="skills"/> (one fight's skill rows, a summon group counting as its
    /// owner skill), or upstream's own when its skills did as much, or when no class skill did any damage. Null, damaged or
    /// negative entries count for nothing, and the sums saturate, so saved data cannot make it throw.
    /// </summary>
    public static int ClassFor(int upstreamClassId, IEnumerable<SkillStats?>? skills)
    {
        Span<long> damage = stackalloc long[Classes];
        foreach (var skill in skills ?? [])
        {
            if (skill is not { TotalDamage: > 0 } || ClassOf(skill.SkillId) is not (> 0 and var classId)) continue;
            ref var sum = ref damage[classId - FirstClass];
            sum = skill.TotalDamage > long.MaxValue - sum ? long.MaxValue : sum + skill.TotalDamage;
        }

        var best = 0;
        for (var i = 1; i < Classes; i++)
            if (damage[i] > damage[best]) best = i;
        if (damage[best] == 0) return upstreamClassId;
        return IsClass(upstreamClassId) && damage[upstreamClassId - FirstClass] == damage[best] ? upstreamClassId : best + FirstClass;
    }
}
