namespace UltiSim.Scenarios.Top;

// Shared IDs and tunables for The Omega Protocol (Ultimate). Values that exist
// in more than one TOP phase live here; phase-specific Constants files keep
// only what is unique to that phase.
//
// Convention for consumers: scenarios use `using static <Phase>Constants;` for
// short-form access to phase-specific IDs, and reference shared values via the
// fully-qualified `TopConstants.<Group>.<Const>` path. This avoids the C#
// nested-class name collision that would otherwise occur if both files were
// imported with `using static`.
public static class TopConstants
{
    public const byte Level = 90;

    public static class BNpcBaseId
    {
        public const uint StarterOmega = 15720;       // boss-rank Omega-M (4000A63C)
        public const uint OmegaHelper = 9020;         // 0x233C — generic invisible helper
        public const uint BeetleHelper = 15724;       // 0x3D6C — beetle / sigma helper visual
        public const uint FinalHelper = 14669;        // 0x394D — wave cannon spinner / ultimate visual
        public const uint RightArmUnit = 15719;       // 0x3D67 — Hyper Pulse caster
    }

    public static class BNpcNameId
    {
        public const uint OmegaM = 12257;
        public const uint OmegaBeetle = 7695;
        public const uint OmegaFinal = 7636;
        public const uint RightArmUnit = 7638;        // 0x1DD6 — log name for BNpc 0x3D67
    }

    public static class ActionId
    {
        // Hello World family — Delta's puddle phase and Sigma's second half both use these.
        public const uint HelloNearWorld = 31625;
        public const uint HelloNearWorldJump = 31626;
        public const uint HelloDistantWorld = 33040;
        public const uint HelloDistantWorldJump = 33041;
        public const uint HelloWorldFail = 31627;             // Helper->self, range-100 wipe fired on any Hello World near/distant fail
    }

    public static class StatusId
    {
        public const ushort QuickeningDynamis = 3444; // 0xD74 — TOP-wide stack buff
        public const ushort VulnerabilityUp = 3366;   // 0xD26 — generic damage-taken-up; canonical TOP debuff for distance-fail / mistake taps
        public const ushort MagicVulnUp1 = 2941;      // 0xB7D — magic-specific vuln, single-stack lethal; used by Delta-specific abilities
        public const ushort HelloNearWorld = 3442;     // 'Hello, Near World'
        public const ushort HelloFarWorld = 3443;      // 'Hello, Distant World'
    }

    public static class TimelineId
    {
        public const ushort Spawn = 0x1E43;           // warp/warp_end
        public const ushort WarpOut = 0x1E39;         // warp/warp_start
    }

    public static class Geometry
    {
        public const float ArenaRadius = 20f;         // TOP arena ring
    }

    public static class BgmId
    {
        public const ushort TopP5 = 964;              // P5 content scene BGM
    }
}
