using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.Map;

namespace UltiSim.Core.SimObjects;

// Holds the live game state Game manipulates: a children list of SimObjects
// (party, enemies, tethers, waymarks, hidden objects). Spawn entry points
// (CreateParty, SpawnEnemy, Tether, PlaceWaymarks, HideObject,
// EnforceArenaBoundary) construct the SimObject and register it for teardown.
// Zone loading and map effects go through world.Map.
public sealed class SimWorld : ISimObject, IDisposable
{
    // Ownership
    private readonly List<ISimObject> children = new();
    private readonly EnmityHud enmityHud = new();
    private readonly PartyHud partyHud = new();

    // Zone loading and map effects entry point.
    public MapController Map { get; } = new();

    // Convenience reference — SimParty.Empty until CreateParty is called.
    public SimParty Party { get; private set; } = SimParty.Empty;
    public IEnumerable<ISimObject> Children => children;
    public EventScheduler Events { get; }
    public Vector3 ScenarioOrigin { get; set; }

    public SimWorld(EventScheduler events)
    {
        Events = events;
    }

    public SimTether Tether(SimCharacter a, SimCharacter b, ushort tetherId, float duration = 0f, ushort debuffStatusId = 0)
    {
        return Tether(a, () => b, tetherId, duration, debuffStatusId);
    }

    public SimTether TetherFarestPlayer(SimCharacter a, ushort tetherId, float duration = 0f, ushort debuffStatusId = 0)
    {
        return Tether(a, () => Party.Find.Farest(a.Position)!, tetherId, duration, debuffStatusId);
    }
    
    public SimTether Tether(SimCharacter a, Func<SimCharacter> b, ushort tetherId, float duration = 0f, ushort debuffStatusId = 0)
    {
        var tether = new SimTether(a, b, tetherId, debuffStatusId, duration);
        children.Add(tether);
        return tether;
    }

    
    public SimEnemy? SpawnEnemy(EnemySpawnConfig config)
    {
        var enemy = SimEnemy.Spawn(config, ScenarioOrigin, Events);
        if (enemy != null) children.Add(enemy);
        return enemy;
    }

    // Places the scenario's waymark layout. Coordinates are scenario-relative;
    // resolved against the snapshotted ScenarioOrigin.
    public void PlaceWaymarks(IReadOnlyList<Waymark> waymarks)
    {
        if (waymarks.Count == 0) return;
        children.Add(new SimWaymarks(waymarks, ScenarioOrigin));
    }

    // Suppress a native GameObject (by BaseId) for the duration of the scenario.
    public void HideObject(uint baseId)
    {
        var hidden = SimHiddenObject.Hide(baseId);
        if (hidden != null) children.Add(hidden);
    }

    // Per-frame arena fence at `radius` from ScenarioOrigin. Kills any active
    // party member (player included) who leaves the ring, and spawns a VFX border.
    public void EnforceArenaBoundary(float radius, string cause = "Walked out of arena")
        => children.Add(new SimArenaBoundary(Party, ScenarioOrigin, radius, cause, showVfx: !Map.IsInInstance));

    // Spawns the eight party slots and wires in the local player. Must be called
    // after ScenarioOrigin is set. Party is added first so it despawns last in
    // Reset's reverse-order teardown (tethers and enemies reference slot positions).
    public SimParty CreateParty(uint playerJob)
    {
        var party = new SimParty();
        // PartyHud always drives addon output (icons, timer text, Targetable)
        // via NumberArray writes + direct text-node stamping. PartyCreator
        // additionally inserts doppels into CharacterManager._battleCharas
        // when the player is in an inn or any duty (low BC density, controlled
        // contexts), enabling row-click targeting and mouseover tooltips
        // there; in the open world we keep them out to avoid the render-cache
        // teardown crash documented in EnmityHud.cs.
        PartyCreator.Populate(party, new SimPlayer(), playerJob, ScenarioOrigin);
        children.Add(party);
        Party = party;
        return party;
    }

    public void Tick(float deltaSeconds)
    {
        // Game.Tick has already advanced Events for this frame; we just tick
        // entities here. Snapshot the count: a child's Tick may register a new
        // child (rare today), so iterating by index against the live count is
        // safe but the snapshot guards against future drift.
        var count = children.Count;
        for (int i = 0; i < count; i++) children[i].Tick(deltaSeconds);
        enmityHud.Refresh(children.OfType<SimEnemy>());
        partyHud.Refresh(Party);
    }

    public void Reset()
    {
        // Events is owned by Game; Game.ResetInternal clears it before calling here.
        // Reverse-insertion order: tethers and enemies (added during scenario.Run)
        // despawn before the party they reference; party despawns before hidden
        // objects, which were added at the start.
        for (int i = children.Count - 1; i >= 0; i--) children[i].Despawn();
        children.Clear();
        Party = SimParty.Empty;
        enmityHud.Clear();
        partyHud.Clear();
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
        Map.Dispose();
    }
}
