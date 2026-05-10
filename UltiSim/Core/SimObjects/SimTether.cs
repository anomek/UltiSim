using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace UltiSim.Core.SimObjects;

// Two-ended tether. Each side writes the channeling-sheet id at slot 0 of its
// VfxContainer.Tethers, pointing at the other side's GameObjectId. Optionally
// asks each character to host a matching debuff for the tether's duration —
// SimCharacter.AddStatus(id, duration) owns the countdown and auto-removes on expiry.
//
// SimTether ticks its own elapsed counter so it can clear the tether VFX when
// duration is reached. Both auto-expire and Despawn (called via SimWorld.Reset
// or directly) use a sentinel check: only clear the slot when our TetherId is
// still occupying it. This handles chained tethers that overwrite the same
// slot in the same frame (e.g. P5 Delta prep → real at t=29s) without racing
// our own ClearTether against the new SetTether.
//
// Distance / endpoint-death break logic lives in the owning scenario — SimTether
// only renders the visual and ticks expiry.
public sealed unsafe class SimTether : ISimObject
{
    private const byte Slot = 0;

    private readonly SimCharacter source;
    private readonly Func<SimCharacter> target;
    private readonly SimStatus? statusA;
    private SimStatus? statusB;
    private readonly float duration;
    private readonly ushort DebuffStatusId;
    private float elapsed;
    private bool active;
    private SimCharacter currentTarget;

    public ushort TetherId { get; }
    
    public SimCharacter A => source;
    public SimCharacter B => currentTarget;
    // Set by the scenario when this tether has been resolved (broken or failed) to
    // prevent duplicate processing if multiple triggers fire in the same frame.
    public bool Resolved { get; set; }

    public static bool IsAnyDead(SimTether t) => !t.A.IsAlive || !t.B.IsAlive;
    public bool StretchGt(float distance) => Vector3.DistanceSquared(A.Position, B.Position) > distance * distance;
    public bool StretchLt(float distance) => Vector3.DistanceSquared(A.Position, B.Position) < distance * distance;

    internal SimTether(SimCharacter source, Func<SimCharacter> target, ushort tetherId, ushort debuffStatusId, float duration)
    {
        this.source = source;
        this.target = target;
        TetherId = tetherId;
        DebuffStatusId = debuffStatusId;
        this.duration = duration;
        currentTarget = target();
        
        VfxFunctions.SetTether((Character*)source.BattleCharaPtr, Slot, TetherId, currentTarget.GameObjectId, 1);
        if (debuffStatusId != 0 && duration > 0f)
        {
            statusA = source.AddStatus(debuffStatusId, duration);
            statusB = currentTarget.AddStatus(debuffStatusId, duration);
        }
        active = true;
    }

    public void Tick(float deltaSeconds)
    {
        if (!active) return;
        if (duration <= 0f) return; // permanent tether — only Despawn clears it
        elapsed += deltaSeconds;
        if (elapsed >= duration)
        {
            ClearTetherVfxIfOwned();
            active = false;
        }
        
        var nextTarget = target();
        if (nextTarget != currentTarget)
        {
            ClearTetherVfxIfOwned();
            statusB?.Despawn();
            currentTarget = nextTarget;
            VfxFunctions.SetTether((Character*)source.BattleCharaPtr, Slot, TetherId, currentTarget.GameObjectId, 1);
            if (DebuffStatusId != 0 && duration > 0f)
            {
                statusB = currentTarget.AddStatus(DebuffStatusId, duration);
            }
        }
    }
    
    public void Despawn()
    {
        if (active)
        {
            ClearTetherVfxIfOwned();
            active = false;
        }
        // SimStatus.Despawn is idempotent — safe even if already auto-expired.
        statusA?.Despawn();
        statusB?.Despawn();
    }

    // Sentinel-checked clear: only wipe a slot we still own. A chained tether
    // (new SetTether on the same slot via the new SimTether ctor) will have
    // overwritten Vfx.Tethers[slot].Id; we leave that alone.
    private void ClearTetherVfxIfOwned()
    {
        var ca = (Character*)source.BattleCharaPtr;
        if (VfxFunctions.GetTetherId(ca, Slot) == TetherId) VfxFunctions.ClearTether(ca, Slot);
    }
}
