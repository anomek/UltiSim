using System;
using System.Numerics;

namespace UltiSim.Core;

public readonly record struct Placement(Vector3 Position, float Rotation)
{
    // Advances Position by `distance` units along the facing direction (Rotation 0 → +Z).
    // Negative distance moves backward.
    public Placement MoveForward(float distance) =>
        this with { Position = Position + new Vector3(MathF.Sin(Rotation), 0f, MathF.Cos(Rotation)) * distance };

    // Rotates Position around world origin in the XZ plane by `angle` radians
    // and advances Rotation by the same amount.
    public Placement RotateAroundOrigin(float angle)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        return this with
        {
            Position = new Vector3(
                Position.X * cos - Position.Z * sin,
                Position.Y,
                Position.X * sin + Position.Z * cos),
            Rotation = Rotation - angle
        };
    }

    public Vector2 Position2 => new(Position.X, Position.Z);

    public Placement LocalToGlobal(Vector3 origin)
    {
        return new Placement(Position + origin, Rotation);
    }

    public Placement GlobalToLocal(Vector3 origin)
    {
        return new Placement(Position - origin, Rotation);
    }
}
