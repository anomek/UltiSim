using Dalamud.Bindings.ImGui;

namespace UltiSim.Scenarios.Top.P5Delta;

public sealed class TopP5DeltaSettingsWindow
{
    public TopP5DeltaStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        DrawEyeSpawn();
        DrawSwivelCannon();
        DrawTetherAssignment();

        var closeOnly = Overrides.TetherAssignment is
            PlayerTetherAssignment.FarAny or
            PlayerTetherAssignment.FarInner or
            PlayerTetherAssignment.FarOuter;
        var bdOnly = closeOnly || Overrides.TetherAssignment == PlayerTetherAssignment.CloseOuter;

        if (closeOnly) ImGui.BeginDisabled();
        DrawMonitor();
        DrawHelloWorld();
        if (closeOnly) ImGui.EndDisabled();

        if (bdOnly) ImGui.BeginDisabled();
        DrawBeyondDefence();
        if (bdOnly) ImGui.EndDisabled();
    }

    private void ResetAll()
    {
        Overrides.EyeSpawn = null;
        Overrides.SwivelCannonSide = null;
        Overrides.TetherAssignment = PlayerTetherAssignment.Auto;
        Overrides.Monitor = TriOption.Auto;
        Overrides.HelloWorld = HelloWorldOption.Auto;
        Overrides.BeyondDefence = TriOption.Auto;
    }

    private void DrawEyeSpawn()
    {
        var mode = 0;
        if (Overrides.EyeSpawn == NorthSouth.North) mode = 1;
        if (Overrides.EyeSpawn == NorthSouth.North) mode = 2;
        ImGui.TextUnformatted("Eye spawn:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##eye",  mode == 0)) Overrides.EyeSpawn = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("North##eye", mode == 1)) Overrides.EyeSpawn = NorthSouth.North;
        ImGui.SameLine();
        if (ImGui.RadioButton("South##eye", mode == 2)) Overrides.EyeSpawn = NorthSouth.South;
    }

    private void DrawSwivelCannon()
    {
        var mode = Overrides.SwivelCannonSide == null ? 0
            : Overrides.SwivelCannonSide == Side.Left ? 1
            : 2;
        ImGui.TextUnformatted("Swivel Cannon:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##swivel",  mode == 0)) Overrides.SwivelCannonSide = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Left##swivel",  mode == 1)) Overrides.SwivelCannonSide = Side.Left;
        ImGui.SameLine();
        if (ImGui.RadioButton("Right##swivel", mode == 2)) Overrides.SwivelCannonSide = Side.Right;
    }

    private void DrawTetherAssignment()
    {
        var t = Overrides.TetherAssignment;
        ImGui.TextUnformatted("Tether:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##tether",        t == PlayerTetherAssignment.Auto))       Overrides.TetherAssignment = PlayerTetherAssignment.Auto;
        ImGui.SameLine();
        if (ImGui.RadioButton("Close any##tether",   t == PlayerTetherAssignment.CloseAny))   Overrides.TetherAssignment = PlayerTetherAssignment.CloseAny;
        ImGui.SameLine();
        if (ImGui.RadioButton("Close inner##tether", t == PlayerTetherAssignment.CloseInner)) Overrides.TetherAssignment = PlayerTetherAssignment.CloseInner;
        ImGui.SameLine();
        if (ImGui.RadioButton("Close outer##tether", t == PlayerTetherAssignment.CloseOuter)) Overrides.TetherAssignment = PlayerTetherAssignment.CloseOuter;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far any##tether",     t == PlayerTetherAssignment.FarAny))     Overrides.TetherAssignment = PlayerTetherAssignment.FarAny;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far inner##tether",   t == PlayerTetherAssignment.FarInner))   Overrides.TetherAssignment = PlayerTetherAssignment.FarInner;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far outer##tether",   t == PlayerTetherAssignment.FarOuter))   Overrides.TetherAssignment = PlayerTetherAssignment.FarOuter;
    }

    private void DrawMonitor()
    {
        var m = Overrides.Monitor;
        ImGui.TextUnformatted("Monitor:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##mon", m == TriOption.Auto)) Overrides.Monitor = TriOption.Auto;
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##mon",  m == TriOption.Yes))  Overrides.Monitor = TriOption.Yes;
        ImGui.SameLine();
        if (ImGui.RadioButton("No##mon",   m == TriOption.No))   Overrides.Monitor = TriOption.No;
    }

    private void DrawHelloWorld()
    {
        var h = Overrides.HelloWorld;
        ImGui.TextUnformatted("Hello World:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##hw", h == HelloWorldOption.Auto)) Overrides.HelloWorld = HelloWorldOption.Auto;
        ImGui.SameLine();
        if (ImGui.RadioButton("Near##hw", h == HelloWorldOption.Near)) Overrides.HelloWorld = HelloWorldOption.Near;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far##hw",  h == HelloWorldOption.Far))  Overrides.HelloWorld = HelloWorldOption.Far;
        ImGui.SameLine();
        if (ImGui.RadioButton("No##hw",   h == HelloWorldOption.No))   Overrides.HelloWorld = HelloWorldOption.No;
    }

    private void DrawBeyondDefence()
    {
        var b = Overrides.BeyondDefence;
        ImGui.TextUnformatted("Beyond Defence:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##bd", b == TriOption.Auto)) Overrides.BeyondDefence = TriOption.Auto;
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##bd",  b == TriOption.Yes))  Overrides.BeyondDefence = TriOption.Yes;
        ImGui.SameLine();
        if (ImGui.RadioButton("No##bd",   b == TriOption.No))   Overrides.BeyondDefence = TriOption.No;
    }
}
