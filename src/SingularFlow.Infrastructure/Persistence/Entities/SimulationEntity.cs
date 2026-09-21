using SingularFlow.Application.Simulations;

namespace SingularFlow.Infrastructure.Persistence.Entities;

public sealed class SimulationEntity
{
    public Guid Id { get; set; }

    public double SingularTime { get; set; }

    public double ConcentrationExponent { get; set; }

    public double StartTime { get; set; }

    public double EndTime { get; set; }

    public int SampleCount { get; set; }

    public SamplingMode SamplingMode { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<SimulationStateEntity> States { get; } =
        new List<SimulationStateEntity>();
}