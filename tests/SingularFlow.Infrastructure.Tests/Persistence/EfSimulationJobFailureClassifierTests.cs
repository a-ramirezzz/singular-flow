using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Infrastructure.Persistence;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobFailureClassifierTests
{
    private readonly ISimulationJobFailureClassifier _classifier =
        CreateClassifier();

    [Fact]
    public void Classify_NullException_ThrowsArgumentNullException()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => _classifier.Classify(null!, SimulationJobFailurePhase.Persistence));

        Assert.Equal("exception", exception.ParamName);
    }

    [Theory]
    [InlineData(SimulationJobFailurePhase.Execution)]
    [InlineData(SimulationJobFailurePhase.Persistence)]
    public void Classify_Cancellation_IsNonRetryable(
        SimulationJobFailurePhase phase)
    {
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            _classifier.Classify(new OperationCanceledException(), phase));
    }

    [Fact]
    public void Classify_ExecutionTimeout_IsNonRetryable()
    {
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            _classifier.Classify(
                new TimeoutException(),
                SimulationJobFailurePhase.Execution));
    }

    [Fact]
    public void Classify_ExecutionTransientNpgsqlFailure_IsNonRetryable()
    {
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            _classifier.Classify(
                CreateTransientNpgsqlException(),
                SimulationJobFailurePhase.Execution));
    }

    [Fact]
    public void Classify_PersistenceTimeout_IsRetryable()
    {
        Assert.Equal(
            SimulationJobFailureClassification.Retryable,
            _classifier.Classify(
                new TimeoutException(),
                SimulationJobFailurePhase.Persistence));
    }

    [Fact]
    public void Classify_TransientNpgsqlFailure_IsRetryable()
    {
        NpgsqlException exception = CreateTransientNpgsqlException();
        Assert.True(exception.IsTransient);

        Assert.Equal(
            SimulationJobFailureClassification.Retryable,
            _classifier.Classify(
                exception,
                SimulationJobFailurePhase.Persistence));
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [InlineData("55P03")]
    [InlineData("53300")]
    [InlineData("57P01")]
    [InlineData("57P02")]
    [InlineData("57P03")]
    public void Classify_SupportedPostgresSqlState_IsRetryable(string sqlState)
    {
        Assert.Equal(
            SimulationJobFailureClassification.Retryable,
            _classifier.Classify(
                CreatePostgresException(sqlState),
                SimulationJobFailurePhase.Persistence));
    }

    [Theory]
    [InlineData("23502")]
    [InlineData("23503")]
    [InlineData("23505")]
    [InlineData("23514")]
    [InlineData("22003")]
    [InlineData("42601")]
    [InlineData("ZZZZZ")]
    public void Classify_NonRetryablePostgresSqlState_IsNonRetryable(
        string sqlState)
    {
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            _classifier.Classify(
                CreatePostgresException(sqlState),
                SimulationJobFailurePhase.Persistence));
    }

    [Fact]
    public void Classify_UnknownOrdinaryException_IsNonRetryable()
    {
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            _classifier.Classify(
                new InvalidOperationException(),
                SimulationJobFailurePhase.Persistence));
    }

    [Fact]
    public void Classify_RetryableInnerException_IsRetryable()
    {
        Exception exception = new InvalidOperationException(
            "outer message is irrelevant",
            new TimeoutException("inner message is irrelevant"));

        Assert.Equal(
            SimulationJobFailureClassification.Retryable,
            _classifier.Classify(
                exception,
                SimulationJobFailurePhase.Persistence));
    }

    [Fact]
    public void Classify_NonRetryableOuterProviderFailureWithRetryableInner_IsRetryable()
    {
        Exception exception = new NpgsqlException(
            "outer message is irrelevant",
            CreatePostgresException("40001"));

        Assert.Equal(
            SimulationJobFailureClassification.Retryable,
            _classifier.Classify(
                exception,
                SimulationJobFailurePhase.Persistence));
    }

    private static NpgsqlException CreateTransientNpgsqlException() =>
        new("message is not required", new TimeoutException());

    private static ISimulationJobFailureClassifier CreateClassifier()
    {
        Type type = typeof(SingularFlowDbContext).Assembly.GetType(
            "SingularFlow.Infrastructure.Persistence.EfSimulationJobFailureClassifier",
            throwOnError: true)!;

        return Assert.IsAssignableFrom<ISimulationJobFailureClassifier>(
            Activator.CreateInstance(type, nonPublic: true));
    }

    private static PostgresException CreatePostgresException(string sqlState) =>
        new(
            "message is not required",
            "ERROR",
            "ERROR",
            sqlState);
}