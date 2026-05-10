using System;
using System.Numerics;
using UltiSim.Core;

namespace UltiSim.Scenarios.Top.P5Delta;

// Phase-specific raw IDs and tunables for the TOP P5 Delta scenario. IDs that
// also exist in TOP P5 Sigma (or any other TOP phase) live in TopConstants —
// reach for them via the fully-qualified path `TopConstants.<Group>.<Const>`.
internal static class TopP5DeltaConstants
{
    public static class BNpcBaseId
    {
        public const uint OpticalUnit = 0x3D64;       // invisible marker for the eye, used as caster later
        public const uint RocketPunchYellow = 0x3D5D; // RocketPunch1 — color 0
        public const uint RocketPunchBlue = 0x3D5E;   // RocketPunch2 — color 1
        public const uint LeftArmUnit = 0x3D66;       // R1.680
        public const uint AlphaShield = 1026757;
    }

    public static class BNpcNameId
    {
        public const uint OpticalUnit = 7640;
        public const uint RocketPunch = 7696;
        public const uint LeftArmUnit = 7637;
        public const uint RightArmUnit = 7638;
    }

    public static class ActionId
    {
        public const uint RunMiDeltaVersion = 31624;
        public const uint PeripheralSynthesis = 31628;      // BeetleHelper visual, no cast
        public const uint DeltaExplosion = 31482;           // RocketPunch->location, 3s cast
        public const uint DeltaUnmitigatedExplosion = 31483;// RocketPunch->location, 3s cast — raidwide wipe when overlap check fails
        public const uint OpticalLaser = 31521;             // OpticalUnit line AOE through arena
        public const uint ArchivePeripheral = 32630;        // FinalHelper->self, no cast — spawns the rotating ring
        public const uint DeltaHyperPulseFirst = 31600;     // arm unit 2.5s cast, range 100 width 8 rect, baited on closest
        public const uint DeltaHyperPulseRest = 31601;      // arm unit no-cast follow-ups
        public const uint BeyondDefense = 31527;            // OmegaMHelper, 4.9s cast — visual jump-bait
        public const uint BeyondDefenseAOE = 31528;         // OmegaMHelper->player, no cast, range 5 circle
        public const uint PilePitch = 31529;                // OmegaMHelper->players, no cast, range 6 circle stack on closest
        public const uint SwivelCannonR = 31636;            // BeetleHelper->self, 10s cast, range 60 210° cone
        public const uint SwivelCannonL = 31637;            // BeetleHelper->self, 10s cast, range 60 210° cone
        public const uint OversampledWaveCannonAOE = 31597; // Helper->players, no cast, range 7 circle spread
        public const uint OversampledWaveCannonRight = 31638; // arm unit cone variant — right side
        public const uint OversampledWaveCannonLeft = 31639;  // arm unit cone variant — left side
        public const uint HwTetherBreak = 31587;              // Helper->self, no cast, range 100 circle — raidwide hit on tether break
        public const uint HwTetherFail = 32505;               // Helper->self, no cast, range 100 circle — wipe when a tether expires unbroken
        public const uint HelloWorldFail = 0;                 // TODO: find action ID — wipe cast fired on any Hello World puddle-chain failure
    }

    public static class TimelineId
    {
        public const ushort RocketPunchSpawn = 1340; // TODO: not the actual canon spawn-in.
    }

    public static class TetherId
    {
        public const ushort HWPrepLocal = 200;  // broken by moving close
        public const ushort HWPrepRemote = 201; // broken by moving away
        public const ushort HWLocal = 224;      // broken by moving close
        public const ushort HWRemote = 225;     // broken by moving away
    }

    public static class StatusId
    {
        public const ushort HWPrepLocalTether = 3503;  // 'local code smell'
        public const ushort HWPrepRemoteTether = 3441; // 'remote code smell'
        public const ushort HWLocalTether = 3529;      // 'local regression'
        public const ushort HWRemoteTether = 3530;     // 'remote regression'
        public const ushort HelloNearWorld = 3442;     // 'Hello, Near World'
        public const ushort HelloFarWorld = 3443;      // 'Hello, Distant World'
        public const ushort PlayerMonitorRight = 3452;
        public const ushort PlayerMonitorLeft = 3453;
        public const ushort TwiceComeRuin = 2534;      // pre-existing — any stack lethal
        public const ushort TriceComeRuin = 2530;      // applied per HW Tether Break hit; lethal pre-check at 2 stacks
        public const ushort MagicVulnUp2 = 3516;       // applied per HW Tether Break hit; lethal pre-check at 2 stacks
    }

    public static class LockonId
    {
        public const uint RotateCw = 156;  // Lockon row → vfx/lockon/eff/m0515_turning_right01c.avfx
        public const uint RotateCcw = 157; // Lockon row → vfx/lockon/eff/m0515_turning_left01c.avfx
    }

    public static class Geometry
    {
        public const float PunchBackDistance = 2f;          // how far behind player to place punch
        public const float HyperPulseStep = MathF.PI / 9f;  // 20° in radians
        public const float HwTetherBreakDistance = 10f;     // remote (short) breaks above; local (long) breaks below
        public const float RocketPunchAoeRadius = 3f;       // Action 31482 EffectRange — radius of each Rocket Punch puddle; pairs must stack within this
        public const float BeyondDefenseAoeRadius = 5f;     // Action 31528 EffectRange — circle centered on the jump target
        public const float OversampledWaveCannonAoeRadius = 7f; // Action 31597 EffectRange — spread circle per targeted player
        public const float PilePitchAoeRadius = 6f;          // Action 31529 EffectRange — stack circle on closest player
        public const float HelloWorldInitialAoeRadius = 8f;  // Action 31625/33040 EffectRange
        public const float HelloWorldJumpAoeRadius = 4f;     // Action 31626/33041 EffectRange
        public const float SwivelCannonRange = 60f;          // Action 31636/31637 range
        public const float SwivelCannonHalfAngle = MathF.PI * 7f / 12f; // 210° cone → 105° half-angle
        public const float HyperPulseHalfWidth = 4f;        // Action 31600/31601 — rect AOE half-width (canon width 8)
        public const float HyperPulseLength = 100f;         // canon range — effectively edge-of-arena
        public const float OpticalLaserHalfWidth = 5f;      // Action 31521 — line beam half-width (tune to match canon)
        public const float OpticalLaserLength = 100f;       // effectively edge-to-edge

        public static readonly Placement[] ArmUnitPlacements =
        [
            new(new Vector3(-17.3205f, 0f, -10f), MathF.PI / 3f),      // NW
            new(new Vector3(-17.3205f, 0f,  10f), MathF.PI * 2f / 3f), // SW
            new(new Vector3(      0f, 0f, -20f), 0f),                  // N
            new(new Vector3(      0f, 0f,  20f), MathF.PI),            // S
            new(new Vector3( 17.3205f, 0f, -10f), MathF.PI * 5f / 3f), // NE
            new(new Vector3( 17.3205f, 0f,  10f), MathF.PI * 4f / 3f), // SE
        ];
    }

    public static class Duration
    {
        public const float HelloWorldDebuff = 44f;
        public const float MonitorHelperLifetime = 5f;
        public const float HwTetherBreakStack = 0.96f;      // Trice Come Ruin / Magic Vuln Up applied per HW break hit
    }
}
