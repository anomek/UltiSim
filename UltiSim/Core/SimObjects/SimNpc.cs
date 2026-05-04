using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// SimCharacter backed by a BattleChara we allocated via ClientObjectManager.
// Identified by its CO index. Overlay state (VFX, statuses) lives on the base;
// this layer adds Index-based pointer lookup, movement, and the "free the
// handle on Despawn" lifecycle.
public unsafe class SimNpc : SimCharacter
{
    public const uint InvalidIndex = 0xFFFFFFFF;

    public uint Index { get; private set; }
    private bool pendingDraw;
    private Vector3? moveTarget;
    private float moveSpeed;
    private float? moveFinalRotation;
    private ushort moveTimelineId;
    private bool moveAnimActive;

    // ActionTimeline rows: 22 = normal/run (looping). PlayerCharacter rigs in normal
    // stance use this for the run loop; setting Timeline.BaseOverride forces the
    // base animation slot to play it while we drive position via SetPosition.
    public const ushort DefaultRunTimelineId = 22;

    protected SimNpc(uint index)
    {
        Index = index;
        pendingDraw = index != InvalidIndex;
    }

    public override bool IsAlive => Index != InvalidIndex && GetGameObject() != null;

    public override uint EntityId
    {
        get
        {
            var obj = GetGameObject();
            return obj == null ? 0u : obj->EntityId;
        }
    }

    public override GameObjectId GameObjectId
    {
        get
        {
            var obj = GetGameObject();
            return obj == null ? default : obj->GetGameObjectId();
        }
    }

    public override Vector3 Position
    {
        get
        {
            var obj = GetGameObject();
            return obj == null ? default : obj->Position;
        }
    }

    public override float Rotation
    {
        get
        {
            var obj = GetGameObject();
            return obj == null ? 0f : obj->Rotation;
        }
    }

    public override float HitboxRadius
    {
        get
        {
            var chara = GetBattleChara();
            return chara == null ? 0f : chara->HitboxRadius;
        }
    }

    public override void Move(Vector3 position)
    {
        var obj = GetGameObject();
        if (obj == null) return;
        // Direct field writes only update GameObject — the attached DrawObject
        // keeps its own transform, so the visible model stays put while only the
        // hitbox/nameplate move. SetPosition propagates to the DrawObject too.
        obj->SetPosition(position.X, position.Y, position.Z);
    }

    public override void Move(Vector3 position, float rotation)
    {
        var obj = GetGameObject();
        if (obj == null) return;
        obj->SetPosition(position.X, position.Y, position.Z);
        // Direct field write at +0xC0 isn't enough for animated NPCs — the game
        // re-derives the visible facing each frame from internal state. The
        // virtual SetRotation(float) propagates the change properly.
        obj->SetRotation(MathUtil.NormalizeRotation(rotation));
    }

    // Sets a destination the Tick loop walks toward at `speed` units/sec, facing
    // the direction of travel. Plays `timelineId` (default normal/run = 22) on the
    // base animation slot for the duration of the motion so the model actually
    // animates instead of sliding. When `finalRotation` is set, the entity snaps
    // to that facing on arrival instead of holding the last direction-of-travel
    // angle (used when callers need an explicit pose at the destination).
    // Cleared on arrival or when MoveTo is called again. Cancels with StopMoving().
    public override void MoveTo(Vector3 target, float speed = 6f, float? finalRotation = null, ushort timelineId = DefaultRunTimelineId)
    {
        moveTarget = target;
        moveSpeed = MathF.Max(0f, speed);
        moveFinalRotation = finalRotation;
        moveTimelineId = timelineId;
        StartMoveAnim();
    }

    public override void StopMoving()
    {
        moveTarget = null;
        StopMoveAnim();
    }

    private void StartMoveAnim()
    {
        var chara = GetBattleChara();
        if (chara == null) return;
        chara->Timeline.BaseOverride = moveTimelineId;
        chara->Timeline.TimelineSequencer.PlayTimeline(moveTimelineId);
        moveAnimActive = true;
    }

    private void StopMoveAnim()
    {
        if (!moveAnimActive) return;
        var chara = GetBattleChara();
        if (chara != null) chara->Timeline.BaseOverride = 0;
        moveAnimActive = false;
    }

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);

        if (pendingDraw)
        {
            var obj = GetGameObject();
            if (obj == null) { pendingDraw = false; }
            else if (obj->IsReadyToDraw())
            {
                obj->EnableDraw();
                pendingDraw = false;
            }
        }

        if (moveTarget is { } target)
        {
            var current = Position;
            var dx = target.X - current.X;
            var dz = target.Z - current.Z;
            var distSq = dx * dx + dz * dz;
            var step = moveSpeed * deltaSeconds;
            if (distSq <= step * step || step <= 0f)
            {
                var rot = moveFinalRotation
                          ?? (distSq > 1e-6f ? MathF.Atan2(dx, dz) : Rotation);
                Move(target, rot);
                moveTarget = null;
                moveFinalRotation = null;
                StopMoveAnim();
            }
            else
            {
                var dist = MathF.Sqrt(distSq);
                var nx = current.X + dx / dist * step;
                var nz = current.Z + dz / dist * step;
                Move(new Vector3(nx, current.Y, nz), MathF.Atan2(dx, dz));
            }
        }
    }

    public override void Despawn()
    {
        if (Index == InvalidIndex) return;
        base.Despawn(); // clears VFX + pinned statuses
        var obj = GetGameObject();
        if (obj != null)
        {
            obj->DisableDraw();
            ClientObjectManager.Instance()->DeleteObjectByIndex((ushort)Index, 0);
        }
        Index = InvalidIndex;
        pendingDraw = false;
    }

    protected GameObject* GetGameObject()
        => Index == InvalidIndex ? null : (GameObject*)ClientObjectManager.Instance()->GetObjectByIndex((ushort)Index);

    protected BattleChara* GetBattleChara() => (BattleChara*)GetGameObject();

    internal override BattleChara* BattleCharaPtr => GetBattleChara();
}
