using Microsoft.EntityFrameworkCore;

using Npgsql;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Tests.Persistence;

public sealed class EfSimulationJobRepositoryTests
{
    [Fact]
    public async Task CreateAsync_NullRequest_ThrowsArgumentNullException()
    {
        DbContextOptions<SingularFlowDbContext> options =
            new DbContextOptionsBuilder<SingularFlowDbContext>()
                .Options;

        await using SingularFlowDbContext context =
            new(options);

        EfSimulationJobRepository repository = new(
            context);

        ArgumentNullException exception =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repository.CreateAsync(
                    null!,
                    CancellationToken.None));

        Assert.Equal(
            "request",
            exception.ParamName);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_PersistsAndRoundTripsPendingJob()
    {
        string connectionString = GetTestConnectionString();

        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        RunSimulationRequest request = new(
            SingularTime: 2.5,
            ConcentrationExponent: 0.004,
            StartTime: 0.25,
            EndTime: 1.75,
            SampleCount: 17,
            SamplingMode: SamplingMode.Logarithmic);

        EfSimulationJobRepository repository = new(
            context);

        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();

        SimulationJobResult created =
            await repository.CreateAsync(
                request,
                CancellationToken.None);

        Assert.Equal(
            jobCount + 1,
            await context.SimulationJobs.CountAsync());
        Assert.Equal(
            simulationCount,
            await context.Simulations.CountAsync());
        Assert.Equal(
            stateCount,
            await context.SimulationStates.CountAsync());

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.CreatedAtUtc);
        Assert.Equal(SimulationJobStatus.Pending, created.Status);
        Assert.Equal(request, created.Request);

        SimulationJobEntity saved =
            await context.SimulationJobs
                .AsNoTracking()
                .SingleAsync(job => job.Id == created.Id);

        Assert.Equal(request.SingularTime, saved.SingularTime);
        Assert.Equal(
            request.ConcentrationExponent,
            saved.ConcentrationExponent);
        Assert.Equal(request.StartTime, saved.StartTime);
        Assert.Equal(request.EndTime, saved.EndTime);
        Assert.Equal(request.SampleCount, saved.SampleCount);
        Assert.Equal(request.SamplingMode, saved.SamplingMode);
        Assert.Equal(SimulationJobStatus.Pending, saved.Status);
        Assert.Equal(created.CreatedAtUtc, saved.CreatedAtUtc);

        context.ChangeTracker.Clear();

        SimulationJobResult? retrieved =
            await repository.GetByIdAsync(
                created.Id,
                CancellationToken.None);

        Assert.Empty(
            context.ChangeTracker.Entries<SimulationJobEntity>());
        Assert.NotNull(retrieved);
        Assert.Equal(created.Id, retrieved.Id);
        Assert.Equal(request, retrieved.Request);
        Assert.Equal(created.Status, retrieved.Status);
        Assert.Equal(created.CreatedAtUtc, retrieved.CreatedAtUtc);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNullWithoutModifyingData()
    {
        string connectionString = GetTestConnectionString();

        DbContextOptions<SingularFlowDbContext> options =
            CreateOptions(connectionString);

        await using SingularFlowDbContext context =
            new(options);

        await context.Database.MigrateAsync();

        await using var transaction =
            await context.Database.BeginTransactionAsync();

        int jobCount = await context.SimulationJobs.CountAsync();
        int simulationCount = await context.Simulations.CountAsync();
        int stateCount = await context.SimulationStates.CountAsync();

        EfSimulationJobRepository repository = new(
            context);

        SimulationJobResult? result =
            await repository.GetByIdAsync(
                Guid.NewGuid(),
                CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(jobCount, await context.SimulationJobs.CountAsync());
        Assert.Equal(
            simulationCount,
            await context.Simulations.CountAsync());
        Assert.Equal(
            stateCount,
            await context.SimulationStates.CountAsync());
        Assert.False(context.ChangeTracker.HasChanges());

        await transaction.RollbackAsync();
    }

    private static string GetTestConnectionString()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(
                "SINGULARFLOW_TEST_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "Set SINGULARFLOW_TEST_CONNECTION_STRING.");

        NpgsqlConnectionStringBuilder connection = new(
            connectionString);

        Assert.Equal(
            "singular_flow_tests",
            connection.Database);

        return connectionString;
    }

    private static DbContextOptions<SingularFlowDbContext> CreateOptions(
        string connectionString)
    {
        return new DbContextOptionsBuilder<SingularFlowDbContext>()
            .UseNpgsql(connectionString)
            .Options;
    }
}