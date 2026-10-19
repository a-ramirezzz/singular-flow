using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.SimulationJobs;

public sealed class SimulationJobEndpointFactory :
    SingularFlowApiFactory
{
    private readonly ControlledSimulationJobRepository _repository =
        new();

    public int CreateCount => _repository.CreateCount;
    public int GetCount => _repository.GetCount;
    public int ClaimCount => _repository.ClaimCount;
    public int CompleteCount => _repository.CompleteCount;
    public int FailCount => _repository.FailCount;
    public RunSimulationRequest? ReceivedCreateRequest =>
        _repository.ReceivedCreateRequest;
    public IReadOnlyList<Guid> ReceivedIds =>
        _repository.ReceivedIds;
    public IReadOnlyList<CancellationToken> ReceivedCreateTokens =>
        _repository.ReceivedCreateTokens;
    public IReadOnlyList<CancellationToken> ReceivedGetTokens =>
        _repository.ReceivedGetTokens;

    public void Reset() => _repository.Reset();

    public void ConfigureCreateResult(
        SimulationJobResult result) =>
        _repository.ConfigureCreateResult(result);

    public void ConfigureLookup(SimulationJobResult result) =>
        _repository.ConfigureLookup(result);

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISimulationJobRepository>();
            services.AddSingleton<ISimulationJobRepository>(
                _repository);
        });
    }

    private sealed class ControlledSimulationJobRepository :
        ISimulationJobRepository
    {
        private readonly Dictionary<Guid, SimulationJobResult> _lookups =
            new();
        private readonly List<Guid> _receivedIds = new();
        private readonly List<CancellationToken> _receivedCreateTokens =
            new();
        private readonly List<CancellationToken> _receivedGetTokens =
            new();
        private SimulationJobResult? _createResult;

        public int CreateCount { get; private set; }
        public int GetCount { get; private set; }
        public int ClaimCount { get; private set; }
        public int CompleteCount { get; private set; }
        public int FailCount { get; private set; }
        public RunSimulationRequest? ReceivedCreateRequest { get; private set; }
        public IReadOnlyList<Guid> ReceivedIds => _receivedIds;
        public IReadOnlyList<CancellationToken> ReceivedCreateTokens =>
            _receivedCreateTokens;
        public IReadOnlyList<CancellationToken> ReceivedGetTokens =>
            _receivedGetTokens;

        public void Reset()
        {
            CreateCount = 0;
            GetCount = 0;
            ClaimCount = 0;
            CompleteCount = 0;
            FailCount = 0;
            ReceivedCreateRequest = null;
            _receivedIds.Clear();
            _receivedCreateTokens.Clear();
            _receivedGetTokens.Clear();
            _lookups.Clear();
            _createResult = null;
        }

        public void ConfigureCreateResult(
            SimulationJobResult result)
        {
            _createResult = result;
        }

        public void ConfigureLookup(SimulationJobResult result)
        {
            _lookups[result.Id] = result;
        }

        public Task<SimulationJobResult> CreateAsync(
            RunSimulationRequest request,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            ReceivedCreateRequest = request;
            _receivedCreateTokens.Add(cancellationToken);

            return Task.FromResult(
                _createResult ?? throw new InvalidOperationException(
                    "A creation result must be configured."));
        }

        public Task<SimulationJobResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            GetCount++;
            _receivedIds.Add(id);
            _receivedGetTokens.Add(cancellationToken);
            _lookups.TryGetValue(id, out SimulationJobResult? result);

            return Task.FromResult(result);
        }

        public Task<SimulationJobResult?> ClaimNextPendingAsync(
            CancellationToken cancellationToken)
        {
            ClaimCount++;
            throw new InvalidOperationException(
                "Endpoint tests must not claim jobs.");
        }

        public Task<SimulationJobResult?> TryCompleteAsync(
            Guid jobId,
            Guid simulationId,
            CancellationToken cancellationToken)
        {
            CompleteCount++;
            throw new InvalidOperationException(
                "Endpoint tests must not complete jobs.");
        }

        public Task<SimulationJobResult?> TryFailAsync(
            Guid jobId,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            FailCount++;
            throw new InvalidOperationException(
                "Endpoint tests must not fail jobs.");
        }
    }
}