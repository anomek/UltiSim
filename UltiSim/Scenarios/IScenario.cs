using System;
using System.Collections.Generic;
using UltiSim.Core;
using UltiSim.Core.SimObjects;

namespace UltiSim.Scenarios;

public interface IScenario
{
    string Name { get; }
    ScenarioOriginOverride? OriginOverride { get; }
    IReadOnlyList<uint> HiddenBaseIds => Array.Empty<uint>();
    IReadOnlyList<Waymark> Waymarks => Array.Empty<Waymark>();
    void Run(SimWorld world, PartyRole playerRole);
    void DrawSettings() { }
}

// When the active territory matches TerritoryId, Game.ResolveScenarioOrigin uses
// (X, Z) as the scenario origin instead of the player's current position.
// Y is taken from the player so spawned objects stay on the floor.
public sealed record ScenarioOriginOverride(uint TerritoryId, float X, float Z);
