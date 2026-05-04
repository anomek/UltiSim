using System.Numerics;

namespace UltiSim.Core;

public enum WaymarkSlot : int
{
    A = 0,
    B = 1,
    C = 2,
    D = 3,
    One = 4,
    Two = 5,
    Three = 6,
    Four = 7,
}

public sealed record Waymark(WaymarkSlot Slot, Vector3 Offset);
