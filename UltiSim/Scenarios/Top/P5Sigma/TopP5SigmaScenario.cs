using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.Map;
using UltiSim.Core.SimObjects;
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

    private TopUtils topUtils = null!;
    
    private TopP5SigmaState state = null!;
    private TopP5SigmaAi ai = null!;
    private SimWorld world = null!;
    private SimParty party = null!;

    public void Run(SimWorld worldParam, PartyRole playerRole)
    {
        world = worldParam;
        party = worldParam.Party;
        state = new TopP5SigmaState(party, settingsWindow.Overrides, playerRole);
        ai = new TopP5SigmaAi(state);
        ai.Run(world);
        topUtils = new TopUtils(world);

        world.Events.Add(1f, () => topUtils.InitTopArena());
        Run_Omega_M_4000A63C();
        Run_Omega_4000A68F();
        Run_Omega_4000A690();
        Run_Right_Arm_Unit_4000A643();
        Run_Omega_M_4000A40C_0();
        Run_Omega_4000A408();
        Run_EventObj_1EB83C_4000A6E7();
        Run_EventObj_1EB83E_4000A6E8();
        Run_Rear_Power_Unit_4000A641();
        Run_Omega_F_4000A40B_2();
        Run_Omega_F_4000A40C_2();
        Run_PlayerTethers();
        Run_OtherDebuffs();
        Run_PlayerLockons();
    }
    
    public void Tick(float delta, float elapsed)
    {
        topUtils.CheckHelloWorldDeath();
    }
    
    private bool IsDamageLethal(SimCharacter character, bool ruin)
    {
        var who = (character as SimPartySlot)?.Role.ToString() ?? character.GetType().Name;
        var vuln = character.HasStatus(StatusId.VulnerabilityUp);
        var magicVuln = character.HasStatus(StatusId.MagicVulnerabilityUp);
        var twiceRuin = character.HasStatus(StatusId.TwiceComeRuin);
        var lethal = vuln || magicVuln || (ruin && twiceRuin);
        Plugin.Log.Info($"IsDamageLethal: {who} ruin={ruin} → {lethal} [VulnUp={vuln} MagicVulnUp={magicVuln} TwiceComeRuin={twiceRuin}]");
        return lethal;
    }

    private void Run_PlayerTethers()
    {
        world.Events.Add(11.82f, () =>
        {
            state.Order.ForEachPair((p1, p2) => world.Tether(
                p1, p2,
                TetherId.Glitch , duration: 32.000f,
                debuffStatusId: state.GlitchType.StatusId)
                     .SetConditionalStatus(StatusId.VulnerabilityUp, state.GlitchType.Condition)
            );
        });
    }

    private void Run_OtherDebuffs()
    {
        state.DynamisTargets.ForEach(p => p.AddStatus(StatusId.QuickeningDynamis, stacks: 1));
        world.Events.Add(11.82f, () => state.HelloWorldTargets[0]?.AddStatus(StatusId.HelloNearWorld, 56.000f));
        world.Events.Add(11.82f, () => state.HelloWorldTargets[1]?.AddStatus(StatusId.HelloDistantWorld, 56.000f));
        world.Events.Add(28.39f, () => party.ForEachActive(member => member.AddStatus(StatusId.Looper, 18.000f)));
    }

    private void Run_PlayerLockons()
    {
        world.Events.Add(11.82f, () => state.Order.ForEachPair((i, p1, p2) =>
        {
            p1.AttachLockonVfx(LockonId.Playstation[i], persistent: false); 
            p2.AttachLockonVfx(LockonId.Playstation[i], persistent: false); 
        }));
        world.Events.Add(22.02f, () => state.WaveCannonTargets.ForEach(p => p.AttachLockonVfx(LockonId.WaveCannon, persistent: false)));
    }


    private void Run_Omega_M_4000A63C()
    {
        SimEnemy? omega_M_4000A63C = null;
        world.Events.Add(0f, () => omega_M_4000A63C = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.OmegaM, NameId: BNpcNameId.OmegaM, Level: 90, Targetable: true, EnemyList: EnemyListMode.Always, IsVisible: true, Placement: new Placement(new Vector3(0.000f, 0.000f, 5.000f), MathF.PI), InitialModeAttributeFlags: 0x10)));
        world.Events.Add(0.1f, () => omega_M_4000A63C?.AddStatus(StatusId.OmegaM));
        world.Events.Add(2.46f, () => omega_M_4000A63C?.Cast(ActionId.Teleport7b42, castSeconds: 0f, targetLocation: Vector3.Zero));
        world.Events.Add(3.75f, () => omega_M_4000A63C?.Cast(ActionId.RunMiSigmaVersion, castSeconds: 4.700f));
        world.Events.Add(11.82f, () => omega_M_4000A63C?.SetTargetable(false));
        world.Events.Add(11.87f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(13f, () => omega_M_4000A63C?.SetPosition(state.NewNorthA.Apply(new Placement(new(0f, 0f, -20f), 0f))));
        world.Events.Add(13.96f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(26.16f, () => omega_M_4000A63C?.Cast(ActionId.SubjectSimulationF, castSeconds: 0f, targetId: omega_M_4000A63C?.GameObjectId));
        world.Events.Add(27.25f, () => omega_M_4000A63C?.SetTransformationId((short)493));
        world.Events.Add(27.23f, () => omega_M_4000A63C?.SetModelState((byte)0x06));
        
        world.Events.Add(27.23f, () => omega_M_4000A63C?.RemoveStatus(StatusId.OmegaM));
        world.Events.Add(27.23f, () => omega_M_4000A63C?.AddStatus(StatusId.Superfluid, stacks: (ushort)493, overrideStacks: true));
        world.Events.Add(28.25f, () => omega_M_4000A63C?.Cast(ActionId.Unknown7b16, castSeconds: 0f, targetId: omega_M_4000A63C?.GameObjectId));
        world.Events.Add(28.79f, () => omega_M_4000A63C?.SetModelState((byte)0x0B));
        world.Events.Add(32.36f, () => omega_M_4000A63C?.Cast(ActionId.Unknown7f30, castSeconds: 0f, targetId: omega_M_4000A63C?.GameObjectId));
        world.Events.Add(36.05f, () => omega_M_4000A63C?.SetTransformationId((short)492));
        world.Events.Add(36.02f, () => omega_M_4000A63C?.SetModelState((byte)0x05));
        world.Events.Add(36.02f, () => omega_M_4000A63C?.RemoveStatus(StatusId.Superfluid));
        world.Events.Add(36.02f, () => omega_M_4000A63C?.AddStatus(StatusId.OmegaF, stacks: (ushort)492, overrideStacks: true));
        world.Events.Add(36.47f, () => omega_M_4000A63C?.Cast(ActionId.Unknown7b20, castSeconds: 0f, targetId: omega_M_4000A63C?.GameObjectId));
        world.Events.Add(37.13f, () => omega_M_4000A63C?.SetModelState((byte)0x0B));
        world.Events.Add(38.56f, () => omega_M_4000A63C?.Cast(ActionId.Teleport7b43, castSeconds: 0f, targetLocation: Vector3.Zero));
        world.Events.Add(39.68f, () => omega_M_4000A63C?.Cast(ActionId.Discharger));
        world.Events.Add(40.18f, () => party.Knockback(new Vector3(0, 0, 0), KnockbackId.Discharger));
        world.Events.Add(42.79f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(44.33f, () => omega_M_4000A63C?.SetVisible(false));
        world.Events.Add(45.88f, () => omega_M_4000A63C?.SetModeAttributeFlags(state.OmegaFAttack.AttributeFlags));
        world.Events.Add(45.88f, () => omega_M_4000A63C?.SetModelState((byte)0x04));
        world.Events.Add(45.88f, () => omega_M_4000A63C?.SetPosition(state.NewNorthB.Apply(new Placement(new Vector3(0f, 0f, -10f), 0))));
        world.Events.Add(45.97f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(46.93f, () => omega_M_4000A63C?.SetVisible(true));
        world.Events.Add(59.62f, () => omega_M_4000A63C?.Cast(state.OmegaFAttack.ActionId, targetLocation: omega_M_4000A63C.Position, targetId: omega_M_4000A63C.GameObjectId, castSeconds: 1.200f, omenDelay: Duration.OmegaFAttackOmenDelay));
        world.Events.Add(60.82f, ResolveOmegaFAttack);
        world.Events.Add(64.21f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(65.77f, () => omega_M_4000A63C?.SetVisible(false));
        world.Events.Add(66.26f, () => omega_M_4000A63C?.SetModeAttributeFlags((byte)0x32));
        world.Events.Add(66.26f, () => omega_M_4000A63C?.SetModelState((byte)0x00));
        world.Events.Add(69.29f, () => omega_M_4000A63C?.SetPosition(new Placement(Vector3.Zero, 3.142f)));
        world.Events.Add(69.38f, () => omega_M_4000A63C?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(70.35f, () => omega_M_4000A63C?.SetVisible(true));
        world.Events.Add(72.46f, () => omega_M_4000A63C?.SetTargetable(true));
    }

    private void ResolveOmegaFAttack()
    {
        // Omega-F stands at NewNorthB-relative (0, 0, -10) facing north (rot = π).
        var center = state.NewNorthB.Apply(new Placement(new Vector3(0f, 0f, -10f), MathF.PI));

        if (state.OmegaFAttack == OmegaFAttack.Legs)
        {
            // Two opposing rectangles flank her facing axis; the safe band runs
            // perpendicular to it. Anyone outside the band dies.
            var safeBand = new Placement(center.Position, center.Rotation + MathF.PI / 2f);
            foreach (var hit in party.Find.OutsideRect(safeBand, Geometry.SuperliminalSteelSafeHalfWidth, Geometry.OmegaFAttackHalfLength))
            {
                Plugin.Log.Info($"Hit: {hit.Role} by Superliminal Steel (lethal)");
                hit.Die("Superliminal Steel");
            }
        }
        else
        {
            // + cross centered on Omega-F. Anyone inside either arm dies.
            foreach (var hit in party.Find.InsideCross(center, Geometry.OptimizedBlizzardArmHalfWidth, Geometry.OmegaFAttackHalfLength))
            {
                Plugin.Log.Info($"Hit: {hit.Role} by Optimized Blizzard III (lethal)");
                hit.Die("Optimized Blizzard III");
            }
        }
    }

    private void Run_Omega_4000A68F()
    {
        SimEnemy? omega_4000A68F = null;
        world.Events.Add(3.94f, () => omega_4000A68F = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.Omega, NameId: BNpcNameId.Omega, Level: 90, Targetable: false, EnemyList: EnemyListMode.OnlyWhenVisible, IsVisible: false, Placement: state.NewNorthA.Apply(new Placement(new Vector3(0f, 0f, 20f), MathF.PI)))));
        world.Events.Add(19.93f, () => omega_4000A68F?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(20.04f, () => omega_4000A68F?.SetVisible(true));
        world.Events.Add(27.63f, () => omega_4000A68F?.Cast(ActionId.ProgramLoop, castSeconds: 0f, targetId: omega_4000A68F?.GameObjectId));
        world.Events.Add(30.75f, () => omega_4000A68F?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(45.27f, () => omega_4000A68F?.Despawn());
    }

    private void Run_Omega_4000A690()
    {
        SimEnemy? omega_4000A690 = null;
        world.Events.Add(3.94f, () => omega_4000A690 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.Omega_394D, NameId: BNpcNameId.Omega_1DD4, Level: 90, Targetable: false, EnemyList: EnemyListMode.OnlyWhenVisible, IsVisible: false, Placement: state.NewNorthA.Apply(new Placement(new Vector3(0.000f, -0.000f, 0.000f), 0)))));
        world.Events.Add(16.95f, () => omega_4000A690?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(17.06f, () => omega_4000A690?.SetVisible(true));
        world.Events.Add(22.11f, () => omega_4000A690?.Cast(ActionId.WaveCannon, castSeconds: 7.700f, targetId: omega_4000A690?.GameObjectId));
        world.Events.Add(33.25f, () => omega_4000A690?.PlayActionTimeline(TimelineId.WarpOut));
        world.Events.Add(35.65f, () => omega_4000A690?.Despawn());
    }

    private void Run_Right_Arm_Unit_4000A643()
    {
        for(int i = 0; i < 2; i++)
        {
            var offset = i * 2 - 1; // -1, 1
            SimEnemy? unit = null;
            SimTether? tether = null;
            world.Events.Add(1, () => unit = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.RightArmUnit, NameId: BNpcNameId.RightArmUnit, Level: 90, Targetable: false, EnemyList: EnemyListMode.OnlyWhenVisible, IsVisible: false, 
                                                                  Placement: state.NewNorthA.Apply(new Placement(new Vector3(7.07f * offset, -0.000f, 7.07f), 3.140f)))));
            world.Events.Add(11.91f, () => unit?.PlayActionTimeline(TimelineId.Spawn));
            world.Events.Add(11.91f, () => unit?.SetVisible(true));
            world.Events.Add(12.53f, () => tether = world.TetherFarestPlayer(unit, TetherId.AutoTarget)
                                                         .SetAutoFaceTarget(true));
            world.Events.Add(30.93f, () => unit?.Cast(ActionId.HyperPulse_7B72, castSeconds: 0f, targetId: tether?.B?.GameObjectId));
            world.Events.Add(30.93f, () => ResolveHyperpulse(tether));
            world.Events.Add(30.96f, () => tether?.Despawn());
            world.Events.Add(32.98f, () => unit?.PlayActionTimeline(TimelineId.WarpOut));
            world.Events.Add(45.88f, () => unit?.SetPosition(state.NewNorthB.Apply(new Placement(new Vector3(14.14f * offset, 0.000f, 14.14f), MathF.PI))));
            world.Events.Add(45.97f, () => unit?.PlayActionTimeline(TimelineId.Spawn));
            world.Events.Add(46.59f, () => tether = world.TetherFarestPlayer(unit, TetherId.AutoTarget)
                  .SetAutoFaceTarget(true));
            world.Events.Add(68.00f, () => unit?.Cast(ActionId.HyperPulse_7B72, castSeconds: 0f, targetId: tether?.B?.GameObjectId));
            world.Events.Add(68.00f, () => ResolveHyperpulse(tether));
            world.Events.Add(68.20f, () => tether?.Despawn());
            world.Events.Add(70.05f, () => unit?.PlayActionTimeline(TimelineId.WarpOut));
        }
    }

    private void ResolveHyperpulse(SimTether? tether)
    {
        if (tether?.A is not { } caster) return;
        foreach (var hit in party.Find.InsideActionAoe(ActionId.HyperPulse_7B72, caster.Placement))
        {
            var lethal = IsDamageLethal(hit, ruin: false);
            Plugin.Log.Info($"Hit: {hit.Role} by Hyper Pulse ({(lethal ? "lethal" : "non-lethal")})");
            if (lethal)
                hit.Die("Hyper Pulse");
            else
                hit.AddStatus(StatusId.MagicVulnerabilityUp, 4.96f);
        }
    }

    private void Run_Omega_M_4000A40C_0()
    {
        SimEnemy? omega_M_4000A40C_0 = null;
        world.Events.Add(1f, () => omega_M_4000A40C_0 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.OmegaM_233C, NameId: BNpcNameId.OmegaM, Level: 1, Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: true, Placement: state.NewNorthA.Apply(new Placement(new Vector3(0f, 0f, 0f), 0f)))));
        world.Events.Add(27.14f, () => omega_M_4000A40C_0?.Cast(ActionId.Unknown7b14, castSeconds: 0f, targetId: omega_M_4000A40C_0?.GameObjectId));
    }

    private void Run_Omega_4000A408()
    {
        for (int index = 0; index < 6; index++)
        {
            SimEnemy? omega_4000A408 = null;
            var i = index;
            var target = state.WaveCannonTargets.Get(i);
            var tower = state.Towers[i];
            var towerLocation = state.Towers[i]?.Position;
            var helloWorldOffset = i / 2;
            
            world.Events.Add(1f, () => omega_4000A408 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.OmegaM_233C, NameId: BNpcNameId.Omega, Level: 1, Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: true, Placement: new Placement(new Vector3(0.000f, 0.000f, 0.000f), -0.000f))));
            world.Events.Add(31.15f, () => {
                 omega_4000A408?.Face(target!.Position);
                 omega_4000A408?.Cast(ActionId.WaveCannon_7B74, castSeconds: 0f, targetLocation: target?.Position, targetId: target?.GameObjectId);
                 ResolveWaveCannon(omega_4000A408);
                 target?.AddStatus(StatusId.MagicVulnerabilityUp, 1.960f);
            });
            world.Events.Add(43.60f, () => omega_4000A408?.SetPosition(towerLocation ?? default));
            world.Events.Add(43.69f, () =>
            {
                if (towerLocation != null)
                {
                    var inTower = party.Find.InsideCircle(towerLocation.Value, Geometry.TowerRadius);
                    foreach (var player in inTower)
                    {
                        omega_4000A408?.Cast(ActionId.StorageViolation, castSeconds: 0f, targetId: player.GameObjectId);
                        ResolveStorageViolation(player);
                        player.AddStatus(StatusId.TwiceComeRuin, 10.960f, stacks: 1);
                    }
                    if (inTower.Count < tower?.MinPlayers)
                    {
                        ResolveFailedTower(omega_4000A408);
                    }
                }
            });
        
            world.Events.Add(67.7f + helloWorldOffset, () => omega_4000A408?.SetPosition(state.HelloWorldTargets[i <= 1 ? i : i - 2]!.Position));
            world.Events.Add(67.9f + helloWorldOffset, () =>
            {
                if (i > 1)
                {
                    var current = state.HelloWorldTargets[i - 2]!;
                    state.HelloWorldTargets[i] = party.Find.Extreme(current.Position, i % 2 == 0, current);
                }
                var actionId = i switch
                {
                    0 => ActionId.HelloNearWorld,
                    1 => ActionId.HelloNearWorld,
                    _ => i % 2 == 0 ? ActionId.HelloNearWorld_7B8A : ActionId.HelloDistantWorld_8110,
                }; 
                omega_4000A408?.Cast(actionId, targetId: state.HelloWorldTargets[i]?.GameObjectId);
                ResolveHelloWorld(state.HelloWorldTargets[i]?.Position, actionId);
            });
        }
    }

    private void ResolveWaveCannon(SimEnemy? caster)
    {
        if (caster is not { IsAlive: true } unit) return;
        foreach (var hit in party.Find.InsideActionAoe(ActionId.WaveCannon_7B74, unit.Placement))
        {
            var lethal = IsDamageLethal(hit, ruin: false);
            Plugin.Log.Info($"Hit: {hit.Role} by Wave Cannon ({(lethal ? "lethal" : "non-lethal")})");
            if (lethal)
                hit.Die("Wave Cannon");
            else
                hit.AddStatus(StatusId.MagicVulnerabilityUp, 4.96f);
        }
    }

    private void ResolveStorageViolation(SimPartySlot player)
    {
        if (!player.IsAlive) return;
        player.RemoveStatus(StatusId.Looper);
        var lethal = IsDamageLethal(player, ruin: true);
        Plugin.Log.Info($"Hit: {player.Role} by Storage Violation ({(lethal ? "lethal" : "non-lethal")})");
        if (lethal)
        {
            player.Die("Storage Violation");
            return;
        }
    }

    private void ResolveFailedTower(SimEnemy? omega4000A408)
    {
        Plugin.Log.Info("Hit: ALL PARTY by Storage Violation — tower unfilled (lethal raidwide)");
        omega4000A408?.Cast(ActionId.StorageViolation_7B04);
        party.WipeAllPlayers("Storage Violation — tower unfilled");
    }

    private void ResolveHelloWorld(Vector3? position, uint actionId)
    {
        if (position is not { } pos) return;
        var inAoe = party.Find.InsideActionAoe(actionId, new Placement(pos, 0f), targetLocation: pos);
        if (inAoe.Count != 1)
        {
            Plugin.Log.Info($"Hit: ALL PARTY by Hello World fail (lethal raidwide, soakers={inAoe.Count})");
            topUtils.HelloWorldFail(pos);
            return;
        }
        var soaker = inAoe[0];
        if (IsDamageLethal(soaker, ruin: false))
        {
            Plugin.Log.Info($"Hit: {soaker.Role} by Hello World (lethal) → raidwide fail");
            topUtils.HelloWorldFail(pos);
            return;
        }
        Plugin.Log.Info($"Hit: {soaker.Role} by Hello World (non-lethal soak)");
        soaker.AddStatus(TopConstants.StatusId.QuickeningDynamis, stacks: 1);
        soaker.AddStatus(StatusId.MagicVulnerabilityUp, 4.96f);
    }

    private void Run_EventObj_1EB83C_4000A6E7()
    {
        for (int index = 0; index < 6; index++)
        {
            var i = index;
            var tower = state.Towers[i];
            if (tower == null) continue;
            SimEventObject? eventObj_1EB83C_4000A6E7 = null;
            world.Events.Add(33.96f, () => eventObj_1EB83C_4000A6E7 = world.SpawnEventObject(new EventObjectSpawnConfig(EObjRowId: EObjId.TowerTimer, Placement: new Placement(tower.Position, -0.000f), IsVisible: false)));
            world.Events.Add(34.02f, () => eventObj_1EB83C_4000A6E7?.SetVisible(true));
            world.Events.Add(43.66f, () => eventObj_1EB83C_4000A6E7?.Despawn());
        }
    }

    private void Run_EventObj_1EB83E_4000A6E8()
    {
        for(int index = 0; index < 6; index++)
        {
            var i = index;
            var tower = state.Towers[i];
            if (tower == null) continue;
            var eObjId = tower.MinPlayers == 1 ? EObjId.TowerSolo : EObjId.TowerPair;
            short[] stateIds = tower.MinPlayers == 1 ? [1, 16] : [1, 16, 32];
            SimEventObject? eventObj_1EB83E_4000A6E8 = null;
            world.Events.Add(33.96f, () => eventObj_1EB83E_4000A6E8 = world.SpawnTower(new EventObjectSpawnConfig(EObjRowId: eObjId, Placement: new Placement(tower.Position, -0.000f), IsVisible: false), stateIds, Geometry.TowerRadius));
            world.Events.Add(34.02f, () => eventObj_1EB83E_4000A6E8?.SetVisible(true));
            world.Events.Add(43.66f, () => eventObj_1EB83E_4000A6E8?.Despawn());
        }
    }

    private void Run_Rear_Power_Unit_4000A641()
    {
        SimEnemy? rear_Power_Unit_4000A641 = null;
        world.Events.Add(1f, () => rear_Power_Unit_4000A641 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.RearPowerUnit, NameId: BNpcNameId.RearPowerUnit, Level: 90, Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false, Placement: state.NewNorthB.Apply(new Placement(new Vector3(0.000f, -0.000f, 0.000f), 0f)))));
        world.Events.Add(45.97f, () => rear_Power_Unit_4000A641?.PlayActionTimeline(TimelineId.Spawn));
        world.Events.Add(46.00f, () => rear_Power_Unit_4000A641?.SetVisible(true));
        world.Events.Add(47.88f, () => rear_Power_Unit_4000A641?.AttachLockonVfx(state.SpinnerRotation.LockonId, persistent: false));
        world.Events.Add(54.97f, () => rear_Power_Unit_4000A641?.Cast(ActionId.RearLasers, targetLocation: state.NewNorthB.Apply(new Vector3(0f, 0f, -25f)), castSeconds: 2.700f, targetId: rear_Power_Unit_4000A641?.GameObjectId));
        world.Events.Add(57.7f, () => ResolveRearLasers(rear_Power_Unit_4000A641));
        for (int i = 0; i < 13; i++)
        {
            var rotation = state.SpinnerRotation.Mul * (i + 1) * MathF.PI / 20;
            world.Events.Add(58.54f + i * 0.58f, () => rear_Power_Unit_4000A641?.SetPosition(state.NewNorthB.Apply(new Placement(new Vector3(0.000f, 0.000f, 0.000f), rotation))));
            world.Events.Add(58.59f + i * 0.58f, () => rear_Power_Unit_4000A641?.Cast(ActionId.RearLasers_7B90, castSeconds: 0f));
            world.Events.Add(58.60f + i * 0.58f, () => ResolveRearLasers(rear_Power_Unit_4000A641));
        }
        world.Events.Add(66.13f, () => rear_Power_Unit_4000A641?.PlayActionTimeline(TimelineId.WarpOut));
    }

    private void ResolveRearLasers(SimEnemy? rearPowerUnit4000A641)
    {
        if (rearPowerUnit4000A641 is not { IsAlive: true } unit) return;
        foreach (var hit in party.Find.InsideActionAoe(ActionId.RearLasers_7B90, unit.Placement))
        {
            Plugin.Log.Info($"Hit: {hit.Role} by Rear Lasers (lethal)");
            hit.Die("Rear Lasers");
        }
    }

    private void Run_Omega_F_4000A40B_2()
    {
        if (state.OmegaFAttack == OmegaFAttack.Legs)
        {
            SimEnemy? omega_F_4000A40B_2 = null;
            world.Events.Add(59.62f, () => omega_F_4000A40B_2 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.OmegaM_233C, NameId: BNpcNameId.OmegaF, Level: 1, Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false, Placement: state.NewNorthB.Apply(new Placement(new Vector3(0, 0.000f, 9.9f), MathF.PI)))));
            world.Events.Add(59.66f, () => omega_F_4000A40B_2?.Cast(ActionId.SuperliminalSteel_7B2C, targetLocation: state.NewNorthB.Apply(new Vector3(21.21f, 0f, 49.50f)), castSeconds: 1.200f, targetId: omega_F_4000A40B_2?.GameObjectId, omenDelay: Duration.OmegaFAttackOmenDelay));
        }
    }

    private void Run_Omega_F_4000A40C_2()
    {
        if (state.OmegaFAttack == OmegaFAttack.Legs)
        {
            SimEnemy? omega_F_4000A40C_2 = null;
            world.Events.Add(59.62f, () => omega_F_4000A40C_2 = world.SpawnEnemy(new EnemySpawnConfig(BNpcBaseId: BNpcBaseId.OmegaM_233C, NameId: BNpcNameId.OmegaF, Level: 1, Targetable: false, EnemyList: EnemyListMode.Never, IsVisible: false, Placement: state.NewNorthB.Apply(new Placement(new Vector3(0f, -0.000f, 9.9f), MathF.PI)))));
            world.Events.Add(59.66f, () => omega_F_4000A40C_2?.Cast(ActionId.SuperliminalSteel_7B2B, targetLocation: state.NewNorthB.Apply(new Vector3(-21.21f, 0, 49.50f)), castSeconds: 1.200f, targetId: omega_F_4000A40C_2?.GameObjectId, omenDelay: Duration.OmegaFAttackOmenDelay));
        }
    }
}
