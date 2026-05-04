using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using UltiSim.Core.SimObjects;
using GroupPartyMember = FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember;

namespace UltiSim.Core;

// Populates GroupManager.MainGroup with the local player + spawned PartyMembers
// so the game's HudManager copier feeds the _PartyList addon (lit names, HP/MP
// bars, selectable rows). Runs in Framework.Update (pre-game-tick) — the
// HudManager copier reads MainGroup inside the game's own Framework::Tick, so
// MainGroup must be populated before that.
//
// Status display is fed from three places, because the _PartyList addon and
// adjacent UI (minimap, etc.) read from different sources depending on path:
//
// 1. MainGroup.PartyMembers[s].StatusManager — per-slot mirror the game keeps
//    for real party members. WriteSlot copies the spawned member's
//    BC.StatusManager block here every Refresh tick so any code path that
//    reads MainGroup gets correct per-slot data without depending on a
//    per-frame BC resolver. This is the keystone for fixing minimap-flicker
//    and "every slot mirrors one BC's debuffs" symptoms.
// 2. AgentHUD._partyMembers[s].Object/.EntityId — overridden in
//    OnPreRequestedUpdate for any addon path that resolves a per-slot BC via
//    the agent rather than via MainGroup.
// 3. PartyListNumberArray.PartyMembers[s].StatusIconIds — icon ids still need
//    to be injected; without this, the addon falls back to one BC's icons on
//    every slot (removing it caused exactly that regression). Icons inject in
//    raw StatusManager order; with Statuses.Remove compaction the BC's array
//    stays packed at low indices.
//
// Requires each spawned PartyMember to be registered in CharacterManager._battleCharas
// so the addon's EntityId lookup succeeds — see reference_charactermanager_register.md.
// That registration is the binding constraint: the array is shared with all nearby
// BattleCharas in the zone, so this only works in low-density spots.
internal sealed unsafe class PartyHud : IDisposable
{
    private const int MemberStride = 43;
    private const int MembersBaseInt = 7;
    private const int MaxSlots = 8;
    private const string AddonName = "_PartyList";

    // We track which spawned NPC sits at each MainGroup slot so the
    // PreRequestedUpdate hook can re-resolve the BattleChara* and inject status
    // icons. The local player slot stays null here — the game populates slot 0
    // natively and we never write spawn metadata over it.
    private readonly SimNpc?[] slotMembers = new SimNpc?[MaxSlots];

    public PartyHud()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
    }

    public void Refresh(SimParty party)
    {
        Array.Clear(slotMembers);

        // ActiveMembers includes the SimPlayer slot once Game wires it in, so
        // IsAlive becomes effectively-always-true. Use a different signal for
        // "no spawned NPCs to render" — check whether ActiveMembers contains
        // any SimNpc.
        var hasSpawned = false;
        foreach (var m in party.ActiveMembers()) if (m is SimNpc) { hasSpawned = true; break; }
        if (!hasSpawned) return;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;
        var localChara = (BattleChara*)localPlayer.Address;
        if (localChara == null) return;

        var gm = GroupManager.Instance();
        if (gm == null) return;

        ref var grp = ref gm->MainGroup;

        var slot = 0;
        WriteSlot(ref grp.PartyMembers[slot++], localChara);

        foreach (var member in party.ActiveMembers())
        {
            // SimPlayer is the player slot — handled natively in slot 0 above,
            // skip here to avoid duplication.
            if (member is not SimNpc npc) continue;
            if (slot >= MaxSlots) break;
            var bc = npc.BattleCharaPtr;
            if (bc == null) continue;
            WriteSlot(ref grp.PartyMembers[slot], bc);
            slotMembers[slot] = npc;
            slot++;
        }

        grp.MemberCount = (byte)slot;
        grp.PartyLeaderIndex = 0;
        if (grp.PartyId == 0) grp.PartyId = unchecked((long)0xFFFFFFFFu);

        ForceTargetable(slot);
    }

    public void Clear()
    {
        Array.Clear(slotMembers);
        var gm = GroupManager.Instance();
        if (gm == null) return;
        ref var grp = ref gm->MainGroup;
        grp.MemberCount = 0;
        grp.PartyLeaderIndex = 0;
        grp.PartyId = 0;
        grp.PartyId_2 = 0;
    }

    // Belt-and-suspenders on top of the MainGroup.PartyMember.StatusManager
    // mirror in WriteSlot. Three things happen here, all keyed off slotMembers
    // (skip slot 0 — local player; the game fills it natively):
    //
    // 1. AgentHUD._partyMembers[s].Object/.EntityId override — for any addon
    //    path that resolves a per-slot BattleChara* via the agent (instead of
    //    via MainGroup); the HudManager copier's resolver misroutes for our
    //    spawned NPCs and would otherwise pin every slot to a single fallback BC.
    // 2. NumberArray icon injection — without this, the addon falls back to
    //    one BC's icons on every slot. (Removing it caused exactly that
    //    regression.) Iteration is in raw StatusManager order; with
    //    Statuses.Remove compacting and the MainGroup.StatusManager mirror
    //    in place, this aligns 1:1 with our packed slot order.
    // 3. StatusCount per slot.
    private void OnPreRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonRequestedUpdateArgs reqArgs) return;
        var numArrays = (NumberArrayData**)reqArgs.NumberArrayData;
        if (numArrays == null) return;
        var numArr = numArrays[(int)NumberArrayType.PartyList];
        if (numArr == null) return;
        var nums = (PartyListNumberArray*)numArr->IntArray;
        if (nums == null) return;

        var agentHud = AgentHUD.Instance();
        var statusSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Status>();

        for (int s = 1; s < MaxSlots; s++)
        {
            var member = slotMembers[s];
            if (member is null || !member.IsAlive) continue;
            var bc = member.BattleCharaPtr;
            if (bc == null) continue;

            if (agentHud != null)
            {
                ref var hudMember = ref agentHud->PartyMembers[s];
                hudMember.Object = bc;
                hudMember.EntityId = ((GameObject*)bc)->EntityId;
            }

            ref var memberRow = ref nums->PartyMembers[s];
            var statuses = bc->StatusManager.Status;
            int written = 0;
            for (int i = 0; i < statuses.Length && written < 10; i++)
            {
                var sid = statuses[i].StatusId;
                if (sid == 0) continue;
                if (!statusSheet.TryGetRow(sid, out var row)) continue;
                memberRow.StatusIconIds[written] = (int)row.Icon;
                memberRow.StatusIsDispellable[written] = false;
                written++;
            }
            for (int i = written; i < 10; i++)
            {
                memberRow.StatusIconIds[i] = 0;
                memberRow.StatusIsDispellable[i] = false;
            }
            memberRow.StatusCount = written;
        }
    }

    // The HudManager copier sets Targetable=false for any slot it considers
    // out of zone; this override on PartyListNumberArray (the buffer the
    // _PartyList addon actually renders from) keeps names un-greyed.
    private static void ForceTargetable(int memberCount)
    {
        var stage = AtkStage.Instance();
        if (stage == null) return;
        var numArr = stage->GetNumberArrayData(NumberArrayType.PartyList);
        if (numArr == null) return;

        numArr->SetValue(1, 0);
        numArr->SetValue(2, 0);
        numArr->SetValue(3, memberCount > 0 ? 1 : 0);
        numArr->SetValue(4, 0);
        numArr->SetValue(6, memberCount);

        for (int i = 0; i < memberCount && i < MaxSlots; i++)
        {
            var baseIdx = MembersBaseInt + i * MemberStride;
            numArr->SetValue(baseIdx + 42, 1);
        }
    }

    private static void WriteSlot(ref GroupPartyMember slot, BattleChara* bc)
    {
        var obj = (GameObject*)bc;
        slot.Position = obj->Position;
        slot.EntityId = obj->EntityId;
        slot.ContentId = 0xFF00000000000000UL | obj->EntityId;
        slot.AccountId = 0xFF00000000000000UL | obj->EntityId;
        slot.CurrentHP = bc->Health;
        slot.MaxHP = bc->MaxHealth;
        slot.CurrentMP = (ushort)bc->Mana;
        slot.MaxMP = (ushort)bc->MaxMana;
        slot.TerritoryType = (ushort)Plugin.ClientState.TerritoryType;
        slot.HomeWorld = (ushort)bc->HomeWorld;
        slot.ClassJob = bc->ClassJob;
        slot.Level = bc->Level;
        slot.Sex = bc->DrawData.CustomizeData.Sex;
        slot.Flags = 0x05;

        for (int i = 0; i < 64; i++)
        {
            var b = obj->Name[i];
            slot.Name[i] = b;
            if (b == 0) break;
        }

        // Mirror BC.StatusManager into MainGroup.PartyMember.StatusManager. For real
        // party members the game syncs these each frame from the BC the resolver
        // binds; for our spawned NPCs that resolution misroutes (one slot wins per
        // frame, the rest read stale/empty), so the party-list addon ends up
        // showing one BC's debuffs across every slot — and which BC "wins" cycles,
        // matching the minimap-flicker symptom. By copying the StatusManager block
        // ourselves, the addon's MainGroup read returns the right per-slot data
        // regardless of what the resolver does.
        slot.StatusManager = bc->StatusManager;
    }
}
