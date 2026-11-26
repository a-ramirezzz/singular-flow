using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobRepositoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task CreateAsync_InvalidMaxAttempts_ThrowsBeforeConnectionAccess(
        int maxAttempts)
    {
        FailingConnectionOpeningInterceptor connectionInterceptor = new();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(
                "Host=127.0.0.1;Port=1;Database=singular_flow_tests;" +
                "Username=unused;Password=unused",
                connectionInterceptor);
        await using SingularFlowDbContext context = new(options);
        EfSimulationJobRepository repository = new(context);
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => repository.CreateAsync(
                    request,
                    maxAttempts,
                    CancellationToken.None));

        Assert.Equal("maxAttempts", exception.ParamName);
        Assert.Equal(0, connectionInterceptor.OpenAttemptCount);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>().Options;
        await using SingularFlowDbContext context = new(options);
        EfSimulationJobRepository repository = new(context);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repository.CreateAsync(
                    null!,
                    SimulationJobRetryPolicy.Default.MaxAttempts,
                    CancellationToken.None));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndRoundTripsPendingJob()
    {
        SimulationJobInsertInterceptor insertInterceptor = new();
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(GetTestConnectionString(), insertInterceptor);
        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync(
                IsolationLevel.RepeatableRead);
        RunSimulationRequest request = new(
            SingularTime: 2.5,
            ConcentrationExponent: 0.004,
            StartTime: 0.25,
            EndTime: 1.75,
            SampleCount: 17,
            SamplingMode: SamplingMode.Logarithmic);
        EfSimulationJobRepository repository = new(context);
        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();
        insertInterceptor.CommandTexts.Clear();

        SimulationJobResult created =
            await repository.CreateAsync(
                request,
                SimulationJobRetryPolicy.Default.MaxAttempts,
                CancellationToken.None);
        Assert.True(
            insertInterceptor.CommandTexts.Count > 0,
            "No commands were intercepted during job creation.");
        List<string> insertCommands = insertInterceptor.CommandTexts
            .Where(SimulationJobInsertInterceptor.IsSimulationJobInsert)
            .ToList();
        Assert.True(
            insertCommands.Count > 0,
            $"Intercepted {insertInterceptor.CommandTexts.Count} command(s), " +
            "but none was an INSERT into simulation_jobs.");
        string insertCommand = Assert.Single(insertCommands);
        int valuesIndex = insertCommand.IndexOf(
            "VALUES",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(valuesIndex >= 0);
        string insertTargetList = insertCommand[..valuesIndex];
        Assert.DoesNotContain(
            "next_attempt_at_utc",
            insertTargetList,
            StringComparison.OrdinalIgnoreCase);
        DateTimeOffset databaseTransactionTimestamp =
            await context.Database.SqlQueryRaw<DateTimeOffset>(
                    "SELECT CURRENT_TIMESTAMP AS \"Value\"")
                .SingleAsync();

        Assert.Equal(jobCount + 1, await context.SimulationJobs.CountAsync());
        Assert.Equal(simulationCount, await context.Simulations.CountAsync());
        Assert.Equal(stateCount, await context.SimulationStates.CountAsync());
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.CreatedAtUtc);
        Assert.Equal(SimulationJobStatus.Pending, created.Status);
        Assert.Null(created.ClaimedAtUtc);
        Assert.Equal(request, created.Request);
        Assert.Equal(0, created.AttemptCount);
        Assert.Equal(3, created.MaxAttempts);
        Assert.Equal(
            databaseTransactionTimestamp,
            created.NextAttemptAtUtc);
        Assert.Null(created.LastFailedAtUtc);
        Assert.Null(created.LastFailureMessage);

        SimulationJobEntity saved = await context.SimulationJobs
            .AsNoTracking()
            .SingleAsync(job => job.Id == created.Id);
        Assert.Equal(request.SingularTime, saved.SingularTime);
        Assert.Equal(request.ConcentrationExponent, saved.ConcentrationExponent);
        Assert.Equal(request.StartTime, saved.StartTime);
        Assert.Equal(request.EndTime, saved.EndTime);
        Assert.Equal(request.SampleCount, saved.SampleCount);
        Assert.Equal(request.SamplingMode, saved.SamplingMode);
        Assert.Equal(SimulationJobStatus.Pending, saved.Status);
        Assert.Equal(created.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Null(saved.ClaimedAtUtc);
        Assert.Null(saved.ClaimToken);
        Assert.Null(saved.LeaseExpiresAtUtc);
        Assert.Equal(created.AttemptCount, saved.AttemptCount);
        Assert.Equal(created.MaxAttempts, saved.MaxAttempts);
        Assert.Equal(created.NextAttemptAtUtc, saved.NextAttemptAtUtc);
        Assert.Equal(
            databaseTransactionTimestamp,
            saved.NextAttemptAtUtc);
        Assert.Null(saved.LastFailedAtUtc);
        Assert.Null(saved.LastFailureMessage);

        context.ChangeTracker.Clear();
        SimulationJobResult? retrieved =
            await repository.GetByIdAsync(created.Id, CancellationToken.None);

        Assert.Empty(context.ChangeTracker.Entries<SimulationJobEntity>());
        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal(request, retrieved.Request);
        Assert.Equal(created.Status, retrieved.Status);
        Assert.Equal(created.CreatedAtUtc, retrieved.CreatedAtUtc);
        Assert.Null(retrieved.ClaimedAtUtc);
        Assert.Equal(created.AttemptCount, retrieved.AttemptCount);
        Assert.Equal(created.MaxAttempts, retrieved.MaxAttempts);
        Assert.Equal(created.NextAttemptAtUtc, retrieved.NextAttemptAtUtc);
        Assert.Null(retrieved.LastFailedAtUtc);
        Assert.Null(retrieved.LastFailureMessage);

        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(10)]
    public async Task CreateAsync_ValidMaxAttempts_PersistsAndRoundTrips(
        int maxAttempts)
    {
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(GetTestConnectionString());
        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync();
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);
        EfSimulationJobRepository repository = new(context);

        SimulationJobResult created = await repository.CreateAsync(
            request,
            maxAttempts,
            CancellationToken.None);
        context.ChangeTracker.Clear();
        SimulationJobResult? retrieved = await repository.GetByIdAsync(
            created.Id,
            CancellationToken.None);

        Assert.Equal(0, created.AttemptCount);
        Assert.Equal(maxAttempts, created.MaxAttempts);
        Assert.NotNull(created.NextAttemptAtUtc);
        Assert.Null(created.LastFailedAtUtc);
        Assert.Null(created.LastFailureMessage);
        Assert.NotNull(retrieved);
        Assert.Equal(maxAttempts, retrieved.MaxAttempts);
        Assert.Equal(created.NextAttemptAtUtc, retrieved.NextAttemptAtUtc);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNullWithoutModifyingData()
    {
        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(GetTestConnectionString());
        await using SingularFlowDbContext context = new(options);
        await context.Database.MigrateAsync();
        await using var transaction =
            await context.Database.BeginTransactionAsync();
        EfSimulationJobRepository repository = new(context);
        RunSimulationRequest sentinelRequest = new(
            SingularTime: 3.25,
            ConcentrationExponent: 0.006,
            StartTime: 0.5,
            EndTime: 2.75,
            SampleCount: 23,
            SamplingMode: SamplingMode.Uniform);
        SimulationJobResult sentinel = await repository.CreateAsync(
            sentinelRequest,
            SimulationJobRetryPolicy.Default.MaxAttempts,
            CancellationToken.None);
        context.ChangeTracker.Clear();
        SimulationJobEntity before = await context.SimulationJobs
            .AsNoTracking()
            .SingleAsync(job => job.Id == sentinel.Id);
        Guid missingId = Guid.NewGuid();
        while (missingId == sentinel.Id)
        {
            missingId = Guid.NewGuid();
        }

        SimulationJobResult? result = await repository.GetByIdAsync(
            missingId,
            CancellationToken.None);

        Assert.Null(result);
        Assert.NotEqual(sentinel.Id, missingId);
        Assert.False(await context.SimulationJobs
            .AsNoTracking()
            .AnyAsync(job => job.Id == missingId));
        SimulationJobEntity after = await context.SimulationJobs
            .AsNoTracking()
            .SingleAsync(job => job.Id == sentinel.Id);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.SingularTime, after.SingularTime);
        Assert.Equal(
            before.ConcentrationExponent,
            after.ConcentrationExponent);
        Assert.Equal(before.StartTime, after.StartTime);
        Assert.Equal(before.EndTime, after.EndTime);
        Assert.Equal(before.SampleCount, after.SampleCount);
        Assert.Equal(before.SamplingMode, after.SamplingMode);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.CreatedAtUtc, after.CreatedAtUtc);
        Assert.Equal(before.ClaimedAtUtc, after.ClaimedAtUtc);
        Assert.Equal(before.ClaimToken, after.ClaimToken);
        Assert.Equal(before.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Equal(before.SimulationId, after.SimulationId);
        Assert.Equal(before.FinishedAtUtc, after.FinishedAtUtc);
        Assert.Equal(before.FailureMessage, after.FailureMessage);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.MaxAttempts, after.MaxAttempts);
        Assert.Equal(before.NextAttemptAtUtc, after.NextAttemptAtUtc);
        Assert.Equal(before.LastFailedAtUtc, after.LastFailedAtUtc);
        Assert.Equal(before.LastFailureMessage, after.LastFailureMessage);
        Assert.Equal(SimulationJobStatus.Pending, after.Status);
        Assert.Equal(0, after.AttemptCount);
        Assert.Equal(3, after.MaxAttempts);
        Assert.NotNull(after.NextAttemptAtUtc);
        Assert.Null(after.ClaimedAtUtc);
        Assert.Null(after.ClaimToken);
        Assert.Null(after.LeaseExpiresAtUtc);
        Assert.Null(after.SimulationId);
        Assert.Null(after.FinishedAtUtc);
        Assert.Null(after.FailureMessage);
        Assert.Null(after.LastFailedAtUtc);
        Assert.Null(after.LastFailureMessage);
        Assert.Empty(context.ChangeTracker.Entries<SimulationJobEntity>());
        Assert.False(context.ChangeTracker.HasChanges());
        await transaction.RollbackAsync();
    }

    private static string GetTestConnectionString()
    {
        string connectionString = Environment.GetEnvironmentVariable(
            "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");
        NpgsqlConnectionStringBuilder connection = new(connectionString);
        Assert.Equal("singular_flow_tests", connection.Database);
        return connectionString;
    }

    private static DbContextOptions<SingularFlowDbContext> CreateOptions(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        DbContextOptionsBuilder<SingularFlowDbContext> optionsBuilder =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .UseNpgsql(connectionString);

        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return optionsBuilder.Options;
    }

    private sealed class FailingConnectionOpeningInterceptor
        : DbConnectionInterceptor
    {
        public int OpenAttemptCount { get; private set; }

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            OpenAttemptCount++;
            throw new InvalidOperationException(
                "The invalid-max-attempts test must not open a connection.");
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            OpenAttemptCount++;
            throw new InvalidOperationException(
                "The invalid-max-attempts test must not open a connection.");
        }
    }

    private sealed class SimulationJobInsertInterceptor : DbCommandInterceptor
    {
        private static readonly Regex SimulationJobInsertPattern = new(
            "^\\s*INSERT\\s+INTO\\s+" +
            "(?:(?:\\\"[^\\\"]+\\\"|[a-z_][a-z0-9_]*)\\s*\\.\\s*)?" +
            "\\\"?simulation_jobs\\\"?\\s*\\(",
            RegexOptions.IgnoreCase | RegexOptions.Multiline |
            RegexOptions.CultureInvariant);

        public List<string> CommandTexts { get; } = [];

        public static bool IsSimulationJobInsert(string commandText) =>
            SimulationJobInsertPattern.IsMatch(commandText);

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            CommandTexts.Add(command.CommandText);

            return base.ReaderExecutingAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }
}