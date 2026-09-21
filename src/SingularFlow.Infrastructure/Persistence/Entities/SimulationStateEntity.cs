namespace SingularFlow.Infrastructure.Persistence.Entities;

public sealed class SimulationStateEntity
{
    public long Id { get; set; }

    public Guid SimulationId { get; set; }

    public int Sequence { get; set; }

    public double Time { get; set; }

    public double RemainingTime { get; set; }

    public double RadialLength { get; set; }

    public double AxialLength { get; set; }

    public double AngularVelocityScale { get; set; }

    public double RadialVelocityScale { get; set; }

    public double CoreVolumeScale { get; set; }

    public double CoreEnergyScale { get; set; }

    public SimulationEntity Simulation { get; set; } = null!;
}