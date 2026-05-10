using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.Map;
using UltiSim.Core.SimObjects;
using UltiSim.Scenarios;
using static UltiSim.Scenarios.Top.P5Sigma.TopP5SigmaConstants;

namespace UltiSim.Scenarios.Top.P5Sigma;

public sealed class TopP5SigmaScenario : IScenario
{
    public string Name => "TOP P5 Sigma";

    public TargetInstance? TargetInstance { get; } = new(
        TerritoryId: 1122,
        Origin: new Vector3(100f, 0f, 100f),
        PlayerPosition: new Vector3(100f, 0f, 116f),
        WeatherId: 174);

    public IReadOnlyList<ScenarioOriginOverride> OriginOverrides { get; } = [
        new(TerritoryId: 801, X: 100f, Z: 100f),
        new(TerritoryId: 1045, X: 0f, Z: 0)
    ];

    public IReadOnlyList<Waymark> Waymarks { get; } = TopUtils.TopWaymarks;

    public ushort Bgm => TopConstants.BgmId.TopP5;

    public void DrawSettings() => settingsWindow.Draw();
    private readonly TopP5SigmaSettingsWindow settingsWindow = new();

    private TopP5SigmaState state = null!;
    private TopP5SigmaAi ai = null!;
    private SimWorld world = null!;
    private SimParty party = null!;

    private SimEnemy? omegaM;
    private SimEnemy? spinner;
    private SimEnemy? sigmaHelper;
    private SimEnemy? omegaMClone;
    private SimEnemy? omegaFCloneA;
    private SimEnemy? armUnitA;
    private SimEnemy? armUnitB;
    private SimTether? tetherUnitA;
    private SimTether? tetherUnitB;
    private SimEnemy? rearPower;
    private List<SimEnemy?>? towers;
    private readonly List<SimTether> sigmaTethers = new();

    public void Run(SimWorld worldParam, PartyRole playerRole)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP5SigmaState(settingsWindow.Overrides, playerRole);
        ai = new TopP5SigmaAi(state);
        ai.Run(world);

        // Sigma fragment timeline. Absolute literal timestamps only, sorted ascending.
        // Don't add events anywhere else unless there is no other way around it.

        world.Events.Add(0.0f, SpawnOmegaM);
        world.Events.Add(0.1f, ApplyQuickeningDynamis);
        world.Events.Add(0.4f, () => omegaM?.Cast(ActionId.Unknown7C01));            // TODO: identify
        world.Events.Add(0.7f, () => omegaM?.Cast(ActionId.Unknown7B42));            // TODO: identify
        world.Events.Add(1.0f, () => TopUtils.InitTopArena(world));
        world.Events.Add(2.0f, () => omegaM?.Cast(ActionId.RunMiSigmaVersion, castSeconds: 4.7f));
        // Log: 4000A63C goes untargetable at 01:23:12.79 (t=10.1) and immediately
        // plays the warp-out timeline (273 0197 1E39 at 01:23:12.83). Without the
        // PlayActionTimeline call the doppel just stands frozen + untargetable.
        world.Events.Add(10.1f, () => omegaM?.SetTargetable(false));
        world.Events.Add(10.1f, () => omegaM?.PlayActionTimeline(TopConstants.TimelineId.WarpOut));
        world.Events.Add(10.1f, ApplySigmaTethers);
        // Log: both arm units (4000A643 / 4000A644) warp in at 01:23:12.879 (t=10.2)
        // and tether to the furthest player at 01:23:13.502 (t=10.8). They cast
        // Hyper Pulse at t=29.2, then warp out at t=31.2 — first of two cycles.
        world.Events.Add(10.2f, SpawnArmUnits);
        world.Events.Add(10.8f, ApplyHyperPulseTethers);
        // Log: AddCombatant for both helpers fires at t=2.2 (preload), but the
        // visible warp-in (273 0197 1E43) is at 01:23:17.92 / 01:23:20.898 —
        // i.e. AFTER the boss warps out at t=10.1. Spawn at the warp-in moments.
        world.Events.Add(15.2f, SpawnSpinner);
        world.Events.Add(18.2f, SpawnSigmaHelper);
        world.Events.Add(20.4f, () => spinner?.Cast(ActionId.WaveCannon, castSeconds: 7.7f));
        world.Events.Add(24.4f, () => omegaM?.Cast(ActionId.SubjectSimulationF));
        world.Events.Add(25.4f, () => omegaMClone?.Cast(ActionId.Unknown7B14));      // TODO: identify
        world.Events.Add(25.9f, () => sigmaHelper?.Cast(ActionId.ProgramLoop));
        world.Events.Add(26.5f, () => omegaM?.Cast(ActionId.Unknown7B16));           // TODO: identify
        world.Events.Add(28.1f, ResolveWaveCannon);
        world.Events.Add(29.2f, FireHyperPulse);
        world.Events.Add(29.4f, SpawnTowers);
        world.Events.Add(29.4f, ResolveTowerWaveCannon);
        world.Events.Add(30.6f, () => omegaM?.Cast(ActionId.Unknown7F30));           // TODO: identify
        world.Events.Add(31.2f, () => { armUnitA?.Despawn(TopConstants.TimelineId.WarpOut, 1f); armUnitB?.Despawn(TopConstants.TimelineId.WarpOut, 1f); });
        // TODO: second arm-unit cycle — log shows warp-in at t=44.2, tether re-target,
        // second Hyper Pulse cast at t=66.2, final warp-out at t=68.3. Wire when ready.
        world.Events.Add(31.7f, SpawnOmegaClones);
        world.Events.Add(34.2f, () => towers?[0]?.Cast(ActionId.Unknown7B15));       // TODO: identify
        world.Events.Add(34.7f, () => omegaM?.Cast(ActionId.Unknown7B20));           // TODO: identify
        world.Events.Add(36.8f, () => omegaM?.Cast(ActionId.Unknown7B43));           // TODO: identify
        world.Events.Add(37.9f, () => omegaM?.Cast(ActionId.Discharger, castSeconds: 3.1f));
        world.Events.Add(41.9f, ResolveStorageViolation);
        world.Events.Add(53.2f, SpawnRearPowerUnit);
        world.Events.Add(53.2f, () => rearPower?.Cast(ActionId.RearLasersStart, castSeconds: 0.6f));
        world.Events.Add(53.8f, () => rearPower?.Cast(ActionId.RearLasersTick, castSeconds: 0.6f));
        world.Events.Add(54.4f, () => rearPower?.Cast(ActionId.RearLasersTick, castSeconds: 0.6f));
        world.Events.Add(55.0f, () => rearPower?.Cast(ActionId.RearLasersTick, castSeconds: 0.6f));
        world.Events.Add(55.6f, () => rearPower?.Cast(ActionId.RearLasersTick, castSeconds: 0.6f));
        world.Events.Add(56.2f, () => rearPower?.Cast(ActionId.RearLasersTick, castSeconds: 0.6f));
        world.Events.Add(57.9f, ResolveSuperliminalSteel);
        world.Events.Add(66.2f, ResolveHelloWorldInitial);
        world.Events.Add(67.2f, ResolveHelloWorldJump);
        // Log: 4000A63C plays warp-in (273 0197 1E43) at 01:24:10.348 (t=67.6)
        // and flips targetable=01 at 01:24:13.43 (t=70.7). Warp-in must precede
        // SetTargetable so the model is back before clicks land.
        world.Events.Add(67.6f, () => omegaM?.PlayActionTimeline(TopConstants.TimelineId.Spawn));
        world.Events.Add(70.7f, () => omegaM?.SetTargetable(true));
    }
    
    public void Tick(float delta, float elapsed)
    {
        List<SimTether> resolved = [];
        foreach (var tether in sigmaTethers)
        {
            if (tether.Resolved)
            {
                tether.A.RemoveStatus(TopConstants.StatusId.VulnerabilityUp);
                tether.B.RemoveStatus(TopConstants.StatusId.VulnerabilityUp);
                resolved.Add(tether);
            }
            if (tether.StretchLt(Geometry.SigmaTetherMinDistance) ||
                tether.StretchGt(Geometry.SigmaTetherMaxDistance))
            {
                tether.A.AddStatus(TopConstants.StatusId.VulnerabilityUp);
                tether.B.AddStatus(TopConstants.StatusId.VulnerabilityUp);
            }
            else
            {
                tether.A.RemoveStatus(TopConstants.StatusId.VulnerabilityUp);
                tether.B.RemoveStatus(TopConstants.StatusId.VulnerabilityUp);
            }
        }
        resolved.ForEach(t => sigmaTethers.Remove(t));
    }

    private void SpawnOmegaM()
    {
        omegaM = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.StarterOmega,
            NameId: TopConstants.BNpcNameId.OmegaM,
            Level: TopConstants.Level,
            Targetable: true,
            Rotation: MathF.PI));
    }

    private void ApplyQuickeningDynamis()
    {
        var members = party.AllMembers().ToArray();
        for (int i = 0; i < members.Length && i < state.QuickenedSlots.Count; i++)
            if (state.QuickenedSlots[i])
                members[i].AddStatus(TopConstants.StatusId.QuickeningDynamis, 0f, 1);
    }

    private void SpawnSpinner()
    {
        spinner = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.FinalHelper,
            NameId: TopConstants.BNpcNameId.OmegaFinal,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: true,
            Offset: new Vector3(0f, 0f, 0f),
            Rotation: MathF.PI / 4f));
        spinner?.PlayActionTimeline(TopConstants.TimelineId.Spawn);
    }

    private void SpawnSigmaHelper()
    {
        // NE intercardinal — generated from log pos (114.14, 114.14) - origin (100, 100).
        // The "new north" RNG (state.NewNorthA) should rotate this around the arena
        // when the user wires up state-aware spawn placement.
        sigmaHelper = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.BeetleHelper,
            NameId: TopConstants.BNpcNameId.OmegaBeetle,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: true,
            Offset: new Vector3(14.14f, 0f, 14.14f),
            Rotation: -MathF.PI * 3f / 4f));
        sigmaHelper?.PlayActionTimeline(TopConstants.TimelineId.Spawn);
    }

    private void ApplySigmaTethers()
    {
        // Four player-to-player pairs from the log type-35 lines at t=10.1.
        // SigmaOrder shuffles roles; pairs are (0,1), (2,3), (4,5), (6,7).
        // The returned tethers are tracked so Tick can apply the distance-fail
        // vuln-up when paired players drift too close or too far.
        var targets = new SimCharacter[8];
        for (int i = 0; i < 8; i++) targets[i] = party.Get(state.SigmaOrder[i])!;
        sigmaTethers.Clear();
        sigmaTethers.Add(world.Tether(targets[0], targets[1], TetherId.SigmaPair, 18f, StatusId.MidGlitch));
        sigmaTethers.Add(world.Tether(targets[2], targets[3], TetherId.SigmaPair, 18f, StatusId.MidGlitch));
        sigmaTethers.Add(world.Tether(targets[4], targets[5], TetherId.SigmaPair, 18f, StatusId.MidGlitch));
        sigmaTethers.Add(world.Tether(targets[6], targets[7], TetherId.SigmaPair, 18f, StatusId.MidGlitch));
    }


    private void ResolveWaveCannon()
    {
        // TODO: spinner sweep — arena-spanning rotating line. Direction comes from
        // state.SpinnerRotation; final orientation from state.NewNorthA.
        spinner?.Cast(ActionId.WaveCannon);
    }

    private void SpawnArmUnits()
    {
        // Log: 4000A643 at (99.92, 88.46) ≈ N (0, 0, -12), 4000A644 at (88.40, 99.93)
        // ≈ W (-12, 0, 0) at the time of the first Hyper Pulse cast. Both warp in
        // simultaneously at log 01:23:12.879 (t=10.2). Each plays the warp-in
        // animation so it doesn't pop in.
        armUnitA = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.RightArmUnit,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: true,
            Offset: new Vector3(0f, 0f, -12f),
            Rotation: 0f));
        armUnitA?.PlayActionTimeline(TopConstants.TimelineId.Spawn);

        armUnitB = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.RightArmUnit,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: false,
            Offset: new Vector3(-12f, 0f, 0f),
            Rotation: MathF.PI / 2f));
        armUnitB?.PlayActionTimeline(TopConstants.TimelineId.Spawn);
    }

    private void ApplyHyperPulseTethers()
    {
        // Log: type-35 tetherId 0x0011 from each arm unit to a player at t=10.78.
        // The mechanic targets the furthest player from each arm unit; players move
        // and the tether re-targets dynamically (line 54461 shows 4000A643 retether
        // to a different player at t=13.2). For now, freeze the target at apply-time.
        if (armUnitA != null) 
            tetherUnitA = world.TetherFarestPlayer(armUnitA, TetherId.HyperPulseBait, duration: 20f);
        if (armUnitB != null)
            tetherUnitB = world.TetherFarestPlayer(armUnitB, TetherId.HyperPulseBait, duration: 20f);
    }

    private void FireHyperPulse()
    {
        // Log: both arm units cast HyperPulse at 01:23:31.897 (t=29.2), each on
        // their currently-tethered player.
        if (tetherUnitA != null)
        {
            armUnitA?.Face(tetherUnitA.B.Position);
            armUnitA?.Cast(ActionId.HyperPulse, tetherUnitA.B.Position);
        }
        if (tetherUnitB != null)
            armUnitB?.Cast(ActionId.HyperPulse, tetherUnitB.B.Position);
    }

    private void SpawnTowers()
    {
        // Four cardinal towers at the arena center. Real positions in the log are
        // (100, 100) (overlapping); separate them visually for the sim.
        towers = new List<SimEnemy?>
        {
            SpawnTower(new Vector3(0f, 0f, -10f)),
            SpawnTower(new Vector3(10f, 0f, 0f)),
            SpawnTower(new Vector3(0f, 0f, 10f)),
            SpawnTower(new Vector3(-10f, 0f, 0f)),
        };
    }

    private SimEnemy? SpawnTower(Vector3 offset) =>
        world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: TopConstants.BNpcBaseId.OmegaHelper,
            NameId: TopConstants.BNpcNameId.OmegaFinal,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: false,
            Offset: offset));

    private void ResolveTowerWaveCannon()
    {
        // TODO: 4 wave-cannon hits on tower-target slots. PairIsTarget +
        // NonTargetMemberIsFirst pick which players the towers fire at.
        towers?.ForEach(t => t?.Cast(ActionId.WaveCannonHit));
    }

    private void SpawnOmegaClones()
    {
        // Per Sigma.md item #3: untargetable Omega-M spawns at "new north"
        // (state.NewNorthA — random cardinal or intercardinal). Distance ~14
        // to match the original sigma-helper intercardinal radius (sqrt(2)*10).
        // Rotation faces inward toward arena center.
        var mPos = OffsetAtDirection(state.NewNorthA, distance: 14f);
        var mFacing = state.NewNorthA.RadiansFromNorth + MathF.PI;        // face center (180° from outward)
        omegaMClone = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.OmegaMClone,
            NameId: TopConstants.BNpcNameId.OmegaM,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: true,
            Offset: mPos,
            Rotation: mFacing));
        omegaMClone?.PlayActionTimeline(TopConstants.TimelineId.Spawn);

        // Omega-F clone — placeholder at the opposite intercardinal until
        // state.NewNorthB / OmegaFForm wiring is hardened.
        omegaFCloneA = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.OmegaFClone,
            NameId: BNpcNameId.OmegaF,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: true,
            Offset: -mPos,
            Rotation: state.NewNorthA.RadiansFromNorth));
        omegaFCloneA?.PlayActionTimeline(TopConstants.TimelineId.Spawn);
    }

    // EightWayDirection -> scenario-local offset. North = -Z, East = +X
    // (standard FFXIV convention, matches TopP5DeltaConstants.Geometry.ArmUnitPlacements).
    private static Vector3 OffsetAtDirection(EightWayDirection dir, float distance)
    {
        var rad = dir.RadiansFromNorth;
        return new Vector3(MathF.Sin(rad) * distance, 0f, -MathF.Cos(rad) * distance);
    }

    private void ResolveStorageViolation()
    {
        // TODO: 2-target stack (7B05) on two players + 1-target spread (7B04) on the rest.
        // Current placeholder fires both via the towers if alive.
        if (towers == null) return;
        towers[0]?.Cast(ActionId.StorageViolationStack);
        towers[1]?.Cast(ActionId.StorageViolationSpread);
    }

    private void SpawnRearPowerUnit()
    {
        rearPower = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.RearPowerUnit,
            Level: TopConstants.Level,
            Targetable: false,
            InEnemyList: false,
            Offset: new Vector3(0f, 0f, 0f),
            Rotation: state.NewNorthB.RadiansFromNorth));
    }

    private void ResolveSuperliminalSteel()
    {
        // M is the visible cast; the two F clones fire the side variants based
        // on state.OmegaFForm (LegBlades vs Staff).
        omegaM?.Cast(ActionId.SuperliminalSteel, castSeconds: 1.2f);
        if (state.OmegaFForm == OmegaFForm.LegBlades)
        {
            omegaFCloneA?.Cast(ActionId.SuperliminalSteelLeft);
            omegaMClone?.Cast(ActionId.SuperliminalSteelRight);
        }
        else
        {
            omegaFCloneA?.Cast(ActionId.SuperliminalSteelRight);
            omegaMClone?.Cast(ActionId.SuperliminalSteelLeft);
        }
    }

    private void ResolveHelloWorldInitial()
    {
        // Two puddles at the F clone positions — one Near, one Distant.
        omegaFCloneA?.Cast(TopConstants.ActionId.HelloNearWorld);
        omegaMClone?.Cast(TopConstants.ActionId.HelloDistantWorld);
    }

    private void ResolveHelloWorldJump()
    {
        // Jump-to-closest / jump-to-farthest follow-ups via tower helpers.
        if (towers == null) return;
        towers[0]?.Cast(TopConstants.ActionId.HelloNearWorldJump);
        towers[1]?.Cast(TopConstants.ActionId.HelloDistantWorldJump);
    }
}
