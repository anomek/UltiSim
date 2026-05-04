using System.Numerics;

namespace UltiSim.Core.SimObjects;

public interface IPositioned
{
    Vector3 Position { get; }
    float Rotation { get; }
    float HitboxRadius { get; }
}
