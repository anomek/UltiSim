using System.Collections.Generic;
using System.Numerics;
using UltiSim.Core;
using UltiSim.Core.SimObjects;
using UltiSim.Scenarios.Top.P5Delta;
using static UltiSim.Scenarios.Top.TopConstants;

public record TopUtils(SimWorld World)
{
    public void InitTopArena()
    {
        for (byte i = 1; i <= 8; i++)
        {
            World.Map.AddEffect(0x00040004, i); // hide eyes
        }

        World.Map.AddEffect(0x00020002, 0x00); // show death wall
    }
    
    public void CheckHelloWorldDeath()
    {
        foreach (var member in World.Party.AllMembers())
        {
            if (member.IsAlive) continue;
            if (member.HasStatus(StatusId.HelloNearWorld))
            {
                member.RemoveStatus(StatusId.HelloNearWorld);
                HelloWorldFail(member.Position);
            }
            else if (member.HasStatus(StatusId.HelloFarWorld))
            {
                member.RemoveStatus(StatusId.HelloFarWorld);
                HelloWorldFail(member.Position);
            }
        }
    }
    
    public void HelloWorldFail(Vector3 pos)
    {
        var helper = World.SpawnEnemy(new EnemySpawnConfig(
                                          BNpcBaseId: BNpcBaseId.OmegaHelper,
                                          Targetable: false,
                                          EnemyList: EnemyListMode.Never,
                                          Placement: new Placement(pos, 0f),
                                          Lifetime: TopP5DeltaConstants.Duration.MonitorHelperLifetime));
        helper?.Cast(ActionId.HelloWorldFail);
        World.Party.WipeAllPlayers("Hello World Fail");
    }

    public static IReadOnlyList<Waymark> TopWaymarks => WaymarkPresets.Ring(13.63f);
}
