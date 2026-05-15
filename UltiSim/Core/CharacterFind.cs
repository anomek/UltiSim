using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core.SimObjects;

namespace UltiSim.Core;

// Geometry and proximity queries over the active party. Accessed via SimParty.Find
// so scenarios can write world.Party.Find.InsideRect(...) etc. without pulling in
// a separate static class. Geometry is XZ-plane only (Y ignored). Rotation 0 → +Z
// (south); forward vector = (sin, 0, cos).
public sealed class CharacterFind<T> where T : IPositioned
{
    private readonly Func<IEnumerable<T>> source;

    internal CharacterFind(Func<IEnumerable<T>> source) => this.source = source;

    internal CharacterFind(List<T> source) => this.source = () => source;
    
    // Members whose XZ position is within `radius` of `center`. Alive only.
    public IReadOnlyList<T> InsideCircle(Vector3 center, float radius)
    {
        var rSq = radius * radius;
        var hits = new List<T>();
        foreach (var m in source())
        {
            var dx = m.Position.X - center.X;
            var dz = m.Position.Z - center.Z;
            if (dx * dx + dz * dz <= rSq) hits.Add(m);
        }
        return hits;
    }

    // Cone fired from `origin` facing `origin.Rotation`, half-angle `halfAngleRad`, max
    // range `length`. Members at exactly the origin count as inside. Alive only.
    public IReadOnlyList<T> InsideCone(Placement origin, float halfAngleRad, float length)
    {
        var lenSq = length * length;
        var forwardX = MathF.Sin(origin.Rotation);
        var forwardZ = MathF.Cos(origin.Rotation);
        var cosHalf = MathF.Cos(halfAngleRad);
        var hits = new List<T>();
        foreach (var m in source())
        {
            var dx = m.Position.X - origin.Position.X;
            var dz = m.Position.Z - origin.Position.Z;
            var distSq = dx * dx + dz * dz;
            if (distSq > lenSq) continue;
            if (distSq < 0.0001f) { hits.Add(m); continue; }
            var dist = MathF.Sqrt(distSq);
            var cos = (dx * forwardX + dz * forwardZ) / dist;
            if (cos >= cosHalf) hits.Add(m);
        }
        return hits;
    }

    // Rectangle with back edge at `origin`, extending `length` forward along
    // `origin.Rotation`, `2 * halfWidth` wide. Matches how line/rect AOEs are authored:
    // caster at the back, AOE projects forward. Alive only.
    public IReadOnlyList<T> InsideRect(Placement origin, float halfWidth, float length)
    {
        var forwardX = MathF.Sin(origin.Rotation);
        var forwardZ = MathF.Cos(origin.Rotation);
        // Right vector = forward rotated 90° CW under atan2(x, z) convention.
        var rightX = MathF.Cos(origin.Rotation);
        var rightZ = -MathF.Sin(origin.Rotation);
        var hits = new List<T>();
        foreach (var m in source())
        {
            var dx = m.Position.X - origin.Position.X;
            var dz = m.Position.Z - origin.Position.Z;
            var fwd = dx * forwardX + dz * forwardZ;
            if (fwd < 0f || fwd > length) continue;
            var side = dx * rightX + dz * rightZ;
            if (MathF.Abs(side) <= halfWidth) hits.Add(m);
        }
        return hits;
    }

    // Members OUTSIDE a centered rectangle. `origin` is the rect's center; the rect
    // extends `halfLength` along `origin.Rotation` in BOTH directions and `halfWidth`
    // perpendicular. Note this differs from InsideRect's back-edge-at-origin
    // convention, deliberately — the natural use case ("outside a safe band crossing
    // the arena") is centered.
    public IReadOnlyList<T> OutsideRect(Placement origin, float halfWidth, float halfLength)
    {
        var forwardX = MathF.Sin(origin.Rotation);
        var forwardZ = MathF.Cos(origin.Rotation);
        var rightX = MathF.Cos(origin.Rotation);
        var rightZ = -MathF.Sin(origin.Rotation);
        var hits = new List<T>();
        foreach (var m in source())
        {
            var dx = m.Position.X - origin.Position.X;
            var dz = m.Position.Z - origin.Position.Z;
            var fwd  = dx * forwardX + dz * forwardZ;
            var side = dx * rightX   + dz * rightZ;
            if (MathF.Abs(fwd) > halfLength || MathF.Abs(side) > halfWidth) hits.Add(m);
        }
        return hits;
    }

    // Plus-shaped AOE centered on `origin`: union of two perpendicular centered
    // rects. Each arm extends `halfLength` along its axis and `halfWidth`
    // perpendicular.
    public IReadOnlyList<T> InsideCross(Placement origin, float halfWidth, float halfLength)
    {
        var forwardX = MathF.Sin(origin.Rotation);
        var forwardZ = MathF.Cos(origin.Rotation);
        var rightX = MathF.Cos(origin.Rotation);
        var rightZ = -MathF.Sin(origin.Rotation);
        var hits = new List<T>();
        foreach (var m in source())
        {
            var dx = m.Position.X - origin.Position.X;
            var dz = m.Position.Z - origin.Position.Z;
            var fwd  = dx * forwardX + dz * forwardZ;
            var side = dx * rightX   + dz * rightZ;
            var inForwardArm = MathF.Abs(fwd)  <= halfLength && MathF.Abs(side) <= halfWidth;
            var inSideArm    = MathF.Abs(side) <= halfLength && MathF.Abs(fwd)  <= halfWidth;
            if (inForwardArm || inSideArm) hits.Add(m);
        }
        return hits;
    }

    // The member nearest to `from` on the XZ plane, optionally skipping one.
    public T? Closest(Vector3 from, T? exclude = default)
    {
        T? best = default;
        var bestDist = float.MaxValue;
        foreach (var c in source())
        {
            if (Equals(c, exclude)) continue;
            var dx = c.Position.X - from.X;
            var dz = c.Position.Z - from.Z;
            var d = dx * dx + dz * dz;
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }
    
    public T? Extreme(Vector3 from, bool closest, T? exclude = default)
    {
        return closest ? Closest(from, exclude) : Farest(from, exclude);
    }

    // The member farthest from `from` on the XZ plane, optionally skipping one.
    public T? Farest(Vector3 from, T? exclude = default)
    {
        T? worst = default;
        var worstDist = float.MinValue;
        foreach (var c in source())
        {
            if (Equals(c, exclude)) continue;
            var dx = c.Position.X - from.X;
            var dz = c.Position.Z - from.Z;
            var d = dx * dx + dz * dz;
            if (d > worstDist) { worstDist = d; worst = c; }
        }
        return worst;
    }

    // Up to `count` filled slots ordered by ascending XZ distance from `from`.
    public IReadOnlyList<T> ClosestN(Vector3 from, int count)
    {
        var entries = new List<(T c, float distSq)>(8);
        foreach (var c in source())
        {
            var dx = c.Position.X - from.X;
            var dz = c.Position.Z - from.Z;
            entries.Add((c, dx * dx + dz * dz));
        }
        entries.Sort((a, b) => a.distSq.CompareTo(b.distSq));
        var result = new List<T>(Math.Min(count, entries.Count));
        for (int i = 0; i < count && i < entries.Count; i++)
            result.Add(entries[i].c);
        return result;
    }

    // Picks one member at random from the `count` closest on the XZ plane.
    // Returns null when the pool is empty.
    public T? RandomClosestN(Vector3 from, int count)
    {
        var pool = ClosestN(from, count);
        return pool.Count == 0 ? default : pool[Random.Shared.Next(pool.Count)];
    }

    // Members hit by an action's AOE, derived from the Action sheet (CastType +
    // EffectRange + XAxisModifier). source.Position is the caster, source.Rotation
    // is the caster facing; for rects/cones the effective forward is
    // source.Rotation + omenRotate (matches SpawnCastOmen). targetLocation is
    // used for CastType 2 (circle-on-ground) as the AOE center; ignored for
    // source-anchored shapes. Cone half-angle isn't carried by the sheet — defaults
    // to 30° (60° wide cone); override per-action if a specific cone needs tighter
    // bounds.
    public IReadOnlyList<T> InsideActionAoe(uint actionId, Placement source,
        Vector3? targetLocation = null, float omenRotate = 0f, float coneHalfAngle = MathF.PI / 6f)
    {
        var actionSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (!actionSheet.TryGetRow(actionId, out var action))
        {
            Plugin.Log.Warning($"InsideActionAoe: action {actionId} not found");
            return Array.Empty<T>();
        }
        var range = (float)action.EffectRange;
        if (range <= 0f) return Array.Empty<T>();
        var halfWidth = action.XAxisModifier > 0 ? action.XAxisModifier * 0.5f : range;
        var forward = new Placement(source.Position, source.Rotation + omenRotate);
        return action.CastType switch
        {
            2     => InsideCircle(targetLocation ?? source.Position, range),
            3 or 8 or 13
                  => InsideCone(forward, coneHalfAngle, range),
            4 or 12
                  => InsideRect(forward, halfWidth, range),
            5     => InsideCircle(source.Position, range),
            6     => InsideCircle(source.Position, range), // donut — inner radius not in sheet
            10 or 11
                  => InsideRect(forward, halfWidth, range)
                         .Concat(InsideRect(new Placement(source.Position, forward.Rotation + MathF.PI / 2f), halfWidth, range))
                         .Distinct()
                         .ToList(),
            _ =>
                LogUnknownCastType(actionId, action.CastType),
        };
    }

    private static IReadOnlyList<T> LogUnknownCastType(uint actionId, byte castType)
    {
        Plugin.Log.Warning($"InsideActionAoe: action {actionId} has unsupported CastType {castType}");
        return Array.Empty<T>();
    }

    // Returns up to `count` members on the intended side of `src`. Right vector
    // under the atan2(x,z) convention is (-cos(rot), sin(rot)); `sideMul` is the
    // sign applied to the dot product to choose the preferred side (Side.Mul).
    // Shuffles each group independently, fills from the preferred side first,
    // then from the opposite side. Optionally skips one specific member.
    public IReadOnlyList<T> OnSideN(Placement src, int sideMul, int count = 2, T? exclude = default)
    {
        var rightX = -MathF.Cos(src.Rotation);
        var rightZ = MathF.Sin(src.Rotation);

        var onSide = new List<T>();
        var others = new List<T>();
        foreach (var m in source())
        {
            if (Equals(m, exclude)) continue;
            var dot = (m.Position.X - src.Position.X) * rightX + (m.Position.Z - src.Position.Z) * rightZ;
            (dot * sideMul < 0 ? onSide : others).Add(m);
        }
        Shuffle(onSide);
        Shuffle(others);

        var picked = new List<T>(count);
        foreach (var m in onSide) { picked.Add(m); if (picked.Count == count) return picked; }
        foreach (var m in others) { picked.Add(m); if (picked.Count == count) return picked; }
        return picked;
    }

    private static void Shuffle<TItem>(List<TItem> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
