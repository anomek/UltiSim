namespace UltiSim.Scenarios.Top.P5Delta;

public enum PlayerTetherAssignment { Auto, CloseAny, CloseInner, CloseOuter, FarAny, FarInner, FarOuter }
public enum TriOption { Auto, Yes, No }
public enum HelloWorldOption { Auto, Near, Far, No }

// User-controlled overrides for TopP5DeltaState's randomized fields. Bound by
// the scenario's settings UI; null/default values leave the field randomized at
// scenario start. The state ctor consumes this directly.
public sealed class TopP5DeltaStateOverrides
{
    public NorthSouth? EyeSpawn { get; set; }
    public Side? SwivelCannonSide { get; set; }
    public PlayerTetherAssignment TetherAssignment { get; set; }
    public TriOption Monitor { get; set; }
    public HelloWorldOption HelloWorld { get; set; }
    public TriOption BeyondDefence { get; set; }
}
