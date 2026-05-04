using System;
using System.Collections.Generic;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.SimObjects;

namespace UltiSim.Scenarios.TopP5Delta;

// First AI strategy for TOP P5 Delta. Reads the shared TopP5DeltaState so its
// movement decisions stay in sync with the scenario's randomized layout, and
// schedules movement through World.Events so it can react to fight timestamps.
public sealed class TopP5DeltaAi
{
    private const float RunSpeed = 6f;             // ~standard FFXIV run
    private const float TankEdgeBuffer = 0.5f;     // tanks stop just past hitbox edge
    private const float ScatterMinBehind = 0.5f;   // gap behind boss back
    private const float ScatterDepth = 2f;         // 2u deep
    private const float ScatterHalfWidth = 1f;     // ±1u wide → 2x2 area
    private const float ArenaRadius = 20f;
    private const float ArenaEdgeBuffer = 0.5f;    // bait stands 0.5u inside arena edge
    private const float ArmBaitDistance = 1.5f;    // chord distance from arm to bait
    private const float BossHitboxRadius = 5f;     // BossP5 (R5.010) — boss spawns at origin facing north

    // Tether-resolve positions in scenario-local coords (origin (0,0) = world (100,100)).
    // Even slots are caller-specified, odd slots are mirrored across the east-west axis
    // (same X, negated Z) so each pair lands on opposite sides. EyeSpawn==South flips
    // the entire layout 180° (negate both X and Z).
    private static readonly Vector2[] TetherSlotsLocal =
    {
        new(-6f, -3f),  // 0
        new(-6f,  3f),  // 1 (mirror of 0)
        new(-10f, -7f), // 2
        new(-10f,  7f), // 3 (mirror of 2)
        new( 4f, -6f),  // 4
        new( 4f,  6f),  // 5 (mirror of 4)
        new( 9f, -10f), // 6
        new( 9f,  10f), // 7 (mirror of 6)
    };

    private readonly TopP5DeltaState state;
    private readonly Random rng = new();

    public TopP5DeltaAi(TopP5DeltaState state)
    {
        this.state = state;
    }

    public void Run(SimWorld world)
    {
        world.Events.Add(0.5f, () => MoveToInitPositions(world));
        // Tethers spawn at scenario t=11s (SpawnDeltaAdds → ApplyDeltaTethers);
        // resolve to assigned tether-stack positions 2s later.
        world.Events.Add(13f, () => MoveToLocalSlots(world, TetherSlotsLocal));
        // Fists (rocket punches) spawn at t=19s; 2s later collapse the inner pair
        // (0,1) onto the outer X line (-10), and stack the inner outer pair (4,5)
        // onto the (6,7) positions. Swap each pair when their colors match the
        // partner pair's leading color.
        world.Events.Add(21f, () => MoveToLocalSlots(world, FistResolveSlots()));
        // Real (resolve) tethers apply at t=29s. 0.5s later, slots 3 and 4 step
        // onto slots 1 and 2 (no-swap canonical positions).
        world.Events.Add(29.5f, () =>
        {
            MoveSlot(world, slotIndex: 2, local: new Vector2(-10f, -3f));
            MoveSlot(world, slotIndex: 3, local: new Vector2(-10f, 3f));
        });
        // PunchExplosion casts start at t=31s. 0.7s into the cast: slots 0/1 stack
        // on (0, ±6) (with FistColors[0]==[2] swap); the other six bait their arm
        // units 1.5u away, just inside the arena edge, on the rotation-into side.
        world.Events.Add(31.7f, () => MoveToHyperPulseBaitPositions(world));
        // HyperPulse cast omens appear at t=37s when arm units start casting.
        // 0.7s after: slots 4-7 retreat to corners, slots 0-3 stack vs the omega
        // monitor cleave (one of them got hit by Beyond Defense and goes to x=11).
        world.Events.Add(37.7f, () => MoveToHyperPulseDodge(world));
    }

    private void MoveToInitPositions(SimWorld world)
    {
        // Boss spawns at the scenario origin facing north (rot=π) — see
        // TopP5DeltaScenario.Run. forward = -Z, right = +X follow from rot=π.
        var bossPos = world.ScenarioOrigin;
        var forward = new Vector3(0f, 0f, -1f);
        var right = new Vector3(1f, 0f, 0f);
        var edgeDist = BossHitboxRadius + TankEdgeBuffer;

        var nnw = MathF.PI + MathF.PI / 8f;
        var nne = MathF.PI - MathF.PI / 8f;
        MoveRoleTo(world, PartyRole.MainTank, AngleOffset(bossPos, nnw, edgeDist));
        MoveRoleTo(world, PartyRole.OffTank, AngleOffset(bossPos, nne, edgeDist));

        // Everyone else scattered in a 2x2 box behind the boss.
        var backEdge = bossPos - forward * (BossHitboxRadius + ScatterMinBehind);

        foreach (PartyRole role in Enum.GetValues<PartyRole>())
        {
            if (role == PartyRole.MainTank || role == PartyRole.OffTank) continue;
            var member = world.Party.Get(role);
            if (member is not { IsAlive: true }) continue;

            var depth = (float)rng.NextDouble() * ScatterDepth;
            var side = ((float)rng.NextDouble() * 2f - 1f) * ScatterHalfWidth;
            var target = backEdge - forward * depth + right * side;
            target.Y = bossPos.Y;
            member.MoveTo(target, RunSpeed);
        }
    }

    // Each slot i drives the party member at TetherOrder[i]. Local coords are
    // scenario-relative; we add ScenarioOrigin to land in world space, with Y taken
    // from the origin so members stay on the floor. EyeSpawn==South flips the layout.
    private void MoveToLocalSlots(SimWorld world, IReadOnlyList<Vector2> localSlots)
    {
        for (int i = 0; i < localSlots.Count; i++)
            MoveSlot(world, i, localSlots[i]);
    }

    private void MoveSlot(SimWorld world, int slotIndex, Vector2 local)
    {
        var role = state.TetherOrder[slotIndex];
        var member = world.Party.Get(role);
        if (member is not { IsAlive: true }) return;

        var sign = state.EyeSpawn == NorthSouth.South ? -1f : 1f;
        var origin = world.ScenarioOrigin;
        var l = local * sign;
        var target = new Vector3(origin.X + l.X, origin.Y, origin.Z + l.Y);
        member.MoveTo(target, RunSpeed);
    }

    // Arm slot index → world angle (i * 60°, starting south). Indexes match the
    // order armUnits[] is filled in TopP5DeltaScenario.SpawnArmUnits.
    //   0 = S,  1 = SE,  2 = NE,  3 = N,  4 = NW,  5 = SW
    private const int ArmSouth = 0;
    private const int ArmSouthEast = 1;
    private const int ArmNorthEast = 2;
    private const int ArmNorth = 3;
    private const int ArmNorthWest = 4;
    private const int ArmSouthWest = 5;

    // After the HyperPulse omens appear: corner retreat for 4-7, monitor stack
    // for 0-3. Slots 4-7 take corners (10, ±10) and (-10, ±10); the (4,5)
    // assignment swaps when FistColors[4]==[6]. Slots 0-3 stack near the omega
    // (x=0); whichever slot was hit by Beyond Defense steps out to x=11. Y axis
    // is positive on the side opposite the omega cleave so the stack is safe.
    // The monitor player snaps to a fixed east/west facing on arrival so their
    // own cleave fires opposite to omega's.
    private void MoveToHyperPulseDodge(SimWorld world)
    {
        // Slots 4-7 → corners. MoveSlot applies the eye-south flip uniformly.
        var swap45 = state.FistColors[4] == state.FistColors[6];
        MoveSlot(world, 4, swap45 ? new Vector2(-10f,  10f) : new Vector2(-10f, -10f));
        MoveSlot(world, 5, swap45 ? new Vector2(-10f, -10f) : new Vector2(-10f,  10f));
        MoveSlot(world, 6, new Vector2(10f, -10f));
        MoveSlot(world, 7, new Vector2(10f,  10f));

        // Slots 0-3 → monitor stack. ySign flips when the omega cleave covers the
        // default (south, +Z) side; xSign flips with eye-south. The Beyond Defense
        // target steps out to x=11 (in the eye-flipped frame); everyone else x=0.
        var omegaCleavesSouth = OmegaCleavesSouth();
        var ySign = omegaCleavesSouth ? -1f : 1f;
        var xSign = state.EyeSpawn == NorthSouth.South ? -1f : 1f;
        var bdSlot = FindBeyondDefenseSlotIndex();
        
        var monitorRot = ComputeMonitorFacing(omegaCleavesSouth);
        var origin = world.ScenarioOrigin;

        for (int i = 0; i < 4; i++)
        {
            var role = state.TetherOrder[i];
            var member = world.Party.Get(role);
            if (member is not { IsAlive: true }) continue;

            var isBdTarget = i == bdSlot;
            var isMonitor = i == state.PlayerMonitorIndex;
            var x = isBdTarget ? 11f * xSign : 0f;
            var y = (isMonitor ? 3f : 1.5f) * ySign;

            var target = new Vector3(origin.X + x, origin.Y, origin.Z + y);
            if (isMonitor)
                member.MoveTo(target, RunSpeed, finalRotation: monitorRot);
            else
                member.MoveTo(target, RunSpeed);
        }
    }

    // Returns 0..3 if the BeyondDefense target role lives in slots 0-3, else -1.
    // (Slots 0/1 are always nearest to omega at the cast time, so this should
    // always hit; -1 falls back to "no slot is the BD target" gracefully.)
    private int FindBeyondDefenseSlotIndex()
    {
        if (state.BeyondDefenseTarget is not { } role) return -1;
        for (int i = 0; i < 4; i++)
            if (state.TetherOrder[i] == role) return i;
        return -1;
    }

    // FinalHelper sits on the eye-opposite side and faces center, so its right
    // vector points north when eye=N (FH at east, facing west) and south when
    // eye=S (FH at west, facing east). OmegaMonitorSide=Right cleaves into that
    // right vector; Left cleaves opposite. Returns true when the cleave hits
    // the +Z (south) half of the arena.
    private bool OmegaCleavesSouth()
        => state.EyeSpawn == NorthSouth.North
            ? state.OmegaMonitorSide == Side.Left
            : state.OmegaMonitorSide == Side.Right;

    // Player should cleave opposite of omega. PlayerMonitorSide picks which side
    // of their facing fires the cleave; we pick the facing so that side ends up
    // pointing at the desired north/south. East-facing rot=π/2 has right=south,
    // left=north; west-facing rot=-π/2 has right=north, left=south.
    private float ComputeMonitorFacing(bool omegaCleavesSouth)
    {
        var playerCleavesSouth = !omegaCleavesSouth;
        var faceEast = (state.PlayerMonitorSide == Side.Right) == playerCleavesSouth;
        return faceEast ? MathF.PI / 2f : -MathF.PI / 2f;
    }

    // Slots 0/1 stack at (0, ±6) with the same swap toggle as fist-resolve.
    // Slots 2/3 take east arms, 4/5 take N/S arms (with 4↔5 swap toggle), 6/7
    // take west arms. Arm assignments are written for the eye=north layout;
    // MoveSlotToArmBait mirrors the arm index when eye=south so each slot baits
    // the arm closest to its (mirrored) post-fist-resolve position.
    private void MoveToHyperPulseBaitPositions(SimWorld world)
    {
        var swap01 = state.FistColors[0] == state.FistColors[2];
        MoveSlot(world, 0, swap01 ? new Vector2(0f,  6f) : new Vector2(0f,  -6f));
        MoveSlot(world, 1, swap01 ? new Vector2(0f, -6f) : new Vector2(0f,  6f));

        var swap45 = state.FistColors[4] == state.FistColors[6];
        MoveSlotToArmBait(world, slotIndex: 2, armIndex: ArmNorthWest);
        MoveSlotToArmBait(world, slotIndex: 3, armIndex: ArmSouthWest);
        MoveSlotToArmBait(world, slotIndex: 4, armIndex: swap45 ? ArmSouth : ArmNorth);
        MoveSlotToArmBait(world, slotIndex: 5, armIndex: swap45 ? ArmNorth : ArmSouth);
        MoveSlotToArmBait(world, slotIndex: 6, armIndex: ArmNorthEast);
        MoveSlotToArmBait(world, slotIndex: 7, armIndex: ArmSouthEast);
    }

    // Bait stands at radius (ArenaRadius - ArenaEdgeBuffer), offset from the arm's
    // radial angle by δ on the rotation-into side. δ solved from law-of-cosines so
    // arm↔bait distance = ArmBaitDistance (chord across two different radii).
    // LeftArm rotates CCW (angle increases) ⇒ bait on +δ side; RightArm ⇒ -δ.
    // Arms spawn at fixed world angles regardless of eye spawn, but slot positions
    // mirror through the origin when eye=south, so each slot baits the arm 180°
    // opposite its eye-north assignment (and uses that arm's own handedness).
    // Result is in absolute world space — bypass MoveSlot to skip the eye-flip.
    private void MoveSlotToArmBait(SimWorld world, int slotIndex, int armIndex)
    {
        var role = state.TetherOrder[slotIndex];
        var member = world.Party.Get(role);
        if (member is not { IsAlive: true }) return;

        var effectiveArm = state.EyeSpawn == NorthSouth.South ? (armIndex + 3) % 6 : armIndex;
        var armAngle = effectiveArm * MathF.PI / 3f;
        var rPlayer = ArenaRadius - ArenaEdgeBuffer;
        var cosDelta = (ArenaRadius * ArenaRadius + rPlayer * rPlayer - ArmBaitDistance * ArmBaitDistance)
                       / (2f * ArenaRadius * rPlayer);
        cosDelta = Math.Clamp(cosDelta, -1f, 1f);
        var delta = MathF.Acos(cosDelta);
        var sideSign = state.ArmHandedness[effectiveArm] == Side.Left ? -1f : +1f;
        var playerAngle = armAngle + sideSign * delta;

        var origin = world.ScenarioOrigin;
        var target = new Vector3(
            origin.X + MathF.Sin(playerAngle) * rPlayer,
            origin.Y,
            origin.Z + MathF.Cos(playerAngle) * rPlayer);
        member.MoveTo(target, RunSpeed);
    }

    // Slots 0/1 collapse west onto X=-10 (in line with 2/3); slots 4/5 stack onto
    // 6/7's positions. Pairs swap when their color matches the partner pair's
    // leading color (FistColors[0]==[2] for 0↔1, FistColors[4]==[6] for 4↔5).
    private IReadOnlyList<Vector2> FistResolveSlots()
    {
        var swap01 = state.FistColors[0] == state.FistColors[2];
        var swap45 = state.FistColors[4] == state.FistColors[6];
        return new[]
        {
            swap01 ? new Vector2(-10f,  3f) : new Vector2(-10f, -3f), // 0
            swap01 ? new Vector2(-10f, -3f) : new Vector2(-10f,  3f), // 1
            TetherSlotsLocal[2],
            TetherSlotsLocal[3],
            swap45 ? new Vector2(  9f,  10f) : new Vector2(  9f, -10f), // 4
            swap45 ? new Vector2(  9f, -10f) : new Vector2(  9f,  10f), // 5
            TetherSlotsLocal[6],
            TetherSlotsLocal[7],
        };
    }

    private static void MoveRoleTo(SimWorld world, PartyRole role, Vector3 target)
    {
        var member = world.Party.Get(role);
        if (member is not { IsAlive: true }) return;
        member.MoveTo(target, RunSpeed);
    }

    private static Vector3 AngleOffset(Vector3 center, float angle, float distance)
        => new(center.X + MathF.Sin(angle) * distance,
               center.Y,
               center.Z + MathF.Cos(angle) * distance);
}
