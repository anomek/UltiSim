using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace UltiSim.Core;

// Direct writes into BattleChara.StatusManager._status. Bypasses the game's
// AddStatus path (which expects server attribution) — fine for sim-only buffs
// since the client renderer just scans the array for non-zero StatusIds and
// the game decrements RemainingTime each frame on its own.
internal static unsafe class Statuses
{
    public static void Apply(Character* chara, ushort statusId, float duration)
    {
        if (chara == null || statusId == 0) return;
        var bc = (BattleChara*)chara;
        var slots = bc->StatusManager.Status;

        var slot = -1;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId == statusId) { slot = i; break; }
            if (slot < 0 && slots[i].StatusId == 0) slot = i;
        }
        if (slot < 0) return;

        slots[slot].StatusId = statusId;
        slots[slot].Param = 0;
        slots[slot].RemainingTime = duration;
        slots[slot].SourceObject = default;

        if (slot >= bc->StatusManager.NumValidStatuses)
            bc->StatusManager.NumValidStatuses = (byte)(slot + 1);
    }

    // Removes the matching status and compacts the array — slides every following
    // non-zero slot down so the array stays packed at low indices. Without this,
    // a Remove leaves a gap at slot K and the next Apply lands at K+1 (or higher).
    // The _PartyList addon pairs display-slot N with raw StatusManager slot N for
    // the timer text, while PartyHud packs icons starting at display 0; if the BC
    // has any gap, those two views drift and timers display on the wrong icons
    // (or vanish). Keeping the array compact keeps both views aligned.
    public static void Remove(Character* chara, ushort statusId)
    {
        if (chara == null || statusId == 0) return;
        var bc = (BattleChara*)chara;
        var slots = bc->StatusManager.Status;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId != statusId) continue;
            for (int j = i; j < slots.Length - 1; j++) slots[j] = slots[j + 1];
            slots[slots.Length - 1] = default;
            RecomputeNumValidStatuses(bc);
            return;
        }
    }

    private static void RecomputeNumValidStatuses(BattleChara* bc)
    {
        var slots = bc->StatusManager.Status;
        var max = 0;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].StatusId != 0) max = i + 1;
        bc->StatusManager.NumValidStatuses = (byte)max;
    }

    // Rewrites RemainingTime on an already-applied status. Used to undo the
    // game's per-frame decrement on entities that get ticked more than once
    // (spawned party members are registered in CharacterManager and
    // ClientObjectManager — both arrays drive a status tick).
    public static void SetRemaining(Character* chara, ushort statusId, float remaining)
    {
        if (chara == null || statusId == 0) return;
        var bc = (BattleChara*)chara;
        var slots = bc->StatusManager.Status;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].StatusId == statusId)
            {
                slots[i].RemainingTime = remaining;
                return;
            }
        }
    }
}
