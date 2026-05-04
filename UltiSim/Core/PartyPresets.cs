using System.Collections.Generic;

namespace UltiSim.Core;

// Hardcoded preset for the standard 8-job party of female Lalafell stand-ins
// dressed in their respective level-50 artifact (AF1) gear.
//
// Standard is laid out in PartyRole order — index N == (PartyRole)N.
// ForPlayerJob returns an 8-element array where the slot the local player
// fills is null, so role indices stay stable across the skip.
public static class PartyPresets
{
    public static IReadOnlyList<PartyMemberPreset?> ForPlayerJob(uint playerJob)
    {
        var skip = SkipRoleForJob(playerJob);
        var result = new PartyMemberPreset?[Standard.Length];
        for (int i = 0; i < Standard.Length; i++)
            result[i] = (PartyRole)i == skip ? null : Standard[i];
        return result;
    }

    // Tanks default to skipping Warrior (PLD player swaps to skipping Paladin).
    // Melee defaults to skipping Monk (MNK player skips Dragoon instead).
    // Casters and unknown jobs fall through to skipping Black Mage.
    private static PartyRole SkipRoleForJob(uint job) => job switch
    {
        19 => PartyRole.OffTank,                              // PLD → skip PLD slot
        21 or 32 or 37 => PartyRole.MainTank,                 // WAR / DRK / GNB
        24 or 33 => PartyRole.RegenHealer,                    // WHM / AST
        28 or 40 => PartyRole.ShieldHealer,                   // SCH / SGE
        20 => PartyRole.MeleeDpsA,                            // MNK → skip Dragoon slot
        22 or 30 or 34 or 39 or 41 => PartyRole.MeleeDpsB,    // DRG / NIN / SAM / RPR / VPR → skip Monk slot
        23 or 31 or 38 => PartyRole.PhysRangedDps,            // BRD / MCH / DNC
        _ => PartyRole.CasterDps,
    };

    public static readonly PartyMemberPreset[] Standard =
    [
        new("Warrior", ClassJob: 21, Level: 90,
            Head: 2899, Body: 3222, Hands: 3684, Legs: 3460, Feet: 3891), // Fighter's
        new("Paladin", ClassJob: 19, Level: 90,
            Head: 2897, Body: 3220, Hands: 3682, Legs: 3458, Feet: 3889), // Gallant
        new("White Mage", ClassJob: 24, Level: 90,
            Head: 2902, Body: 3225, Hands: 3687, Legs: 3463, Feet: 3894), // Healer's
        new("Scholar", ClassJob: 28, Level: 90,
            Head: 2905, Body: 3228, Hands: 3689, Legs: 3466, Feet: 3897), // Scholar's
        new("Dragoon", ClassJob: 22, Level: 90,
            Head: 2900, Body: 3223, Hands: 3685, Legs: 3461, Feet: 3892), // Drachen
        new("Monk", ClassJob: 20, Level: 90,
            Head: 2898, Body: 3221, Hands: 3683, Legs: 3459, Feet: 3890), // Temple
        new("Bard", ClassJob: 23, Level: 90,
            Head: 2901, Body: 3224, Hands: 3686, Legs: 3462, Feet: 3893), // Choral
        new("Black Mage", ClassJob: 25, Level: 90,
            Head: 2903, Body: 3226, Hands: 3690, Legs: 3464, Feet: 3895), // Wizard's
    ];
}

public sealed record PartyMemberPreset(
    string Name,
    byte ClassJob,
    byte Level,
    uint Head,
    uint Body,
    uint Hands,
    uint Legs,
    uint Feet);
