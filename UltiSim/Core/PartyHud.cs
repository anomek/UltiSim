using System;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using UltiSim.Core.SimObjects;
using GroupPartyMember = FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace UltiSim.Core;

// Drives the in-game _PartyList addon for spawned doppels. Two paths:
//   1. MainGroup.PartyMembers writes (per-frame Refresh) — required because
//      the addon only enters its render-members path when MemberCount > 0.
//      Position, name, base HP/MP, class-job all flow through here.
//   2. Addon-array writes during PreRequestedUpdate / PostRequestedUpdate.
//      Pre stamps PartyListNumberArray (Targetable / HP / MP / level / class
//      icon / per-slot status icon ids) — overrides what the agent computes
//      from a (null) resolved BC. Post stamps the duration countdown text
//      directly into each AtkComponentIconText's first AtkTextNode child,
//      because the addon's own update path produces blank text when the agent
//      can't resolve our doppel.
internal sealed unsafe class PartyHud : IDisposable
{
    private const int MaxSlots = 8;
    private const string AddonName = "_PartyList";
    private const int StatusIconCount = 10;

    private readonly SlotSnapshot[] slotSnapshots = new SlotSnapshot[MaxSlots];
    private readonly ExcelSheet<LuminaStatus> statusSheet = Plugin.DataManager.GetExcelSheet<LuminaStatus>();
    // Per-slot per-icon remaining-time captured during PreRequestedUpdate, read
    // by PostRequestedUpdate to format timer text. Two-phase because by the
    // time Post fires the BC pointer might race against Despawn — but the
    // primitive copy survives.
    private readonly float[,] scratchTimers = new float[MaxSlots, StatusIconCount];
    private readonly int[] scratchIconCounts = new int[MaxSlots];
    private bool listenerRegistered;

    public PartyHud()
    {
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostRequestedUpdate, AddonName, OnPostRequestedUpdate);
        listenerRegistered = true;
    }

    public void Dispose()
    {
        if (!listenerRegistered) return;
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, AddonName, OnPreRequestedUpdate);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostRequestedUpdate, AddonName, OnPostRequestedUpdate);
        listenerRegistered = false;
    }

    public void Refresh(SimParty party)
    {
        if (party.AllMembers().Count() < 2) { ClearSnapshots(); return; }

        var gm = GroupManager.Instance();
        if (gm == null) return;
        ref var grp = ref gm->MainGroup;

        ClearSnapshots();
        var slot = 1;
        foreach (var member in party.AllMembers())
        {
            var bc = member.BattleCharaPtr;
            if (bc == null) continue;
            var index = member is SimNpc ? slot++ : 0;
            WriteSlot(ref grp.PartyMembers[index], bc);
            slotSnapshots[index] = new SlotSnapshot(bc);
        }

        grp.MemberCount = (byte)slot;
        grp.PartyLeaderIndex = 0;
        if (grp.PartyId == 0) grp.PartyId = 0xFFFFFFFFu;
    }

    public void Clear()
    {
        ClearSnapshots();
        var gm = GroupManager.Instance();
        if (gm == null) return;
        ref var grp = ref gm->MainGroup;
        grp.MemberCount = 0;
        grp.PartyLeaderIndex = 0;
        grp.PartyId = 0;
        grp.PartyId_2 = 0;
    }

    private void ClearSnapshots()
    {
        for (int i = 0; i < slotSnapshots.Length; i++) slotSnapshots[i] = default;
        for (int i = 0; i < scratchIconCounts.Length; i++) scratchIconCounts[i] = 0;
    }

    private void OnPreRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        var anyTracked = false;
        for (int i = 0; i < slotSnapshots.Length; i++)
            if (slotSnapshots[i].Bc != null) { anyTracked = true; break; }
        if (!anyTracked) return;

        if (args is not AddonRequestedUpdateArgs reqArgs) return;
        var numArrays = (NumberArrayData**)reqArgs.NumberArrayData;
        if (numArrays == null) return;
        var numArr = numArrays[(int)NumberArrayType.PartyList];
        if (numArr == null) return;
        var partyArr = (PartyListNumberArray*)numArr->IntArray;
        if (partyArr == null) return;

        for (int i = 1; i < MaxSlots; i++)
        {
            var snap = slotSnapshots[i];
            if (snap.Bc == null) { scratchIconCounts[i] = 0; continue; }
            var bc = snap.Bc;

            ref var member = ref partyArr->PartyMembers[i];
            member.Targetable = true;
            scratchIconCounts[i] = WriteStatuses(ref member, bc, i);
        }
    }

    // PostRequestedUpdate fires after the addon's OnRequestedUpdate has run,
    // so any text the addon wrote into the icon components has already landed.
    // We then overwrite the duration text from our captured RemainingTime so
    // doppel rows show counting-down timers instead of blank text.
    private void OnPostRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        var anyTracked = false;
        for (int i = 0; i < scratchIconCounts.Length; i++)
            if (scratchIconCounts[i] > 0) { anyTracked = true; break; }
        if (!anyTracked) return;

        if (args is not AddonRequestedUpdateArgs reqArgs) return;
        var addon = (AddonPartyList*)reqArgs.Addon.Address;
        if (addon == null) return;

        for (int i = 1; i < MaxSlots; i++)
        {
            var iconCount = scratchIconCounts[i];
            if (iconCount <= 0) continue;
            ref var member = ref addon->PartyMembers[i];
            for (int s = 0; s < StatusIconCount; s++)
            {
                var iconComp = member.StatusIcons[s].Value;
                if (iconComp == null) continue;
                var textNode = FindFirstTextNode(&iconComp->AtkComponentBase);
                if (textNode == null) continue;
                if (s >= iconCount) continue;

                var label = FormatTimer(scratchTimers[i, s]);
                textNode->NodeFlags |= NodeFlags.Visible;
                textNode->SetText(label);
            }
        }
    }

    // Pack the BC's StatusManager into the addon's per-slot icon array. Statuses
    // is already kept compact at low indices by Statuses.Apply / Statuses.Remove,
    // so a straight prefix-pack matches what's actually live on the entity.
    // Side effect: fills scratchTimers[slot, ...] with each icon's RemainingTime
    // so PostRequestedUpdate can stamp the duration text.
    private int WriteStatuses(ref PartyListNumberArray.PartyListMemberNumberArray member, BattleChara* bc, int slot)
    {
        var slots = bc->StatusManager.Status;
        var written = 0;
        for (int s = 0; s < slots.Length && written < StatusIconCount; s++)
        {
            var statusId = slots[s].StatusId;
            if (statusId == 0) continue;
            if (!statusSheet.TryGetRow(statusId, out var status)) continue;
            var iconId = (int)status.Icon;
            if (iconId == 0) continue;
            member.StatusIconIds[written] = iconId;
            member.StatusIsDispellable[written] = status.CanDispel;
            scratchTimers[slot, written] = status.IsPermanent ? -1 : slots[s].RemainingTime;
            written++;
        }
        for (int extra = written; extra < StatusIconCount; extra++)
        {
            member.StatusIconIds[extra] = 0;
            member.StatusIsDispellable[extra] = false;
        }
        member.StatusCount = written;
        return written;
    }

    private static AtkTextNode* FindFirstTextNode(AtkComponentBase* component)
    {
        if (component == null) return null;
        var nodes = component->UldManager.Nodes;
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i].Value;
            if (node == null) continue;
            if (node->Type == NodeType.Text) return node->GetAsAtkTextNode();
        }
        return null;
    }

    private static string FormatTimer(float seconds)
    {
        if (seconds <= 0) return string.Empty;
        var rounded = (int)MathF.Ceiling(seconds);
        return rounded >= 60 ? $"{rounded / 60}m" : rounded.ToString();
    }

    private static int ClassIconId(byte classJob)
    {
        // Party-list class icons are sheet rows 62100..62142 (62100 + ClassJob).
        return classJob == 0 ? 0 : 62100 + classJob;
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
        slot.HomeWorld = bc->HomeWorld;
        slot.ClassJob = bc->ClassJob;
        slot.Level = bc->Level;
        slot.Sex = bc->DrawData.CustomizeData.Sex;
        slot.Flags = 0x5;
        slot.DamageShield = 0;
        slot.StatusManager = bc->StatusManager;

        for (int i = 0; i < 64; i++)
        {
            var b = obj->Name[i];
            slot.Name[i] = b;
            if (b == 0) break;
        }
    }

    private readonly struct SlotSnapshot
    {
        public readonly BattleChara* Bc;
        public SlotSnapshot(BattleChara* bc) { Bc = bc; }
    }
}
