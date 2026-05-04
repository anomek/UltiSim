using UltiSim.Core.SimObjects;

namespace UltiSim.Core;

// A no-countdown status pinned at RemainingTime = -1f (engine sentinel for
// "hide timer"). The game's per-frame StatusManager tick decrements
// RemainingTime regardless of the sentinel — and on spawned party members
// twice per frame because they live in both ClientObjectManager and
// CharacterManager arrays — so the value drifts off -1f and the UI starts
// rendering a counter. Tick re-stamps -1f each frame to keep the timer hidden.
public sealed class PinnedStatus
{
    private readonly SimCharacter target;
    private readonly ushort statusId;

    public bool IsActive { get; private set; }

    internal PinnedStatus(SimCharacter target, ushort statusId)
    {
        this.target = target;
        this.statusId = statusId;
        Apply();
    }

    private unsafe void Apply()
    {
        Statuses.Apply((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.BattleCharaPtr, statusId, -1f);
        IsActive = true;
    }

    internal unsafe void Tick()
    {
        if (!IsActive) return;
        Statuses.SetRemaining((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.BattleCharaPtr, statusId, -1f);
    }

    public unsafe void Clear()
    {
        if (!IsActive) return;
        Statuses.Remove((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.BattleCharaPtr, statusId);
        IsActive = false;
    }
}
