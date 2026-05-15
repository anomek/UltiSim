using System;
using System.Collections.Generic;
using System.Numerics;

namespace UltiSim.Core.SimObjects;

// Fixed-size 8-slot party indexed by PartyRole. Each slot holds either a
// SimPartyMember (spawned NPC) or the SimPlayer (the player's own role).
// Get(role) returns the SimPartySlot at the slot or null when unfilled.
//
// HUD mirroring is owned by SimWorld (not SimParty) — see SimWorld.partyHud —
// so the addon-lifecycle listener has the same lifespan as the plugin and the
// SimParty.Empty sentinel doesn't accidentally register one at static init.
public sealed class SimParty : ISimObject
{
    public static readonly SimParty Empty = new();

    private readonly SimPartySlot?[] slots = new SimPartySlot?[8];

    public SimParty() { Find = new CharacterFind<SimPartySlot>(ActiveMembers); }

    public CharacterFind<SimPartySlot> Find { get; }

    public SimPartySlot? Get(int roleId)
        => roleId >= 0 && roleId < slots.Length ? slots[roleId] : null;

    public SimPartySlot? Get(PartyRole role) => Get((int)role);

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

    public SimPlayer? Player => Get(PlayerRole) as SimPlayer;

    internal void SetSlot(PartyRole role, SimPartySlot slot)
    {
        slot.Role = role;
        slots[(int)role] = slot;
    }

    internal void ForEachActive(Action<SimPartySlot> action)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { IsAlive: true } m) action(m);
    }

    // Kills every alive member with the given cause. Raidwide wipe primitive
    // used by mechanics whose failure is unsurvivable.
    public void WipeAllPlayers(string cause)
        => ForEachActive(m => { if (m.IsAlive) m.Die(cause); });

    // Raidwide knockback: pushes every active slot `distance` units away from
    // `source`. Each slot resolves its own direction from its current position.
    public void Knockback(Vector3 source, float distance)
        => ForEachActive(m => m.Knockback(source, distance));

    // Raidwide knockback resolved from KnockbackTable + Lumina Knockback sheet.
    // Resolves once at the party level so an unmapped action logs a single
    // warning rather than one per slot.
    public void Knockback(Vector3 source, uint knockbackId)
    {
        if (!KnockbackLookup.TryGet(knockbackId, out var distance, out var speed))
            return;
        ForEachActive(m => m.Knockback(source, distance, speed));
    }
    
    internal IEnumerable<SimPartySlot> ActiveMembers()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { IsAlive: true } m) yield return m;
    }

    // All filled slots with their role index, alive or dead. Used by PartyFinder
    // proximity queries (which follow the same no-alive-filter contract the old
    // FindClosest* methods had).
    internal IEnumerable<(PartyRole role, SimPartySlot member)> FilledSlots()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { } m) yield return ((PartyRole)i, m);
    }

    // Every filled slot, alive or dead. Used by the party HUD so dead members
    // keep their slot (HP=0 is the visible "dead" state) instead of being
    // silently dropped — and so other members don't shift up the list when
    // someone dies. Mechanic resolvers should keep using ActiveMembers.
    internal IEnumerable<SimPartySlot> AllMembers()
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] is { } m) yield return m;
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
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is null) continue;
            slots[i]!.Tick(deltaSeconds);
        }
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
