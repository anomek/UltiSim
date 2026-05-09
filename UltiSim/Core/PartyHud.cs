using System;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel;
using UltiSim.Core.SimObjects;
using GroupPartyMember = FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace UltiSim.Core;

// Drives the in-game _PartyList addon for spawned doppels. Three paths:
//   1. MainGroup.PartyMembers writes (per-frame Refresh) — required because
//      the addon only enters its render-members path when MemberCount > 0.
//      Position, name, base HP/MP, class-job all flow through here.
//   2. PartyListNumberArray writes during PreRequestedUpdate — overrides the
//      values the agent computes from a (null) resolved BC. Drives Targetable
//      (no greying), HP/MP/level/class icon, and per-slot status icon ids.
//   3. AtkComponentIconText text-node writes during PostRequestedUpdate — the
//      addon's own update path computes timer text from
//      AgentHUD._partyMembers[i].Object->StatusManager. For our doppels Object
//      is null and the timer text comes out blank, so we stamp the formatted
//      countdown directly into the icon's first text-node child after the
//      addon has run. Visibility flag is toggled on too — the addon may
//      hide the text node when it has nothing to show.
internal sealed unsafe class PartyHud : IDisposable
{
    private const int MaxSlots = 8;
    private const string AddonName = "_PartyList";

    private const int HeaderInts = 7;
    private const int MemberInts = 43;

    // Field offsets within PartyListMemberNumberArray (see FFXIVClientStructs
    // PartyListNumberArray.cs). All values are int-indexed.
    private const int FieldLevel = 3;
    private const int FieldClassIconId = 4;
    private const int FieldCurrentHealth = 7;
    private const int FieldMaxHealth = 8;
    private const int FieldCurrentMana = 10;
    private const int FieldMaxMana = 11;
    private const int FieldStatusCount = 17;
    private const int FieldStatusIconIdsBase = 18;
    private const int FieldStatusIsDispellableBase = 28;
    private const int FieldTargetable = 42;
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

        for (int i = 0; i < MaxSlots; i++)
        {
            var snap = slotSnapshots[i];
            if (snap.Bc == null) { scratchIconCounts[i] = 0; continue; }
            var bc = snap.Bc;
            var memberBase = HeaderInts + i * MemberInts;

            numArr->SetValue(memberBase + FieldTargetable, 1);
            numArr->SetValue(memberBase + FieldLevel, bc->Level);
            numArr->SetValue(memberBase + FieldClassIconId, ClassIconId(bc->ClassJob));
            numArr->SetValue(memberBase + FieldCurrentHealth, (int)bc->Health);
            numArr->SetValue(memberBase + FieldMaxHealth, (int)bc->MaxHealth);
            numArr->SetValue(memberBase + FieldCurrentMana, (int)bc->Mana);
            numArr->SetValue(memberBase + FieldMaxMana, (int)bc->MaxMana);

            scratchIconCounts[i] = WriteStatuses(numArr, memberBase, bc, i);
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

        for (int i = 0; i < MaxSlots; i++)
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
    // As a side-effect, fills scratchTimers[slot, ...] with per-icon
    // RemainingTime so PostRequestedUpdate can stamp the duration text.
    private int WriteStatuses(NumberArrayData* numArr, int memberBase, BattleChara* bc, int slot)
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
            numArr->SetValue(memberBase + FieldStatusIconIdsBase + written, iconId);
            numArr->SetValue(memberBase + FieldStatusIsDispellableBase + written, status.CanDispel ? 1 : 0);
            scratchTimers[slot, written] = slots[s].RemainingTime;
            written++;
        }
        for (int extra = written; extra < StatusIconCount; extra++)
        {
            numArr->SetValue(memberBase + FieldStatusIconIdsBase + extra, 0);
            numArr->SetValue(memberBase + FieldStatusIsDispellableBase + extra, 0);
        }
        numArr->SetValue(memberBase + FieldStatusCount, written);
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
        slot.Flags = 0x05;
        // Damage-shield overlay (small bar over the HP bar). We never simulate
        // shields, so explicitly zero — leaving it untouched lets stale or
        // uninitialized values render a phantom shield strip on every member.
        slot.DamageShield = 0;

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
