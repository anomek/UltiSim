using System;
using System.Collections.Generic;
using System.Linq;
using UltiSim.Core;

namespace UltiSim.Scenarios.Top.P5Sigma;

// Eight cardinal/intercardinal direction with rotation in radians measured
// from north (clockwise). Index 0..7 maps N, NE, E, SE, S, SW, W, NW.
public sealed record EightWayDirection(string Name, float RadiansFromNorth, int Index)
{
    public static readonly EightWayDirection N  = new("N",  0f,                           0);
    public static readonly EightWayDirection NE = new("NE", MathF.PI * 1f / 4f,           1);
    public static readonly EightWayDirection E  = new("E",  MathF.PI * 2f / 4f,           2);
    public static readonly EightWayDirection SE = new("SE", MathF.PI * 3f / 4f,           3);
    public static readonly EightWayDirection S  = new("S",  MathF.PI * 4f / 4f,           4);
    public static readonly EightWayDirection SW = new("SW", MathF.PI * 5f / 4f,           5);
    public static readonly EightWayDirection W  = new("W",  MathF.PI * 6f / 4f,           6);
    public static readonly EightWayDirection NW = new("NW", MathF.PI * 7f / 4f,           7);
    public static readonly EightWayDirection[] All = [N, NE, E, SE, S, SW, W, NW];
}

public sealed class TopP5SigmaState
{
    private readonly Random rng = new();

    public IReadOnlyList<PartyRole> SigmaOrder { get; }
    public IReadOnlyList<bool> QuickenedSlots { get; }              // length 8 — true means starts with QuickeningDynamis
    public IReadOnlyList<bool> PairIsTarget { get; }                // length 4 — true means this pair receives a tower target
    public IReadOnlyList<bool> NonTargetMemberIsFirst { get; }      // length 4 — for non-target pairs, which member of the pair is the actual non-target

    public EightWayDirection NewNorthA { get; }
    public CloseFar CloseFarTether { get; }
    public bool TowerNorthFlipped { get; }
    public EightWayDirection NewNorthB { get; }
    public Rotation SpinnerRotation { get; }
    public OmegaFForm OmegaFForm { get; }

    public TopP5SigmaState(TopP5SigmaStateOverrides overrides, PartyRole playerRole)
    {
        SigmaOrder = ShuffleRoles();

        var quickened = new bool[8];
        // Six of eight start with the buff — pick two slots without it.
        var withoutBuff = new HashSet<int>();
        while (withoutBuff.Count < 2) withoutBuff.Add(rng.Next(8));
        for (int i = 0; i < 8; i++) quickened[i] = !withoutBuff.Contains(i);
        QuickenedSlots = quickened;

        // Two of four pairs are tower targets, two are not.
        var pairTarget = new bool[4];
        var nonTargetIdx = new HashSet<int>();
        while (nonTargetIdx.Count < 2) nonTargetIdx.Add(rng.Next(4));
        for (int i = 0; i < 4; i++) pairTarget[i] = !nonTargetIdx.Contains(i);
        PairIsTarget = pairTarget;

        var nonTargetFirst = new bool[4];
        for (int i = 0; i < 4; i++) nonTargetFirst[i] = rng.Next(2) == 0;
        NonTargetMemberIsFirst = nonTargetFirst;

        NewNorthA = overrides.NewNorthA ?? RandomDirection();
        CloseFarTether = overrides.CloseFarTether ?? (rng.Next(2) == 0 ? CloseFar.Close : CloseFar.Far);
        TowerNorthFlipped = overrides.TowerNorthFlip switch
        {
            TriOption.Yes => true,
            TriOption.No  => false,
            _             => rng.Next(2) == 0,
        };
        NewNorthB = overrides.NewNorthB ?? RandomDirection();
        SpinnerRotation = overrides.SpinnerRotation ?? (rng.Next(2) == 0 ? Rotation.Clockwise : Rotation.CounterClockwise);
        OmegaFForm = overrides.OmegaFForm ?? (rng.Next(2) == 0 ? OmegaFForm.LegBlades : OmegaFForm.Staff);
    }

    private EightWayDirection RandomDirection() => EightWayDirection.All[rng.Next(8)];

    private PartyRole[] ShuffleRoles()
    {
        var roles = (PartyRole[])Enum.GetValues(typeof(PartyRole));
        for (int i = roles.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (roles[i], roles[j]) = (roles[j], roles[i]);
        }
        return roles;
    }
}
