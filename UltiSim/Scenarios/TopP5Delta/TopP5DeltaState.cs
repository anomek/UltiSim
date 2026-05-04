using System;
using System.Collections.Generic;
using UltiSim.Core;
using static UltiSim.Scenarios.TopP5Delta.TopP5DeltaConstants;

namespace UltiSim.Scenarios.TopP5Delta;

public sealed class Side
{
    public uint DeltaOversampledWaveCannonActionId { get; }
    public uint SwivelCannonActionId { get; }
    public ushort MonitorDebuffId { get; }
    public string MonitorVfxPath { get; }
    public int Mul { get; }
    public uint ArmUnitId { get; }
    public uint ArmUnitNameId { get; }
    public uint RotateLockonId { get; }

    private Side(uint waveCannonActionId, uint swivelCannonActionId, ushort monitorDebuffId, string monitorVfxPath, int mul, uint armUnitId, uint armUnitNameId, uint rotateLockonId)
    {
        DeltaOversampledWaveCannonActionId = waveCannonActionId;
        SwivelCannonActionId = swivelCannonActionId;
        MonitorDebuffId = monitorDebuffId;
        MonitorVfxPath = monitorVfxPath;
        Mul = mul;
        ArmUnitId = armUnitId;
        ArmUnitNameId = armUnitNameId;
        RotateLockonId = rotateLockonId;
    }

    // StatusLoopVFX rows 534/535 → VFX rows 1591/1592 → these files. The m0771
    // prefix on the filename is just naming convention; the file lives under common/.
    public static readonly Side Right = new(31638, 31636, 3452, "vfx/common/eff/m0771stlp3c0c.avfx", -1, BNpcBaseId.RightArmUnit, BNpcNameId.RightArmUnit, LockonId.RotateCw);
    public static readonly Side Left = new(31639, 31637, 3453, "vfx/common/eff/m0771stlp4c0c.avfx", 1, BNpcBaseId.LeftArmUnit, BNpcNameId.LeftArmUnit, LockonId.RotateCcw);
}

public enum NorthSouth 
{
    North = 1,
    South = -1
}

public sealed class TopP5DeltaState
{
    public IReadOnlyList<PartyRole> TetherOrder { get; }
    public NorthSouth EyeSpawn { get; }
    public IReadOnlyList<int> FistRotations { get; }  // length 6, three +1 and three -1
    public IReadOnlyList<int> FistColors { get; }     // length 8, each half has two 0s and two 1s
    public IReadOnlyList<Side> ArmHandedness { get; } // length 6, three Left and three Right
    public Side SwivelCannonSide { get; }
    public Side OmegaMonitorSide { get; }
    public Side PlayerMonitorSide { get; }
    public int PlayerMonitorIndex { get; }   // 0..3
    public int NearWorldTetherIndex { get; } // 0..3, distinct from FarWorldTetherIndex
    public int FarWorldTetherIndex { get; }  // 0..3, distinct from NearWorldTetherIndex

    // Set by the scenario when BeyondDefenseAOE picks its target. The AI uses it
    // to position the targeted slot away from the rest of the monitor stack.
    public PartyRole? BeyondDefenseTarget { get; set; }

    public TopP5DeltaState(TopP5DeltaStateOverrides overrides, PartyRole playerRole)
    {
        var rng = new Random();

        var roles = ShuffleRoles(rng);
        // Force-player-on-monitor pins the player to TetherOrder[0] (and below sets
        // PlayerMonitorIndex=0). Swap the player's role into slot 0 instead of biasing
        // the shuffle so the rest of the order stays uniformly random.
        if (overrides.ForcePlayerOnMonitor)
        {
            var idx = Array.IndexOf(roles, playerRole);
            if (idx > 0) (roles[0], roles[idx]) = (roles[idx], roles[0]);
        }

        TetherOrder = roles;

        EyeSpawn = overrides.EyeSpawn ?? (rng.Next(2) == 0 ? NorthSouth.North : NorthSouth.South);
        FistRotations = ShuffleInPlace(new[] { 1, 1, 1, -1, -1, -1 }, rng);
        ArmHandedness = ShuffleSides(rng);

        var colors = new int[8];
        var first = ShuffleInPlace(new[] { 0, 0, 1, 1 }, rng);
        var second = ShuffleInPlace(new[] { 0, 0, 1, 1 }, rng);
        Array.Copy(first, 0, colors, 0, 4);
        Array.Copy(second, 0, colors, 4, 4);
        FistColors = colors;

        SwivelCannonSide = overrides.SwivelCannonSide ?? RandomSide(rng);
        OmegaMonitorSide = RandomSide(rng);
        PlayerMonitorSide = RandomSide(rng);
        PlayerMonitorIndex = overrides.ForcePlayerOnMonitor ? 0 : rng.Next(4);

        var near = rng.Next(4);
        var far = rng.Next(3);
        if (far >= near) far++;
        NearWorldTetherIndex = near;
        FarWorldTetherIndex = far;
    }

    private static Side RandomSide(Random rng) => rng.Next(2) == 0 ? Side.Left : Side.Right;

    private static PartyRole[] ShuffleRoles(Random rng)
    {
        var roles = (PartyRole[])Enum.GetValues(typeof(PartyRole));
        for (int i = roles.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (roles[i], roles[j]) = (roles[j], roles[i]);
        }

        return roles;
    }

    private static Side[] ShuffleSides(Random rng)
    {
        var sides = new[] { Side.Left, Side.Left, Side.Left, Side.Right, Side.Right, Side.Right };
        for (int i = sides.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (sides[i], sides[j]) = (sides[j], sides[i]);
        }

        return sides;
    }

    private static int[] ShuffleInPlace(int[] values, Random rng)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }

        return values;
    }
}
