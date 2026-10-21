using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class SimulationJobLeaseHandlerTests
{
    private static readonly Guid JobId =
        Guid.Parse("64a17baa-a434-4597-b437-a852944497e1");

    private static readonly Guid ClaimToken =
        Guid.Parse("31441328-1fb4-47fa-a9f7-dfd978e14d95");

    private static readonly TimeSpan LeaseDuration =
        TimeSpan.FromMinutes(2);

    private static readonly DateTimeOffset LeaseExpiresAtUtc =
        new(2026, 10, 19, 18, 2, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClaimHandler_ClaimedJob_DelegatesOnceAndReturnsExactClaim()
    {
        SimulationJobClaim repositoryResult = new(
            CreateRunningJob(),
            ClaimToken,
            LeaseExpiresAtUtc);
        RecordingSimulationJobLeaseRepository repository = new()
        {
            ClaimResult = repositoryResult
        };
        ClaimSimulationJobLeaseHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobClaim? result = await handler.HandleAsync(
            LeaseDuration,
            cancellation.Token);

        Assert.Equal(1, repository.ClaimInvocationCount);
        Assert.Equal(LeaseDuration, repository.ReceivedLeaseDuration);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedClaimCancellationToken);
        Assert.Same(repositoryResult, result);
    }

    [Fact]
    public async Task ClaimHandler_EmptyQueue_DelegatesOnceAndReturnsNull()
    {
        RecordingSimulationJobLeaseRepository repository = new();
        ClaimSimulationJobLeaseHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        SimulationJobClaim? result = await handler.HandleAsync(
            LeaseDuration,
            cancellation.Token);

        Assert.Equal(1, repository.ClaimInvocationCount);
        Assert.Equal(LeaseDuration, repository.ReceivedLeaseDuration);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedClaimCancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task RenewHandler_MatchingOwner_DelegatesOnceAndReturnsExpiration()
    {
        RecordingSimulationJobLeaseRepository repository = new()
        {
            RenewalResult = LeaseExpiresAtUtc
        };
        RenewSimulationJobLeaseHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        DateTimeOffset? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            LeaseDuration,
            cancellation.Token);

        Assert.Equal(1, repository.RenewalInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(ClaimToken, repository.ReceivedClaimToken);
        Assert.Equal(LeaseDuration, repository.ReceivedLeaseDuration);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedRenewalCancellationToken);
        Assert.Equal(LeaseExpiresAtUtc, result);
    }

    [Fact]
    public async Task RenewHandler_OwnershipMismatch_DelegatesOnceAndReturnsNull()
    {
        RecordingSimulationJobLeaseRepository repository = new();
        RenewSimulationJobLeaseHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        DateTimeOffset? result = await handler.HandleAsync(
            JobId,
            ClaimToken,
            LeaseDuration,
            cancellation.Token);

        Assert.Equal(1, repository.RenewalInvocationCount);
        Assert.Equal(JobId, repository.ReceivedJobId);
        Assert.Equal(ClaimToken, repository.ReceivedClaimToken);
        Assert.Equal(LeaseDuration, repository.ReceivedLeaseDuration);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedRenewalCancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task RecoveryHandler_RecoveredJobs_DelegatesOnceAndReturnsExactList()
    {
        const int batchSize = 17;
        IReadOnlyList<Guid> repositoryResult = new[]
        {
            Guid.Parse("847f7313-4bd8-4515-87db-1b35728c4cc7"),
            Guid.Parse("01a34f9f-231e-4f67-8e46-ea73da2cf19d")
        };
        RecordingSimulationJobLeaseRepository repository = new()
        {
            RecoveryResult = repositoryResult
        };
        RecoverExpiredSimulationJobsHandler handler = new(repository);
        using CancellationTokenSource cancellation = new();

        IReadOnlyList<Guid> result = await handler.HandleAsync(
            batchSize,
            cancellation.Token);

        Assert.Equal(1, repository.RecoveryInvocationCount);
        Assert.Equal(batchSize, repository.ReceivedBatchSize);
        Assert.Equal(
            cancellation.Token,
            repository.ReceivedRecoveryCancellationToken);
        Assert.Same(repositoryResult, result);
        Assert.Equal(repositoryResult, result);
    }

    [Fact]
    public async Task RecoveryHandler_NoExpiredJobs_ReturnsExactEmptyList()
    {
        IReadOnlyList<Guid> repositoryResult = Array.Empty<Guid>();
        RecordingSimulationJobLeaseRepository repository = new()
        {
            RecoveryResult = repositoryResult
        };
        RecoverExpiredSimulationJobsHandler handler = new(repository);

        IReadOnlyList<Guid> result = await handler.HandleAsync(
            batchSize: 5,
            CancellationToken.None);

        Assert.Equal(1, repository.RecoveryInvocationCount);
        Assert.Same(repositoryResult, result);
        Assert.Empty(result);
    }

    [Fact]
    public void SimulationJobClaim_Construction_PreservesOwnershipValues()
    {
        SimulationJobResult job = CreateRunningJob();

        SimulationJobClaim claim = new(
            job,
            ClaimToken,
            LeaseExpiresAtUtc);

        Assert.Same(job, claim.Job);
        Assert.NotEqual(Guid.Empty, claim.ClaimToken);
        Assert.Equal(ClaimToken, claim.ClaimToken);
        Assert.Equal(LeaseExpiresAtUtc, claim.LeaseExpiresAtUtc);
    }

    private static SimulationJobResult CreateRunningJob()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        return new SimulationJobResult(
            Id: JobId,
            Request: request,
            Status: SimulationJobStatus.Running,
            CreatedAtUtc: LeaseExpiresAtUtc.AddMinutes(-3),
            ClaimedAtUtc: LeaseExpiresAtUtc.AddMinutes(-2),
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
    }

    private sealed class RecordingSimulationJobLeaseRepository :
        ISimulationJobLeaseRepository
    {
        public SimulationJobClaim? ClaimResult { get; init; }

        public DateTimeOffset? RenewalResult { get; init; }

        public IReadOnlyList<Guid> RecoveryResult { get; init; } =
            Array.Empty<Guid>();

        public int ClaimInvocationCount { get; private set; }

        public int RenewalInvocationCount { get; private set; }

        public int RecoveryInvocationCount { get; private set; }

        public Guid ReceivedJobId { get; private set; }

        public Guid ReceivedClaimToken { get; private set; }

        public TimeSpan ReceivedLeaseDuration { get; private set; }

        public int ReceivedBatchSize { get; private set; }

        public CancellationToken ReceivedClaimCancellationToken
        {
            get;
            private set;
        }

        public CancellationToken ReceivedRenewalCancellationToken
        {
            get;
            private set;
        }

        public CancellationToken ReceivedRecoveryCancellationToken
        {
            get;
            private set;
        }

        public Task<SimulationJobClaim?> ClaimNextPendingAsync(
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            ClaimInvocationCount++;
            ReceivedLeaseDuration = leaseDuration;
            ReceivedClaimCancellationToken = cancellationToken;

            return Task.FromResult(ClaimResult);
        }

        public Task<DateTimeOffset?> TryRenewLeaseAsync(
            Guid jobId,
            Guid claimToken,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            RenewalInvocationCount++;
            ReceivedJobId = jobId;
            ReceivedClaimToken = claimToken;
            ReceivedLeaseDuration = leaseDuration;
            ReceivedRenewalCancellationToken = cancellationToken;

            return Task.FromResult(RenewalResult);
        }

        public Task<IReadOnlyList<Guid>> RecoverExpiredAsync(
            int batchSize,
            CancellationToken cancellationToken)
        {
            RecoveryInvocationCount++;
            ReceivedBatchSize = batchSize;
            ReceivedRecoveryCancellationToken = cancellationToken;

            return Task.FromResult(RecoveryResult);
        }
    }
}