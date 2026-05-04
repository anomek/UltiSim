using System;

namespace UltiSim.Scenarios.TopP5Delta;

// All raw IDs and tunables for the TOP P5 Delta scenario, grouped by domain.
// Mirrors the bossmod TOPEnums layout so the scenario file stays declarative.
internal static class TopP5DeltaConstants
{
    public const byte Level = 90;

    public static class BNpcBaseId
    {
        public const uint StarterOmega = 15720;       // BossP5 — boss-rank, R5.010, gold body
        public const uint BeetleHelper = 0x3D6C;      // P5 beetle visual (ModelChara 3771)
        public const uint FinalHelper = 0x394D;       // P5 ultimate visual (ModelChara 3775)
        public const uint OpticalUnit = 0x3D64;       // invisible marker for the eye, used as caster later
        public const uint RocketPunchYellow = 0x3D5D; // RocketPunch1 — color 0
        public const uint RocketPunchBlue = 0x3D5E;   // RocketPunch2 — color 1
        public const uint LeftArmUnit = 0x3D66;       // R1.680
        public const uint RightArmUnit = 0x3D67;      // R1.680
        public const uint OmegaHelper = 0x233C;       // generic invisible Helper, used as AOE caster
        public const uint AlphaShield = 1026757;
    }

    public static class BNpcNameId
    {
        public const uint OmegaM = 12257;
        public const uint Omega = 7666;
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
    }

    public static class TimelineId
    {
        public const ushort Spawn = 0x1E43;          // warp/warp_end — BeetleHelper / FinalHelper spawn anim
        public const ushort RocketPunchSpawn = 1340; // TODO: not the actual canon spawn-in.
        public const ushort WarpOut = 0x1E39;        // warp/warp_start — despawn animation
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
    }

    public static class LockonId
    {
        public const uint RotateCw = 156;  // Lockon row → vfx/lockon/eff/m0515_turning_right01c.avfx
        public const uint RotateCcw = 157; // Lockon row → vfx/lockon/eff/m0515_turning_left01c.avfx
    }

    public static class Geometry
    {
        public const float ArenaRadius = 20f;
        public const float OpticalUnitDistance = 45f;       // canon: spawns well outside arena ring
        public const float PunchBackDistance = 2f;          // how far behind player to place punch
        public const float HyperPulseStep = MathF.PI / 9f;  // 20° in radians
    }

    public static class Duration
    {
        public const float HelloWorldDebuff = 45f;
        public const float MonitorHelperLifetime = 5f;
    }
}
