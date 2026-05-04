using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace UltiSim.Core.SimObjects;

// Holds the live game state Game manipulates: a fixed-slot SimParty plus a
// children list of transient scenario SimObjects (enemies, tethers, waymarks,
// hidden objects). Spawn entry points (SpawnEnemy, Tether, PlaceWaymarks,
// HideObject) construct the SimObject and register it for teardown. The actual
// BattleChara construction lives on the SimObject types themselves.
public sealed class SimWorld : ISimObject, IDisposable
{
    // Transient scenario children: enemies, tethers, waymarks. Order matters for
    // teardown — we Despawn in reverse-insertion order so things added later
    // (tethers reference entities; tethers spawn after entities) clear first.
    // Party is intentionally NOT in this list: it's a permanent fixture whose
    // Despawn clears slots without freeing the container.
    private readonly List<ISimObject> children = new();
    private readonly PartyHud partyHud = new();
    private readonly EnmityHud enmityHud = new();

    public SimParty Party { get; } = new();
    public SimPlayer Player { get; } = new();
    public IEnumerable<SimEnemy> Enemies => children.OfType<SimEnemy>();
    // Passthrough to Game's EventScheduler so scenarios and SimObjects keep
    // calling world.Events.Add(...). The instance itself is owned by Game.
    public EventScheduler Events { get; }
    // Set by Game before scenario.Run; read by SpawnEnemy / PlaceWaymarks for
    // scenario-relative coordinate resolution. Ad-hoc callers (e.g. MainWindow
    // debug Spawn) are responsible for stamping this themselves before spawning.
    public Vector3 ScenarioOrigin { get; set; }

    public SimWorld(EventScheduler events)
    {
        Events = events;
    }

    // Sets a bidirectional tether between two targets and tracks it for cleanup.
    // duration > 0 makes the tether self-expire on its own elapsed counter (driven
    // by SimWorld.Tick); Reset also clears it so a mid-scenario abort can't leave VFX
    // dangling on still-alive party members or the local player. debuffStatusId
    // optionally stamps a matching status on both ends, removed in lockstep.
    public SimTether Tether(SimCharacter a, SimCharacter b, ushort tetherId, float duration = 0f, ushort debuffStatusId = 0)
    {
        var tether = new SimTether(a, b, tetherId, debuffStatusId, duration);
        children.Add(tether);
        return tether;
    }

    // Places the scenario's waymark layout via MarkingController.PlacePreset.
    // Coordinates are scenario-relative; we resolve them against the snapshotted origin.
    internal void PlaceWaymarks(IReadOnlyList<Waymark> waymarks)
    {
        if (waymarks.Count == 0) return;
        children.Add(new SimWaymarks(waymarks, ScenarioOrigin));
    }

    internal void HideObject(uint baseId)
    {
        var hidden = SimHiddenObject.Hide(baseId);
        if (hidden != null) children.Add(hidden);
    }

    public SimEnemy? SpawnEnemy(EnemySpawnConfig config)
    {
        var enemy = SimEnemy.Spawn(config, ScenarioOrigin, Events);
        if (enemy != null) children.Add(enemy);
        return enemy;
    }

    public void Tick(float deltaSeconds)
    {
        // Game.Tick has already advanced Events for this frame; we just tick
        // entities here. Snapshot the count: a child's Tick may register a new
        // child (rare today), so iterating by index against the live count is
        // safe but the snapshot guards against future drift.
        var count = children.Count;
        for (int i = 0; i < count; i++) children[i].Tick(deltaSeconds);
        Party.Tick(deltaSeconds);
        // Tick the player explicitly too: Party only ticks Player when the
        // player slot has been wired in (Game does this after PartyCreator).
        // Outside that window, Player still needs ticking for any overlays
        // that may have been applied (defensive — no-op if none).
        Player.Tick(deltaSeconds);
        enmityHud.Refresh(Enemies);
        partyHud.Refresh(Party);
    }

    public void Reset()
    {
        // Events is owned by Game; Game.ResetInternal clears it before calling here.
        // Reverse-insertion order: tethers (added late) clear before the entities
        // they reference; hidden objects (added at scenario start) restore last
        // among children.
        for (int i = children.Count - 1; i >= 0; i--) children[i].Despawn();
        children.Clear();
        Party.Despawn();      // SimPartyMember slots free their handles; SimPlayer slot (if any) clears overlays
        Player.Despawn();     // defensive: ensure overlays cleared even if Party didn't hold the player slot
        partyHud.Clear();
        enmityHud.Clear();
        ScenarioOrigin = default;
    }

    // ISimObject.Despawn is the contract entry point for teardown; it forwards to
    // Reset() so a parent driver (e.g. Game) can treat SimWorld uniformly.
    void ISimObject.Despawn() => Reset();

    public void Dispose()
    {
        Reset();
        enmityHud.Dispose();
        partyHud.Dispose();
    }
}
