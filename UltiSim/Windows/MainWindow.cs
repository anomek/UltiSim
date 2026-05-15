using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Lumina.Excel.Sheets;
using UltiSim.Core.Map;
using UltiSim.Core;
using UltiSim.Core.SimObjects;
using UltiSim.Scenarios;

namespace UltiSim.Windows;

public unsafe class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private bool _leftPanelOpen = true;
    internal IScenario? SelectedScenario => _selectedScenario;
    private IScenario? _selectedScenario;

#if DEBUG
    private string debugBNpcBaseIdText = "15720";
    private string debugSpawnScaleText = "0";
    private string debugSpawnModeAttrFlagsText = "";
    private string debugTimelineIdText = "0x53C";
    private string debugModelStateText = "0x00";
    private string debugAnimStateText = "0x00";
    private string debugPoseTimelineText = "0x1E43";
    private string debugModeAttrFlagsText = "0x00";
    private string debugModelCharaIdText = "0";
    private string debugTransformationIdText = "493";
    private string debugCastActionIdText = "0";
    private string debugCastAnimVariationText = "0";
    private string debugMapEffectIndexText = "0x00";
    private string debugMapEffectStatusText = "0x0000";
    private string debugMapEffectFlagText = "0x00";
    private string debugDirectorCategoryText = "0x8000001E";
    private string debugDirectorArg1Text = "0x2AC";
    private string debugBgmIdText = "964";
    // EObj 1EB83C (decimal 2013244) = the TOP P5 Sigma falling-orb tower; useful default.
    private string debugEObjRowIdText = "2013244";
#endif

    public MainWindow(Plugin plugin)
        : base("UltiSim##MainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(220, 80),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Flags |= ImGuiWindowFlags.AlwaysAutoResize;

        this.plugin = plugin;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var leftWidth = _leftPanelOpen ? 180f : 30f;

        if (ImGui.BeginTable("##layout", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("##left", ImGuiTableColumnFlags.WidthFixed, leftWidth);
            ImGui.TableSetupColumn("##right", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            DrawScenariosPanel();
            ImGui.TableSetColumnIndex(1);
            DrawMainContent();
            ImGui.EndTable();
        }
    }

    private void DrawScenariosPanel()
    {
        if (_leftPanelOpen)
        {
            ImGui.TextUnformatted("Scenarios");
            ImGui.SameLine();
            if (ImGui.SmallButton("<##collapse")) _leftPanelOpen = false;
            ImGui.Separator();

            foreach (var scenario in plugin.Game.Scenarios)
            {
                var selected = _selectedScenario == scenario;
                if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                ImGui.PushID(scenario.Name);
                if (ImGui.Button(scenario.Name, new Vector2(-1, 0))) _selectedScenario = scenario;
                ImGui.PopID();
                if (selected) ImGui.PopStyleColor();
            }
        }
        else
        {
            if (ImGui.Button(">##expand")) _leftPanelOpen = true;
        }
    }

    private void DrawMainContent()
    {
        if (_selectedScenario == null)
        {
            ImGui.TextDisabled("Select a scenario");
            return;
        }

        var game = plugin.Game;

        ImGui.TextUnformatted(_selectedScenario.Name);
        ImGui.Separator();
        DrawLocationHint();

        if (ImGui.Button("Start")) game.RunScenario(_selectedScenario);
        ImGui.SameLine();
        if (ImGui.Button("Reset")) game.Reset();
        if (game.World.Map.IsInInstance)
        {
            ImGui.SameLine();
            if (ImGui.Button("Leave")) game.Leave();
        }

        var god = game.GodMode;
        if (ImGui.Checkbox("God mode", ref god)) game.GodMode = god;

#if DEBUG
        DrawSpeedControl();
#endif

        if (game.Paused) ImGui.TextDisabled("(scenario paused — press Reset to clear)");

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Scenario config", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            _selectedScenario.DrawSettings();
            ImGui.Unindent();
        }

#if DEBUG
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Debug"))
        {
            DrawDebugContent();
        }
#endif
    }

    private void DrawLocationHint()
    {
        var scenario = _selectedScenario!;
        var territory = Plugin.ClientState.TerritoryType;

        if (ZoneSession.IsInInn())
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Full simulation available");
        }
        else if (IsScenarioTerritory(scenario, territory))
        {
            var name = GetTerritoryName(territory) ?? territory.ToString();
            ImGui.TextColored(new Vector4(1f, 0.75f, 0.2f, 1f), $"Some visuals might be missing — {name}");
        }
        else
        {
            ImGui.TextDisabled("Works anywhere · Inn gives the full experience");
        }

        ImGui.SameLine();
        var help =
            "Inn: full scenario with correct arena geometry.\n" +
            "Supported instance: some visuals might be missing, zone layout auto-detected.\n" +
            "Anywhere else: some visuals might be missing, not adjusted for ground geometry — origin anchors to your position.";
        var supportedNames = GetSupportedDutyNames(scenario);
        if (supportedNames.Count > 0)
            help += "\n\nSupported instances:\n  " + string.Join("\n  ", supportedNames);
        ImGuiComponents.HelpMarker(help);
    }

    private static bool IsScenarioTerritory(IScenario scenario, uint territory)
    {
        if (scenario.TargetInstance?.TerritoryId == territory) return true;
        foreach (var ovr in scenario.OriginOverrides)
            if (ovr.TerritoryId == territory) return true;
        return false;
    }

    private static List<string> GetSupportedDutyNames(IScenario scenario)
    {
        var names = new List<string>();
        foreach (var ovr in scenario.OriginOverrides)
        {
            var n = GetDutyName(ovr.TerritoryId);
            if (!string.IsNullOrEmpty(n)) names.Add(n);
        }
        return names;
    }

    private static string? GetTerritoryName(uint id) =>
        Plugin.DataManager.GetExcelSheet<TerritoryType>()
            ?.GetRowOrDefault(id)?.PlaceName.ValueNullable?.Name.ExtractText();

    private static string? GetDutyName(uint territoryId) =>
        Plugin.DataManager.GetExcelSheet<TerritoryType>()
            ?.GetRowOrDefault(territoryId)?.ContentFinderCondition.ValueNullable?.Name.ExtractText();

    // Buttons modify Game.EventTimeScale live so callers can speed up / slow down a
    // scenario mid-run. Only event scheduling is affected; cast bars and animations
    // continue at real time (see Game.Tick).
    private void DrawSpeedControl()
    {
        var game = plugin.Game;
        ImGui.TextUnformatted("Speed:");
        for (int x = 1; x <= 4; x++)
        {
            ImGui.SameLine();
            var active = MathF.Abs(game.EventTimeScale - x) < 0.01f;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ImGui.Button($"x{x}")) game.EventTimeScale = x;
            if (active) ImGui.PopStyleColor();
        }
    }

#if DEBUG
    private void DrawDebugContent()
    {
        ImGui.TextUnformatted($"TerritoryId: {Plugin.ClientState.TerritoryType}");

        ImGui.Spacing();
        ImGui.TextUnformatted("Manual spawn");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("BNpcBaseId", ref debugBNpcBaseIdText, 16);
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("Scale (0 = default)", ref debugSpawnScaleText, 16);
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("ModeAttrFlags (blank = none)", ref debugSpawnModeAttrFlagsText, 16);
        if (ImGui.Button("Spawn"))
        {
            if (!TryParseId(debugBNpcBaseIdText, out var baseId))
            {
                Plugin.Log.Warning($"Spawn: can't parse BNpcBaseId '{debugBNpcBaseIdText}'");
            }
            else
            {
                float scale = 0f;
                var trimmed = debugSpawnScaleText.Trim();
                if (trimmed.Length > 0 && !float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                    Plugin.Log.Warning($"Spawn: can't parse Scale '{debugSpawnScaleText}', using default");
                byte? initialModeAttrFlags = null;
                var mafTrimmed = debugSpawnModeAttrFlagsText.Trim();
                if (mafTrimmed.Length > 0)
                {
                    if (TryParseId(mafTrimmed, out var maf) && maf <= 0xFF)
                        initialModeAttrFlags = (byte)maf;
                    else
                        Plugin.Log.Warning($"Spawn: can't parse ModeAttrFlags '{debugSpawnModeAttrFlagsText}', using default");
                }
                // Ad-hoc spawn outside any scenario — anchor to the player so
                // Offset=0 lands at our feet. Scenario runs overwrite this in
                // Game.RunScenarioInternal, so the stamp is non-leaking.
                var player = Plugin.ObjectTable.LocalPlayer;
                if (player != null) plugin.Game.World.ScenarioOrigin = player.Position;
                plugin.Game.World.SpawnEnemy(new EnemySpawnConfig(
                    BNpcBaseId: baseId,
                    Targetable: true,
                    Scale: scale,
                    InitialModeAttributeFlags: initialModeAttrFlags));
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Manual EObj spawn");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("EObjRowId", ref debugEObjRowIdText, 16);
        if (ImGui.Button("Spawn EObj"))
        {
            if (!TryParseId(debugEObjRowIdText, out var eObjRowId))
            {
                Plugin.Log.Warning($"Spawn EObj: can't parse EObjRowId '{debugEObjRowIdText}'");
            }
            else
            {
                // Same anchor trick as the BNpc spawn — drop the prop at the
                // player's feet by stamping ScenarioOrigin. Scenarios overwrite
                // this in Game.RunScenarioInternal.
                var player = Plugin.ObjectTable.LocalPlayer;
                if (player != null) plugin.Game.World.ScenarioOrigin = player.Position;
                plugin.Game.World.SpawnEventObject(new EventObjectSpawnConfig(
                    EObjRowId: eObjRowId,
                    IsVisible: true));
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Play animation on target");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("TimelineId", ref debugTimelineIdText, 16);
        if (ImGui.Button("Play on target"))
        {
            if (TryParseId(debugTimelineIdText, out var timelineId))
                PlayAnimationOnTarget((ushort)timelineId);
            else Plugin.Log.Warning($"Play animation: can't parse TimelineId '{debugTimelineIdText}'");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Apply pose change on target");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("ModelState", ref debugModelStateText, 16);
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("AnimState", ref debugAnimStateText, 16);
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("CommitTimelineId", ref debugPoseTimelineText, 16);
        if (ImGui.Button("Apply pose change"))
        {
            if (!TryParseId(debugModelStateText, out var modelState) || modelState > 0xFF)
                Plugin.Log.Warning($"Pose: can't parse ModelState '{debugModelStateText}'");
            else if (!TryParseId(debugAnimStateText, out var animState) || animState > 0xFF)
                Plugin.Log.Warning($"Pose: can't parse AnimState '{debugAnimStateText}'");
            else if (!TryParseId(debugPoseTimelineText, out var commitTimeline) || commitTimeline > ushort.MaxValue)
                Plugin.Log.Warning($"Pose: can't parse CommitTimelineId '{debugPoseTimelineText}'");
            else
                ApplyPoseChangeOnTarget((byte)modelState, (byte)(animState >> 4), (byte)(animState & 0xF), (ushort)commitTimeline);
        }
        ImGui.SameLine();
        if (ImGui.Button("Set ModelState only"))
        {
            if (!TryParseId(debugModelStateText, out var modelState) || modelState > 0xFF)
                Plugin.Log.Warning($"ModelState: can't parse '{debugModelStateText}'");
            else
                SetModelStateOnTarget((byte)modelState);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Apply ModeAttributeFlags on target");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("ModeAttrFlags", ref debugModeAttrFlagsText, 16);
        if (ImGui.Button("Apply ModeAttributeFlags"))
        {
            if (TryParseId(debugModeAttrFlagsText, out var flags) && flags <= 0xFF)
                SetModeAttributeFlagsOnTarget((byte)flags);
            else Plugin.Log.Warning($"ModeAttrFlags: can't parse '{debugModeAttrFlagsText}'");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Apply ModelCharaId on target");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("ModelCharaId", ref debugModelCharaIdText, 16);
        if (ImGui.Button("Apply ModelCharaId"))
        {
            if (TryParseId(debugModelCharaIdText, out var modelCharaId))
                SetModelCharaIdOnTarget((int)modelCharaId);
            else Plugin.Log.Warning($"ModelCharaId: can't parse '{debugModelCharaIdText}'");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Apply TransformationId on target");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("TransformationId", ref debugTransformationIdText, 16);
        if (ImGui.Button("Apply TransformationId"))
        {
            if (TryParseId(debugTransformationIdText, out var transformationId) && transformationId <= 0xFFFF)
                SetTransformationIdOnTarget((short)transformationId);
            else Plugin.Log.Warning($"TransformationId: can't parse '{debugTransformationIdText}'");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Cast on player");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("ActionId", ref debugCastActionIdText, 16);
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("AnimationVariation", ref debugCastAnimVariationText, 16);
        if (ImGui.Button("Cast on player"))
        {
            if (!TryParseId(debugCastActionIdText, out var actionId))
                Plugin.Log.Warning($"Cast: can't parse ActionId '{debugCastActionIdText}'");
            else if (!TryParseId(debugCastAnimVariationText, out var animVar) || animVar > 0xFF)
                Plugin.Log.Warning($"Cast: can't parse AnimationVariation '{debugCastAnimVariationText}'");
            else
                CastOnPlayerFromTarget(actionId, (byte)animVar);
        }
        ImGui.SameLine();
        if (ImGui.Button("Cast on self"))
        {
            if (!TryParseId(debugCastActionIdText, out var actionId))
                Plugin.Log.Warning($"Cast: can't parse ActionId '{debugCastActionIdText}'");
            else if (!TryParseId(debugCastAnimVariationText, out var animVar) || animVar > 0xFF)
                Plugin.Log.Warning($"Cast: can't parse AnimationVariation '{debugCastAnimVariationText}'");
            else
                CastOnSelfFromTarget(actionId, (byte)animVar);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Dump objects near player");
        ImGui.Separator();
        if (ImGui.Button("Dump"))
            DumpNearbyObjects();
        ImGui.SameLine();
        if (ImGui.Button("Dump target fields"))
            DumpTargetFields();
        ImGui.SameLine();
        if (ImGui.Button("Enumerate SharedGroups"))
            DumpSharedGroups();
        ImGui.SameLine();
        if (ImGui.Button("Bump EObj State"))
            BumpEventObjectState();

        ImGui.Spacing();
        ImGui.TextUnformatted("Map effect");
        ImGui.Separator();
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("Index", ref debugMapEffectIndexText, 16);
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("Status", ref debugMapEffectStatusText, 16);
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("Flag", ref debugMapEffectFlagText, 16);
        if (ImGui.Button("Apply map effect"))
        {
            if (!TryParseId(debugMapEffectIndexText, out var idx) || idx > 0xFF)
                Plugin.Log.Warning($"Map effect: can't parse Index '{debugMapEffectIndexText}'");
            else if (!TryParseId(debugMapEffectStatusText, out var status) || status > 0xFFFF)
                Plugin.Log.Warning($"Map effect: can't parse Status '{debugMapEffectStatusText}'");
            else if (!TryParseId(debugMapEffectFlagText, out var flag) || flag > 0xFF)
                Plugin.Log.Warning($"Map effect: can't parse Flag '{debugMapEffectFlagText}'");
            else
                plugin.Game.World.Map.AddEffect((status << 16) | (flag & 0xFFu), (byte)idx);
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Director update (ActorControl replay)");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("Category", ref debugDirectorCategoryText, 16);
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("Arg1", ref debugDirectorArg1Text, 16);
        if (ImGui.Button("Fire director update"))
        {
            if (!TryParseId(debugDirectorCategoryText, out var cat))
                Plugin.Log.Warning($"Director update: can't parse Category '{debugDirectorCategoryText}'");
            else if (!TryParseId(debugDirectorArg1Text, out var arg1))
                Plugin.Log.Warning($"Director update: can't parse Arg1 '{debugDirectorArg1Text}'");
            else
                DirectorFunctions.FireDirectorUpdate(cat, arg1);
        }
        ImGui.SameLine();
        if (ImGui.Button("Fire P5 Sigma transition"))
            DirectorFunctions.FireP5SigmaTransition();

        ImGui.Spacing();
        ImGui.TextUnformatted("Death system");
        ImGui.Separator();
        if (ImGui.Button("Kill MT"))
        {
            var mt = plugin.Game.World.Party.Get(PartyRole.MainTank);
            if (mt != null) plugin.Game.Kill(mt, "Debug kill");
        }
        ImGui.SameLine();
        if (ImGui.Button("Kill player") && plugin.Game.Player is { } p) plugin.Game.Kill(p, "Debug kill");

        ImGui.Spacing();
        ImGui.TextUnformatted("BGM test");
        ImGui.Separator();
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("BgmId", ref debugBgmIdText, 16);
        ImGui.SameLine();
        if (ImGui.Button("Play##bgm"))
        {
            if (!TryParseId(debugBgmIdText, out var bgmId) || bgmId > ushort.MaxValue)
                Plugin.Log.Warning($"BGM: can't parse BgmId '{debugBgmIdText}'");
            else
                plugin.Game.Bgm.Play((ushort)bgmId);
        }
        ImGui.SameLine();
        if (ImGui.Button("Stop##bgm")) plugin.Game.Bgm.Reset();
    }

    // Accepts decimal ("1340") or hex ("0x53C" / "53Ch" — case-insensitive).
    private static bool TryParseId(string input, out uint value)
    {
        var s = input.Trim();
        if (s.Length == 0) { value = 0; return false; }
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        if (s.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(s[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private void PlayAnimationOnTarget(ushort timelineId)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("Play animation: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.PlayActionTimeline(timelineId);
                Plugin.Log.Info($"Play animation: timeline 0x{timelineId:X} on '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"Play animation: target '{target.Name}' is not a tracked enemy");
    }

    private void ApplyPoseChangeOnTarget(byte modelState, byte animStateHi, byte animStateLo, ushort commitTimelineId)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("Apply pose: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.ApplyPoseChange(modelState, animStateHi, animStateLo, commitTimelineId);
                Plugin.Log.Info($"Apply pose: ModelState=0x{modelState:X2} AnimState=({animStateHi:X},{animStateLo:X}) CommitTimeline=0x{commitTimelineId:X} on '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"Apply pose: target '{target.Name}' is not a tracked enemy");
    }

    private void SetModelStateOnTarget(byte value)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("ModelState: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.SetModelState(value);
                Plugin.Log.Info($"ModelState: 0x{value:X2} on '{enemy.DisplayName}' (no commit)");
                return;
            }
        }
        Plugin.Log.Warning($"ModelState: target '{target.Name}' is not a tracked enemy");
    }

    private void SetModeAttributeFlagsOnTarget(byte value)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("ModeAttrFlags: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.SetModeAttributeFlags(value);
                Plugin.Log.Info($"ModeAttrFlags: 0x{value:X2} on '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"ModeAttrFlags: target '{target.Name}' is not a tracked enemy");
    }

    private void SetModelCharaIdOnTarget(int modelCharaId)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("ModelCharaId: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.SetModelCharaId(modelCharaId);
                Plugin.Log.Info($"ModelCharaId: {modelCharaId} on '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"ModelCharaId: target '{target.Name}' is not a tracked enemy");
    }

    private void SetTransformationIdOnTarget(short transformationId)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("TransformationId: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.SetTransformationId(transformationId);
                Plugin.Log.Info($"TransformationId: {transformationId} on '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"TransformationId: target '{target.Name}' is not a tracked enemy");
    }

    private void CastOnPlayerFromTarget(uint actionId, byte animationVariation)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("Cast: no target selected");
            return;
        }
        var player = plugin.Game.Player;
        if (player == null)
        {
            Plugin.Log.Warning("Cast: no local player");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.Cast(actionId, targetLocation: player.Position, targetId: player.GameObjectId, animationVariation: animationVariation);
                Plugin.Log.Info($"Cast: action 0x{actionId:X} (anim variation {animationVariation}) on player from '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"Cast: target '{target.Name}' is not a tracked enemy");
    }

    private void CastOnSelfFromTarget(uint actionId, byte animationVariation)
    {
        var target = Plugin.TargetManager.Target;
        if (target == null)
        {
            Plugin.Log.Warning("Cast: no target selected");
            return;
        }
        var targetId = target.GameObjectId;
        foreach (var enemy in plugin.Game.World.Children.OfType<SimEnemy>())
        {
            if ((ulong)enemy.GameObjectId == targetId)
            {
                enemy.Cast(actionId, targetLocation: enemy.Position, targetId: enemy.GameObjectId, animationVariation: animationVariation);
                Plugin.Log.Info($"Cast: action 0x{actionId:X} (anim variation {animationVariation}) on self from '{enemy.DisplayName}'");
                return;
            }
        }
        Plugin.Log.Warning($"Cast: target '{target.Name}' is not a tracked enemy");
    }

    // Dumps the BattleChara fields we suspect drive Omega-M's shield/weapon variant —
    // Mode/ModeParam, TransformationId, ModelContainer, DrawData weapon slots, Timeline.ModelState,
    // plus the DrawObject* address — so before/after snapshots can be diffed to find the field
    // that actually changes when boss appearance mutates.
    private static void DumpTargetFields()
    {
        var target = Plugin.TargetManager.Target;
        if (target == null) { Plugin.Log.Warning("DumpTarget: no target selected"); return; }

        var go = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
        if (go == null) { Plugin.Log.Warning("DumpTarget: target address is null"); return; }

        var name = target.Name.TextValue;
        if (string.IsNullOrEmpty(name)) name = "<unnamed>";

        var ch = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)go;

        Plugin.Log.Info($"=== DumpTarget '{name}' addr=0x{(nint)go:X} kind={target.ObjectKind} BaseId=0x{target.BaseId:X} ({target.BaseId}) ===");
        Plugin.Log.Info($"  Pos=({go->Position.X:F2},{go->Position.Y:F2},{go->Position.Z:F2}) Rot={go->Rotation:F3} Scale={go->Scale:F2}");
        Plugin.Log.Info($"  DrawObject*=0x{(nint)go->DrawObject:X}");
        Plugin.Log.Info($"  Mode={ch->Mode} ({(byte)ch->Mode}) ModeParam=0x{ch->ModeParam:X2} ({ch->ModeParam})");
        Plugin.Log.Info($"  TransformationId={ch->TransformationId} StatusLoopVfxId={ch->StatusLoopVfxId} Battalion={ch->Battalion} ShieldValue={ch->ShieldValue}");
        Plugin.Log.Info($"  ModelContainer: ModelCharaId={ch->ModelContainer.ModelCharaId} ModelSkeletonId={ch->ModelContainer.ModelSkeletonId} ModelCharaId_2={ch->ModelContainer.ModelCharaId_2} ModelSkeletonId_2={ch->ModelContainer.ModelSkeletonId_2}");
        Plugin.Log.Info($"  ModelContainer: ModelScaleId=0x{ch->ModelContainer.ModelScaleId:X2} ModeAttributeFlags=0x{ch->ModelContainer.ModeAttributeFlags:X2} UnscaledRadius={ch->ModelContainer.UnscaledRadius:F2}");
        Plugin.Log.Info($"  WeaponFlags=0x{ch->WeaponFlags:X2} ActorControlFlags=0x{ch->ActorControlFlags:X2}");
        Plugin.Log.Info($"  Timeline.ModelState=0x{ch->Timeline.ModelState:X2} AnimationState=[0x{ch->Timeline.AnimationState[0]:X2},0x{ch->Timeline.AnimationState[1]:X2}]");
        for (int s = 0; s < 3; s++)
        {
            ref var w = ref ch->DrawData.WeaponData[s];
            Plugin.Log.Info($"  DrawData.Weapon[{s}]: Id={w.ModelId.Id} Type={w.ModelId.Type} Variant={w.ModelId.Variant} Stain=({w.ModelId.Stain0},{w.ModelId.Stain1}) State=0x{w.State:X2} Flags1=0x{w.Flags1:X4} Flags2=0x{w.Flags2:X2} DrawObject*=0x{(nint)w.DrawObject:X}");
        }
        Plugin.Log.Info($"  DrawData.Flags1=0x{ch->DrawData.Flags1:X2} Flags2=0x{ch->DrawData.Flags2:X2}");
    }

    // Logs every object in the table sorted by distance from the local player. BaseId is
    // the underlying BNpcBase row for BNpcs (and the equivalent base id for other kinds);
    // Scale is read from the unsafe GameObject struct.
    private static void DumpNearbyObjects()
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) { Plugin.Log.Warning("DumpObjects: no local player"); return; }
        var origin = player.Position;

        var rows = new List<(float Dist, string Line)>();
        foreach (var obj in Plugin.ObjectTable)
        {
            var dx = obj.Position.X - origin.X;
            var dz = obj.Position.Z - origin.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name)) name = "<unnamed>";
            var scale = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address)->Scale;
            rows.Add((dist, $"  [{obj.ObjectKind,-12}] BaseId=0x{obj.BaseId:X} ({obj.BaseId}) dist={dist,7:F2} scale={scale,5:F2}  '{name}'"));
        }
        rows.Sort((a, b) => a.Dist.CompareTo(b.Dist));

        Plugin.Log.Info($"=== ObjectTable: {rows.Count} objects ===");
        foreach (var (_, line) in rows) Plugin.Log.Info(line);
    }

    // Lists every SharedGroup ILayoutInstance in the active layout with its
    // sgb path + world position. Used to verify which EObj scenery (TOP arena
    // tiles, the 1EA1A1 fixture, the Exit portal, Sigma ring spokes) is
    // LGB-baked vs. duty-director-runtime-spawned. Match the output against
    // ACT log positions to identify which acquirable instances exist.
    private static void DumpSharedGroups()
    {
        var rows = new List<(float Dist, string Line)>();
        var player = Plugin.ObjectTable.LocalPlayer;
        var origin = player?.Position ?? default;
        int total = LayoutQuery.EnumerateAll(p =>
        {
            var sg = (FFXIVClientStructs.FFXIV.Client.LayoutEngine.Group.SharedGroupLayoutInstance*)p;
            var pos = sg->Transform.Translation;
            var path = LayoutQuery.GetSgbPath(sg) ?? "(no resource handle)";
            var dx = pos.X - origin.X;
            var dz = pos.Z - origin.Z;
            var dist = MathF.Sqrt(dx * dx + dz * dz);
            var inst = (FFXIVClientStructs.FFXIV.Client.LayoutEngine.ILayoutInstance*)sg;
            rows.Add((dist, $"  dist={dist,7:F2} pos=({pos.X,8:F2},{pos.Y,7:F2},{pos.Z,8:F2}) active={inst->IsActive,-5} key=0x{inst->Id.InstanceKey:X8} sub=0x{inst->SubId:X8}  '{path}'"));
        });
        rows.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        Plugin.Log.Info($"=== SharedGroups in active layout: {total} ===");
        foreach (var (_, line) in rows) Plugin.Log.Info(line);
    }

    // Cycles SetState(N) across every live SimEventObject in the world, where
    // N increments each click. SGs gate sub-instance visibility on this state
    // field — we use this to find empirically which value activates a given
    // EObj's hidden visuals (e.g., the P5 Sigma tower ground circles).
    private static short eventObjectStateProbe;
    private void BumpEventObjectState()
    {
        eventObjectStateProbe++;
        int n = 0;
        foreach (var child in plugin.Game.World.Children)
        {
            if (child is not SimEventObject eo || !eo.IsAlive) continue;
            eo.SetState(eventObjectStateProbe);
            n++;
        }
        Plugin.Log.Info($"Bumped EObj state to {eventObjectStateProbe} on {n} SimEventObjects");
    }
#endif
}
