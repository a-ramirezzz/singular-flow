using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class CreateSimulationJobHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse(
            "89e58894-f2ab-4528-8030-75c727887611");

    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            year: 2026,
            month: 10,
            day: 1,
            hour: 12,
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Request_DelegatesOnceAndReturnsRepositoryResult()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        SimulationJobResult repositoryResult = new(
            Id: JobId,
            Request: request,
            Status: SimulationJobStatus.Pending,
            CreatedAtUtc: CreatedAtUtc);

        RecordingSimulationJobRepository repository = new(
            repositoryResult);

        CreateSimulationJobHandler handler = new(
            repository);

        using CancellationTokenSource cancellation = new();

        SimulationJobResult result =
            await handler.HandleAsync(
                request,
                cancellation.Token);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Same(request, repository.ReceivedRequest);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedCancellationToken);
        Assert.Same(repositoryResult, result);
        Assert.Same(request, result.Request);
    }

    [Fact]
    public void SimulationJobStatus_DefinesExpectedLifecycleValues()
    {
        string[] names =
            Enum.GetNames<SimulationJobStatus>();

        Assert.Equal(
            new[]
            {
                "Pending",
                "Running",
                "Completed",
                "Failed",
                "Cancelled"
            },
            names);

        Assert.Equal(0, (int)SimulationJobStatus.Pending);
        Assert.Equal(1, (int)SimulationJobStatus.Running);
        Assert.Equal(2, (int)SimulationJobStatus.Completed);
        Assert.Equal(3, (int)SimulationJobStatus.Failed);
        Assert.Equal(4, (int)SimulationJobStatus.Cancelled);
    }

    private sealed class RecordingSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly SimulationJobResult _result;

        public RecordingSimulationJobRepository(
            SimulationJobResult result)
        {
            _result = result;
        }

        public int InvocationCount { get; private set; }

        public RunSimulationRequest? ReceivedRequest
        {
            get;
            private set;
        }

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedRequest = request;
            ReceivedCancellationToken = cancellationToken;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}