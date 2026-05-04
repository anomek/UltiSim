using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace UltiSim.Core.SimObjects;

public sealed unsafe class SimPartyMember : SimNpc
{
    public PartyRole Role { get; }
    public byte ClassJob { get; }
    public string DisplayName { get; }

    internal SimPartyMember(uint index, PartyRole role, byte classJob, string name) : base(index)
    {
        Role = role;
        ClassJob = classJob;
        DisplayName = name;
    }

    public override void Despawn()
    {
        var bc = BattleCharaPtr;
        if (bc != null) UnregisterFromCharacterManager(bc);
        base.Despawn();
    }

    internal static void RegisterInCharacterManager(BattleChara* chara)
    {
        var cm = CharacterManager.Instance();
        if (cm == null || chara == null) return;
        var arr = cm->BattleCharas;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == chara) return;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == null) { arr[i] = chara; return; }
    }

    internal static void UnregisterFromCharacterManager(BattleChara* chara)
    {
        var cm = CharacterManager.Instance();
        if (cm == null || chara == null) return;
        var arr = cm->BattleCharas;
        for (int i = 0; i < arr.Length; i++)
            if (arr[i].Value == chara) { arr[i] = (BattleChara*)null; return; }
    }
}
