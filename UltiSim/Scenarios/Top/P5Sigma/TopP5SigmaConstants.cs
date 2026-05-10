using System;
using System.Numerics;
using UltiSim.Core;

namespace UltiSim.Scenarios.Top.P5Sigma;

// Phase-specific raw IDs and tunables for the TOP P5 Sigma scenario. IDs that
// also exist in TOP P5 Delta (or any other TOP phase) live in TopConstants —
// reach for them via the fully-qualified path `TopConstants.<Group>.<Const>`.
//
// Sourced from logs/pulls/TOP_pull_05_clear.log fragment 2026-05-05T01:23:02.7
// (sigma start - 2s) -> 2026-05-05T01:24:13.4 (Omega-M targetable=01).
internal static class TopP5SigmaConstants
{
    public static class BNpcBaseId
    {
        public const uint OmegaMClone = 15721;        // Omega-M intercardinal clones (4000A72B / 4000A72D);
                                                      // boss-rank starter is TopConstants.BNpcBaseId.StarterOmega
        public const uint OmegaFClone = 15722;        // Omega-F clones (4000A72A / 4000A72C)
        public const uint RearPowerUnit = 15706;      // Rear Lasers caster — TODO: confirm against sheet
    }

    public static class BNpcNameId
    {
        public const uint OmegaF = 12258;
    }

    public static class ActionId
    {
        public const uint RunMiSigmaVersion = 32788;        // 0x8014 — Omega-M, 4.7s cast
        public const uint Unknown7C01 = 31745;              // 0x7C01 — Omega-M instant; TODO identify
        public const uint Unknown7B42 = 31554;              // 0x7B42 — Omega-M instant; TODO identify
        public const uint ProgramLoop = 31640;              // 0x7B98 — sigma helper, the loop visual
        public const uint WaveCannon = 31603;               // 0x7B73 — spinner cast (7.7s)
        public const uint WaveCannonHit = 31604;            // 0x7B74 — tower-target wave-cannon hit
        public const uint SubjectSimulationF = 32559;       // 0x7F2F — Omega-M -> Omega-F transition
        public const uint Unknown7F30 = 32560;              // TODO identify
        public const uint Unknown7B14 = 31508;              // 0x7B14 — Omega-M clone instant; TODO
        public const uint Unknown7B15 = 31509;              // 0x7B15 — Omega clone instant; TODO
        public const uint Unknown7B16 = 31510;              // 0x7B16 — Omega-M instant; TODO
        public const uint Unknown7B20 = 31520;              // 0x7B20 — Omega-M instant; TODO
        public const uint Unknown7B43 = 31555;              // 0x7B43 — Omega-M instant; TODO
        public const uint HyperPulse = 31602;               // 0x7B72 — Right Arm Unit rect AOE
        public const uint Discharger = 31534;               // 0x7B2E — Omega-M, 3.1s cast, knockback raidwide
        public const uint StorageViolationStack = 31493;    // 0x7B05 — 2-target stack
        public const uint StorageViolationSpread = 31492;   // 0x7B04 — 1-target spread
        public const uint RearLasersStart = 31631;          // 0x7B8F — Rear Power Unit, first tick
        public const uint RearLasersTick = 31632;           // 0x7B90 — repeating ticks
        public const uint SuperliminalSteel = 31530;        // 0x7B2A — Omega-M, 1.2s cast
        public const uint SuperliminalSteelLeft = 31532;    // 0x7B2C — Omega-F left variant
        public const uint SuperliminalSteelRight = 31531;   // 0x7B2B — Omega-F right variant
    }

    public static class StatusId
    {
        public const ushort MidGlitch = 3427;         // 0xD63 — Sigma tether per-side debuff, 32s; applied to all 8 players ~0.7s before the type-35 visual
    }

    public static class TetherId
    {
        public const ushort SigmaPair = 222;          // 0x00DE — 4 player-to-player pairs at t=10.1
        public const ushort HyperPulseBait = 17;      // 0x0011 — Right Arm Unit bait tether
    }

    public static class Geometry
    {
        // Sigma tether (Mid Glitch) safe-distance band. Vulnerability Up applies
        // when paired players are outside [Min, Max] and is removed when they
        // re-enter. Bounds derived from the TOP_pull_05_clear log by reading
        // the pair distance at every D26 (Vulnerability Up) apply event:
        //
        //   too-close apply edges (t=11.10, all pairs stacked at start):
        //     A 2.53,  B 7.11,  C 6.91,  D 4.97  → safe-min boundary > 7.11
        //   too-far apply edges (t=31.23–33.51, pairs spread):
        //     A 23.33, B 21.60, C 23.44, D 23.53 → safe-max boundary < 21.60
        //
        // So the in-fight safe band is somewhere inside (7.11, 21.60). Setting
        // bounds just outside the observed apply edges so the sim's punishment
        // zone matches the observed punishment zone with minimal over-extension.
        public const float SigmaTetherMinDistance = 8f;
        public const float SigmaTetherMaxDistance = 21f;
    }

    public static class Durations
    {
        // TODO: fill in convenience timings
    }
}
