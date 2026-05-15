using System.Collections.Generic;

namespace UltiSim.Scenarios.Top.P5Sigma;

public static class TopP5SigmaConstants
{
    public static class BNpcBaseId
    {
        public const uint Omega = 15724;
        public const uint OmegaM = 15720;
        public const uint OmegaM_233C = 9020;
        public const uint Omega_394D = 14669;
        public const uint RearPowerUnit = 15723;
        public const uint RightArmUnit = 15719;
    }

    public static class BNpcNameId
    {
        public const uint Omega = 7695;
        public const uint OmegaF = 12258;
        public const uint OmegaM = 12257;
        public const uint Omega_1DD4 = 7636;
        public const uint RearPowerUnit = 7639;
        public const uint RightArmUnit = 7638;
    }

    public static class EObjId
    {
        public const uint EventObj1EA1A1 = 2007457;
        public const uint TowerTimer = 2013244;
        public const uint TowerSolo = 2013245;
        public const uint TowerPair = 2013246;
    }

    public static class ActionId
    {
        public const uint BallisticImpact = 0x7B0CU;
        public const uint BeyondDefense = 0x7B27U;
        public const uint BeyondDefense_7B28 = 0x7B28U;
        public const uint CriticalOverflowBug = 0x7B57U;
        public const uint CriticalSynchronizationBug = 0x7B56U;
        public const uint Discharger = 0x7B2EU;
        public const uint FlameThrower = 0x7E70U;
        public const uint HelloDistantWorld = 0x8111U;
        public const uint HelloDistantWorld_8110 = 0x8110U;
        public const uint HelloNearWorld = 0x7B89U;
        public const uint HelloNearWorld_7B8A = 0x7B8AU;
        public const uint HighPoweredSniperCannon = 0x7B54U;
        public const uint HyperPulse = 0x7B70U;
        public const uint HyperPulse_7B71 = 0x7B71U;
        public const uint HyperPulse_7B72 = 0x7B72U;
        public const uint OptimizedFireIII = 0x7B2FU;
        public const uint OversampledWaveCannon = 0x7B6DU;
        public const uint Patch = 0x7B63U;
        public const uint PilePitch = 0x7B29U;
        public const uint ProgramLoop = 0x7B98U;
        public const uint RearLasers = 0x7B8FU;
        public const uint RearLasers_7B90 = 0x7B90U;
        public const uint RunMiDeltaVersion = 0x7B88U;
        public const uint RunMiSigmaVersion = 0x8014U;
        public const uint SniperCannon = 0x7B53U;
        public const uint SolarRay = 0x81ACU;
        public const uint SolarRay_7B01 = 0x7B01U;
        public const uint StorageViolation = 0x7B05U;
        public const uint StorageViolation_7B04 = 0x7B04U;
        public const uint SubjectSimulationF = 0x7F2FU;
        public const uint SuperliminalSteel = 0x7B2AU;
        public const uint OptimizedBlizzard = 0x7B2D; 
        public const uint SuperliminalSteel_7B2B = 0x7B2BU;
        public const uint SuperliminalSteel_7B2C = 0x7B2CU;
        public const uint Unknown7b14 = 0x7B14U;
        public const uint Unknown7b15 = 0x7B15U;
        public const uint Unknown7b16 = 0x7B16U;
        public const uint Unknown7b20 = 0x7B20U;
        public const uint Teleport7b42 = 0x7B42U;
        public const uint Teleport7b43 = 0x7B43U;
        public const uint Unknown7b85 = 0x7B85U;
        public const uint Unknown7c01 = 0x7C01U;
        public const uint Unknown7f30 = 0x7F30U;
        public const uint WaveCannon = 0x7B73U;
        public const uint WaveCannonKyrios = 0x7B11U;
        public const uint WaveCannon_7B74 = 0x7B74U;
        public const uint WaveCannon_7B80 = 0x7B80U;
    }

    public static class StatusId
    {
        public const ushort HelloDistantWorld = (ushort)0xD73;
        public const ushort HelloNearWorld = (ushort)0xD72;
        public const ushort Looper = (ushort)0xD80;
        public const ushort MagicVulnerabilityUp = (ushort)0xB7D;
        public const ushort MagicVulnerabilityUp_DBC = (ushort)0xDBC;
        public const ushort MidGlitch = (ushort)0xD63;
        public const ushort FarGlitch = (ushort)0xD64;
        public const ushort OmegaF = (ushort)0x68B;
        public const ushort OmegaM = (ushort)0x68A;
        public const ushort QuickeningDynamis = (ushort)0xD74;
        public const ushort Superfluid = (ushort)0x68C;
        public const ushort ThriceComeRuin = (ushort)0x9E2;
        public const ushort TwiceComeRuin = (ushort)0x9E6;
        public const ushort VulnerabilityUp = (ushort)0xD26;
    }

    public static class TetherId
    {
        public const ushort AutoTarget = (ushort)0x11;
        public const ushort Glitch = (ushort)0xDE;
    }

    public static class TimelineId
    {
        public const ushort Spawn = (ushort)0x1E43;
        public const ushort WarpOut = (ushort)0x1E39;
    }

    public static class LockonId
    {
        public const uint PlaystationX = 419;
        public const uint PlaystationSq = 418;
        public const uint PlaystationO = 416;
        public const uint PlaystationTr = 417;
        
        public const uint X_9C = 156;
        public const uint X_9D = 157;
        public const uint WaveCannon = 244;

        public static readonly IReadOnlyList<uint> Playstation = [PlaystationX, PlaystationSq, PlaystationO, PlaystationTr];
    }
    
    public static class KnockbackId
    {
        public const uint Discharger = 72;
    }

    public static class Geometry
    {
        public const float MidGlitchMinDistance = 21f; // no idea how accurate this is
        public const float MidGlitchMaxDistance = 26f; // fine-tuned to be just right for towers and "in front of marker" position
        public const float FarGlitchMinDistance = 34f; // fine-tuned to be just right for towers
        public const float TowerRadius = 3f;

        // Superliminal Steel: each leg blade is one rect; together they form
        // opposing bands flanking Omega-F's facing axis. The SAFE strip
        // perpendicular to her facing is ~8m wide (4m half-width).
        public const float SuperliminalSteelSafeHalfWidth = 4f;

        // Optimized Blizzard III: + cross through Omega-F. Each arm matches
        // Superliminal Steel's blade width.
        public const float OptimizedBlizzardArmHalfWidth = 4f;

        // Both Omega-F attacks reach the arena edge in this phase.
        public const float OmegaFAttackHalfLength = 25f;
    }
    
    public static class Duration
    {
        public const float OmegaFAttackOmenDelay = 0.6f;
    }
}
