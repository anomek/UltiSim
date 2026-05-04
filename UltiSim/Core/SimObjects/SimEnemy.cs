using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace UltiSim.Core.SimObjects;

// Offset is in scenario-space world axes from SimWorld.ScenarioOrigin: +X = east, +Z = south.
// Rotation is absolute (radians); 0 = south, π/2 = east, π = north, -π/2 = west.
// ModelCharaId, when non-zero, overrides the visual sourced from BNpcBase — handy for
// reusing one BNpc identity while swapping the rendered body (e.g., no-shield variants).
// Hitbox radius is derived from BNpcBase.Scale × ModelChara's unscaled radius.
public record struct EnemySpawnConfig(
    uint BNpcBaseId,
    uint NameId = 0,
    byte Level = 0,
    bool Targetable = false,
    bool InEnemyList = true,
    Vector3 Offset = default,
    float Rotation = 0f,
    uint ModelCharaId = 0,
    float Scale = 0f, // 0 = use BNpcBase.Scale
    float Lifetime = 0f); // 0 = persist until explicit Despawn / scenario reset

public sealed unsafe class SimEnemy : SimNpc
{
    // Monotonically increasing across all enemies; mirrors what the server's GlobalSequence does.
    private static uint NextGlobalSequence = 1;

    private bool casting;
    private float castElapsed;
    private float castTotal;
    private Vector3? castTargetLocation;
    private GameObjectId? castTargetId;
    private VfxFunctions.StaticVfxStruct* castOmen;
    private float pendingOmenDelay;
    private float pendingOmenRotate;
    private uint pendingOmenActionId;
    private bool pendingOmenScheduled;

    public string DisplayName { get; }
    public bool InEnemyList { get; }
    public bool IsCasting => casting;
    public uint CastActionId { get; private set; }
    public float CastProgress => castTotal <= 0f ? 0f : Math.Clamp(castElapsed / castTotal, 0f, 1f);

    internal SimEnemy(uint index, string displayName, bool inEnemyList, EventScheduler events, float lifetime) : base(index)
    {
        DisplayName = displayName;
        InEnemyList = inEnemyList;
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
    internal static SimEnemy? Spawn(EnemySpawnConfig config, Vector3 origin, EventScheduler events)
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
        chara->ObjectKind = ObjectKind.BattleNpc;
        chara->Position = new Vector3(
            origin.X + config.Offset.X,
            origin.Y + config.Offset.Y,
            origin.Z + config.Offset.Z);
        chara->SetRotation(MathUtil.NormalizeRotation(config.Rotation));
        var scale = config.Scale > 0f ? config.Scale : bnpc.Scale;
        chara->Scale = scale;
        var modelCharaId = config.ModelCharaId != 0 ? config.ModelCharaId : bnpc.ModelChara.RowId;
        chara->ModelContainer.ModelCharaId = (int)modelCharaId;
        var hitboxRadius = ResolveHitboxRadius(modelCharaId, scale);

        var displayName = ResolveBNpcName(config.NameId) ?? $"BNpc {config.BNpcBaseId:X}";
        BattleCharaSpawn.WriteName(obj, displayName);
        obj->RenderFlags = 0;

        chara->CharacterSetup.CopyFromCharacter((Character*)chara, CharacterSetupContainer.CopyFlags.None);

        chara->BattleNpcSubKind = BattleNpcSubKind.Combatant;
        chara->TargetableStatus = config.Targetable
            ? (ObjectTargetableFlags.IsTargetable | ObjectTargetableFlags.Unk1)
            : 0;
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
        chara->CastInfo.IsCasting = false;
        if (config.NameId != 0) chara->NameId = config.NameId;
        if (config.Level != 0) chara->Level = config.Level;

        Plugin.Log.Info($"SimEnemy: spawned BNpcBase {config.BNpcBaseId} (ModelChara {bnpc.ModelChara.RowId}, scale {bnpc.Scale}) at index {idx}");
        return new SimEnemy(idx, displayName, config.InEnemyList, events, config.Lifetime);
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

    private static string? ResolveBNpcName(uint nameId)
    {
        if (nameId == 0) return null;
        var sheet = Plugin.DataManager.GetExcelSheet<BNpcName>();
        if (!sheet.TryGetRow(nameId, out var row)) return null;
        var s = row.Singular.ExtractText();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public override void Despawn()
    {
        pendingOmenScheduled = false;
        ClearCastOmen();
        base.Despawn();
    }

    public void PlayActionTimeline(ushort timelineId)
    {
        var chara = GetBattleChara();
        if (chara == null) return;
        chara->Timeline.PlayActionTimeline(timelineId);
    }

    public void SetTargetable(bool targetable)
    {
        var chara = GetBattleChara();
        if (chara == null) return;
        chara->TargetableStatus = targetable
            ? (ObjectTargetableFlags.IsTargetable | ObjectTargetableFlags.Unk1)
            : 0;
    }

    // Single entry point for any action the simulator drives. When castSeconds resolves
    // to <= 0 (either passed explicitly or read as Cast100ms=0 from the sheet), the
    // action fires immediately with no cast bar — replaces the old UseAction /
    // UseActionOnTarget paths. targetLocation drives the AOE landing point and the
    // pre-fire facing snap; targetId, if set, makes the packet carry NumTargets=1
    // (some actions only animate on the caster when an entity target is delivered).
    public bool Cast(uint actionId, Vector3? targetLocation = null, float? castSeconds = null, GameObjectId? targetId = null, float omenDelay = 0f, float omenRotate = 0f)
    {
        var chara = GetBattleChara();
        if (chara == null) return false;

        if (castSeconds is null)
        {
            var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
            if (!actionSheet.TryGetRow(actionId, out var action))
            {
                Plugin.Log.Warning($"Action row {actionId} not found");
                return false;
            }
            castSeconds = action.Cast100ms / 10f;
        }

        var seconds = castSeconds.Value;
        chara->CastInfo.ActionType = 1; // ActionType.Action
        chara->CastInfo.ActionId = actionId;
        if (targetId is { } tid)
            chara->CastInfo.TargetId = tid;
        else if (targetLocation is not null)
            chara->CastInfo.TargetId = 0xE0000000;
        else
            chara->CastInfo.TargetId = chara->GetGameObjectId();
        if (targetLocation is { } loc) chara->CastInfo.TargetLocation = loc;

        castTargetLocation = targetLocation;
        castTargetId = targetId;
        CastActionId = actionId;

        if (seconds <= 0f)
        {
            FaceCastTarget(chara);
            FireActionEffect(chara, targetLocation, targetId);
            castTargetLocation = null;
            castTargetId = null;
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
            SpawnCastOmen(actionId, chara, targetLocation, omenRotate);
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
        if (!actionSheet.TryGetRow(actionId, out var action)) return;
        if (action.Omen.ValueNullable is not { } omen || omen.RowId == 0) return;
        var rawPath = omen.Path.ToString();
        var resolvedPath = ResolveOmenPath(rawPath);
        if (resolvedPath == null)
        {
            Plugin.Log.Warning($"SpawnCastOmen: omen file not found for action {actionId} (raw='{rawPath}')");
            return;
        }

        var origin = targetLocation ?? new Vector3(chara->Position.X, chara->Position.Y, chara->Position.Z);
        var range = action.EffectRange;
        if (range <= 0) range = 1;
        // Cast type 4/12 (rect) and 11 (cross) use XAxisModifier as half-width along X.
        var halfWidth = action.XAxisModifier > 0 ? action.XAxisModifier * 0.5f : range;
        var scale = action.CastType switch
        {
            4 or 11 or 12 => new Vector3(halfWidth, 1f, range),
            _ => new Vector3(range, 1f, range),
        };
        // Cones / rectangles need to point along the caster's facing; circles ignore rotation.
        castOmen = VfxFunctions.SpawnStaticVfx(resolvedPath, origin, MathUtil.NormalizeRotation(chara->Rotation + extraRotation), scale);
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
        if (castOmen == null) return;
        VfxFunctions.RemoveStaticVfx(castOmen);
        castOmen = null;
    }

    // Lets scenarios suppress the default cast telegraph when they want to
    // render a custom omen (e.g. SwivelCannon's 210° cone covers the whole arena
    // from the edge; the scenario draws a half-disc at arena center instead).
    public void HideCastOmen() => ClearCastOmen();

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
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
            FireActionEffect(chara, castTargetLocation, castTargetId);
            chara->CastInfo.IsCasting = false;
            casting = false;
            castTargetLocation = null;
            castTargetId = null;
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
    private static void FireActionEffect(BattleChara* chara, Vector3? targetLocation = null, GameObjectId? deliverTo = null)
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
        header.AnimationVariation = 0;
        header.ActionType = chara->CastInfo.ActionType;
        header.NumTargets = (byte)(deliverTo.HasValue ? 1 : 0);
        header.ForceAnimationLock = true;

        var pos = targetLocation ?? new Vector3(chara->Position.X, chara->Position.Y, chara->Position.Z);

        if (deliverTo is { } targetId)
        {
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
            ActionEffectHandler.Receive(
                chara->EntityId,
                (Character*)chara,
                &pos,
                &header,
                null,
                null);
        }
    }
}
