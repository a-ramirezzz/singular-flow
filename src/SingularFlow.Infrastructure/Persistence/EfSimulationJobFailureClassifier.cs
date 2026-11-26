using Npgsql;

using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobFailureClassifier :
    ISimulationJobFailureClassifier
{
    private static readonly HashSet<string> RetryableSqlStates =
    [
        "40001",
        "40P01",
        "55P03",
        "53300",
        "57P01",
        "57P02",
        "57P03"
    ];

    public SimulationJobFailureClassification Classify(
        Exception exception,
        SimulationJobFailurePhase phase)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (phase != SimulationJobFailurePhase.Persistence)
        {
            return SimulationJobFailureClassification.NonRetryable;
        }

        List<Exception> chain = [];
        HashSet<Exception> visited = new(ReferenceEqualityComparer.Instance);

        for (Exception? current = exception;
             current is not null && visited.Add(current);
             current = current.InnerException)
        {
            chain.Add(current);
        }

        if (chain.Any(current => current is OperationCanceledException))
        {
            return SimulationJobFailureClassification.NonRetryable;
        }

        foreach (Exception current in chain)
        {
            if (current is PostgresException postgresException &&
                RetryableSqlStates.Contains(postgresException.SqlState))
            {
                return SimulationJobFailureClassification.Retryable;
            }

            if (current is TimeoutException ||
                current is NpgsqlException { IsTransient: true })
            {
                return SimulationJobFailureClassification.Retryable;
            }
        }

        return SimulationJobFailureClassification.NonRetryable;
    }
}