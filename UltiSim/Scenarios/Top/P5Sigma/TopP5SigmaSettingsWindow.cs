using Dalamud.Bindings.ImGui;

namespace UltiSim.Scenarios.Top.P5Sigma;

public sealed class TopP5SigmaSettingsWindow
{
    public TopP5SigmaStateOverrides Overrides { get; } = new();

    public void Draw()
    {
        if (ImGui.Button("Auto")) ResetAll();
        DrawNewNorthA();
        DrawCloseFar();
        DrawTowerNorthFlip();
        DrawNewNorthB();
        DrawSpinnerRotation();
        DrawOmegaFForm();
    }

    private void ResetAll()
    {
        Overrides.NewNorthA = null;
        Overrides.CloseFarTether = null;
        Overrides.TowerNorthFlip = TriOption.Auto;
        Overrides.NewNorthB = null;
        Overrides.SpinnerRotation = null;
        Overrides.OmegaFForm = null;
    }

    private void DrawNewNorthA()
    {
        ImGui.TextUnformatted("New north (A — sigma resolve):");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##northA", Overrides.NewNorthA == null)) Overrides.NewNorthA = null;
        foreach (var d in EightWayDirection.All)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{d.Name}##northA", Overrides.NewNorthA == d)) Overrides.NewNorthA = d;
        }
    }

    private void DrawCloseFar()
    {
        var v = Overrides.CloseFarTether;
        ImGui.TextUnformatted("Tether range:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##cf",  v == null))            Overrides.CloseFarTether = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Close##cf", v == CloseFar.Close))  Overrides.CloseFarTether = CloseFar.Close;
        ImGui.SameLine();
        if (ImGui.RadioButton("Far##cf",   v == CloseFar.Far))    Overrides.CloseFarTether = CloseFar.Far;
    }

    private void DrawTowerNorthFlip()
    {
        var v = Overrides.TowerNorthFlip;
        ImGui.TextUnformatted("Tower-north flip:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##flip", v == TriOption.Auto)) Overrides.TowerNorthFlip = TriOption.Auto;
        ImGui.SameLine();
        if (ImGui.RadioButton("Yes##flip",  v == TriOption.Yes))  Overrides.TowerNorthFlip = TriOption.Yes;
        ImGui.SameLine();
        if (ImGui.RadioButton("No##flip",   v == TriOption.No))   Overrides.TowerNorthFlip = TriOption.No;
    }

    private void DrawNewNorthB()
    {
        ImGui.TextUnformatted("New north (B — second half):");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##northB", Overrides.NewNorthB == null)) Overrides.NewNorthB = null;
        foreach (var d in EightWayDirection.All)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{d.Name}##northB", Overrides.NewNorthB == d)) Overrides.NewNorthB = d;
        }
    }

    private void DrawSpinnerRotation()
    {
        var v = Overrides.SpinnerRotation;
        ImGui.TextUnformatted("Spinner rotation:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##spin", v == null))                       Overrides.SpinnerRotation = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("CW##spin",   v == Rotation.Clockwise))         Overrides.SpinnerRotation = Rotation.Clockwise;
        ImGui.SameLine();
        if (ImGui.RadioButton("CCW##spin",  v == Rotation.CounterClockwise))  Overrides.SpinnerRotation = Rotation.CounterClockwise;
    }

    private void DrawOmegaFForm()
    {
        var v = Overrides.OmegaFForm;
        ImGui.TextUnformatted("Omega-F form:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Auto##form",       v == null))                  Overrides.OmegaFForm = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("Leg blades##form", v == OmegaFForm.LegBlades))  Overrides.OmegaFForm = OmegaFForm.LegBlades;
        ImGui.SameLine();
        if (ImGui.RadioButton("Staff##form",      v == OmegaFForm.Staff))      Overrides.OmegaFForm = OmegaFForm.Staff;
    }
}
