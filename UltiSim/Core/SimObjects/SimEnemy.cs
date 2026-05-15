using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace UltiSim.Core.SimObjects;

// Placement.Position is scenario-local (offset from SimWorld.ScenarioOrigin) on world axes: +X = east, +Z = south.
// Same coordinate space as everything else in the SimXxx API — reads, writes, Cast targets, etc.
// Placement.Rotation is absolute (radians); 0 = south, π/2 = east, π = north, -π/2 = west.
// ModelCharaId, when non-zero, overrides the visual sourced from BNpcBase — handy for
// reusing one BNpc identity while swapping the rendered body (e.g., no-shield variants).
// Hitbox radius is derived from BNpcBase.Scale × ModelChara's unscaled radius.
// Drives whether a SimEnemy appears in the _EnemyList HUD. Read by EnmityHud.Refresh
// each frame via the computed SimEnemy.InEnemyList property.
//
// Always          — stays in the list as long as the enemy is alive.
// OnlyWhenVisible — mirrors the engine's DrawObject.IsVisible flag. Suitable for
//                   adds that warp in/out during a phase. Don't combine with
//                   SetModelState: that briefly DisableDraws during model rebuild
//                   and would flap the list. Bosses that transform should use Always.
// Never           — never enters the list (AOE-source dummies, tether endpoints).
// Manual          — scenario controls via SimEnemy.SetInEnemyList(bool); default false.
public enum EnemyListMode
{
    Always,
    OnlyWhenVisible,
    Never,
    Manual,
}

public record struct EnemySpawnConfig(
    uint BNpcBaseId,
    uint NameId = 0,
    byte Level = 0,
    bool Targetable = false,
    EnemyListMode EnemyList = EnemyListMode.Always,
    bool IsVisible = true,
    Placement Placement = default,
    uint ModelCharaId = 0,
    float Scale = 0f,    // 0 = use BNpcBase.Scale
    float Lifetime = 0f, // 0 = persist until explicit Despawn / scenario reset
    byte? InitialModeAttributeFlags = null); // null = leave at engine default (0x00); set when the boss's canonical idle sub-mesh variant differs (e.g. Omega-M = 0x10)

public sealed unsafe class SimEnemy : SimNpc
{
    // Monotonically increasing across all enemies; mirrors what the server's GlobalSequence does.
    private static uint NextGlobalSequence = 1;

    private bool casting;
    private float castElapsed;
    private float castTotal;
    private Vector3? castTargetLocation;
    private GameObjectId? castTargetId;
    private byte castAnimationVariation;
    private VfxFunctions.StaticVfxStruct* castOmen;
    private VfxFunctions.StaticVfxStruct* castOmenAlt;
    private float pendingOmenDelay;
    private float pendingOmenRotate;
    private uint pendingOmenActionId;
    private bool pendingOmenScheduled;

    // Visibility is driven via the DrawObject lifecycle: SetVisible records a
    // desired state and Tick's reconciler fires EnableDraw / DisableDraw at
    // most once per state change, gated on IsReadyToDraw so consecutive toggles
    // can't race the engine's async model load. RenderFlags writes were tried
    // and don't reliably keep enemies visible — only the DrawObject lifecycle
    // does. currentVisible starts true because the base SimNpc.Tick fires the
    // initial EnableDraw on its own (via pendingDraw).
    private bool desiredVisible = true;
    private bool currentVisible = true;

    public uint BNpcBaseId { get; }

    // Live-read via GameObject::GetName() (vfunc 6) — same path the in-game
    // target bar uses, so engine-driven renames mid-fight (e.g. TOP P5 Sigma
    // Omega transitions: 1DD3 -> 1DD4 -> 1E0F -> 2FE2) propagate without
    // notification. The vfunc resolves NameId -> BNpcName sheet via
    // RaptureTextModule; reading the GameObject.Name[] byte buffer directly
    // does NOT work for client-spawned doppels because the engine never
    // refreshes that buffer on rename. Falls back to the spawn-time name when
    // the BattleChara is gone (mid-despawn).
    public string DisplayName
    {
        get
        {
            var chara = GetBattleChara();
            if (chara == null) return initialDisplayName;
            var name = ((GameObject*)chara)->GetName().ToString();
            return string.IsNullOrEmpty(name) ? initialDisplayName : name;
        }
    }

    private readonly string initialDisplayName;
    public EnemyListMode EnemyListMode { get; }
    private bool manualInEnemyList;

    // EnmityHud.Refresh reads this each frame. Always/Never are constant;
    // OnlyWhenVisible reads the engine's DrawObject.IsVisible flag directly so
    // anything that toggles the draw lifecycle (SetVisible, the WarpOut/Spawn
    // timelines if they end up flipping it, or any future engine path) is
    // reflected without extra plumbing. Manual lets the scenario drive it.
    public bool InEnemyList => EnemyListMode switch
    {
        EnemyListMode.Always          => true,
        EnemyListMode.Never           => false,
        EnemyListMode.Manual          => manualInEnemyList,
        EnemyListMode.OnlyWhenVisible => IsEngineVisible(),
        _ => false,
    };
    public bool IsCasting => casting;
    public uint CastActionId { get; private set; }
    public float CastProgress => castTotal <= 0f ? 0f : Math.Clamp(castElapsed / castTotal, 0f, 1f);

    internal SimEnemy(uint index, uint bNpcBaseId, string displayName, EnemyListMode enemyListMode, SimWorld world, EventScheduler events, float lifetime) : base(index, world)
    {
        BNpcBaseId = bNpcBaseId;
        initialDisplayName = displayName;
        EnemyListMode = enemyListMode;
        // Auto-despawn schedule for short-lived helpers (e.g. AOE-source dummies).
        // Lifetime == 0 means persist until explicit Despawn / scenario reset.
        // Despawn is idempotent (IsAlive guard) so it's safe even if a manual
        // despawn fires first.
        if (lifetime > 0f) events.Add(lifetime, Despawn);
    }

    // Allocates a BattleChara, configures it as a BattleNpc per the supplied
    // config, and returns a SimEnemy wrapping it. Caller is responsible for
    // registering the result in the world's children list (so reset/teardown
    // covers it). Returns null on missing LocalPlayer, BNpcBase miss, or
    // CreateBattleChara failure.
    internal static SimEnemy? Spawn(EnemySpawnConfig config, SimWorld world, EventScheduler events)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return null;

        var bnpcSheet = Plugin.DataManager.GetExcelSheet<BNpcBase>();
        if (!bnpcSheet.TryGetRow(config.BNpcBaseId, out var bnpc))
        {
            Plugin.Log.Warning($"BNpcBase row {config.BNpcBaseId} (0x{config.BNpcBaseId:X}) not found");
            return null;
        }

        if (!BattleCharaSpawn.CreateBattleChara(out var idx, out var obj)) return null;

        var chara = (BattleChara*)obj;
        // Engine's canonical BNpc initializer — populates ModelContainer fields from
        // BNpcBase sheet data (including ModeAttributeFlags, which drives body sub-mesh
        // variants like Omega-M's shield). Must run before our explicit overrides below.
        chara->CharacterSetup.SetupBNpc(config.BNpcBaseId, config.NameId);
        chara->ObjectKind = ObjectKind.BattleNpc;
        chara->Position = world.ToWorld(config.Placement.Position);
        chara->SetRotation(MathUtil.NormalizeRotation(config.Placement.Rotation));
        var scale = config.Scale > 0f ? config.Scale : bnpc.Scale;
        chara->Scale = scale;
        var modelCharaId = config.ModelCharaId != 0 ? config.ModelCharaId : bnpc.ModelChara.RowId;
        chara->ModelContainer.ModelCharaId = (int)modelCharaId;
        chara->SEPack = bnpc.SEPack;
        var hitboxRadius = ResolveHitboxRadius(modelCharaId, scale);

        // Read the engine-resolved name (NameId -> BNpcName via RaptureTextModule, vfunc 6).
        // Same source the target bar / nameplate use, so the Name[] buffer we stamp below
        // stays consistent with what the rest of the UI renders.
        var displayName = ((GameObject*)chara)->GetName().ToString();
        if (string.IsNullOrEmpty(displayName)) displayName = $"BNpc {config.BNpcBaseId:X}";
        BattleCharaSpawn.WriteName(obj, displayName);
        obj->RenderFlags = 0;

        chara->CharacterSetup.CopyFromCharacter((Character*)chara, CharacterSetupContainer.CopyFlags.None);

        chara->BattleNpcSubKind = BattleNpcSubKind.Combatant;
        chara->HitboxRadius = hitboxRadius;
        chara->MaxHealth = 1_000_000;
        chara->Health = 1_000_000;
        chara->Battalion = 4;
        chara->IsHostile = true;
        chara->InCombat = true;
        chara->CombatTagType = 1;
        chara->CombatTaggerId = ((GameObject*)player.Address)->GetGameObjectId();
        chara->Mode = CharacterModes.Normal;
        chara->ModeParam = 0;
        if (config.InitialModeAttributeFlags is { } maf)
            chara->ModelContainer.ModeAttributeFlags = maf;
        chara->CastInfo.IsCasting = false;
        if (config.NameId != 0) chara->NameId = config.NameId;
        if (config.Level != 0) chara->Level = config.Level;
        
        // BattleCharaSpawn.RegisterInCharacterManager(chara);

        Plugin.Log.Info($"SimEnemy: spawned BNpcBase {config.BNpcBaseId} (ModelChara {bnpc.ModelChara.RowId}, scale {bnpc.Scale}) at index {idx}");
        var enemy = new SimEnemy(idx, config.BNpcBaseId, displayName, config.EnemyList, world, events, config.Lifetime);
        // Seed the stored Position/Rotation. The native struct writes above
        // are what the engine reads; SetPosition mirrors them into the C#-side
        // fields (and harmlessly re-pushes to native + DrawObject).
        enemy.SetPosition(config.Placement);
        enemy.SetTargetable(config.Targetable);
        if (!config.IsVisible) enemy.SetVisible(false);
        return enemy;
    }

    // The game stores each model's unscaled hitbox radius in ModelChara's first numeric
    // column (CSV header: "Unknown0"). Final hitbox = that × BNpcBase.Scale. Models with
    // no body (helpers, optical units) have 0 there; the game falls back to ~0.5.
    private static float ResolveHitboxRadius(uint modelCharaId, float scale)
    {
        const float DefaultUnscaledRadius = 0.5f;
        var sheet = Plugin.DataManager.GetExcelSheet<ModelChara>();
        var unscaled = DefaultUnscaledRadius;
        if (sheet.TryGetRow(modelCharaId, out var row) && row.Unknown0 > 0f)
            unscaled = row.Unknown0;
        return unscaled * scale;
    }

    public override void Despawn()
    {
        // BattleCharaSpawn.UnregisterFromCharacterManager(BattleCharaPtr);
        pendingOmenScheduled = false;
        ClearCastOmen();
        base.Despawn();
    }

    // Scenarios rarely call Game.Kill on enemies (boss HP isn't modeled), but
    // include the override for symmetry — a killed enemy just despawns.
    internal override void OnKilled() => Despawn();

    public void SetTargetable(bool targetable)
    {
        var chara = GetBattleChara();
        if (chara == null) return;
        if (targetable)
        {
            chara->TargetableStatus |= ObjectTargetableFlags.IsTargetable | ObjectTargetableFlags.Unk1;
            // chara->RenderFlags &= ~VisibilityFlags.Nameplate;
        }
        else
        {
            chara->TargetableStatus &= ~(ObjectTargetableFlags.IsTargetable | ObjectTargetableFlags.Unk1);
            // chara->RenderFlags |= VisibilityFlags.Nameplate;
        }
    }

    // Only meaningful when the spawn config set EnemyListMode.Manual. Other modes
    // ignore the call and warn — a misplaced toggle on an Always/OnlyWhenVisible
    // enemy is almost always a scenario bug we'd want to surface, not silently
    // swallow.
    public void SetInEnemyList(bool inEnemyList)
    {
        if (EnemyListMode != EnemyListMode.Manual)
        {
            Plugin.Log.Warning($"SetInEnemyList({inEnemyList}) ignored: SimEnemy {DisplayName} has mode {EnemyListMode}; declare EnemyListMode.Manual in EnemySpawnConfig to use explicit toggles.");
            return;
        }
        manualInEnemyList = inEnemyList;
    }

    // Records the desired render state; the reconciler in Tick fires
    // EnableDraw / DisableDraw at most once per state change. Targetability is
    // left untouched — callers that want the standard "warp out + untargetable"
    // combo should pair this with SetTargetable(false).
    public void SetVisible(bool visible) => desiredVisible = visible;

    private void ReconcileVisibility()
    {
        if (desiredVisible == currentVisible) return;
        var obj = GetGameObject();
        if (obj == null) return;
        if (!obj->IsReadyToDraw()) return;
        if (desiredVisible) obj->EnableDraw();
        else                obj->DisableDraw();
        currentVisible = desiredVisible;
    }

    // Reads the engine's authoritative draw-state bits set by EnableDraw/DisableDraw
    // (DrawObject.Flags bits 0 and 3). Returns false during the async model-load
    // window where DrawObject is still null. Used by EnemyListMode.OnlyWhenVisible.
    private bool IsEngineVisible()
    {
        var obj = GetGameObject();
        if (obj == null) return false;
        var draw = obj->DrawObject;
        return draw != null && draw->IsVisible;
    }

    // Single entry point for any action the simulator drives. When castSeconds resolves
    // to <= 0 (either passed explicitly or read as Cast100ms=0 from the sheet), the
    // action fires immediately with no cast bar — replaces the old UseAction /
    // UseActionOnTarget paths. targetLocation (scenario-local, like all SimXxx position
    // APIs) drives the AOE landing point and the pre-fire facing snap; targetId, if
    // set, makes the packet carry NumTargets=1 (some actions only animate on the
    // caster when an entity target is delivered).
    public bool Cast(uint actionId, Vector3? targetLocation = null, float? castSeconds = null, GameObjectId? targetId = null, float omenDelay = 0f, float omenRotate = 0f, byte animationVariation = 0)
    {
        var chara = GetBattleChara();
        if (chara == null) return false;

        Lumina.Excel.Sheets.Action action;
        if (castSeconds is null)
        {
            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (!actionSheet.TryGetRow(actionId, out action))
            {
                Plugin.Log.Warning($"Action row {actionId} not found");
                return false;
            }
            castSeconds = action.Cast100ms / 10f;
        }

        // Convert the scenario-local target to world once. CastInfo.TargetLocation,
        // the omen VFX spawn, and the synthetic ActionEffect packet all need world
        // coords; storing as world internally keeps those downstream call sites
        // unchanged.
        var worldTargetLocation = targetLocation is { } locL ? (Vector3?)world.ToWorld(locL) : null;

        var seconds = castSeconds.Value;
        chara->CastInfo.ActionType = 1; // ActionType.Action
        chara->CastInfo.ActionId = actionId;
        if (targetId is { } tid)
            chara->CastInfo.TargetId = tid;
        else if (targetLocation is not null)
            chara->CastInfo.TargetId = 0xE0000000;
        else
            chara->CastInfo.TargetId = chara->GetGameObjectId();
        if (worldTargetLocation is { } loc) chara->CastInfo.TargetLocation = loc;

        castTargetLocation = worldTargetLocation;
        castTargetId = targetId;
        castAnimationVariation = animationVariation;
        CastActionId = actionId;
        if (seconds <= 0f)
        {
            FaceCastTarget(chara);
            FireActionEffect(chara, worldTargetLocation, targetId, animationVariation);
            castTargetLocation = null;
            castTargetId = null;
            castAnimationVariation = 0;
            CastActionId = 0;
            return true;
        }

        chara->CastInfo.IsCasting = true;
        chara->CastInfo.Interruptible = false;
        chara->CastInfo.CurrentCastTime = 0f;
        chara->CastInfo.BaseCastTime = seconds;
        chara->CastInfo.TotalCastTime = seconds;

        casting = true;
        castElapsed = 0f;
        castTotal = seconds;
        pendingOmenScheduled = false;
        pendingOmenDelay = MathF.Max(0f, omenDelay);
        pendingOmenRotate = omenRotate;
        pendingOmenActionId = actionId;
        if (pendingOmenDelay <= 0f)
        {
            SpawnCastOmen(actionId, chara, worldTargetLocation, omenRotate);
        }
        else
        {
            pendingOmenScheduled = true;
        }
        return true;
    }

    // Manually spawning the cast bypasses Character::StartCast, so the AOE telegraph
    // omen is never created. Replicate it by reading the action's Omen sheet entry and
    // spawning a StaticVfx at the target location. Bad paths cause an async crash on
    // the file thread, so we validate via DataManager.FileExists before calling create.
    private void SpawnCastOmen(uint actionId, BattleChara* chara, Vector3? targetLocation, float extraRotation = 0f)
    {
        ClearCastOmen();

        var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (!actionSheet.TryGetRow(actionId, out var action))
        {
            Plugin.Log.Information($"SpawnCastOmen: action row {actionId:X} ({actionId}) not found in sheet");
            return;
        }
        if (action.Omen.ValueNullable is not { } omen || omen.RowId == 0)
        {
            Plugin.Log.Information($"SpawnCastOmen: action {actionId:X} ({actionId}) has no Omen entry (Omen.RowId=0 or null)");
            return;
        }
        var resolvedPath = ResolveActionOmenPath(actionId, omen.Path.ToString());
        if (resolvedPath == null) return;
        Plugin.Log.Information($"SpawnCastOmen: action {actionId:X} omen path resolved to '{resolvedPath}' (CastType={action.CastType}, EffectRange={action.EffectRange}, XAxisMod={action.XAxisModifier})");

        var origin = targetLocation ?? new Vector3(chara->Position.X, chara->Position.Y, chara->Position.Z);
        var range = action.EffectRange;
        if (range <= 0) range = 1;
        // CastType 4/11/12 use XAxisModifier as the rectangle's full width along X.
        var halfWidth = action.XAxisModifier > 0 ? action.XAxisModifier * 0.5f : range;
        var scale = action.CastType switch
        {
            4 or 11 or 12 => new Vector3(halfWidth, 1f, range),
            _ => new Vector3(range, 1f, range),
        };
        var rotation = MathUtil.NormalizeRotation(chara->Rotation + extraRotation);
        Plugin.Log.Information($"SpawnCastOmen: action {actionId:X} origin=<{origin.X:F2},{origin.Y:F2},{origin.Z:F2}> charaRot={chara->Rotation:F3} extraRot={extraRotation:F3} finalRot={rotation:F3} scale=<{scale.X:F2},{scale.Y:F2},{scale.Z:F2}>");
        castOmen = VfxFunctions.SpawnStaticVfx(resolvedPath, new Placement(origin, rotation), scale);

        // CastType 11 is a "+" cross whose Omen sheet entry points at the same single-bar
        // file (`general_x02f`) as a regular rect; the cross visual is formed by spawning
        // that bar twice — second copy rotated 90° to make the perpendicular arm.
        if (action.CastType == 11)
        {
            var perpRotation = MathUtil.NormalizeRotation(rotation + MathF.PI / 2f);
            castOmenAlt = VfxFunctions.SpawnStaticVfx(resolvedPath, new Placement(origin, perpRotation), scale);
        }
        else if (action.OmenAlt.ValueNullable is { } omenAlt && omenAlt.RowId != 0)
        {
            // Defensive: some non-cross actions populate OmenAlt with a paired shape.
            var altPath = ResolveActionOmenPath(actionId, omenAlt.Path.ToString());
            if (altPath != null)
            {
                castOmenAlt = VfxFunctions.SpawnStaticVfx(altPath, new Placement(origin, rotation), scale);
            }
        }
    }

    private static string? ResolveActionOmenPath(uint actionId, string rawPath)
    {
        var resolved = ResolveOmenPath(rawPath);
        if (resolved == null)
        {
            Plugin.Log.Warning($"SpawnCastOmen: omen file not found for action {actionId} (raw='{rawPath}')");
        }
        return resolved;
    }

    // Lumina's Omen.Path is typically a bare name (`gl_circle_5007_x1`); the on-disk
    // resource lives at `vfx/omen/eff/{name}.avfx`. Lumina.FileExists throws on paths
    // without an extension, so we only test candidates that have one.
    private static string? ResolveOmenPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var withExt = raw.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase) ? raw : raw + ".avfx";
        var fullPath = withExt.Contains('/') ? withExt : $"vfx/omen/eff/{withExt}";
        try
        {
            if (Plugin.DataManager.FileExists(fullPath)) return fullPath;
            if (fullPath != withExt && Plugin.DataManager.FileExists(withExt)) return withExt;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"ResolveOmenPath: FileExists threw for '{fullPath}': {ex.Message}");
        }
        return null;
    }

    private void ClearCastOmen()
    {
        if (castOmen != null)
        {
            VfxFunctions.RemoveStaticVfx(castOmen);
            castOmen = null;
        }
        if (castOmenAlt != null)
        {
            VfxFunctions.RemoveStaticVfx(castOmenAlt);
            castOmenAlt = null;
        }
    }

    // Lets scenarios suppress the default cast telegraph when they want to
    // render a custom omen (e.g. SwivelCannon's 210° cone covers the whole arena
    // from the edge; the scenario draws a half-disc at arena center instead).
    public void HideCastOmen() => ClearCastOmen();

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        ReconcileVisibility();

        if (!casting) return;

        var chara = GetBattleChara();
        if (chara == null) { casting = false; return; }

        castElapsed += deltaSeconds;
        if (pendingOmenScheduled && castElapsed >= pendingOmenDelay)
        {
            pendingOmenScheduled = false;
            SpawnCastOmen(pendingOmenActionId, chara, castTargetLocation, pendingOmenRotate);
        }
        if (castElapsed >= castTotal)
        {
            chara->CastInfo.CurrentCastTime = castTotal;
            FaceCastTarget(chara);
            FireActionEffect(chara, castTargetLocation, castTargetId, castAnimationVariation);
            chara->CastInfo.IsCasting = false;
            casting = false;
            castTargetLocation = null;
            castTargetId = null;
            castAnimationVariation = 0;
            CastActionId = 0;
            pendingOmenScheduled = false;
            ClearCastOmen();
        }
        else
        {
            chara->CastInfo.CurrentCastTime = castElapsed;
        }
    }

    // Targeted casts (ground location now; entity targets later) snap to face the target
    // on the final tick so the release animation plays in the intended direction even if
    // the target moved during the cast. FireActionEffect snapshots Rotation into the
    // packet header, so this must run first.
    private void FaceCastTarget(BattleChara* chara)
    {
        if (castTargetLocation is not { } loc) return;
        var dx = loc.X - chara->Position.X;
        var dz = loc.Z - chara->Position.Z;
        if (dx * dx + dz * dz < 1e-6f) return;
        chara->Rotation = MathUtil.NormalizeRotation(MathF.Atan2(dx, dz));
    }

    // Mimics the server's ActionEffect packet so the game plays the action's release
    // animation/VFX on the caster. When deliverTo is set, the packet carries
    // NumTargets=1 with that GameObjectId and a zeroed (no-op) effect entry; some
    // actions only animate on the caster if the engine sees at least one target
    // to deliver to. When null, NumTargets=0 (used for self-targeted UseAction
    // calls and cast releases that don't have an entity target).
    private static void FireActionEffect(BattleChara* chara, Vector3? targetLocation = null, GameObjectId? deliverTo = null, byte animationVariation = 0)
    {
        var actionId = chara->CastInfo.ActionId;
        var rotationInt = (ushort)Math.Clamp(
            (int)((chara->Rotation / MathF.PI) * 32767f + 32767f), 0, 65535);

        var header = default(ActionEffectHandler.Header);
        header.AnimationTargetId = chara->CastInfo.TargetId;
        header.ActionId = actionId;
        header.GlobalSequence = NextGlobalSequence++;
        header.AnimationLock = 0f;
        header.BallistaEntityId = 0xE0000000;
        header.SourceSequence = 0;
        header.RotationInt = rotationInt;
        header.SpellId = (ushort)actionId;
        header.AnimationVariation = animationVariation;
        header.ActionType = chara->CastInfo.ActionType;
        header.NumTargets = (byte)(deliverTo.HasValue ? 1 : 0);
        header.ForceAnimationLock = true;

        var pos = targetLocation ?? new Vector3(chara->Position.X, chara->Position.Y, chara->Position.Z);

        
        if (deliverTo is { } targetId)
        {
            Plugin.Log.Info($"ActionEffectHandler.Receive: caster: {chara->EntityId:X}, position: {pos}, targetId: {targetId.Id:X}");
            var effects = default(ActionEffectHandler.TargetEffects);
            ActionEffectHandler.Receive(
                chara->EntityId,
                (Character*)chara,
                &pos,
                &header,
                &effects,
                &targetId);
        }
        else
        {
            Plugin.Log.Info($"ActionEffectHandler.Receive: caster: {chara->EntityId:X}, position: {pos}");
            ActionEffectHandler.Receive(
                chara->EntityId,
                (Character*)chara,
                &pos,
                &header,
                null,
                null);
        }
    }

    public CharacterFind<T> Find<T>(List<T> targets) where T : IPositioned
    {
        return new CharacterFind<T>(targets);
    }
}
