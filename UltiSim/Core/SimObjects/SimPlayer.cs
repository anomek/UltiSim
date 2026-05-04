using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core.SimObjects;

// The real local player as a SimCharacter — lets scenarios apply VFX and pinned
// statuses to the player uniformly with spawned NPCs, and lets SimTether /
// PinnedStatus / TimedStatus take a single SimCharacter argument either side.
//
// Owned by SimWorld as a permanent fixture. Move/MoveTo inherit the no-op
// defaults from SimCharacter — we can't move the real player. Despawn clears
// our overlays (attached VFX, pinned statuses) but doesn't touch the real
// player object.
public sealed unsafe class SimPlayer : SimCharacter
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

    public override uint EntityId
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
}
