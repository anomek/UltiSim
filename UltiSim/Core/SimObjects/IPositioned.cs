using System.Numerics;

namespace UltiSim.Core.SimObjects;

// Position is scenario-space (relative to SimWorld.ScenarioOrigin). Use
// SimWorld.ToWorld(...) if you need world coords for native interop.
// Rotation is absolute radians (independent of scenario origin).
public interface IPositioned
{
    Vector3 Position { get; }
    float Rotation { get; }
    Placement Placement => new(Position, Rotation);
}
