using System;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.SimObjects;
using static UltiSim.Scenarios.Top.P5Delta.TopP5DeltaConstants.Geometry;

namespace UltiSim.Scenarios.Top.P5Delta;

// First AI strategy for TOP P5 Delta. Reads the shared TopP5DeltaState so its
// movement decisions stay in sync with the scenario's randomized layout, and
// schedules movement through World.Events so it can react to fight timestamps.
public sealed class TopP5DeltaAi
{
    // Tether-resolve positions in scenario-local coords (origin (0,0) = world (100,100)).
    // Even slots are caller-specified, odd slots are mirrored across the east-west axis
    // (same X, negated Z) so each pair lands on opposite sides. EyeSpawn==South flips
    // the entire layout 180° (negate both X and Z).

    private readonly TopP5DeltaState state;

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.5f, InitialPositions);
        ai.Move(13f, TetherPrePosition);
        ai.Move(21f, FistResolveSlots);
        ai.Move(28.8f, TetherResolveStep);
        ai.Move(31.2f, HyperPulseBaitArms);
        ai.Move(36.2f, HyperPulseDodge, 0);
        ai.Move(38.4f, MonitorPositions);
        ai.Move(40, MonitorAdjustment, 0);
        ai.Move(46f, SwivelDodge);
        ai.Move(50f, RescueUnsafe);
        ai.Move(56f, ReturnToMiddle);
        ai.Move(56.5f, TankForward);
        ai.Move(60.5f, BreakLastTether);
    }


    public TopP5DeltaAi(TopP5DeltaState state)
    {
        this.state = state;
    }

    private void Swap01(AiMove move)
    {
        if (state.FistColors[0] == state.FistColors[2])
            move.Swap(0, 1);
    }

    private void Swap45(AiMove move)
    {
        if (state.FistColors[4] == state.FistColors[6])
            move.Swap(4, 5);
    }

    private void BeyondDefence(AiMove move)
    {
        Plugin.Log.Info($"Beyond defense index: {state.BeyondDefenseIndex()}");
        move.Swap(0, state.BeyondDefenseIndex());
    }

    private void PlayerMonitorOffset(AiMove move)
    {
        move.AddY(state.PlayerMonitorIndex, 2);
    }

    private void PlayerMonitorFacing(AiMove move)
    {
        move.AddX(state.PlayerMonitorIndex, 1.2f * state.PlayerMonitorSide.Mul * state.OmegaMonitorSide.Mul);
    }

    private void OmegaMonitorSafeSide(AiMove move)
    {
        var mul = -state.OmegaMonitorSide.Mul * state.EyeSpawn.Mul;
        move.MultiplyY(0, mul);
        move.MultiplyY(1, mul);
        move.MultiplyY(2, mul);
        move.MultiplyY(3, mul);
    }

    private void SwivelSafeSide(AiMove move)
    {
        move.MultiplyY(state.SwivelCannonSide.Mul * state.EyeSpawn.Mul);
    }

    private void SwivelAdjustments(AiMove move)
    {
        move.Swap(0, state.FarWorldTetherIndex);
        move.Swap(state.FarWorldTetherIndex == 1 ? 0 : 1, state.NearWorldTetherIndex);
        // 4/5 does not to be adjusted by swivel side, so unadjust it
        move.MultiplyY(4, state.SwivelCannonSide.Mul * state.EyeSpawn.Mul);
        move.MultiplyY(5, state.SwivelCannonSide.Mul * state.EyeSpawn.Mul);
    }

    private void TetherAssignment(AiMove move)
    {
        move.Reorder(state.TetherOrder.Select(role => (int)role).ToArray());
    }

    private void AdjustEyePosition(AiMove move)
    {
        move.MultiplyX(state.EyeSpawn.Mul);
    }

    private AiMove InitialPositions()
    {
        return new AiMove(
            new(-2.10f, -5.08f),
            new(2.10f, -5.08f),
            new(-0.7f, 5.7f),
            new(-0.7f, 6.5f),
            new(-0.7f, 7.3f),
            new(0.7f, 5.7f),
            new(0.7f, 6.5f),
            new(0.7f, 7.3f)
        );
    }

    private AiMove TetherPrePosition()
    {
        return new AiMove(
            new(-6f, -3f),
            new(-6f, 3f),
            new(-10f, -7f),
            new(-10f, 7f),
            new(4f, -6f),
            new(4f, 6f),
            new(9.5f, -10f),
            new(9.5f, 10f)
        ).Apply(
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove FistResolveSlots()
    {
        return new AiMove(
            new Vector2(-10f, -3f),
            new Vector2(-10f, 3f),
            null,
            null,
            new Vector2(8.5f, -10f),
            new Vector2(8.5f, 10f),
            null,
            null
        ).Apply(
            AdjustEyePosition,
            Swap01,
            Swap45,
            TetherAssignment
        );
    }

    private AiMove TetherResolveStep()
    {
        return new AiMove(
            null, null,
            new(-10f, -3f),
            new(-10f, 3f),
            null, null, null, null
        ).Apply(AdjustEyePosition, TetherAssignment);
    }

    private AiMove HyperPulseBaitArms()
    {
        return new AiMove(
            ArmUnitPlacements.Select((placement, i) =>
                                         placement.MoveForward(0.5f)
                                                  .RotateAroundOrigin(
                                                      0.15f * state.ArmHandedness[i].Mul * state.EyeSpawn.Mul)
                                                  .Position2)
                             .Prepend(new Vector2(0f, 6f))
                             .Prepend(new Vector2(0f, -6f))
                             .Cast<Vector2?>()
                             .ToArray()
        ).Apply(
            AdjustEyePosition,
            Swap01,
            Swap45,
            TetherAssignment);
    }

    private AiMove HyperPulseDodge()
    {
        return new AiMove(
            new(13f, 1f), // beyond defense spot
            new(0, 1f),
            new(0, 1f),
            new(0, 1f),
            new(0, -12),
            new(0, 12),
            new(9, -10),
            new(9, 10)
        ).Apply(
            BeyondDefence,
            PlayerMonitorOffset,
            OmegaMonitorSafeSide,
            Swap45,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove MonitorPositions()
    {
        return new AiMove(
            null, null, null, null,
            new(-10, -12),
            new(-10, 12),
            new(10, -12),
            new(10, 12)
        ).Apply(
            Swap45,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove MonitorAdjustment()
    {
        return new AiMove(
            new(13f, 1f), // beyond defense spot
            new(0, 1f),
            new(0, 1f),
            new(0, 1f)
        ).Apply(
            BeyondDefence,
            PlayerMonitorOffset,
            PlayerMonitorFacing, // move monitor player a little so they face their monitor properly
            OmegaMonitorSafeSide,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove SwivelDodge()
    {
        return new AiMove(
            new(0f, 19f), // far
            new(0f, 6f),  // near
            new(9.5f, 17f),
            new(9.5f, 17f),
            new(-9.5f, -9.5f),
            new(-9.5f, 9.5f),
            new(16f, 10f),
            new(-19f, 1f)
        ).Apply(
            SwivelAdjustments,
            Swap45,
            SwivelSafeSide,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove RescueUnsafe()
    {
        return AiMove.Single(
            state.SwivelCannonSide.Mul * state.EyeSpawn.Mul > 0 ? 4 : 5,
            new(-9.5f, 3.5f)
        ).Apply(
            Swap45,
            SwivelSafeSide,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove ReturnToMiddle()
    {
        return new AiMove(
            new(-0.7f, 5.7f),
            new(-0.7f, 6.5f),
            new(-0.7f, 7.3f),
            new(0.7f, 5.7f),
            new(0.7f, 6.5f),
            new(0.7f, 7.3f),
            new(7f, 0f),
            new(-8f, -2f)
        ).Apply(
            SwivelSafeSide,
            AdjustEyePosition,
            TetherAssignment
        );
    }

    private AiMove TankForward()
    {
        return AiMove.Single(1, new(0, -3.5f)
        ).Apply(
            SwivelSafeSide,
            AdjustEyePosition
        );
    }

    private AiMove BreakLastTether()
    {
        return new AiMove(
            null, null, null, null, null, null,
            new(4f, 2.7f),
            new(-4f, 2.5f)
        ).Apply(
            SwivelSafeSide,
            AdjustEyePosition,
            TetherAssignment
        );
    }
}
