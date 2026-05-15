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

    private readonly SimCharacter? source;
    private readonly Func<SimCharacter?> target;
    private readonly SimStatus? statusA;
    private SimStatus? statusB;
    private readonly float duration;
    private readonly ushort debuffStatusId;
    private float elapsed;
    private bool active;
    private SimCharacter? currentTarget;
    private ConditionalStatus? conditionalStatus;
    private bool autoFaceTarget;

    public ushort TetherId { get; }
    
    public SimCharacter? A => source;
    public SimCharacter? B => currentTarget;
    // Set by the scenario when this tether has been resolved (broken or failed) to
    // prevent duplicate processing if multiple triggers fire in the same frame.
    public bool Resolved { get; set; }
    public bool IsActive => active;

    public static bool IsAnyDead(SimTether t) =>
        t.A is not { IsAlive: true } || t.B is not { IsAlive: true };
    public bool StretchGt(float distance) =>
        A is { } a && B is { } b && Vector3.DistanceSquared(a.Position, b.Position) > distance * distance;
    public bool StretchLt(float distance) =>
        A is { } a && B is { } b && Vector3.DistanceSquared(a.Position, b.Position) < distance * distance;

    internal SimTether(SimCharacter? source, Func<SimCharacter?> target, ushort tetherId, ushort debuffStatusId, float duration)
    {
        this.source = source;
        this.target = target;
        TetherId = tetherId;
        this.debuffStatusId = debuffStatusId;
        this.duration = duration;
        currentTarget = target();
        
        CreateVfx();
        if (debuffStatusId != 0 && duration > 0f)
        {
            statusA = source?.AddStatus(debuffStatusId, duration);
            statusB = currentTarget?.AddStatus(debuffStatusId, duration);
        }
        active = true;
    }

    public void Tick(float deltaSeconds)
    {
        if (!active) return;
        if (duration > 0f) 
        {
            elapsed += deltaSeconds;
            if (elapsed >= duration)
            {
                if (conditionalStatus != null)
                {
                    source?.RemoveStatus(conditionalStatus.StatusId);
                    currentTarget?.RemoveStatus(conditionalStatus.StatusId);
                }
                ClearTetherVfxIfOwned();
                active = false;
                return;
            }
        }
        
        var nextTarget = target();
        if (nextTarget != currentTarget)
        {
            ClearTetherVfxIfOwned();
            statusB?.Despawn();
            currentTarget = nextTarget;
            CreateVfx();
            if (debuffStatusId != 0 && duration > 0f)
            {
                statusB = currentTarget?.AddStatus(debuffStatusId, duration);
            }
        }

        if (conditionalStatus != null)
        {
            if (conditionalStatus.Condition(this))
            {
                source?.AddStatus(conditionalStatus.StatusId);
                currentTarget?.AddStatus(conditionalStatus.StatusId);
            }
            else
            {
                source?.RemoveStatus(conditionalStatus.StatusId);
                currentTarget?.RemoveStatus(conditionalStatus.StatusId);
            }
        }
        
        if (autoFaceTarget && currentTarget != null)
        {
            source?.Face(currentTarget);
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
        if (conditionalStatus != null)
        {
            source?.RemoveStatus(conditionalStatus.StatusId);
            currentTarget?.RemoveStatus(conditionalStatus.StatusId);
        }
    }

    private void CreateVfx()
    {
        if (source != null && currentTarget != null)
            VfxFunctions.SetTether((Character*)source.BattleCharaPtr, Slot, TetherId, currentTarget.GameObjectId, 1);
    }

    // Sentinel-checked clear: only wipe a slot we still own. A chained tether
    // (new SetTether on the same slot via the new SimTether ctor) will have
    // overwritten Vfx.Tethers[slot].Id; we leave that alone.
    private void ClearTetherVfxIfOwned()
    {
        if (source != null)
        {
            var ca = (Character*)source.BattleCharaPtr;
            if (VfxFunctions.GetTetherId(ca, Slot) == TetherId) VfxFunctions.ClearTether(ca, Slot);
        }
    }

    public SimTether SetConditionalStatus(ushort statusId, Predicate<SimTether> predicate)
    {
        conditionalStatus = new ConditionalStatus(statusId, predicate);
        return this;
    }
    
    private record ConditionalStatus(ushort StatusId, Predicate<SimTether> Condition);

    public SimTether SetAutoFaceTarget(bool value)
    {
        autoFaceTarget = value;
        return this;
    }
}
