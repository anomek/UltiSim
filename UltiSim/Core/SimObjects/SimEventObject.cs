using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// Placement.Position is scenario-local (offset from SimWorld.ScenarioOrigin) —
// same coordinate space as SimEventObject.Position / SetPosition once spawned.
// EObjRowId references Lumina's EObj sheet — the row's SgbPath/PopType drives
// the model picked by the engine's internal SharedGroup attach. ModelChara
// substitution is not part of the EObj pipeline; pick the right EObj row.
//
// VisibleState is the SG state index that means "visible" for this EObj. The
// orb (1EB83C) is already visible at the engine default state=0, so leave it
// at 0. The Sigma ground circles (1EB83D / 1EB83E) need state=16 to render
// fully; state=1..6 partial-renders are the engine's player-proximity animation
// frames. SetVisible toggles between this value and 0.
public record struct EventObjectSpawnConfig(
    uint EObjRowId,
    Placement Placement = default,
    bool IsVisible = true,
    short VisibleState = 0,
    float Lifetime = 0f);

// Handle around an EventObject GameObject allocated via the manager's
// CreateEventObject (the 40-slot pool exposed in GameObjectManager indices
// 449-488). Mirror of SimEnemy / SimNpc for the EObj actor pool: we own the
// slot, write position/rotation/state directly on the GameObject, and release
// the slot on Despawn via GameObject.Terminate (vfunc 60).
//
// Rendering note: EObjs render via the LayoutEngine scene graph using their
// attached SharedGroup, NOT via GameObject.DrawObject. Visibility is therefore
// driven by the state field at actor+0x1B2 (which gates which SG sub-instances
// are visible), not by EnableDraw/DisableDraw — those are character-only.
//
// Why not packets: the canonical spawn path is HandleSpawnObjectPacket, which
// brings zone-state guards, housing/MJI branches, and forwards to SetEventId/
// SetFateId/SetEventState that don't apply to simulated scenery. We use the
// same internals-only pattern BattleCharaSpawn uses for SimEnemy/SimPartyMember.
public unsafe class SimEventObject : ISimObject, IPositioned
{
    private int slot = -1;
    private GameObject* obj;
    private readonly SimWorld world;
    private readonly short visibleState;

    public uint EObjRowId { get; }
    public string DisplayName => $"EObj 0x{EObjRowId:X}";
    public int Slot => slot;
    public nint Address => (nint)obj;

    public bool IsAlive => slot >= 0 && obj != null;

    // Stored Position/Rotation mirror the native GameObject — mutators write
    // both, and Tick re-syncs from native to catch any direct-struct writes.
    public Vector3 Position { get; private set; }
    public float Rotation { get; private set; }

    protected SimEventObject(int slot, GameObject* obj, SimWorld world, uint eObjRowId, short visibleState)
    {
        this.slot = slot;
        this.obj = obj;
        this.world = world;
        this.visibleState = visibleState;
        EObjRowId = eObjRowId;
    }

    internal static SimEventObject? Spawn(EventObjectSpawnConfig config, SimWorld world, EventScheduler events)
    {
        if (!EventObjectSpawn.Create(config.EObjRowId, out var slot, out var obj))
            return null;

        var worldPos = world.ToWorld(config.Placement.Position);
        obj->SetPosition(worldPos.X, worldPos.Y, worldPos.Z);
        obj->SetRotation(MathUtil.NormalizeRotation(config.Placement.Rotation));

        var eo = new SimEventObject(slot, obj, world, config.EObjRowId, config.VisibleState);
        // Seed stored Position/Rotation. The native struct writes above are
        // authoritative for the engine; SetPosition mirrors them into the C#
        // fields (and harmlessly re-pushes to native).
        eo.SetPosition(config.Placement);
        if (config.IsVisible && config.VisibleState != 0)
            eo.SetState(config.VisibleState);

        if (config.Lifetime > 0f) events.Add(config.Lifetime, eo.Despawn);

        Plugin.Log.Info($"SimEventObject: spawned EObj 0x{config.EObjRowId:X} at slot {slot} ({worldPos.X:F2},{worldPos.Y:F2},{worldPos.Z:F2})");
        return eo;
    }

    public void SetPosition(Vector3 position)
    {
        Position = position;
        if (obj == null) return;
        var w = world.ToWorld(position);
        obj->SetPosition(w.X, w.Y, w.Z);
    }

    public void SetPosition(Placement placement)
    {
        Position = placement.Position;
        Rotation = MathUtil.NormalizeRotation(placement.Rotation);
        if (obj == null) return;
        var w = world.ToWorld(placement.Position);
        obj->SetPosition(w.X, w.Y, w.Z);
        obj->SetRotation(Rotation);
    }

    // Writes the EObj state field at actor+0x1B2 and (when the SharedGroup
    // layout instance is attached at actor+0x108) notifies the SG to flip
    // sub-instance visibility. Per-EObj state values are SG-specific —
    // experiment empirically to find what activates a given visual. Safe to
    // call before the SG instance is attached: only the field write happens,
    // the notify silently no-ops; the engine picks up the field once attached.
    public void SetState(short state)
    {
        if (obj == null) return;
        EventObjectSpawn.SetState(obj, state);
    }

    // Convenience for parser-driven scenarios that emit SetVisible from
    // ACT 261|Change ModelStatus events. Flips between the configured
    // VisibleState and 0 (the engine default / "hidden" for gated SGs).
    public void SetVisible(bool visible) => SetState(visible ? visibleState : (short)0);

    public virtual void Tick(float deltaSeconds)
    {
        // Re-sync stored Position/Rotation from native — catches any
        // direct-struct writes between Ticks (engine doesn't move EObjs on
        // its own, but the parallel pattern with SimNpc keeps the contract
        // uniform across IPositioned implementers).
        if (obj == null) return;
        Position = world.ToLocal(obj->Position);
        Rotation = obj->Rotation;
    }

    public void Despawn()
    {
        if (slot < 0) return;
        var releasedSlot = slot;
        slot = -1;
        obj = null;
        EventObjectSpawn.Destroy(releasedSlot);
        Plugin.Log.Info($"SimEventObject: despawned slot {releasedSlot}");
    }
}
