using AionDpsMeter.Core.GameData.Services;

namespace ScarletMeter.Engine.Upstream.Demo;

/// <summary>
/// Checks <see cref="DemoRoster"/> against upstream's game data, so a game-data update that renames or drops an
/// entry the demo relies on fails loudly (at demo start, and in
/// DemoModeTests.RosterResolvesInUpstreamsGameDataWithSyntheticNames) instead of showing unknown skills.
/// </summary>
internal static class DemoRosterCheck
{
    /// <summary>Every mismatch, described; empty when the roster resolves fully.</summary>
    public static IEnumerable<string> Problems(GameDataProvider data)
    {
        var partyIds = DemoRoster.Party.Select(member => member.EntityId).ToHashSet();
        foreach (var combatant in DemoRoster.Combatants)
        {
            var who = combatant.Name ?? $"summon {combatant.EntityId}";
            if (data.Classes.GetById(combatant.ClassId) is null)
                yield return $"{who}: class {combatant.ClassId} is not in classes.json";

            // A summon's hits carry either its own class or its owner's, as the game's skill codes do.
            int? ownerClass = combatant.OwnerId is { } ownerId
                ? DemoRoster.Party.FirstOrDefault(member => member.EntityId == ownerId)?.ClassId
                : null;
            if (combatant.OwnerId is { } owner && !partyIds.Contains(owner))
                yield return $"{who}: owner {owner} is not in the party";

            foreach (var skill in combatant.Skills)
            {
                var resolved = data.GetSkillOrDefault(skill.Code);
                var skillClass = data.GetClassBySkillCode(skill.Code);
                if (resolved.Name.StartsWith("Unknown Skill", StringComparison.Ordinal) || resolved.Icon is null)
                    yield return $"{who}: skill code {skill.Code} has no name or icon in skills.json";
                if (skillClass is null || (skillClass.Id != combatant.ClassId && skillClass.Id != ownerClass))
                    yield return $"{who}: skill code {skill.Code} belongs to class {skillClass?.Id.ToString() ?? "none"}";
                if (data.IsHealingSkill(skill.Code))
                    yield return $"{who}: skill code {skill.Code} is a healing skill, which upstream ignores";
                if (skill.DotTicks > 0 && !data.IsDotDamageSkill(skill.Code))
                    yield return $"{who}: skill code {skill.Code} is not in dot_skill_ids.json";
            }
        }

        foreach (var buff in DemoRoster.Buffs)
        {
            if (!data.IsBuff(buff.Code))
                yield return $"buff code {buff.Code} is not a buff upstream accepts";
            if (!partyIds.Contains(buff.CasterId))
                yield return $"buff code {buff.Code}: caster {buff.CasterId} is not in the party";
        }

        foreach (var mobCode in DemoRoster.BossMobCodes)
        {
            // A dummy counts as a boss upstream but ends on a 10-second idle timeout; the demo needs a real boss.
            if (!data.IsBoss(mobCode) || data.IsDummy(mobCode))
                yield return $"mob code {mobCode} is not a boss in mobs.json";
        }

        if (DemoRoster.Party.Count(member => member.IsUser) != 1)
            yield return "the party must hold exactly one user";
    }
}
