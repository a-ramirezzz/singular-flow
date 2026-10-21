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

    public DateTimeOffset? ClaimedAtUtc { get; set; }

    public Guid? ClaimToken { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public Guid? SimulationId { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public string? FailureMessage { get; set; }
}