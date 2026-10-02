using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Infrastructure.Persistence.Entities;

public sealed class SimulationJobEntity
{
    public Guid Id { get; set; }

    public double SingularTime { get; set; }

    public double ConcentrationExponent { get; set; }

    public double StartTime { get; set; }

    public double EndTime { get; set; }

    public int SampleCount { get; set; }

    public SamplingMode SamplingMode { get; set; }

    public SimulationJobStatus Status { get; set; } =
        SimulationJobStatus.Pending;

    public DateTimeOffset CreatedAtUtc { get; set; }
}