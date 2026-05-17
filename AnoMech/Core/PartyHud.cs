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
using AnoMech.Core.SimObjects;
using GroupPartyMember = FFXIVClientStructs.FFXIV.Client.Game.Group.PartyMember;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace AnoMech.Core;

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
    // When doppels are inserted into CharacterManager._battleCharas (inn/duty),
    // the addon agent resolves them via LookupBattleCharaByEntityId and produces
    // correct status icons + timer text on its own. The Pre/Post overrides
    // become redundant and risk drifting from the engine-driven output, so we
    // skip them in that case. Snapshotted from any SimPartyMember in the party
    // (all members share the same registration fate).
    private bool partyRegisteredInCharacterManager;
    private bool listenerRegistered;

    // Snapshot of real MainGroup taken on the first Refresh of a sim run, restored
    // verbatim on Clear so leaving a sim doesn't strand the player with stale or
    // zeroed party state. Null when no sim is active.
    private MainGroupSnapshot? realPartySnapshot;
    // Identifying fields of what we wrote into each MainGroup slot last frame.
    // ReconcileEngineWrites uses these to spot slots the engine has changed
    // since (real joins/leaves during sim) and folds those into realPartySnapshot.
    private readonly (uint EntityId, ulong ContentId)[] lastWrittenSlots = new (uint, ulong)[MaxSlots];
    private byte lastWrittenMemberCount;
    private bool hasLastWritten;

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

        // First Refresh of the sim run: capture the engine's real-party state.
        // Subsequent frames: fold any engine-driven slot changes (real joins /
        // leaves that happened between our writes) back into the snapshot so the
        // restore on Clear reflects the live party, not the pre-sim party.
        if (realPartySnapshot == null)
            realPartySnapshot = SnapshotMainGroup(ref grp);
        else
            ReconcileEngineWrites(ref grp);

        ClearSnapshots();
        var registered = false;
        var slot = 1;
        foreach (var member in party.AllMembers())
        {
            var bc = member.BattleCharaPtr;
            if (bc == null) continue;
            var index = member is SimNpc ? slot++ : 0;
            WriteSlot(ref grp.PartyMembers[index], bc);
            // The local player's slot 0 must keep its real ContentId/AccountId so
            // AgentReadyCheck (matches incoming packets by ContentId) and other
            // engine-side party lookups still resolve the local player correctly.
            if (index == 0 && realPartySnapshot is { } snap && snap.Members[0].ContentId != 0)
            {
                grp.PartyMembers[0].ContentId = snap.Members[0].ContentId;
                grp.PartyMembers[0].AccountId = snap.Members[0].AccountId;
            }
            slotSnapshots[index] = new SlotSnapshot(bc);
            if (member is SimPartyMember pm && pm.RegisteredInCharacterManager) registered = true;
        }
        partyRegisteredInCharacterManager = registered;

        grp.MemberCount = (byte)slot;
        grp.PartyLeaderIndex = 0;
        if (grp.PartyId == 0) grp.PartyId = 0xFFFFFFFFu;

        for (int i = 0; i < MaxSlots; i++)
        {
            ref var s = ref grp.PartyMembers[i];
            lastWrittenSlots[i] = (s.EntityId, s.ContentId);
        }
        lastWrittenMemberCount = grp.MemberCount;
        hasLastWritten = true;
    }

    public void Clear()
    {
        ClearSnapshots();
        hasLastWritten = false;

        // No snapshot means we never wrote to MainGroup this session, so the
        // engine's party state is intact — leave it alone. Zeroing here would
        // wipe the real party on plugin disable when no sim ran (or after a
        // prior Clear already restored and nulled the snapshot).
        if (realPartySnapshot is not { } snap) return;

        var gm = GroupManager.Instance();
        if (gm == null) { realPartySnapshot = null; return; }
        ref var grp = ref gm->MainGroup;

        // Restore real-party state captured at sim start (folded with any
        // join/leave deltas detected during the run). Our PreRequestedUpdate
        // / PostRequestedUpdate interceptors early-out once ClearSnapshots
        // ran above, so the addon's natural update path redraws the rows
        // from this restored MainGroup on the next frame.
        RestoreMainGroup(ref grp, snap);
        realPartySnapshot = null;
    }

    private void ClearSnapshots()
    {
        for (int i = 0; i < slotSnapshots.Length; i++) slotSnapshots[i] = default;
        for (int i = 0; i < scratchIconCounts.Length; i++) scratchIconCounts[i] = 0;
        partyRegisteredInCharacterManager = false;
    }

    private void OnPreRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        if (partyRegisteredInCharacterManager) return;
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
        if (partyRegisteredInCharacterManager) return;
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

            var maxStacks = status.MaxStacks;
            if (maxStacks > 0)
            {
                var param = slots[s].Param;
                if (param > 0)
                {
                    var offset = param - 1;
                    if (offset >= maxStacks) offset = maxStacks - 1;
                    iconId += offset;
                }
            }

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

    private struct MainGroupSnapshot
    {
        public GroupPartyMember[] Members;
        public byte MemberCount;
        public uint PartyLeaderIndex;
        public long PartyId;
        public long PartyId_2;
        public byte AllianceFlags;
    }

    private static MainGroupSnapshot SnapshotMainGroup(ref GroupManager.Group grp)
    {
        var snap = new MainGroupSnapshot
        {
            Members = new GroupPartyMember[MaxSlots],
            MemberCount = grp.MemberCount,
            PartyLeaderIndex = grp.PartyLeaderIndex,
            PartyId = grp.PartyId,
            PartyId_2 = grp.PartyId_2,
            AllianceFlags = grp.AllianceFlags,
        };
        for (int i = 0; i < MaxSlots; i++) snap.Members[i] = grp.PartyMembers[i];
        return snap;
    }

    private static void RestoreMainGroup(ref GroupManager.Group grp, MainGroupSnapshot snap)
    {
        for (int i = 0; i < MaxSlots; i++) grp.PartyMembers[i] = snap.Members[i];
        grp.MemberCount = snap.MemberCount;
        grp.PartyLeaderIndex = snap.PartyLeaderIndex;
        grp.PartyId = snap.PartyId;
        grp.PartyId_2 = snap.PartyId_2;
        grp.AllianceFlags = snap.AllianceFlags;
    }

    // Doppel ContentIds are synthesised as 0xFF00000000000000UL | EntityId in
    // WriteSlot; real engine-written ContentIds never hit that top byte, so it
    // works as a sentinel to tell our own writes apart from the engine's.
    private const ulong DoppelContentIdSentinel = 0xFF00000000000000UL;

    private static bool IsDoppelContentId(ulong contentId)
        => (contentId & DoppelContentIdSentinel) == DoppelContentIdSentinel;

    // Detect slots the engine modified between our last write and now. A real
    // ContentId means someone joined (or the engine re-arranged); zero means
    // they left. Fold the change into realPartySnapshot so the eventual restore
    // reflects the live party, not the pre-sim party.
    private void ReconcileEngineWrites(ref GroupManager.Group grp)
    {
        if (!hasLastWritten || realPartySnapshot is not { } snap) return;

        bool dirty = false;
        for (int i = 0; i < MaxSlots; i++)
        {
            ref var current = ref grp.PartyMembers[i];
            var last = lastWrittenSlots[i];
            if (current.EntityId == last.EntityId && current.ContentId == last.ContentId)
                continue;

            if (current.ContentId == 0)
            {
                if (snap.Members[i].ContentId != 0)
                {
                    snap.Members[i] = default;
                    dirty = true;
                }
            }
            else if (!IsDoppelContentId(current.ContentId))
            {
                snap.Members[i] = current;
                dirty = true;
            }
        }

        if (grp.MemberCount != lastWrittenMemberCount)
        {
            byte count = 0;
            for (int i = 0; i < MaxSlots; i++)
                if (snap.Members[i].ContentId != 0) count++;
            snap.MemberCount = count;
            dirty = true;
        }

        if (dirty) realPartySnapshot = snap;
    }
}
