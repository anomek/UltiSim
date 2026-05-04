using Dalamud.Bindings.ImGui;

namespace UltiSim.Scenarios.TopP5Delta;

public sealed class TopP5DeltaSettingsWindow
{
    public TopP5DeltaStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        var eyeMode = Overrides.EyeSpawn switch
        {
            NorthSouth.North => 1,
            NorthSouth.South => 2,
            _ => 0,
        };
        ImGui.TextUnformatted("Eye spawn:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##eye", eyeMode == 0)) Overrides.EyeSpawn = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("North##eye", eyeMode == 1)) Overrides.EyeSpawn = NorthSouth.North;
        ImGui.SameLine();
        if (ImGui.RadioButton("South##eye", eyeMode == 2)) Overrides.EyeSpawn = NorthSouth.South;

        var swivelMode = Overrides.SwivelCannonSide == null ? 0
            : Overrides.SwivelCannonSide == Side.Left ? 1
            : 2;
        ImGui.TextUnformatted("Swivel Cannon:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##swivel", swivelMode == 0)) Overrides.SwivelCannonSide = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Left##swivel", swivelMode == 1)) Overrides.SwivelCannonSide = Side.Left;
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##swivel", swivelMode == 2)) Overrides.SwivelCannonSide = Side.Right;

        var force = Overrides.ForcePlayerOnMonitor;
        if (ImGui.Checkbox("Force player on monitor", ref force)) Overrides.ForcePlayerOnMonitor = force;
    }
}
