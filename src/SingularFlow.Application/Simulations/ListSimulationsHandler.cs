namespace SingularFlow.Application.Simulations;

public sealed class ListSimulationsHandler
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    public const int MaximumPageSize = 100;

    private readonly ISimulationRepository _repository;

    public ListSimulationsHandler(
        ISimulationRepository repository)
    {
        _repository = repository;
    }

    public Task<PagedSimulationResult> HandleAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Page must be greater than or equal to one.");
        }

        if (pageSize < 1 ||
            pageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                $"Page size must be between 1 and " +
                $"{MaximumPageSize}.");
        }

        return _repository.ListAsync(
            page,
            pageSize,
            cancellationToken);
    }
}