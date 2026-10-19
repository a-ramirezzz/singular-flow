using SingularFlow.Api.Contracts.Simulations;
using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Contracts.SimulationJobs;

internal static class SimulationJobContractMapper
{
    public static SimulationJobApiResponse ToApi(
        SimulationJobResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        RunSimulationApiRequest request = new(
            SingularTime: result.Request.SingularTime,
            ConcentrationExponent:
                result.Request.ConcentrationExponent,
            StartTime: result.Request.StartTime,
            EndTime: result.Request.EndTime,
            SampleCount: result.Request.SampleCount,
            SamplingMode: SimulationContractMapper.ToApiValue(
                result.Request.SamplingMode));

        return new SimulationJobApiResponse(
            Id: result.Id,
            Status: ToApiValue(result.Status),
            Request: request,
            CreatedAtUtc: result.CreatedAtUtc,
            ClaimedAtUtc: result.ClaimedAtUtc,
            FinishedAtUtc: result.FinishedAtUtc,
            SimulationId: result.SimulationId,
            FailureMessage: result.FailureMessage);
    }

    private static string ToApiValue(
        SimulationJobStatus status)
    {
        return status switch
        {
            SimulationJobStatus.Pending => "pending",
            SimulationJobStatus.Running => "running",
            SimulationJobStatus.Completed => "completed",
            SimulationJobStatus.Failed => "failed",
            SimulationJobStatus.Cancelled => "cancelled",

            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Simulation job status is not supported.")
        };
    }
}