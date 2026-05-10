using System.Numerics;
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

    public TopP5SigmaAi(TopP5SigmaState state)
    {
        this.state = state;
    }

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);

        // 0.5s — initial spread before the Sigma cast
        ai.Move(0.5f, () => new AiMove(
            new Vector2(0f, -5f),    // MainTank
            new Vector2(0f, 5f),     // OffTank
            new Vector2(-2f, 5f),    // RegenHealer
            new Vector2(2f, 5f),     // ShieldHealer
            new Vector2(-4f, 0f),    // MeleeDpsA
            new Vector2(4f, 0f),     // MeleeDpsB
            new Vector2(-2f, -5f),   // PhysRangedDps
            new Vector2(2f, -5f)));  // CasterDps

        // 10.1s — settle after sigma tether assignment (4 player-to-player pairs)
        ai.Move(10.1f, () => new AiMove(
            new Vector2(-6f, -3f),
            new Vector2(-6f,  3f),
            new Vector2(-10f, -7f),
            new Vector2(-10f,  7f),
            new Vector2( 6f, -3f),
            new Vector2( 6f,  3f),
            new Vector2( 10f, -7f),
            new Vector2( 10f,  7f)));

        // 28.1s — settle after Wave Cannon resolve (spinner first sweep)
        ai.Move(28.1f, () => new AiMove(
            new Vector2(0f,  -8f),
            new Vector2(0f,   8f),
            new Vector2(-8f, -8f),
            new Vector2(-8f,  8f),
            new Vector2( 8f, -8f),
            new Vector2( 8f,  8f),
            null,
            null));

        // 29.4s — settle after tower wave-cannon (4 tower targets baited)
        ai.Move(29.4f, () => new AiMove(
            new Vector2(0f, -10f),
            new Vector2(0f,  10f),
            new Vector2(-10f, 0f),
            new Vector2(10f,  0f),
            null, null, null, null));

        // 37.9s — settle after Discharger (raidwide knockback prep)
        ai.Move(37.9f, () => new AiMove(
            new Vector2(0f, -2f),
            new Vector2(0f,  2f),
            new Vector2(-2f, 0f),
            new Vector2(2f,  0f),
            new Vector2(-3f, -3f),
            new Vector2(3f, -3f),
            new Vector2(-3f, 3f),
            new Vector2(3f, 3f)));

        // 41.9s — settle after Storage Violation (stack + spread)
        ai.Move(41.9f, () => new AiMove(
            new Vector2(-7f, -7f),
            new Vector2( 7f, -7f),
            new Vector2(-7f,  7f),
            new Vector2( 7f,  7f),
            new Vector2(-12f, 0f),
            new Vector2( 12f, 0f),
            new Vector2(0f, -12f),
            new Vector2(0f,  12f)));

        // 53.2s — settle around boss for Rear Lasers dodge
        ai.Move(53.2f, () => new AiMove(
            new Vector2(0f, -5f),
            new Vector2(0f,  5f),
            new Vector2(-3f, -5f),
            new Vector2(3f, -5f),
            new Vector2(-3f,  5f),
            new Vector2(3f,  5f),
            new Vector2(-6f, 0f),
            new Vector2(6f,  0f)));

        // 66.2s — Hello World initial puddle setup
        ai.Move(66.2f, () => new AiMove(
            new Vector2(-13f,  0f),
            new Vector2( 13f,  0f),
            new Vector2(-10f, -10f),
            new Vector2(-10f,  10f),
            new Vector2( 10f, -10f),
            new Vector2( 10f,  10f),
            new Vector2(0f, -13f),
            new Vector2(0f,  13f)));
    }
}
