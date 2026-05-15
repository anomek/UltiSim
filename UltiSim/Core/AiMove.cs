using System;
using System.Numerics;

namespace UltiSim.Core;

// Holds 8 scenario-local XZ positions (one per party slot). Null entries mean
// "no movement for that slot." Fluent transforms (MultiplyX, Reorder) let
// position functions adjust for eye-spawn and tether-order before handing off
// to AiManager, which converts each non-null entry to world space and drives movement.
public sealed class AiMove
{
    private readonly Vector2?[] positions = new Vector2?[8];

    public static AiMove Single(int index, Vector2? position)
    {
        var positions = new Vector2?[8];
        positions[index] = position;
        return new AiMove(positions);
    }
    
    public AiMove(params Vector2?[] initialPositions)
    {
        for (var i = 0; i < positions.Length && i < initialPositions.Length; i++)
        {
            positions[i] = initialPositions[i];
        }
    }

    public Vector2? this[int i] => positions[i];
    
    public AiMove Apply(params Action<AiMove>[] actions)
    {
        foreach (var action in actions)
            action(this);
        return this;
    }
    
    public void AddX(int i, float add)
    {
        if (positions[i] is { } v)
            positions[i] = v with { X = v.X + add };
    }
    
    public void AddY(int i, int add)
    {
        if (positions[i] is { } v)
            positions[i] = v with { Y = v.Y + add };
    }

    public AiMove MultiplyX(float mul)
    {
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] is { } v)
                positions[i] = v with { X = v.X * mul };
        return this;
    }
    
    public AiMove MultiplyY(float mul)
    {
        for (int i = 0; i < positions.Length; i++)
            MultiplyY(i, mul);
        return this;
    }
    
    public void MultiplyY(int i, float mul)
    {
        if (positions[i] is { } v)
            positions[i] = v with { Y = v.Y * mul };
    }
    
    public void Multiply(float mul)
    {
        MultiplyX(mul);
        MultiplyY(mul);
    }

    // Scatter: for each source index i, write its value to destination `order[i]`.
    // Reads naturally as "the value currently authored as i belongs at slot order[i]."
    // Use this when you have a permutation expressed as `dstByIdx[srcIdx] = dst`.
    // Example: RoleList.Reorder passes [Order[0]..Order[7]] so authored tether-index i
    // moves to slot (int)role — i.e., the chain ends in role-indexed positions.
    public AiMove Reorder(int[] order)
    {
        var old = (Vector2?[])positions.Clone();
        for (int i = 0; i < positions.Length; i++)
            positions[order[i]] = old[i];
        return this;
    }

    // Gather: for each destination slot i, pull from source index `order[i]`.
    // Reads naturally as "slot i should contain the value originally at order[i]."
    // Use this when you have a permutation expressed as `srcByIdx[dstIdx] = src`.
    // Example: WaveCannon authoring in clockspot order with ClockSpots[tether]=clockspot
    // → ReorderReverse pulls `positions[tether] = old[ClockSpots[tether]]`.
    public AiMove ReorderReverse(int[] order)
    {
        var old = (Vector2?[])positions.Clone();
        for (int i = 0; i < positions.Length; i++)
            positions[i] = old[order[i]];
        return this;
    }

    public AiMove Swap(int a, int b)
    {
        (positions[a], positions[b]) = (positions[b], positions[a]);
        return this;
    }
    
    public AiMove SwapPair(int a, int b)
    {
        Swap(2 * a, 2 * b);
        Swap(2 * a + 1, 2 * b + 1);
        return this;
    }

    public AiMove Rotate(float radiansFromNorth)
    {
        var cos = MathF.Cos(radiansFromNorth);
        var sin = MathF.Sin(radiansFromNorth);
        for (int i = 0; i < positions.Length; i++)
            if (positions[i] is { } v)
                positions[i] = new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        return this;
    }

    // Cyclic gather: each slot i pulls from offset (i + count), wrapping around.
    // Equivalent to ReorderReverse with order [(0+count)%n, (1+count)%n, ...].
    // Positive count rotates the *view* forward — e.g. OffsetOrder(1) turns
    // [A,B,C,D,E,F,G,H] into [B,C,D,E,F,G,H,A]. Negative counts are normalized
    // through the modulo so callers can pass any int.
    public AiMove OffsetOrder(int count)
    {
        var old = (Vector2?[])positions.Clone();
        var len = positions.Length;
        for (var i = 0; i < len; i++)
            positions[i] = old[((i + count) % len + len) % len];
        return this;
    }

}
