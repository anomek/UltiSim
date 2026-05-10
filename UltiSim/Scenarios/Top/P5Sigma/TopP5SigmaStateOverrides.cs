namespace UltiSim.Scenarios.Top.P5Sigma;

// Scenario-local enums consumed by the Overrides + State pair. Auto = randomize.
public enum CloseFar { Close, Far }
public enum Rotation { Clockwise, CounterClockwise }
public enum OmegaFForm { LegBlades, Staff }
public enum TriOption { Auto, Yes, No }

// User-controlled overrides for TopP5SigmaState's randomized fields. Bound by
// the scenario's settings UI; null/Auto values leave the field randomized at
// scenario start. The state ctor consumes this directly.
public sealed class TopP5SigmaStateOverrides
{
    public EightWayDirection? NewNorthA { get; set; }
    public CloseFar? CloseFarTether { get; set; }
    public TriOption TowerNorthFlip { get; set; }
    public EightWayDirection? NewNorthB { get; set; }
    public Rotation? SpinnerRotation { get; set; }
    public OmegaFForm? OmegaFForm { get; set; }
}
