using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace UltiSim.Core.SimObjects;

// Common base for anything in the simulated world that has a BattleChara behind
// it — spawned NPCs (SimNpc) and the real local player (SimPlayer). Owns the
// shared overlay state (attached VFX, pinned statuses) and Move/MoveTo as
// virtual no-ops so callers can treat all characters uniformly. Subclasses
// expose the pointer + identity via the abstract properties below; everything
// else is concrete here.
public abstract unsafe class SimCharacter : ISimObject, IPositioned
{
    private readonly Dictionary<string, IntPtr> attachedVfx = new();
    private readonly Dictionary<string, float> attachedVfxExpiry = new();
    private readonly Dictionary<ushort, PinnedStatus> pinnedStatuses = new();

    internal abstract BattleChara* BattleCharaPtr { get; }
    public abstract GameObjectId GameObjectId { get; }
    public abstract uint EntityId { get; }
    public abstract Vector3 Position { get; }
    public abstract float Rotation { get; }
    public abstract float HitboxRadius { get; }

    // Default IsAlive: a character is alive if its BattleChara handle resolves.
    // SimNpc overrides to also check Index validity; SimPlayer can rely on this.
    public virtual bool IsAlive => BattleCharaPtr != null;

    // Movement is a no-op by default — only SimNpc has writable position because
    // the real local player moves themselves. Scenarios call .MoveTo on a
    // uniform SimCharacter without checking the concrete type; calls that land
    // on SimPlayer are silently dropped. Override in SimNpc.
    public virtual void Move(Vector3 position) { }
    public virtual void Move(Vector3 position, float rotation) { }
    public virtual void MoveTo(Vector3 target, float speed = 6f, float? finalRotation = null, ushort timelineId = SimNpc.DefaultRunTimelineId) { }
    public virtual void StopMoving() { }

    // Self-attached actor VFX keyed by path. Path is validated via FileExists before
    // spawning to avoid the file-thread crash StaticVfxCreate has on bad paths.
    // Re-adding the same path is a no-op so callers can be lazy.
    public IntPtr AddVfx(string path)
    {
        if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
        if (attachedVfx.TryGetValue(path, out var existing)) return existing;
        var chara = BattleCharaPtr;
        if (chara == null) return IntPtr.Zero;
        try
        {
            if (!Plugin.DataManager.FileExists(path))
            {
                Plugin.Log.Warning($"AddVfx: path not found '{path}'");
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"AddVfx: FileExists threw for '{path}': {ex.Message}");
            return IntPtr.Zero;
        }

        var vfx = VfxFunctions.SpawnActorVfx(path, (Character*)chara, (Character*)chara);
        if (vfx == IntPtr.Zero) return IntPtr.Zero;
        attachedVfx[path] = vfx;
        return vfx;
    }

    public void RemoveVfx(string path)
    {
        attachedVfxExpiry.Remove(path);
        if (!attachedVfx.Remove(path, out var vfx)) return;
        VfxFunctions.RemoveActorVfx(vfx);
    }

    // Raid head-marker style attachment (e.g. arm-unit rotation arrows). lockonId maps
    // to the Lockon excel sheet's IconName, which resolves to vfx/lockon/eff/{IconName}.avfx.
    // When duration > 0, the VFX auto-detaches after that many seconds (driven by Tick).
    public void AttachLockonVfx(uint lockonId, float duration = 0f)
    {
        if (!IsAlive) return;
        var sheet = Plugin.DataManager.GetExcelSheet<Lockon>();
        if (!sheet.TryGetRow(lockonId, out var row)) return;
        var iconName = row.IconName.ExtractText();
        if (string.IsNullOrEmpty(iconName)) return;
        var path = $"vfx/lockon/eff/{iconName}.avfx";
        var ptr = AddVfx(path);
        if (ptr != IntPtr.Zero && duration > 0f) attachedVfxExpiry[path] = duration;
    }

    public void ClearAttachedVfx()
    {
        foreach (var vfx in attachedVfx.Values) VfxFunctions.RemoveActorVfx(vfx);
        attachedVfx.Clear();
        attachedVfxExpiry.Clear();
    }

    // Pinned (no-countdown) status tracked per-id. Re-stamped to -1f each Tick so
    // the engine's per-frame decrement (twice per frame on dual-registered party
    // members) doesn't drift the value into a visible counter.
    public void AddStatus(ushort statusId)
    {
        if (statusId == 0 || pinnedStatuses.ContainsKey(statusId)) return;
        pinnedStatuses[statusId] = new PinnedStatus(this, statusId);
    }

    public void RemoveStatus(ushort statusId)
    {
        if (pinnedStatuses.Remove(statusId, out var pin)) pin.Clear();
    }

    public virtual void Tick(float deltaSeconds)
    {
        foreach (var pin in pinnedStatuses.Values) pin.Tick();

        if (attachedVfxExpiry.Count > 0)
        {
            List<string>? expired = null;
            foreach (var (path, remaining) in attachedVfxExpiry)
            {
                var next = remaining - deltaSeconds;
                if (next <= 0f) (expired ??= new List<string>()).Add(path);
                else attachedVfxExpiry[path] = next;
            }
            if (expired != null) foreach (var path in expired) RemoveVfx(path);
        }
    }

    public virtual void Despawn()
    {
        ClearAttachedVfx();
        foreach (var pin in pinnedStatuses.Values) pin.Clear();
        pinnedStatuses.Clear();
    }
}
