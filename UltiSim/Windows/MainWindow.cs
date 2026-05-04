using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using UltiSim.Core;
using UltiSim.Core.SimObjects;

namespace UltiSim.Windows;

public unsafe class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
#if DEBUG
    private string debugBNpcBaseIdText = "0x3D69";
    private string debugSpawnScaleText = "0";
    private string debugTimelineIdText = "0x53C";
#endif

    public MainWindow(Plugin plugin)
        : base("UltiSim##MainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        IsOpen = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawSpeedControl();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("##UltiSimTabs"))
        {
            if (ImGui.BeginTabItem("Scenarios"))
            {
                DrawScenariosTab();
                ImGui.EndTabItem();
            }
#if DEBUG
            if (ImGui.BeginTabItem("Debug"))
            {
                DrawDebugTab();
                ImGui.EndTabItem();
            }
#endif
            ImGui.EndTabBar();
        }
    }

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
            if (active)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                if (ImGui.Button($"x{x}")) game.EventTimeScale = x;
                ImGui.PopStyleColor();
            }
            else
            {
                if (ImGui.Button($"x{x}")) game.EventTimeScale = x;
            }
        }
    }

    private void DrawScenariosTab()
    {
        foreach (var scenario in plugin.Game.Scenarios)
        {
            ImGui.PushID(scenario.Name);
            if (ImGui.Button(scenario.Name))
            {
                plugin.Game.RunScenario(scenario);
            }
            ImGui.Indent();
            scenario.DrawSettings();
            ImGui.Unindent();
            ImGui.Spacing();
            ImGui.PopID();
        }
    }

#if DEBUG
    private void DrawDebugTab()
    {
        ImGui.TextUnformatted("Manual spawn");
        ImGui.Separator();
        ImGui.SetNextItemWidth(120);
        ImGui.InputText("BNpcBaseId", ref debugBNpcBaseIdText, 16);
        ImGui.SetNextItemWidth(80);
        ImGui.InputText("Scale (0 = default)", ref debugSpawnScaleText, 16);
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
                // Ad-hoc spawn outside any scenario — anchor to the player so
                // Offset=0 lands at our feet. Scenario runs overwrite this in
                // Game.RunScenarioInternal, so the stamp is non-leaking.
                var player = Plugin.ObjectTable.LocalPlayer;
                if (player != null) plugin.Game.World.ScenarioOrigin = player.Position;
                plugin.Game.World.SpawnEnemy(new EnemySpawnConfig(
                    BNpcBaseId: baseId,
                    Targetable: true,
                    Scale: scale));
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
        ImGui.TextUnformatted("Dump objects near player");
        ImGui.Separator();
        if (ImGui.Button("Dump"))
            DumpNearbyObjects();

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Reset"))
        {
            plugin.Game.Reset();
        }
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
        foreach (var enemy in plugin.Game.World.Enemies)
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
#endif
}
