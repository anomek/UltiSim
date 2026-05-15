using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// The real local player as a SimCharacter — lets scenarios apply VFX and
// statuses to the player uniformly with spawned NPCs, and lets SimTether /
// SimStatus take a single SimCharacter argument either side.
//
// Owned by SimWorld as a permanent fixture. Move/MoveTo inherit the no-op
// defaults from SimCharacter — we can't move the real player. Despawn clears
// our overlays (attached VFX, pinned statuses) but doesn't touch the real
// player object.
public sealed unsafe class SimPlayer : SimPartySlot
{
    private static GameObject* RawObject => (GameObject*)(Plugin.ObjectTable.LocalPlayer?.Address ?? 0);

    internal override BattleChara* BattleCharaPtr => (BattleChara*)RawObject;

    public override GameObjectId GameObjectId
    {
        get
        {
            var obj = RawObject;
            return obj == null ? default : obj->GetGameObjectId();
        }
    }

    public uint EntityId
    {
        get
        {
            var obj = RawObject;
            return obj == null ? 0u : obj->EntityId;
        }
    }

    public override float HitboxRadius
    {
        get
        {
            var chara = BattleCharaPtr;
            return chara == null ? 0f : chara->HitboxRadius;
        }
    }

    // "Down for the Count" (896) — IsPermanent + LockControl variant.
    private const ushort StunStatusId = 896;

    private float deadElapsed;

    private Vector3? slideTarget;
    private float slideSpeed;
    // True while the slide is what enabled ZeroMovement, so we only clear the
    // flag on slide-end when we were the one who flipped it on — avoids
    // stomping OnKilled / external callers that also use the same flag.
    private bool slideOwnsZeroMovement;

    // Writing directly to the local player's GameObject is only safe while the
    // simulator owns the zone (our client-side fake instance); doing it in a
    // real duty would desync against the server. Outside the fake instance
    // this is a no-op.
    //
    // Tick-driven slide rather than snap: stash target + speed and advance the
    // player's position toward the target each Tick. ZeroMovement is enabled
    // for the duration so RMI walk input doesn't fight the slide; cleared on
    // arrival when the slide is what enabled it.
    public override void Knockback(Vector3 source, float distance, float speed)
    {
        if (!Plugin.GameInstance.World.Map.IsInInstance) return;
        var current = Position;
        var dx = current.X - source.X;
        var dz = current.Z - source.Z;
        var distSq = dx * dx + dz * dz;
        if (distSq < 1e-6f) return;
        var scale = distance / MathF.Sqrt(distSq);
        slideTarget = new Vector3(
            current.X + dx * scale,
            current.Y,
            current.Z + dz * scale);
        slideSpeed = MathF.Max(0f, speed);
        var hooks = Plugin.GameInstance?.PlayerInputHooks;
        if (hooks != null && !hooks.ZeroMovement)
        {
            hooks.ZeroMovement = true;
            slideOwnsZeroMovement = true;
        }
    }

    private void AdvanceSlide(float deltaSeconds)
    {
        if (slideTarget is not { } target) return;
        var obj = RawObject;
        if (obj == null) { ClearSlide(); return; }

        var current = Position;
        var dx = target.X - current.X;
        var dz = target.Z - current.Z;
        var distSq = dx * dx + dz * dz;
        var step = slideSpeed * deltaSeconds;
        Vector3 nextLocal;
        if (distSq <= step * step || step <= 0f)
        {
            nextLocal = new Vector3(target.X, current.Y, target.Z);
            ClearSlide();
        }
        else
        {
            var dist = MathF.Sqrt(distSq);
            nextLocal = new Vector3(
                current.X + dx / dist * step,
                current.Y,
                current.Z + dz / dist * step);
        }
        Position = nextLocal;
        var world = Plugin.GameInstance.World.ToWorld(nextLocal);
        obj->SetPosition(world.X, world.Y, world.Z);
        if (obj->DrawObject != null) obj->DrawObject->Object.Position = world;
    }

    private void ClearSlide()
    {
        slideTarget = null;
        if (!slideOwnsZeroMovement) return;
        var hooks = Plugin.GameInstance?.PlayerInputHooks;
        if (hooks != null) hooks.ZeroMovement = false;
        slideOwnsZeroMovement = false;
    }

    internal override void OnKilled()
    {
        // KO takes over ZeroMovement; drop any in-flight slide so AdvanceSlide
        // doesn't clear the flag we're about to rely on.
        slideTarget = null;
        slideOwnsZeroMovement = false;

        var hooks = Plugin.GameInstance?.PlayerInputHooks;
        if (hooks != null)
        {
            hooks.DisableAllActions = true;
            hooks.ZeroMovement = true;
        }
        AddStatus(StunStatusId, 0);
        // Visual KO pose only — leave HP alone (the server owns it). Input
        // lockout above is what actually keeps the player still long enough
        // for the prone pose to read.
        KoAnimation.Play(BattleCharaPtr);
        deadElapsed = 0f;
    }

    // Re-stamp BaseOverride after the intro fall so the engine can't pop the
    // player back to standing. Same delayed-pin pattern as SimPartyMember.
    public override void Tick(float deltaSeconds)
    {
        // Re-sync stored Position/Rotation from the real player's GameObject.
        // The engine moves the local player every frame in response to input;
        // without this re-sync, anything reading SimPlayer.Position would see
        // stale data from the last mutator call. SimPlayer doesn't hold a
        // SimWorld reference (chicken-and-egg at construction), but
        // GameInstance.World is live by the time Tick runs.
        var obj = RawObject;
        if (obj != null)
        {
            Position = Plugin.GameInstance.World.ToLocal(obj->Position);
            Rotation = obj->Rotation;
        }

        base.Tick(deltaSeconds);
        AdvanceSlide(deltaSeconds);
        if (!Dead) return;
        deadElapsed += deltaSeconds;
        if (deadElapsed >= KoAnimation.IntroDurationSeconds) KoAnimation.PinLoop(BattleCharaPtr);
    }

    // SimPlayer is a permanent singleton on SimWorld — unlike SimPartyMember
    // (which is destroyed on reset), the same instance persists across runs.
    // So Despawn must actively unwind the dead state we wrote: play the
    // revive animation (clearing BaseOverride alone leaves the prone loop
    // playing), reset Dead, and zero the elapsed counter so the next kill
    // replays the intro from the start. Input-hook flags are cleared
    // separately by Game.ResetInternal.
    public override void Despawn()
    {
        base.Despawn();
        // Drop slide state so a reset mid-knockback doesn't leave a stale
        // target driving the player on the next run. ZeroMovement is reset by
        // Game.ResetInternal, so don't touch it here.
        slideTarget = null;
        slideOwnsZeroMovement = false;
        if (!Dead) return;
        KoAnimation.Revive(BattleCharaPtr);
        Dead = false;
        deadElapsed = 0f;
    }
}
