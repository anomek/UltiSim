using System;

namespace UltiSim.Core;

internal static class MathUtil
{
    // Wraps any rotation into the half-open range (-π, +π]. The game stores
    // facings unbounded; some math (atan2, accumulated rotates from MoveTo)
    // can drift outside that band and trip animation/state code that assumes
    // it. Normalize on every write rather than relying on every caller.
    public static float NormalizeRotation(float r)
    {
        r = MathF.IEEERemainder(r, 2f * MathF.PI);
        if (r <= -MathF.PI) r = MathF.PI;
        return r;
    }
}
