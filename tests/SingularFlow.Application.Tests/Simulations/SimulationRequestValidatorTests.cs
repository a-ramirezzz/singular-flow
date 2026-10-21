using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.Simulations;

public sealed class SimulationRequestValidatorTests
{
    public static TheoryData<RunSimulationRequest, string>
        InvalidRequests => new()
        {
            {
                CreateRequest(samplingMode: (SamplingMode)999),
                "samplingMode"
            },
            {
                CreateRequest(singularTime: 0.0),
                "singularTime"
            },
            {
                CreateRequest(concentrationExponent: 0.01),
                "concentrationExponent"
            },
            {
                CreateRequest(startTime: -0.1),
                "startTime"
            },
            {
                CreateRequest(startTime: 0.0, endTime: 0.0),
                "endTime"
            },
            {
                CreateRequest(endTime: 1.0),
                "seriesParameters"
            },
            {
                CreateRequest(sampleCount: 1),
                "sampleCount"
            },
            {
                CreateRequest(sampleCount: 100_001),
                "sampleCount"
            }
        };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequest_BothHandlersThrowEquivalentExceptionsBeforeCreation(
        RunSimulationRequest request,
        string expectedParameterName)
    {
        RunSimulationHandler simulationHandler = new();
        RecordingSimulationJobRepository repository = new(request);
        CreateSimulationJobHandler creationHandler = new(repository);

        ArgumentOutOfRangeException synchronousException =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => simulationHandler.Handle(request));

        ArgumentOutOfRangeException creationException =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => creationHandler.HandleAsync(
                    request,
                    CancellationToken.None));

        Assert.Equal(
            synchronousException.GetType(),
            creationException.GetType());
        Assert.Equal(expectedParameterName, synchronousException.ParamName);
        Assert.Equal(expectedParameterName, creationException.ParamName);
        Assert.Equal(0, repository.InvocationCount);
    }

    [Fact]
    public async Task NullRequest_BothHandlersThrowForRequestBeforeCreation()
    {
        RunSimulationHandler simulationHandler = new();
        RunSimulationRequest request = CreateRequest();
        RecordingSimulationJobRepository repository = new(request);
        CreateSimulationJobHandler creationHandler = new(repository);

        ArgumentNullException synchronousException =
            Assert.Throws<ArgumentNullException>(
                () => simulationHandler.Handle(request: null!));

        ArgumentNullException creationException =
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => creationHandler.HandleAsync(
                    request: null!,
                    CancellationToken.None));

        Assert.Equal("request", synchronousException.ParamName);
        Assert.Equal("request", creationException.ParamName);
        Assert.Equal(0, repository.InvocationCount);
    }

    [Theory]
    [InlineData(2, 0.009, 0.8)]
    [InlineData(100_000, 0.005, 0.9999)]
    public async Task BoundaryValidRequest_CreationPreservesRequestWithoutCalculation(
        int sampleCount,
        double concentrationExponent,
        double endTime)
    {
        RunSimulationRequest request = CreateRequest(
            concentrationExponent: concentrationExponent,
            endTime: endTime,
            sampleCount: sampleCount);

        RecordingSimulationJobRepository repository = new(request);
        CreateSimulationJobHandler handler = new(repository);

        SimulationJobResult result = await handler.HandleAsync(
            request,
            CancellationToken.None);

        Assert.Equal(1, repository.InvocationCount);
        Assert.Same(request, repository.ReceivedRequest);
        Assert.Same(request, result.Request);
    }

    private static RunSimulationRequest CreateRequest(
        double singularTime = 1.0,
        double concentrationExponent = 0.005,
        double startTime = 0.0,
        double endTime = 0.8,
        int sampleCount = 3,
        SamplingMode samplingMode = SamplingMode.Uniform)
    {
        return new RunSimulationRequest(
            SingularTime: singularTime,
            ConcentrationExponent: concentrationExponent,
            StartTime: startTime,
            EndTime: endTime,
            SampleCount: sampleCount,
            SamplingMode: samplingMode);
    }

    private sealed class RecordingSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly SimulationJobResult _result;

        public RecordingSimulationJobRepository(
            RunSimulationRequest request)
        {
            _result = new SimulationJobResult(
                Id: Guid.Empty,
                Request: request,
                Status: SimulationJobStatus.Pending,
                CreatedAtUtc: DateTimeOffset.UnixEpoch,
                ClaimedAtUtc: null,
                SimulationId: null,
                FinishedAtUtc: null,
                FailureMessage: null);
        }

        public int InvocationCount { get; private set; }

        public RunSimulationRequest? ReceivedRequest { get; private set; }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            ReceivedRequest = request;

            return Task.FromResult(_result);
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid claimToken,
            Guid simulationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            Guid claimToken,
            string failureMessage,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}