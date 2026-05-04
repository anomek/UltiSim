using System;
using System.Collections.Generic;
using System.Numerics;

namespace UltiSim.Core.SimObjects;

// Fixed-size 8-slot party indexed by PartyRole. Each slot holds either a
// SimPartyMember (spawned NPC) or the SimPlayer (the player's own role).
// Get(role) returns the SimCharacter at the slot or null when unfilled.
public sealed class SimParty : ISimObject
{
    private readonly SimCharacter?[] slots = new SimCharacter?[8];

    public SimCharacter? Get(int roleId)
        => roleId >= 0 && roleId < slots.Length ? slots[roleId] : null;

    public SimCharacter? Get(PartyRole role) => Get((int)role);

    // The role the local player fills — i.e. the slot SimPartyMember spawning
    // skipped (PartyPresets returns null for the player's own job). After Game
    // wires SimPlayer in, that slot is the SimPlayer reference. Falls back to
    // MainTank if no slot is occupied by a SimPlayer (no scenario hits this today).
    public PartyRole PlayerRole
    {
        get
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] is SimPlayer) return (PartyRole)i;
            return PartyRole.MainTank;
        }
    }

    // Walks all eight role slots and returns whichever resolved entity is closest
    // (XZ plane) to the given point. Empty slots are skipped.
    public SimCharacter? FindClosest(Vector3 from)
    {
        SimCharacter? best = null;
        var bestDist = float.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } c) continue;
            var pos = c.Position;
            var dx = pos.X - from.X;
            var dz = pos.Z - from.Z;
            var d = dx * dx + dz * dz;
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    // Returns up to `count` roles ordered by ascending XZ distance from `from`.
    // Useful when the caller needs to remember which slot was picked (e.g. so a
    // later AI step can position the chosen role's member). Empty slots are skipped.
    public IReadOnlyList<PartyRole> FindClosestRolesN(Vector3 from, int count)
    {
        var entries = new List<(PartyRole role, float distSq)>(8);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } c) continue;
            var pos = c.Position;
            var dx = pos.X - from.X;
            var dz = pos.Z - from.Z;
            entries.Add(((PartyRole)i, dx * dx + dz * dz));
        }
        entries.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        var result = new List<PartyRole>(Math.Min(count, entries.Count));
        for (int i = 0; i < count && i < entries.Count; i++)
            result.Add(entries[i].role);
        return result;
    }

    // Returns the count nearest party slots ordered by ascending XZ distance from
    // `from`. Empty slots are skipped.
    public IReadOnlyList<SimCharacter> FindClosestN(Vector3 from, int count)
    {
        var entries = new List<(SimCharacter c, float distSq)>(8);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } c) continue;
            var pos = c.Position;
            var dx = pos.X - from.X;
            var dz = pos.Z - from.Z;
            entries.Add((c, dx * dx + dz * dz));
        }
        entries.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        var result = new List<SimCharacter>(Math.Min(count, entries.Count));
        for (int i = 0; i < count && i < entries.Count; i++)
            result.Add(entries[i].c);
        return result;
    }

    internal void SetSlot(PartyRole role, SimCharacter character) => slots[(int)role] = character;

    internal IEnumerable<SimCharacter> ActiveMembers()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { IsAlive: true } m) yield return m;
    }

    public bool IsAlive
    {
        get
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] is { IsAlive: true }) return true;
            return false;
        }
    }

    public void Tick(float deltaSeconds)
    {
        for (int i = 0; i < slots.Length; i++) slots[i]?.Tick(deltaSeconds);
    }

    public void Despawn()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i]?.Despawn();
            slots[i] = null;
        }
    }
}
