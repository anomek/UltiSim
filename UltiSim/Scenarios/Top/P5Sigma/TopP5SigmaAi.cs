using System.Collections.Generic;
using System.Linq;
using UltiSim.Core;
using UltiSim.Core.SimObjects;

namespace UltiSim.Scenarios.Top.P5Sigma;

// First-pass AI for TOP P5 Sigma. The auto-generated beats below come from
// the cast timeline at major resolve points; the user replaces the bare
// positions with state-aware ones (NewNorthA / SpinnerRotation / etc.) as
// they harden the choreography. See TopP5DeltaAi for the canonical shape
// once choreography matures (named methods, Apply-chained transforms).
public sealed class TopP5SigmaAi
{
    private readonly TopP5SigmaState state;
    private RoleList markingsOrder = null!;

    public TopP5SigmaAi(TopP5SigmaState state)
    {
        this.state = state;
    }

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        
;       var handBait = state.DynamisTargets.Random(2, state.HelloWorldRoles);
        var hWJumpsOrder = RoleList.AllExcept(world.Party, state.HelloWorldRoles.Concat(handBait.List).ToArray());
        markingsOrder = new(world.Party, [handBait[0], hWJumpsOrder[0], handBait[1], hWJumpsOrder[1],
                        hWJumpsOrder[2], hWJumpsOrder[3], state.HelloWorldRoles[0], state.HelloWorldRoles[1]]);

        ai.Move(0.5f, InitialPositions);
        ai.Move(14.1f, LineupNextToOmegaM);
        ai.Move(20f, WaveCannonSpread, arrivalTime: 29f);
        ai.Move(32f, KnockbackPrePosition);
        ai.Move(36f, KnockbackPosition, jitter: 0.1f, arrivalTime: 39.5f);
        ai.Move(41.5f, TowerPositions, jitter: 0.1f);
        ai.Automarker(43.5f, MarkerMapping);
        ai.Move(44f, InitialPositions, jitter: 3f);
        ai.Move(50f, RearLasersPrePosition, arrivalTime: 56.5f);
        ai.Move(58f, AdjustForLegs, arrivalTime: 61f);
        ai.Move(62f, HelloWorldPositions, arrivalTime: 66.5f);
        ai.Move(73f, InitialPositions);
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

    private AiMove LineupNextToOmegaM()
    {
        return new AiMove(
            new(-2, -18), new(2, -18),
            new(-2, -15), new(2, -15),
            new(-2, -13), new(2, -13),
            new(-2, -11), new(2, -11)
        ).Apply(
            state.NewNorthA.Apply,
            state.Order.Reorder);
    }

    private AiMove WaveCannonSpread()
    {
        return new AiMove(
            new(0f, -12.5f),  // N
            new(8.8f, -8.8f), // NE
            new(12.5f, 0f),   // E
            new(8.8f, 8.8f),  // SE
            new(0f, 12.5f),   // S
            new(-8.8f, 8.8f), // SW
            new(-12.5f, 0f),  // W
            new(-8.8f, -8.8f) // NW
        ).Apply(
            FarGlitchWaveCannonAdjustment,
            WaveCannonShuffle,
            state.NewNorthA.Apply,
            state.Order.Reorder);
    }

    private AiMove KnockbackPrePosition()
    {
        return new AiMove(
            new(0f, -4f),     // N (absolute)
            new(2.8f, -2.8f), // NE
            new(4f, 0f),      // E
            new(2.8f, 2.8f),  // SE
            new(0f, 4f),      // S
            new(-2.8f, 2.8f), // SW
            new(-4f, 0f),     // W
            new(-2.8f, -2.8f) // NW
        ).Apply(
            AbsoluteToRelativeClockSpot,
            WaveCannonShuffle,
            state.Order.Reorder);
    }

    private AiMove KnockbackPosition()
    {
        return (state.GlitchType == GlitchType.Mid
                    ? new AiMove(
                        new(-1.5f, 0.7f),  // A
                        new(-0.7f, -1.5f), // 1
                        new(-0.7f, 1.5f),  // B
                        new(0.7f, -1.5f),  // 2
                        new(0.7f, 1.5f),   // C
                        new(-1.5f, 0.7f),  //  3
                        new(1.5f, 0.7f),  //D
                        new(1.5f, 0.7f)   //4
                    )
                    : new AiMove(
                        new (-1.8f, 0),    // A
                        new (0, -1.8f),    // 1
                        new (-1.2f, 1.2f), // B
                        new (0, -1.8f),    // 2
                        new (1.2f, 1.2f),  // C
                        new (-1.2f, 1.2f), // 3
                        new(1.8f, 0),      // D
                        new (1.2f, 1.2f)   // 4
                    )
               ).Apply(
            AbsoluteToRelativeClockSpot,
            WaveCannonShuffle,
            state.AdjustedNorthA.Apply,
            state.Order.Reorder);
    }
    
    private AiMove TowerPositions()
    {
        return (state.GlitchType == GlitchType.Mid
                    ? new AiMove(
                        new(-15.7f, 6.5f),  // A
                        new(-6.5f, -15.7f), // 1
                        new(-6.5f, 15.7f),  // B
                        new(6.5f, -15.7f),  // 2
                        new(6.5f, 15.7f),   // C
                        new(-15.7f, 6.5f),  //  3
                        new(15.7f, 6.5f),   //D
                        new(15.7f, 6.5f)    //4
                    )
                    : new AiMove(
                        new (-19f, -1),    // A
                        new (1, -19f),    // 1
                        new (-13.7f, 13.1f), // B
                        new (-1, -19f),    // 2
                        new (13.1f, 13.7f),  // C
                        new (-13.1f, 13.7f), // 3
                        new(19f, -1),      // D
                        new (13.7f, 13.1f)   // 4
                    )
               ).Apply(
            AbsoluteToRelativeClockSpot,
            WaveCannonShuffle,
            state.AdjustedNorthA.Apply,
            state.Order.Reorder);
    }
    
    private Dictionary<PartyRole, Sign> MarkerMapping()
    {
        return new Dictionary<PartyRole, Sign>()
        {
            [markingsOrder[0]] = Sign.Attack1,
            [markingsOrder[1]] = Sign.Attack2,
            [markingsOrder[2]] = Sign.Attack3,
            [markingsOrder[3]] = Sign.Attack4,
            [markingsOrder[4]] = Sign.Attack5,
            [markingsOrder[5]] = Sign.Attack6,
            [markingsOrder[6]] = Sign.Triangle,
            [markingsOrder[7]] = Sign.Cross,
        };
    }
    
    private AiMove RearLasersPrePosition()
    {
        return new AiMove(
            new(6.5f, -17),
            new(6.5f, -17),
            new(6.5f, -17),
            new(-6.5f, 17),
            new(-6.5f, 17),
            new(-6.5f, 17),
            new(-6.5f, 17),
            new(-6.5f, 17)
        ).Apply(
            SpinnerRotation,
            state.NewNorthB.Apply,
            markingsOrder.Reorder
        );
    }
    
    private AiMove AdjustForLegs()
    {
        return state.OmegaFAttack == OmegaFAttack.Staff ? new AiMove()
                   : new AiMove(
                       new(2f, -18f),
                       new(2f, -18f),
                       new(2f, -18f),
                       new (-2f, 18f),
                       new (-2f, 18f),
                       new (-2f, 18f),
                       new (-2f, 18f),
                       new (-2f, 18f)
            ).Apply(
                SpinnerRotation,
                state.NewNorthB.Apply,
                markingsOrder.Reorder
        );
    }
    

    private AiMove HelloWorldPositions()
    {
        return new AiMove(
            new(-13.5f, -14.2f),
            new(0, -19.5f),
            new(13.5f, -14.2f),
            new(19.5f, 0),
            new(18.9f, 5),
            new(0, 19.5f),
            new(10f, 0),
            new(0f, 10f)
        ).Apply(
            SpinnerRotation,
            state.NewNorthB.Apply,
            markingsOrder.Reorder
        );
    }

    private void WaveCannonShuffle(AiMove move)
    {
        move.Reorder([0, 4, 7, 3, 1, 5, 6, 2]);

        if (state.FirstMissing % 2 == 1) move.Swap(2, 3);
        if (state.SecondMissing % 2 == 1) move.Swap(4, 5);

        HashSet<int> missingPairs = [state.FirstMissing / 2, state.SecondMissing / 2];
        var i = 0;
        while (missingPairs.Contains(i++)) move.SwapPair(i, i - 1);
        i = 3;
        while (missingPairs.Contains(i--)) move.SwapPair(i, i + 1);
    }

    private void AbsoluteToRelativeClockSpot(AiMove move)
    {
        move.OffsetOrder(state.NewNorthA.Index);
    }

    private void FarGlitchWaveCannonAdjustment(AiMove move)
    {
        if (state.GlitchType == GlitchType.Far)
        {
            move.Multiply(1.5f); // goes to wall
        }
    }
    
    private void SpinnerRotation(AiMove move)
    {
        move.MultiplyX(state.SpinnerRotation.Mul);
    }
}
