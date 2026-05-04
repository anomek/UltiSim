namespace UltiSim.Scenarios.TopP5Delta;

// User-controlled overrides for TopP5DeltaState's randomized fields. Bound by
// the scenario's settings UI; null/false leaves the field randomized at scenario
// start. The state ctor consumes this directly.
public sealed class TopP5DeltaStateOverrides
{
    public NorthSouth? EyeSpawn { get; set; }
    public Side? SwivelCannonSide { get; set; }
    public bool ForcePlayerOnMonitor { get; set; }
}
