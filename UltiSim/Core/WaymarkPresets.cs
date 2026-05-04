using System;
using System.Numerics;

namespace UltiSim.Core;

public static class WaymarkPresets
{
    // Ring of A,1,B,2,C,3,D,4 spaced 45° apart, A at north going clockwise.
    // Angle convention matches scenario spawn helpers: offset = (sin(a)*r, 0, cos(a)*r),
    // so a=π is north (-Z), a=π/2 is east (+X), a=0 is south (+Z), a=-π/2 is west.
    // Clockwise from north therefore steps angle by -π/4 each slot.
    public static Waymark[] Ring(float radius)
    {
        var slots = new[]
        {
            WaymarkSlot.A, WaymarkSlot.One,
            WaymarkSlot.B, WaymarkSlot.Two,
            WaymarkSlot.C, WaymarkSlot.Three,
            WaymarkSlot.D, WaymarkSlot.Four,
        };
        var ring = new Waymark[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var angle = MathF.PI - i * (MathF.PI / 4f);
            var offset = new Vector3(radius * MathF.Sin(angle), 0, radius * MathF.Cos(angle));
            ring[i] = new Waymark(slots[i], offset);
        }
        return ring;
    }
}
