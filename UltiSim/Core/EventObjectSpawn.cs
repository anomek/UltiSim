using System;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace UltiSim.Core;

// Low-level EventObject allocation primitives, mirror of BattleCharaSpawn for
// the EventObjectManager 40-slot pool (GameObjectManager indices 449-488).
//
// EventObjectManager exposes CreateGatheringPointObject / CreateEventObject /
// CreateAreaObject / CreateHousing* publicly, but ClientStructs only binds the
// manager fields — none of the Create* functions are bound. We sig-scan
// CreateEventObject from its prologue (auto-find slot, validates EObj sheet
// row, allocates pool memory, constructs GameObject in place, calls the heavy
// SharedGroup wire-up FUN_14174dac0 internally). The normal caller is
// HandleSpawnObjectPacket, which adds position/rotation/event-state/fate-id
// writes on top; we skip the packet handler and replicate only the post-create
// writes our scenarios need (position, rotation, visibility).
//
// Despawn uses vfunc 0x1E0 — the per-slot teardown function. CreateEventObject
// itself calls this when it reuses an already-occupied slot, so we know the
// engine considers it safe in-place destruction. After firing it we null the
// _EventObjects[slot] pointer to release the slot for reuse.
internal static unsafe class EventObjectSpawn
{
    private delegate int CreateEventObjectDelegate(
        EventObjectManager* self,
        uint param2,
        uint eObjRowId,
        ushort param4,
        uint param5,
        uint param6,
        int slotHint,
        byte flag);

    // State-change driver. Writes `actor[0x1B2] = state` and notifies the
    // attached SharedGroupLayoutInstance (actor[0x108]) so its sub-instances
    // can switch visible content according to the new state. Used by the
    // packet handler with packet[0x2c] (the TimelineId / state field).
    // If actor[0x108] is null (SG context not attached yet), the function
    // only writes the state field and skips the notify — engine populates
    // actor[0x108] asynchronously after the SG resource loads, so a later
    // call will propagate.
    private delegate void SetEventObjectStateDelegate(
        GameObject* actor,
        short state,
        byte immediate,
        ulong extra);

    private static CreateEventObjectDelegate? createEventObject;
    private static SetEventObjectStateDelegate? setEventObjectState;

    private static CreateEventObjectDelegate? Resolve()
    {
        if (createEventObject != null) return createEventObject;
        try
        {
            // Prologue sig for Client::Game::Object::EventObjectManager::CreateEventObject.
            var addr = Plugin.SigScanner.ScanText(
                "48 89 5C 24 ?? 48 89 6C 24 ?? 57 41 54 41 57 48 83 EC ?? 8B 9C 24");
            createEventObject = System.Runtime.InteropServices.Marshal
                .GetDelegateForFunctionPointer<CreateEventObjectDelegate>(addr);
            Plugin.Log.Info($"EventObjectSpawn: resolved CreateEventObject at 0x{addr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"EventObjectSpawn: failed to resolve CreateEventObject sig — {ex.Message}");
        }
        return createEventObject;
    }

    private static SetEventObjectStateDelegate? ResolveSetState()
    {
        if (setEventObjectState != null) return setEventObjectState;
        try
        {
            // Call-site sig for FUN_14174e000 — the state-change driver that
            // HandleSpawnObjectPacket fires with packet[0x2c] right after
            // CreateEventObject. Dalamud's ScanText auto-follows the E8 rel32
            // and returns the call target, so we hand it straight to Marshal.
            var addr = Plugin.SigScanner.ScanText(
                "E8 ?? ?? ?? ?? 49 8B 06 44 0F B6 C5");
            setEventObjectState = System.Runtime.InteropServices.Marshal
                .GetDelegateForFunctionPointer<SetEventObjectStateDelegate>(addr);
            Plugin.Log.Info($"EventObjectSpawn: resolved SetEventObjectState at 0x{addr.ToInt64():X}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"EventObjectSpawn: failed to resolve SetEventObjectState sig — {ex.Message}");
        }
        return setEventObjectState;
    }

    // Writes the EObj state field (actor[0x1B2]) and notifies the attached
    // SharedGroupLayoutInstance to switch sub-instances. Different SGs use
    // different state values to gate which sub-instances are visible —
    // simulator code finds the right value empirically per EObj row.
    public static void SetState(GameObject* obj, short state)
    {
        if (obj == null) return;
        var fn = ResolveSetState();
        if (fn == null) return;
        fn(obj, state, 1, 0);
    }

    // Allocates a new EventObject slot bound to the given EObj sheet row.
    // Returns the manager slot index (0..39) on success, -1 on failure. The
    // engine sets up the SharedGroup model internally via FUN_14174dac0; the
    // caller is responsible for SetPosition/SetRotation/SetVisible afterwards.
    public static bool Create(uint eObjRowId, out int slot, out GameObject* obj)
    {
        slot = -1;
        obj = null;
        var fn = Resolve();
        if (fn == null) return false;

        var mgr = EventObjectManager.Instance();
        if (mgr == null) { Plugin.Log.Warning("EventObjectSpawn: EventObjectManager.Instance() == null"); return false; }

        // All other params 0: we want the engine to auto-resolve the SharedGroup
        // from EObj.SgbPath. Inside CreateEventObject, FUN_14174dac0 contains:
        //
        //   if (param_5 == 0 && (actor[0x9c] & 4) == 0) {
        //       actor->ExportedSGRowPtr = ExdModule.GetExportedSGRow(eobjRow.SgbPath);
        //   }
        //
        // param_5 = 0 ✓. The bit-2 check on actor[0x9c] depends on our final
        // `flag` arg: CreateEventObject writes `actor[0x9c] |= flag << 2`. flag=1
        // sets bit 2, which causes the engine to skip the auto-lookup (the packet
        // path needs the layer-side resolution since the server passes an explicit
        // param_5). For simulator-spawned EObjs we want the EObj-sheet lookup, so
        // flag must be 0 — confirmed empirically by reading actor->ExportedSGRowPtr
        // after spawn.
        slot = fn(mgr, 0, eObjRowId, 0, 0, 0, -1, 0);
        if (slot < 0)
        {
            Plugin.Log.Warning($"EventObjectSpawn: CreateEventObject returned -1 for EObj row {eObjRowId} (0x{eObjRowId:X}) — pool full or invalid row");
            return false;
        }

        // CreateEventObject writes the new actor pointer into _EventObjects[slot]
        // itself (matches the `param_1[slot + 2] = puVar3` line in the decompile).
        // No GetObjectByIndex binding needed — index straight in.
        obj = mgr->EventObjects[slot].Value;
        if (obj == null)
        {
            Plugin.Log.Warning($"EventObjectSpawn: slot {slot} resolved but EventObjects[{slot}] is null");
            return false;
        }
        return true;
    }

    // Per-slot teardown. CreateEventObject itself fires the vfunc at vtable+0x1E0
    // when reusing an occupied slot — that's GameObject.Terminate (VirtualFunction(60))
    // as bound by ClientStructs. We invoke the same canonical path, then null the
    // manager's _EventObjects[slot] pointer to release the slot for reuse.
    public static void Destroy(int slot)
    {
        if (slot < 0 || slot >= 40) return;
        var mgr = EventObjectManager.Instance();
        if (mgr == null) return;
        var arr = mgr->EventObjects;
        var entry = arr[slot].Value;
        if (entry == null) return;
        entry->Terminate();
        arr[slot] = (GameObject*)null;
    }
}
