using System;
using System.Numerics;

namespace UltiSim.Core.SimObjects;

// Abstract layer between SimCharacter and the two party-slot types: SimPlayer
// (the real local player) and SimPartyMember (a spawned NPC doppel, via SimNpc).
// Carries PartyRole so any code that holds a party-slot reference can identify
// which slot it is without down-casting to the concrete type.
//
// The setter is internal so only engine code (SimParty.SetSlot, SimPartyMember's
// constructor) can assign the role; scenario code observes it read-only.
public abstract class SimPartySlot : SimCharacter
{
    public PartyRole Role { get; internal set; }

    // Default knockback speed (yalms/sec) when the caller doesn't know the
    // specific action — matches the FFXIV Knockback sheet's most common Speed
    // for short slides (~25). MoveTo uses the run timeline (not a proper
    // knockback animation) and faces direction-of-travel, so the visual is
    // approximate; positions are correct.
    protected const float KnockbackSpeed = 25f;

    // No-op when the slot is co-located with `source` (direction undefined).
    // The 3-arg form is the virtual one — SimPlayer overrides it to drive a
    // tick-based slide on the real player's position (gated to the fake
    // instance). The 2-arg and actionId overloads thread through it.
    public void Knockback(Vector3 source, float distance)
        => Knockback(source, distance, KnockbackSpeed);

    public virtual void Knockback(Vector3 source, float distance, float speed)
    {
        var current = Position;
        var dx = current.X - source.X;
        var dz = current.Z - source.Z;
        var distSq = dx * dx + dz * dz;
        if (distSq < 1e-6f) return;
        var scale = distance / MathF.Sqrt(distSq);
        var target = new Vector3(
            current.X + dx * scale,
            current.Y,
            current.Z + dz * scale);
        MoveTo(target, speed);
    }

    // Resolves the actual Distance/Speed from the Lumina Knockback sheet via
    // the auto-generated actionId -> row table. Logs a warning and no-ops when
    // the action isn't in the table (re-run tools/parser.py --emit-knockback-table
    // against a log containing the action to populate it).
    public void Knockback(Vector3 source, uint actionId)
    {
        if (!KnockbackLookup.TryGet(actionId, out var distance, out var speed))
            return;
        Knockback(source, distance, speed);
    }
}
