using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// EventObject-backed tower whose SharedGroup state is indexed by occupancy.
// `states[i]` is the state to display when exactly i party members stand
// within `radius` of the tower (XZ plane). Counts past states.Length-1 clamp
// to the last entry, so a 3-element array gives "empty / 1-inside / 2+ inside".
// Occupancy is sampled each tick via Party.Find.InsideCircle; SetState only
// fires on a count change so the engine SG notify isn't spammed every frame.
public sealed unsafe class SimTower : SimEventObject
{
    private readonly SimParty party;
    private readonly float radius;
    private readonly short[] states;
    private int? lastCount;

    private SimTower(int slot, GameObject* obj, SimWorld world, uint eObjRowId,
                     short[] states, float radius, SimParty party)
        : base(slot, obj, world, eObjRowId, states[0])
    {
        this.party = party;
        this.radius = radius;
        this.states = states;
    }

    internal static SimTower? Spawn(
        EventObjectSpawnConfig config, SimWorld world, EventScheduler events,
        short[] states, float radius, SimParty party)
    {
        if (states == null || states.Length == 0)
        {
            Plugin.Log.Warning("SimTower: states array must contain at least one entry (states[0] = empty)");
            return null;
        }
        if (!EventObjectSpawn.Create(config.EObjRowId, out var slot, out var obj))
            return null;

        var worldPos = world.ToWorld(config.Placement.Position);
        obj->SetPosition(worldPos.X, worldPos.Y, worldPos.Z);
        obj->SetRotation(MathUtil.NormalizeRotation(config.Placement.Rotation));

        var tower = new SimTower(slot, obj, world, config.EObjRowId, states, radius, party);

        if (config.Lifetime > 0f) events.Add(config.Lifetime, tower.Despawn);

        Plugin.Log.Info($"SimTower: spawned EObj 0x{config.EObjRowId:X} at slot {slot} ({worldPos.X:F2},{worldPos.Y:F2},{worldPos.Z:F2}) radius={radius:F1} states=[{string.Join(",", states)}]");
        return tower;
    }

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (!IsAlive) return;
        var count = party.Find.InsideCircle(Position, radius).Count;
        if (lastCount == count) return;
        lastCount = count;
        var idx = count >= states.Length ? states.Length - 1 : count;
        SetState(states[idx]);
    }
}
