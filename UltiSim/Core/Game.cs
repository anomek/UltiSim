using System;
using System.Collections.Generic;
using System.Numerics;
using UltiSim.Core.SimObjects;
using UltiSim.Scenarios;
using UltiSim.Scenarios.TopP5Delta;

namespace UltiSim.Core;

// High-level orchestrator: owns the World, holds the scenario catalog, drives
// the active scenario's lifecycle, and is the single entry point UI talks to.
public sealed class Game : IDisposable
{
    // EventObj for the duty Exit portal — hidden on every scenario start so the
    // teleport-out interactable doesn't sit inside the simulated arena.
    private const uint ExitObjectBaseId = 2000139;

    public EventScheduler Events { get; } = new();
    public SimWorld World { get; }
    public IReadOnlyList<IScenario> Scenarios { get; }

    // Multiplier applied only to the EventScheduler's delta. Intentionally does not
    // scale enemy/party/tether/status ticks so cast bars, animations, and movement
    // run at real time — only the timeline of scheduled events stretches/compresses.
    public float EventTimeScale { get; set; } = 1f;

    private IScenario? activeScenario;

    public Game()
    {
        World = new SimWorld(Events);
        Scenarios = new IScenario[]
        {
            new TopP5DeltaScenario(),
        };
    }

    public void RunScenario(IScenario scenario)
    {
        Plugin.Framework.Run(() => RunScenarioInternal(scenario));
    }

    private void RunScenarioInternal(IScenario scenario)
    {
        ResetInternal();

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
        {
            Plugin.Log.Warning("Game: no local player; aborting scenario start");
            return;
        }

        World.HideObject(ExitObjectBaseId);
        foreach (var baseId in scenario.HiddenBaseIds) World.HideObject(baseId);

        var origin = ResolveScenarioOrigin(scenario, player.Position);
        World.ScenarioOrigin = origin;
        World.PlaceWaymarks(scenario.Waymarks);

        var playerJob = player.ClassJob.RowId;
        PartyCreator.Populate(World.Party, World.Player, playerJob, origin);

        scenario.Run(World, World.Party.PlayerRole);
        activeScenario = scenario;
    }

    // Scenario origin = (X, Z) anchor for all scenario-relative offsets, with Y
    // taken from the player so spawns stay on the floor. If the scenario declares
    // an OriginOverride matching the active territory, use that fixed (X, Z);
    // otherwise snapshot the player's current position. Orientation is intentionally
    // ignored — offsets are interpreted in world axes (+X east, +Z south) so the
    // arena layout doesn't drift if the player turns.
    private static Vector3 ResolveScenarioOrigin(IScenario scenario, Vector3 playerPosition)
    {
        var ovr = scenario.OriginOverride;
        if (ovr != null && Plugin.ClientState.TerritoryType == ovr.TerritoryId)
            return new Vector3(ovr.X, playerPosition.Y, ovr.Z);
        return playerPosition;
    }

    public void Tick(float deltaSeconds)
    {
        Events.Tick(deltaSeconds * EventTimeScale);
        World.Tick(deltaSeconds);
    }

    public void Reset() => Plugin.Framework.Run(ResetInternal);

    private void ResetInternal()
    {
        activeScenario = null;
        Events.Clear();
        World.Reset();
    }

    public void Dispose()
    {
        Plugin.Framework.Run(() =>
        {
            activeScenario = null;
            Events.Clear();
            World.Dispose();
        });
    }
}
