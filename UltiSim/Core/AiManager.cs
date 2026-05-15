using System;
using System.Collections.Generic;
using System.Numerics;
using UltiSim.Core.SimObjects;

namespace UltiSim.Core;

// Drives slot-ordered party movement from a scenario's position functions.
// Owns jitter, run speed, and event scheduling. Position functions return an
// AiMove whose entries are scenario-local XZ coords — same space MoveTo
// consumes, so AiManager forwards them as-is. Eye-spawn flip and slot
// reordering are handled inside the AiMove before it reaches here.
public sealed class AiManager
{
    private const float RunSpeed = 6f;
    private const float DefaultJitter = 0.3f;

    private readonly SimWorld world;
    private readonly Random rng = new();

    public AiManager(SimWorld world)
    {
        this.world = world;
    }

    // Schedule a slot-move at `time`. `positions` is evaluated at fire-time;
    // null entries in the returned AiMove are skipped (no movement that slot).
    // When `arrivalTime` > 0, each member's MoveTo is deferred so they arrive at
    // the destination at scenario-time `arrivalTime` (running at RunSpeed). If a
    // member is too far to make it in time, it falls back to leaving immediately
    // and arriving late.
    public void Move(float time, Func<AiMove> positions, float jitter = DefaultJitter, float arrivalTime = 0f)
    {
        world.Events.Add(time, () =>
        {
            var move = positions();
            for (int i = 0; i < 8; i++)
            {
                if (move[i] is not { } local) continue;
                var member = world.Party.Get(i);
                if (member is not { IsAlive: true }) continue;
                var target = Jitter(new Vector3(local.X, 0f, local.Y), jitter);

                if (arrivalTime > 0f)
                {
                    var dx = target.X - member.Position.X;
                    var dz = target.Z - member.Position.Z;
                    var delay = arrivalTime - time - MathF.Sqrt(dx * dx + dz * dz) / RunSpeed;
                    if (delay > 0f)
                    {
                        world.Events.Add(delay, () => member.MoveTo(target));
                        continue;
                    }
                }
                member.MoveTo(target);
            }
        });
    }

    public void Automarker(float time, Func<Dictionary<PartyRole, Sign>> mapping)
    {
        world.Events.Add(time, () =>
        {
            Markings.ClearAll();
            foreach (var (role, sign) in mapping())
                if (world.Party.Get(role) is { IsAlive: true } member)
                    Markings.Set(sign, member.GameObjectId);
        });
    }

    private Vector3 Jitter(Vector3 target, float radius)
    {
        var theta = rng.NextDouble() * 2.0 * Math.PI;
        var r = radius * MathF.Sqrt((float)rng.NextDouble());
        return new Vector3(
            target.X + r * MathF.Cos((float)theta),
            target.Y,
            target.Z + r * MathF.Sin((float)theta));
    }
}
