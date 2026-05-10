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

    public override Vector3 Position
    {
        get
        {
            var obj = RawObject;
            return obj == null ? default : obj->Position;
        }
    }

    public override float Rotation
    {
        get
        {
            var obj = RawObject;
            return obj == null ? 0f : obj->Rotation;
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

    internal override void OnKilled()
    {
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
        base.Tick(deltaSeconds);
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
        if (!Dead) return;
        KoAnimation.Revive(BattleCharaPtr);
        Dead = false;
        deadElapsed = 0f;
    }
}
