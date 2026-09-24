namespace SingularFlow.Api.Contracts.Simulations;

public sealed record PagedSimulationsResponse(
    IReadOnlyList<SimulationSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);