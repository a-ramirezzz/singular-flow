namespace SingularFlow.Application.Simulations;

public sealed record PagedSimulationResult(
    IReadOnlyList<SimulationSummaryResult> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages =>
        TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                TotalCount / (double)PageSize);
}