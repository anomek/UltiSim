using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.SimObjects;

namespace UltiSim.Scenarios;

public sealed record EightWayDirection(string Name, float RadiansFromNorth, int Index)
{
    public static readonly EightWayDirection N = new("N", 0f, 0);
    public static readonly EightWayDirection NE = new("NE", MathF.PI * 1f / 4f, 1);
    public static readonly EightWayDirection E = new("E", MathF.PI * 2f / 4f, 2);
    public static readonly EightWayDirection SE = new("SE", MathF.PI * 3f / 4f, 3);
    public static readonly EightWayDirection S = new("S", MathF.PI * 4f / 4f, 4);
    public static readonly EightWayDirection SW = new("SW", MathF.PI * 5f / 4f, 5);
    public static readonly EightWayDirection W = new("W", MathF.PI * 6f / 4f, 6);
    public static readonly EightWayDirection NW = new("NW", MathF.PI * 7f / 4f, 7);
    public static readonly EightWayDirection[] All = [N, NE, E, SE, S, SW, W, NW];

    public Placement Apply(Placement placement)
    {
        return placement.RotateAroundOrigin(RadiansFromNorth);
    }
    
    public Vector3 Apply(Vector3 placement)
    {
        return new Placement(placement, 0).RotateAroundOrigin(RadiansFromNorth).Position;
    }
    
    public Vector3? Apply(Vector3? placement)
    {
        return placement == null ? null : Apply(placement.Value);
    }
    
    public void Apply(AiMove move)
    {
        move.Rotate(RadiansFromNorth);
    }
    
    public EightWayDirection Flip()
    {
        return All[(Index + 4) % 8];
    }
}

public record Tower(Vector3 Position, int MinPlayers);

public class RoleList
{
    private static readonly Random Rng = new Random();

    private readonly IReadOnlyList<PartyRole> list;
    private readonly SimParty party;
    
    public SimParty Party => party;
    public PartyRole[] List => list.ToArray();

    public PartyRole this[int index] => list[index];

    public RoleList(SimParty party, IReadOnlyList<PartyRole> list)
    {
        this.party = party;
        this.list = list;
    }

    public static RoleList Random(SimParty party)
    {
        return Random(party, 8);
    }
    
    public static RoleList Random(SimParty party, int count)
    {
       HashSet<int> set = [];
       List<PartyRole> list = [];
       while (list.Count < count)
       {
           var next = Rng.Next(8);
           if (set.Add(next))
               list.Add((PartyRole)next);
       }
       return new RoleList(party, list);
    }

    public static RoleList AllExcept(SimParty party, params PartyRole[] roles)
    {
        var set = new HashSet<PartyRole>(roles);
        var list = Enum.GetValues<PartyRole>()
            .Where(role => !set.Contains(role))
            .Shuffle()
            .ToList();
        return new RoleList(party, list);
    }
    
    public RoleList Random(int count, params PartyRole[] except)
    {
        var exceptSet = new HashSet<PartyRole>(except);
        var pool = list.Where(r => !exceptSet.Contains(r)).ToList();
        var picked = new List<PartyRole>(count);
        while (picked.Count < count && pool.Count > 0)
        {
            var idx = Rng.Next(pool.Count);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return new RoleList(party, picked);
    }
    
    
    public List<TResult> ForEachPair<TResult>(Func<int, SimPartySlot, SimPartySlot, TResult> func)
    {
        return Enumerable.Range(0, list.Count / 2)
                         .Select(i => func(i, party.Get(list[2 * i])!, party.Get(list[2 * i + 1])!))
                         .ToList();
    }

    public List<TResult> ForEachPair<TResult>(Func<SimPartySlot, SimPartySlot, TResult> func)
    {
        return ForEachPair((i, p1, p2) => func(p1, p2));
    }
    
    public void ForEachPair(Action<int, SimPartySlot, SimPartySlot> action)
    {
        ForEachPair((i, p1, p2) =>
        {
            action(i, p1, p2);
            return 0;
        });
    }

    public void ForEach(Action<SimPartySlot> action)
    {
        foreach (var partyRole in list)
        {
            action(party.Get(partyRole)!);    
        }
    }

    public SimPartySlot? Get(int i)
    {
        return party.Get(list[i]);
    }
    
    public void Reorder(AiMove move)
    {
        move.Reorder(list.Select(role => (int)role).ToArray());
    }
}
