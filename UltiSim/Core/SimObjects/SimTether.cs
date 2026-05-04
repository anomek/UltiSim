using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace UltiSim.Core.SimObjects;

// Two-ended tether. Each side writes the channeling-sheet id at slot 0 of its
// VfxContainer.Tethers, pointing at the other side's GameObjectId. Optionally
// also stamps a status (debuff) on both ends with a matching duration. Created
// by SimWorld.Tether(...); SimWorld owns the lifetime so a scenario reset always
// tears the tether (and the debuff) down even if the timed Despawn hasn't fired.
//
// Tick re-stamps RemainingTime each frame so the visible debuff timer matches
// our own elapsed counter regardless of how many times the game decrements it
// — spawned party members get ticked twice per frame because they live in both
// ClientObjectManager and CharacterManager arrays.
public sealed class SimTether : ISimObject
{
    private const byte Slot = 0;

    private readonly SimCharacter a;
    private readonly SimCharacter b;
    private readonly ushort debuffStatusId;
    private readonly float duration;
    private float elapsed;

    public ushort TetherId { get; }
    private bool active;

    internal SimTether(SimCharacter a, SimCharacter b, ushort tetherId, ushort debuffStatusId, float duration)
    {
        this.a = a;
        this.b = b;
        TetherId = tetherId;
        this.debuffStatusId = debuffStatusId;
        this.duration = duration;
        Apply();
    }

    private unsafe void Apply()
    {
        VfxFunctions.SetTether((Character*)a.BattleCharaPtr, Slot, TetherId, b.GameObjectId, 1);
        VfxFunctions.SetTether((Character*)b.BattleCharaPtr, Slot, TetherId, a.GameObjectId, 1);
        if (debuffStatusId != 0)
        {
            Statuses.Apply((Character*)a.BattleCharaPtr, debuffStatusId, duration);
            Statuses.Apply((Character*)b.BattleCharaPtr, debuffStatusId, duration);
        }
        active = true;
    }

    public unsafe void Tick(float deltaSeconds)
    {
        if (!active) return;
        elapsed += deltaSeconds;
        if (duration > 0f && elapsed >= duration)
        {
            // Auto-expire: drop the debuff and mark inactive but leave the VFX slot
            // alone. A chained tether (e.g. P5 Delta prep → real) can then Apply over
            // the slot in the same frame without us racing it with ClearTether(0).
            // SimWorld.Reset still calls Despawn() explicitly which forcibly tears the VFX.
            if (debuffStatusId != 0)
            {
                Statuses.Remove((Character*)a.BattleCharaPtr, debuffStatusId);
                Statuses.Remove((Character*)b.BattleCharaPtr, debuffStatusId);
            }
            active = false;
            return;
        }
        if (debuffStatusId == 0 || duration <= 0f) return;

        var remaining = duration - elapsed;
        Statuses.SetRemaining((Character*)a.BattleCharaPtr, debuffStatusId, remaining);
        Statuses.SetRemaining((Character*)b.BattleCharaPtr, debuffStatusId, remaining);
    }

    public unsafe void Despawn()
    {
        // Always clear VFX (even if already inactive from auto-expire) so SimWorld.Reset
        // tears down any leftover tether visual that auto-expire intentionally left in
        // place. ClearTether on an already-empty slot is a harmless no-op.
        VfxFunctions.ClearTether((Character*)a.BattleCharaPtr, Slot);
        VfxFunctions.ClearTether((Character*)b.BattleCharaPtr, Slot);
        if (debuffStatusId != 0 && active)
        {
            Statuses.Remove((Character*)a.BattleCharaPtr, debuffStatusId);
            Statuses.Remove((Character*)b.BattleCharaPtr, debuffStatusId);
        }
        active = false;
    }
}
