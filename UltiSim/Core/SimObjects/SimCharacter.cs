using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace UltiSim.Core.SimObjects;

// Common base for anything in the simulated world that has a BattleChara behind
// it — spawned NPCs (SimNpc) and the real local player (SimPlayer). Owns the
// shared overlay state (attached VFX, statuses) and Move/MoveTo as virtual
// no-ops so callers can treat all characters uniformly. Subclasses expose the
// pointer + identity via the abstract properties below; everything else is
// concrete here.
public abstract unsafe class SimCharacter : ISimObject, IPositioned
{
    private readonly Dictionary<string, IntPtr> attachedVfx = new();
    private readonly Dictionary<string, float> attachedVfxExpiry = new();
    private readonly List<SimStatus> statuses = new();

    internal abstract BattleChara* BattleCharaPtr { get; }
    public abstract GameObjectId GameObjectId { get; }
    public abstract Vector3 Position { get; }
    public abstract float Rotation { get; }
    public abstract float HitboxRadius { get; }
    public Placement Placement => new(Position, Rotation);

    // Set by Game.Kill() — the character is corpse-on-floor / stunned / despawned
    // depending on subclass. Subclass IsAlive overrides AND with this so engine
    // checks (party.Tick, AI movement, scenario lookups) all see them as gone.
    public bool Dead { get; internal set; }

    // Default IsAlive: a character is alive if its BattleChara handle resolves
    // and Game.Kill hasn't been called on it. SimNpc overrides to also check
    // Index validity; SimPlayer can rely on this.
    public virtual bool IsAlive => !Dead && BattleCharaPtr != null;

    // Per-subclass effects of dying. Game.Kill() flips Dead and routes here so
    // subclasses do the right thing — SimPartyMember sets HP=0 + plays the KO
    // timeline, SimPlayer kicks the input-lockout hooks, SimEnemy despawns.
    internal virtual void OnKilled() { }

    // Scenario-facing facade for Game.Kill — same global side effects (chat
    // line, freeze timer, godmode short-circuit), just shorter at the call
    // site than reaching for Plugin.GameInstance.
    public void Die(string cause) => Plugin.GameInstance.Kill(this, cause);

    // Movement is a no-op by default — only SimNpc has writable position because
    // the real local player moves themselves. Scenarios call .MoveTo on a
    // uniform SimCharacter without checking the concrete type; calls that land
    // on SimPlayer are silently dropped. Override in SimNpc.
    public virtual void Move(Vector3 position) { }
    public virtual void Move(Placement placement) { }
    public virtual void MoveTo(Vector3 target, float speed = 6f, float? finalRotation = null, ushort timelineId = SimNpc.DefaultRunTimelineId) { }
    public virtual void StopMoving() { }

    // Self-attached actor VFX keyed by path. Path is validated via FileExists before
    // spawning to avoid the file-thread crash StaticVfxCreate has on bad paths.
    //
    // persistent: true  → tracked in attachedVfx; cleaned up on RemoveVfx or by
    //                     ClearAttachedVfx at Despawn / Reset. Re-adding the same
    //                     path is a no-op so callers can be lazy. With duration > 0
    //                     also auto-removed on its own counter via attachedVfxExpiry.
    // persistent: false → fire-and-forget. The AVFX self-completes and the game
    //                     frees the VfxData internally; we must NOT call
    //                     ActorVfxRemove on it later (that's the VfxData::Dtor
    //                     crash). duration is ignored in this mode.
    public IntPtr AddVfx(string path, float duration = 0f, bool persistent = true)
    {
        if (string.IsNullOrEmpty(path)) return IntPtr.Zero;
        var chara = BattleCharaPtr;
        if (chara == null) return IntPtr.Zero;

        if (persistent && attachedVfx.TryGetValue(path, out var existing))
        {
            if (duration > 0f) attachedVfxExpiry[path] = duration;
            return existing;
        }

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

        if (persistent)
        {
            attachedVfx[path] = vfx;
            if (duration > 0f) attachedVfxExpiry[path] = duration;
        }
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
    // See AddVfx for the persistent / duration semantics.
    public void AttachLockonVfx(uint lockonId, float duration = 0f, bool persistent = true)
    {
        if (!IsAlive) return;
        var sheet = Plugin.DataManager.GetExcelSheet<Lockon>();
        if (!sheet.TryGetRow(lockonId, out var row)) return;
        var iconName = row.IconName.ExtractText();
        if (string.IsNullOrEmpty(iconName)) return;
        AddVfx($"vfx/lockon/eff/{iconName}.avfx", duration, persistent);
    }

    public void ClearAttachedVfx()
    {
        foreach (var vfx in attachedVfx.Values) VfxFunctions.RemoveActorVfx(vfx);
        attachedVfx.Clear();
        attachedVfxExpiry.Clear();
    }

    // Status on this character. Default duration of 0 means apply once and
    // leave alone (no auto-remove from our side); pass a positive duration for
    // a visible countdown that auto-removes on expiry. Param is the stack
    // count / sheet Param the slot should carry (0 if the status doesn't use
    // it). Returned reference lets callers Despawn() it early (e.g. SimTether
    // tearing its tether debuff); otherwise the character ticks and prunes it.
    public SimStatus AddStatus(ushort statusId, float duration = 0f, ushort stacks = 0)
    {
        var current = statuses
            .FirstOrDefault(status => status.StatusId == statusId);
        if (current == null)
        {
            var s = new SimStatus(this, statusId, duration, stacks);
            statuses.Add(s);
            return s;
        }
        else
        {
            current.Reapply(duration, stacks);
            return current;
        }
    }

    public void RemoveStatus(ushort statusId)
    {
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            if (statuses[i].StatusId != statusId) continue;
            statuses[i].Despawn();
            statuses.RemoveAt(i);
        }
    }

    // Returns the most recently added active SimStatus for this id, or null if
    // there isn't one. Used by mechanic resolvers (stack increment) to read
    // the current Param without re-walking the BattleChara slot array.
    public SimStatus? GetStatus(ushort statusId)
    {
        for (int i = statuses.Count - 1; i >= 0; i--)
            if (statuses[i].IsActive && statuses[i].StatusId == statusId) return statuses[i];
        return null;
    }

    public bool HasStatus(ushort statusId) => GetStatus(statusId) != null;

    public virtual void Tick(float deltaSeconds)
    {
        // Tick statuses; prune ones that auto-expired (or were explicitly
        // Despawn'd) so the list doesn't grow unbounded across a long scenario.
        for (int i = statuses.Count - 1; i >= 0; i--)
        {
            statuses[i].Tick(deltaSeconds);
            if (!statuses[i].IsActive) statuses.RemoveAt(i);
        }

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
        foreach (var s in statuses) s.Despawn();
        statuses.Clear();
    }
}
