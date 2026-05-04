using FFXIVClientStructs.FFXIV.Client.Game.Character;
using UltiSim.Core.SimObjects;

namespace UltiSim.Core;

// A status with a visible countdown that re-stamps RemainingTime each frame from
// our own elapsed counter so the displayed timer stays correct on dual-registered
// party-spawn entities (which the engine ticks twice per frame). Auto-removes the
// status when elapsed >= duration.
public sealed class TimedStatus
{
    private readonly SimCharacter target;
    private readonly ushort statusId;
    private readonly float duration;
    private float elapsed;

    public bool IsActive { get; private set; }

    internal TimedStatus(SimCharacter target, ushort statusId, float duration)
    {
        this.target = target;
        this.statusId = statusId;
        this.duration = duration;
        Apply();
    }

    private unsafe void Apply()
    {
        Statuses.Apply((Character*)target.BattleCharaPtr, statusId, duration);
        IsActive = true;
    }

    internal unsafe void Tick(float deltaSeconds)
    {
        if (!IsActive) return;
        elapsed += deltaSeconds;
        if (duration > 0f && elapsed >= duration)
        {
            Statuses.Remove((Character*)target.BattleCharaPtr, statusId);
            IsActive = false;
            return;
        }
        var remaining = duration - elapsed;
        Statuses.SetRemaining((Character*)target.BattleCharaPtr, statusId, remaining);
    }

    public unsafe void Clear()
    {
        if (!IsActive) return;
        Statuses.Remove((Character*)target.BattleCharaPtr, statusId);
        IsActive = false;
    }
}
