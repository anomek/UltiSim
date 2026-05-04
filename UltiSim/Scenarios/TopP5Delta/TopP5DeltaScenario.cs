using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.SimObjects;
using static UltiSim.Scenarios.TopP5Delta.TopP5DeltaConstants;

namespace UltiSim.Scenarios.TopP5Delta;

public sealed class TopP5DeltaScenario : IScenario
{
    public string Name => "TOP P5 Delta";
    public ScenarioOriginOverride? OriginOverride { get; } = new(TerritoryId: 801, X: 100f, Z: 100f);
    public IReadOnlyList<uint> HiddenBaseIds { get; } = [BNpcBaseId.AlphaShield];
    public IReadOnlyList<Waymark> Waymarks { get; } = WaymarkPresets.Ring(13.63f);
    private readonly TopP5DeltaSettingsWindow settingsWindow = new();

    private TopP5DeltaState state = null!;
    private TopP5DeltaAi ai = null!;
    private SimWorld world = null!;
    private readonly Random rng = new();

    private SimEnemy? omega;
    private SimEnemy? beetle;
    private SimEnemy? finalHelper;
    private SimEnemy? opticalUnit;
    private List<SimEnemy?>? rocketPunches;
    private List<SimEnemy?>? armUnits;

    public void Run(SimWorld worldParam, PartyRole playerRole)
    {
        world = worldParam;
        state = new TopP5DeltaState(settingsWindow.Overrides, playerRole);
        ai = new TopP5DeltaAi(state);
        ai.Run(world);

        world.Events.Add(0.1f, SpawnOmega);
        world.Events.Add(2f, () => omega?.Cast(ActionId.RunMiDeltaVersion));
        world.Events.Add(7f, () => omega?.SetTargetable(false));
        world.Events.Add(11f, () => omega?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(11f, SpawnDeltaAdds);
        world.Events.Add(11f, ApplyDeltaTethers);
        world.Events.Add(19f, SpawnRocketPunches);
        world.Events.Add(23f, SpawnArmUnits);
        world.Events.Add(24f, MarkArmUnitRotations);
        world.Events.Add(32f, StartMonitors);
        // BeyondDefense is a 4.9s cast; resolve it 0.05s after the auto-fire so the
        // jump animation has the body lock locked in before the AOE drops.
        world.Events.Add(36.95f, FireBeyondDefenseAoe);
        world.Events.Add(42f, ResolveMonitors);
        world.Events.Add(42f, FirePilePitch);
        world.Events.Add(29f, () => omega?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(29f, () => opticalUnit?.Cast(ActionId.OpticalLaser));
        world.Events.Add(29f, ApplyDeltaRealTethers);
        world.Events.Add(31f, StartPunchExplosions);
        world.Events.Add(39f, DespawnRocketPunches);
        world.Events.Add(37.0f, StartHyperPulse);
        world.Events.Add(40.1f, NextHyperPulse);
        world.Events.Add(40.7f, NextHyperPulse);
        world.Events.Add(41.3f, NextHyperPulse);
        world.Events.Add(41.9f, NextHyperPulse);
        world.Events.Add(42.5f, NextHyperPulse);
        world.Events.Add(42.9f, PlayArmUnitsDespawnAnim);
        world.Events.Add(43.9f, DespawnArmUnits);
        world.Events.Add(45f, StartSwivelCannon);
        world.Events.Add(45f, () => omega?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(45f, PlayFinalHelperDespawnAnim);
        world.Events.Add(47f, DespawnFinalHelper);
        world.Events.Add(57f, PlayBeetleDespawnAnim);
        world.Events.Add(59f, DespawnBeetle);
    }

    private void SpawnOmega()
    {
        omega = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: BNpcBaseId.StarterOmega,
            NameId: BNpcNameId.OmegaM,
            Level: Level,
            Targetable: true,
            Rotation: MathF.PI));
    }

    private void StartSwivelCannon()
    {
        beetle?.Cast(
            state.SwivelCannonSide.SwivelCannonActionId,
            omenDelay: 8.5f,
            omenRotate: state.SwivelCannonSide.Mul * MathF.PI / 2);
    }

    private void SpawnDeltaAdds()
    {
        // Eye position angle: north (-Z) = π, south (+Z) = 0 in our atan2(x, z) convention.
        var eyeAngle = state.EyeSpawn == NorthSouth.North ? MathF.PI : 0f;
        var beetleAngle = eyeAngle + MathF.PI / 2f; // 90° CCW from eye
        var finalAngle = eyeAngle - MathF.PI / 2f;  // 90° CW from eye

        beetle = SpawnAtAngle(BNpcBaseId.BeetleHelper, BNpcNameId.Omega, beetleAngle, inEnemyList: true, playSpawnAnim: true);
        opticalUnit = SpawnAtAngle(BNpcBaseId.OpticalUnit, BNpcNameId.OpticalUnit, eyeAngle, inEnemyList: false, playSpawnAnim: false, radius: Geometry.OpticalUnitDistance);
        finalHelper = SpawnAtAngle(BNpcBaseId.FinalHelper, BNpcNameId.Omega, finalAngle, inEnemyList: true, playSpawnAnim: true);

    }

    private void SpawnRocketPunches()
    {
        beetle?.Cast(ActionId.PeripheralSynthesis);
        rocketPunches = Enumerable.Range(0, 8).Select(i =>
        {
            var baseId = state.FistColors[i] == 0 ? BNpcBaseId.RocketPunchYellow : BNpcBaseId.RocketPunchBlue;
            var punch = SpawnPunchBehindPlayer(baseId, state.TetherOrder[i]);
            punch?.PlayActionTimeline(TimelineId.RocketPunchSpawn);
            return punch;
        }).ToList();
    }

    private void StartPunchExplosions()
    {
        rocketPunches?.Zip(state.TetherOrder, (punch, role) => (punch, role))
            .ToList()
            .ForEach(p => p.punch?.Cast(
                ActionId.DeltaExplosion,
                world.Party.Get(p.role)!.Position));
    }

    private void DespawnRocketPunches()
    {
        rocketPunches?.ForEach(punch => punch?.Despawn());
        rocketPunches = null;
    }

    private void SpawnArmUnits()
    {
        finalHelper?.Cast(ActionId.ArchivePeripheral);
        armUnits = Enumerable.Range(0, 6).Select(i => SpawnAtAngle(
            state.ArmHandedness[i].ArmUnitId,
            state.ArmHandedness[i].ArmUnitNameId,
            i * MathF.PI / 3f,
            inEnemyList: true, playSpawnAnim: true)).ToList();
    }

    private void MarkArmUnitRotations()
    {
        armUnits?.Select((unit, i) => (unit, i))
            .ToList()
            .ForEach(t => t.unit?.AttachLockonVfx(state.ArmHandedness[t.i].RotateLockonId, duration: 8f));
    }

    private void StartMonitors()
    {
        omega?.Cast(ActionId.BeyondDefense);
        finalHelper?.Cast(state.OmegaMonitorSide.DeltaOversampledWaveCannonActionId);

        var playerMonitor = world.Party.Get(state.TetherOrder[state.PlayerMonitorIndex])!;
        playerMonitor.AddStatus(state.PlayerMonitorSide.MonitorDebuffId);
        playerMonitor.AddVfx(state.PlayerMonitorSide.MonitorVfxPath);
    }

    private void FireBeyondDefenseAoe()
    {
        if (omega is null) return;
        var closestRoles = world.Party.FindClosestRolesN(omega.Position, 2);
        if (closestRoles.Count == 0) return;
        var chosenRole = closestRoles[rng.Next(closestRoles.Count)];
        state.BeyondDefenseTarget = chosenRole;
        var target = world.Party.Get(chosenRole)!;
        omega.Cast(
            ActionId.BeyondDefenseAOE,
            targetLocation: target.Position,
            targetId: target.GameObjectId);
    }

    // No-cast stack on the player closest to Omega-M, fired the moment the monitors
    // resolve. Canon source is OmegaMHelper but we fire it from the visible boss so
    // the animation lands on the body players are tracking.
    private void FirePilePitch()
    {
        if (omega is null) return;
        var target = world.Party.FindClosest(omega.Position)!;
        omega.Cast(
            ActionId.PilePitch,
            targetLocation: target.Position,
            targetId: target.GameObjectId);
    }

    private void ResolveMonitors()
    {
        if (finalHelper is { IsAlive: true } helper)
            FireMonitorOnSide(helper.Position, helper.Rotation, state.OmegaMonitorSide, exclude: null);

        var monitorRole = state.TetherOrder[state.PlayerMonitorIndex];
        var playerMonitor = world.Party.Get(monitorRole)!;
        FireMonitorOnSide(playerMonitor.Position, playerMonitor.Rotation, state.PlayerMonitorSide, exclude: monitorRole);

        // TODO: move these to timed application
        playerMonitor.RemoveStatus(state.PlayerMonitorSide.MonitorDebuffId);
        playerMonitor.RemoveVfx(state.PlayerMonitorSide.MonitorVfxPath);
    }

    private void FireMonitorOnSide(Vector3 srcPos, float srcRot, Side side, PartyRole? exclude)
    {
        var targets = PickMonitorTargets(srcPos, srcRot, side, exclude);
        foreach (var role in targets)
        {
            var member = world.Party.Get(role)!;
            var pos = member.Position;
            var spawned = world.SpawnEnemy(new EnemySpawnConfig(
                BNpcBaseId: BNpcBaseId.OmegaHelper,
                Targetable: false,
                InEnemyList: false,
                Offset: pos - world.ScenarioOrigin,
                Lifetime: Duration.MonitorHelperLifetime));
            spawned?.Cast(ActionId.OversampledWaveCannonAOE, targetLocation: pos, targetId: member.GameObjectId);
        }
    }

    // Right vector for a unit at rotation rot is (-cos(rot), sin(rot)) under our
    // atan2(x, z) convention (rot=0 → +Z forward, right hand → -X). Players whose
    // dot(player - src, right) > 0 are on the source's right side; < 0 on its left.
    // Returns up to 2 roles, preferring the chosen side and filling from the rest.
    private List<PartyRole> PickMonitorTargets(Vector3 srcPos, float srcRot, Side side, PartyRole? exclude)
    {
        var rightX = -MathF.Cos(srcRot);
        var rightZ = MathF.Sin(srcRot);

        var onSide = new List<PartyRole>();
        var others = new List<PartyRole>();
        foreach (PartyRole role in Enum.GetValues<PartyRole>())
        {
            if (exclude is { } ex && role == ex) continue;
            var pos = world.Party.Get(role)!.Position;
            var dot = (pos.X - srcPos.X) * rightX + (pos.Z - srcPos.Z) * rightZ;
            var roleOnSide = dot * side.Mul < 0;
            (roleOnSide ? onSide : others).Add(role);
        }
        ShuffleInPlace(onSide);
        ShuffleInPlace(others);

        var picked = new List<PartyRole>(2);
        foreach (var r in onSide) { picked.Add(r); if (picked.Count == 2) break; }
        foreach (var r in others) { if (picked.Count == 2) break; picked.Add(r); }
        return picked;
    }

    private void ShuffleInPlace<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Each arm unit faces the closest player and starts the 2.5s baited line cast
    // (sheet-defined omen). The five rest casts are scheduled separately from Run().
    private void StartHyperPulse()
    {
        armUnits?.OfType<SimEnemy>()
            .Where(unit => unit.IsAlive)
            .ToList()
            .ForEach(unit =>
            {
                var closestPos = world.Party.FindClosest(unit.Position)!.Position;
                var dx = closestPos.X - unit.Position.X;
                var dz = closestPos.Z - unit.Position.Z;
                unit.Move(unit.Position, MathF.Atan2(dx, dz));
                unit.Cast(ActionId.DeltaHyperPulseFirst);
            });
    }

    // Fired once per rest beat. Rotates each unit 20° in its handedness direction
    // (LeftArm → CCW, RightArm → CW) and fires the no-cast follow-up.
    private void NextHyperPulse()
    {
        armUnits?.Select((unit, i) => (unit, i))
            .Where(t => t.unit is { IsAlive: true })
            .ToList()
            .ForEach(t =>
            {
                var step = state.ArmHandedness[t.i].Mul * Geometry.HyperPulseStep;
                t.unit!.Move(t.unit.Position, t.unit.Rotation + step);
                t.unit.Cast(ActionId.DeltaHyperPulseRest);
            });
    }

    private void PlayArmUnitsDespawnAnim()
    {
        armUnits?.ForEach(unit => unit?.PlayActionTimeline(TimelineId.WarpOut));
    }

    private void DespawnArmUnits()
    {
        armUnits?.ForEach(unit => unit?.Despawn());
        armUnits = null;
    }

    private void PlayFinalHelperDespawnAnim()
    {
        if (finalHelper is not { IsAlive: true }) return;
        finalHelper.PlayActionTimeline(TimelineId.WarpOut);
    }

    private void DespawnFinalHelper()
    {
        finalHelper?.Despawn();
        finalHelper = null;
    }

    private void PlayBeetleDespawnAnim()
    {
        if (beetle is not { IsAlive: true }) return;
        beetle.PlayActionTimeline(TimelineId.WarpOut);
    }

    private void DespawnBeetle()
    {
        beetle?.Despawn();
        beetle = null;
    }


    private SimEnemy? SpawnPunchBehindPlayer(uint baseId, PartyRole role)
    {
        var member = world.Party.Get(role)!;
        var pos = member.Position;
        var rot = member.Rotation;
        // facing direction: rot=0 is +Z (south), rot=π is -Z (north).
        var facing = new Vector3(MathF.Sin(rot), 0, MathF.Cos(rot));
        var spawnPos = pos - facing * Geometry.PunchBackDistance;
        var offset = spawnPos - world.ScenarioOrigin;
        return world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: BNpcNameId.RocketPunch,
            Level: Level,
            Targetable: false,
            InEnemyList: true,
            Offset: offset,
            Rotation: rot));
    }

    // Pairs the eight party slots (in shuffled state.TetherOrder) into four tethers:
    // remote (201) on indices 0↔1 and 2↔3, local (200) on indices 4↔5 and 6↔7.
    // The local player slot in Party holds the SimPlayer reference, so this is uniform.
    private void ApplyDeltaTethers()
    {
        var targets = new SimCharacter[8];
        for (int i = 0; i < 8; i++) targets[i] = world.Party.Get(state.TetherOrder[i])!;

        world.Tether(targets[0], targets[1], TetherId.HWPrepRemote, 18f, StatusId.HWPrepRemoteTether);
        world.Tether(targets[2], targets[3], TetherId.HWPrepRemote, 18f, StatusId.HWPrepRemoteTether);
        world.Tether(targets[4], targets[5], TetherId.HWPrepLocal, 18f, StatusId.HWPrepLocalTether);
        world.Tether(targets[6], targets[7], TetherId.HWPrepLocal, 18f, StatusId.HWPrepLocalTether);

        ApplyHelloWorldDebuffs(targets);
    }

    private void ApplyHelloWorldDebuffs(SimCharacter[] targets)
    {
        // world.ApplyTimedStatus(targets[state.NearWorldTetherIndex], StatusId.HelloNearWorld, Duration.HelloWorldDebuff);
        // world.ApplyTimedStatus(targets[state.FarWorldTetherIndex], StatusId.HelloFarWorld, Duration.HelloWorldDebuff);
    }

    // Real tethers replace the prep set on the same pair indices, so the prep tether
    // type is the player's preview of what they'll resolve. 36s duration matches canon.
    private void ApplyDeltaRealTethers()
    {
        var targets = new SimCharacter[8];
        for (int i = 0; i < 8; i++) targets[i] = world.Party.Get(state.TetherOrder[i])!;

        world.Tether(targets[0], targets[1], TetherId.HWRemote, 36f, StatusId.HWRemoteTether);
        world.Tether(targets[2], targets[3], TetherId.HWRemote, 36f, StatusId.HWRemoteTether);
        world.Tether(targets[4], targets[5], TetherId.HWLocal, 36f, StatusId.HWLocalTether);
        world.Tether(targets[6], targets[7], TetherId.HWLocal, 36f, StatusId.HWLocalTether);
    }

    public void DrawSettings() => settingsWindow.Draw();

    // The local player's slot is the one Party didn't fill (PartyPresets.ForPlayerJob
    // returns null for it). Walk slots once to find it.
    private SimEnemy? SpawnAtAngle(uint baseId, uint nameId, float angle, bool inEnemyList, bool playSpawnAnim, float radius = Geometry.ArenaRadius)
    {
        var offset = new Vector3(radius * MathF.Sin(angle), 0, radius * MathF.Cos(angle));
        var enemy = world.SpawnEnemy(new EnemySpawnConfig(
            BNpcBaseId: baseId,
            NameId: nameId,
            Level: Level,
            Targetable: false,
            InEnemyList: inEnemyList,
            Offset: offset,
            Rotation: angle + MathF.PI));
        if (playSpawnAnim) enemy?.PlayActionTimeline(TimelineId.Spawn);
        return enemy;
    }
}
