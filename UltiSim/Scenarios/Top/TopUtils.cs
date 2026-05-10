using System.Collections.Generic;
using UltiSim.Core.SimObjects;

public static class TopUtils
{
    // ── TOP P5 arena MapEffect sequences ─────────────────────────────────────
    // ACT log format: flags(32-bit)|index. Encoding: high16=State, low8=Flags.
    // Observed in TOP P5 clear pull; needs in-game verification once ZoneSession works.

    public static void InitTopArena(SimWorld world)
    {
        for (byte i = 1; i <= 8; i++)
        {
            world.Map.AddEffect(0x00040004, i); // hide eyes
            // world.Map.AddEffect(0x00010004, i); // hide eyes
            // world.Map.AddEffect(0x00080000, i); // clear?
            // world.Map.AddEffect(0x108A0000, i); // hide eyes
        }

        // for (byte i = 0x0C; i <= 0x13; i++)
        //     world.Map.AddEffect(0x00040004, i); // enable base arena elements
        world.Map.AddEffect(0x00020002, 0x00); // show death wall
    }

    public static IReadOnlyList<Waymark> TopWaymarks => WaymarkPresets.Ring(13.63f);
}
