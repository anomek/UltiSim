using System;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core;

// Low-level BattleChara allocation primitives shared by SimEnemy.Spawn and
// PartyCreator. Both need to allocate via ClientObjectManager and stamp a
// display name into the GameObject; everything else (appearance, equipment,
// stats, registration) is type-specific and stays with the caller.
internal static unsafe class BattleCharaSpawn
{
    public static bool CreateBattleChara(out uint index, out GameObject* obj)
    {
        index = ClientObjectManager.Instance()->CreateBattleCharacter(0xFFFFFFFF, 0);
        if (index == 0xFFFFFFFF)
        {
            Plugin.Log.Warning($"CreateBattleCharacter returned invalid index {index}");
            obj = null;
            return false;
        }
        obj = (GameObject*)ClientObjectManager.Instance()->GetObjectByIndex((ushort)index);
        if (obj == null)
        {
            Plugin.Log.Warning($"Failed to retrieve spawned object at index {index}");
            return false;
        }

        obj->EntityId = 0xE0000001 + index;
        return true;
    }

    public static void WriteName(GameObject* obj, string name)
    {
        var max = Math.Min(name.Length, 63);
        for (int i = 0; i < max; i++) obj->Name[i] = (byte)name[i];
        obj->Name[max] = 0;
    }

    // Inserts a spawned BC into the first free CharacterManager._battleCharas
    // slot. Caller decides when this is safe — see PartyCreator's inn/duty gate.
    // No-op if already registered or if no free slot exists.
    public static void RegisterInCharacterManager(BattleChara* chara)
    {
        var cm = CharacterManager.Instance();
        if (cm == null || chara == null) return;
        var arr = cm->BattleCharas;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == chara) return;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == null) { arr[i] = chara; return; }
    }

    public static void UnregisterFromCharacterManager(BattleChara* chara)
    {
        var cm = CharacterManager.Instance();
        if (cm == null || chara == null) return;
        var arr = cm->BattleCharas;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == chara) { arr[i] = (BattleChara*)null; return; }
    }
}
