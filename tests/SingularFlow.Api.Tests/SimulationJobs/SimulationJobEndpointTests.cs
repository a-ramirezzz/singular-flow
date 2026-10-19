using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.SimulationJobs;

public sealed class SimulationJobEndpointTests :
    IClassFixture<SimulationJobEndpointFactory>
{
    private static readonly Guid JobId = Guid.Parse(
        "89e58894-f2ab-4528-8030-75c727887611");
    private static readonly Guid SimulationId = Guid.Parse(
        "33ca3368-ef63-4b65-8075-54bcf40c34a5");
    private static readonly DateTimeOffset CreatedAtUtc =
        new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClaimedAtUtc =
        new(2026, 10, 18, 12, 0, 1, TimeSpan.FromHours(2));
    private static readonly DateTimeOffset FinishedAtUtc =
        new(2026, 10, 18, 12, 0, 2, TimeSpan.FromHours(2));

    private readonly SimulationJobEndpointFactory _factory;
    private readonly HttpClient _client;

    public SimulationJobEndpointTests(
        SimulationJobEndpointFactory factory)
    {
        _factory = factory;
        _factory.Reset();

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri("https://localhost")
        };

        _client = factory.CreateClient(options);
    }

    [Fact]
    public async Task PostSimulationJob_ValidRequest_ReturnsAcceptedSnapshotAndLocation()
    {
        SimulationJobResult configured = CreateResult(
            SimulationJobStatus.Pending);
        _factory.ConfigureCreateResult(configured);

        using HttpResponseMessage response =
            await _client.PostAsJsonAsync(
                "/api/simulation-jobs",
                CreateApiRequest());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(
            $"/api/simulation-jobs/{JobId}",
            response.Headers.Location?.AbsolutePath);
        Assert.Equal(
            "application/json",
            response.Content.Headers.ContentType?.MediaType);

        using JsonDocument document = await ReadJsonAsync(response);
        AssertSnapshot(
            document.RootElement,
            configured,
            "pending");

        Assert.Equal(1, _factory.CreateCount);
        Assert.Equal(0, _factory.GetCount);
        Assert.Single(_factory.ReceivedCreateTokens);
        Assert.True(_factory.ReceivedCreateTokens[0].CanBeCanceled);
        Assert.False(_factory.ReceivedCreateTokens[0].IsCancellationRequested);
        Assert.Equal(CreateRequest(), _factory.ReceivedCreateRequest);
        AssertNoLifecycleMutation();
    }

    [Theory]
    [InlineData(1.0, 1.0, "uniform")]
    [InlineData(1.0, 0.8, "adaptive")]
    public async Task PostSimulationJob_InvalidRequest_ReturnsProblemWithoutCreating(
        double singularTime,
        double endTime,
        string samplingMode)
    {
        var request = new
        {
            singularTime,
            concentrationExponent = 0.005,
            startTime = 0.0,
            endTime,
            sampleCount = 5,
            samplingMode
        };

        using HttpResponseMessage response =
            await _client.PostAsJsonAsync(
                "/api/simulation-jobs",
                request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(
            400,
            document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _factory.GetCount);
        AssertNoLifecycleMutation();
    }

    [Fact]
    public async Task PostSimulationJob_MalformedJson_ReturnsValidationProblemWithoutCreating()
    {
        using StringContent content = new(
            "{\"singularTime\":1.0,",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await _client.PostAsync(
                "/api/simulation-jobs",
                content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(
            JsonValueKind.Object,
            document.RootElement.GetProperty("errors").ValueKind);
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _factory.GetCount);
        AssertNoLifecycleMutation();
    }

    [Fact]
    public async Task PostSimulationJob_NullJsonBody_ReturnsValidationProblemWithoutCreating()
    {
        using StringContent content = new(
            "null",
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await _client.PostAsync(
                "/api/simulation-jobs",
                content);

        await AssertInvalidPostWithoutRepositoryActivity(response);
    }

    [Theory]
    [InlineData("0", "0.005", "0.0", "0.8", "5")]
    [InlineData("1.0", "0", "0.0", "0.8", "5")]
    [InlineData("1.0", "0.01", "0.0", "0.8", "5")]
    [InlineData("1.0", "0.005", "-0.1", "0.8", "5")]
    [InlineData("1.0", "0.005", "0.0", "0.0", "5")]
    [InlineData("1.0", "0.005", "0.0", "0.8", "1")]
    [InlineData("1.0", "0.005", "0.0", "0.8", "100001")]
    public async Task PostSimulationJob_InvalidNumericBoundary_ReturnsProblemWithoutCreating(
        string singularTime,
        string concentrationExponent,
        string startTime,
        string endTime,
        string sampleCount)
    {
        string json = CreateRawApiRequest(
            singularTime,
            concentrationExponent,
            startTime,
            endTime,
            sampleCount);

        using StringContent content = new(
            json,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await _client.PostAsync(
                "/api/simulation-jobs",
                content);

        await AssertInvalidPostWithoutRepositoryActivity(response);
    }

    [Fact]
    public async Task PostSimulationJob_OverflowingNumber_ReturnsValidationProblemWithoutCreating()
    {
        string json = CreateRawApiRequest(
            singularTime: "1e309",
            concentrationExponent: "0.005",
            startTime: "0.0",
            endTime: "0.8",
            sampleCount: "5");

        using StringContent content = new(
            json,
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response =
            await _client.PostAsync(
                "/api/simulation-jobs",
                content);

        await AssertInvalidPostWithoutRepositoryActivity(response);
    }

    [Theory]
    [InlineData(SimulationJobStatus.Pending, "pending")]
    [InlineData(SimulationJobStatus.Running, "running")]
    [InlineData(SimulationJobStatus.Completed, "completed")]
    [InlineData(SimulationJobStatus.Failed, "failed")]
    [InlineData(SimulationJobStatus.Cancelled, "cancelled")]
    public async Task GetSimulationJob_ExistingJob_ReturnsLifecycleSnapshot(
        SimulationJobStatus status,
        string expectedStatus)
    {
        SimulationJobResult configured = CreateLifecycleResult(status);
        _factory.ConfigureLookup(configured);

        using HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/simulation-jobs/{JobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument document = await ReadJsonAsync(response);
        AssertSnapshot(
            document.RootElement,
            configured,
            expectedStatus);
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(1, _factory.GetCount);
        Assert.Equal([JobId], _factory.ReceivedIds);
        Assert.Single(_factory.ReceivedGetTokens);
        Assert.True(_factory.ReceivedGetTokens[0].CanBeCanceled);
        Assert.False(_factory.ReceivedGetTokens[0].IsCancellationRequested);
        AssertNoLifecycleMutation();
    }

    [Fact]
    public async Task GetSimulationJob_CompletedAfterSimulationDeletion_PreservesCompletedStatus()
    {
        SimulationJobResult configured = CreateResult(
            SimulationJobStatus.Completed,
            claimedAtUtc: ClaimedAtUtc,
            finishedAtUtc: FinishedAtUtc,
            samplingMode: SamplingMode.Logarithmic);
        _factory.ConfigureLookup(configured);

        using HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/simulation-jobs/{JobId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = await ReadJsonAsync(response);
        JsonElement root = document.RootElement;
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal(
            "logarithmic",
            root.GetProperty("request")
                .GetProperty("samplingMode")
                .GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("simulationId").ValueKind);
        Assert.Equal(
            FinishedAtUtc,
            root.GetProperty("finishedAtUtc").GetDateTimeOffset());
        AssertGetOnly(JobId);
    }

    [Fact]
    public async Task GetSimulationJob_MissingValidId_ReturnsNotFound()
    {
        using HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/simulation-jobs/{JobId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertGetOnly(JobId);
    }

    [Fact]
    public async Task GetSimulationJob_InvalidId_ReturnsRoutingNotFoundWithoutRepositoryCall()
    {
        using HttpResponseMessage response =
            await _client.GetAsync(
                "/api/simulation-jobs/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _factory.GetCount);
        AssertNoLifecycleMutation();
    }

    [Fact]
    public async Task GetSimulationJob_EmptyId_ForwardsEmptyIdAndReturnsNotFound()
    {
        using HttpResponseMessage response =
            await _client.GetAsync(
                $"/api/simulation-jobs/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        AssertGetOnly(Guid.Empty);
    }

    private static object CreateApiRequest() => new
    {
        singularTime = 1.0,
        concentrationExponent = 0.005,
        startTime = 0.0,
        endTime = 0.8,
        sampleCount = 5,
        samplingMode = "uniform"
    };

    private static string CreateRawApiRequest(
        string singularTime,
        string concentrationExponent,
        string startTime,
        string endTime,
        string sampleCount) => $$"""
        {
          "singularTime": {{singularTime}},
          "concentrationExponent": {{concentrationExponent}},
          "startTime": {{startTime}},
          "endTime": {{endTime}},
          "sampleCount": {{sampleCount}},
          "samplingMode": "uniform"
        }
        """;

    private static RunSimulationRequest CreateRequest(
        SamplingMode samplingMode = SamplingMode.Uniform) => new(
        SingularTime: 1.0,
        ConcentrationExponent: 0.005,
        StartTime: 0.0,
        EndTime: 0.8,
        SampleCount: 5,
        SamplingMode: samplingMode);

    private static SimulationJobResult CreateLifecycleResult(
        SimulationJobStatus status)
    {
        return status switch
        {
            SimulationJobStatus.Pending => CreateResult(status),
            SimulationJobStatus.Running => CreateResult(
                status,
                claimedAtUtc: ClaimedAtUtc),
            SimulationJobStatus.Completed => CreateResult(
                status,
                claimedAtUtc: ClaimedAtUtc,
                simulationId: SimulationId,
                finishedAtUtc: FinishedAtUtc),
            SimulationJobStatus.Failed => CreateResult(
                status,
                claimedAtUtc: ClaimedAtUtc,
                finishedAtUtc: FinishedAtUtc,
                failureMessage: "Simulation processing failed safely."),
            SimulationJobStatus.Cancelled => CreateResult(
                status,
                claimedAtUtc: ClaimedAtUtc,
                finishedAtUtc: FinishedAtUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private static SimulationJobResult CreateResult(
        SimulationJobStatus status,
        DateTimeOffset? claimedAtUtc = null,
        Guid? simulationId = null,
        DateTimeOffset? finishedAtUtc = null,
        string? failureMessage = null,
        SamplingMode samplingMode = SamplingMode.Uniform)
    {
        return new SimulationJobResult(
            Id: JobId,
            Request: CreateRequest(samplingMode),
            Status: status,
            CreatedAtUtc: CreatedAtUtc,
            ClaimedAtUtc: claimedAtUtc,
            SimulationId: simulationId,
            FinishedAtUtc: finishedAtUtc,
            FailureMessage: failureMessage);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response)
    {
        Stream stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private async Task AssertInvalidPostWithoutRepositoryActivity(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        using JsonDocument document = await ReadJsonAsync(response);
        Assert.Equal(
            400,
            document.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(0, _factory.GetCount);
        AssertNoLifecycleMutation();
    }

    private static void AssertSnapshot(
        JsonElement root,
        SimulationJobResult expected,
        string expectedStatus)
    {
        Assert.Equal(expected.Id, root.GetProperty("id").GetGuid());
        Assert.Equal(expectedStatus, root.GetProperty("status").GetString());

        JsonElement request = root.GetProperty("request");
        Assert.Equal(1.0, request.GetProperty("singularTime").GetDouble());
        Assert.Equal(0.005, request.GetProperty("concentrationExponent").GetDouble());
        Assert.Equal(0.0, request.GetProperty("startTime").GetDouble());
        Assert.Equal(0.8, request.GetProperty("endTime").GetDouble());
        Assert.Equal(5, request.GetProperty("sampleCount").GetInt32());
        Assert.Equal("uniform", request.GetProperty("samplingMode").GetString());

        Assert.Equal(
            expected.CreatedAtUtc,
            root.GetProperty("createdAtUtc").GetDateTimeOffset());
        AssertNullableDateTimeOffset(root, "claimedAtUtc", expected.ClaimedAtUtc);
        AssertNullableDateTimeOffset(root, "finishedAtUtc", expected.FinishedAtUtc);
        AssertNullableGuid(root, "simulationId", expected.SimulationId);

        JsonElement failureMessage = root.GetProperty("failureMessage");
        if (expected.FailureMessage is null)
        {
            Assert.Equal(JsonValueKind.Null, failureMessage.ValueKind);
        }
        else
        {
            Assert.Equal(expected.FailureMessage, failureMessage.GetString());
        }
    }

    private static void AssertNullableDateTimeOffset(
        JsonElement root,
        string propertyName,
        DateTimeOffset? expected)
    {
        JsonElement property = root.GetProperty(propertyName);
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, property.ValueKind);
        }
        else
        {
            Assert.Equal(expected, property.GetDateTimeOffset());
        }
    }

    private static void AssertNullableGuid(
        JsonElement root,
        string propertyName,
        Guid? expected)
    {
        JsonElement property = root.GetProperty(propertyName);
        if (expected is null)
        {
            Assert.Equal(JsonValueKind.Null, property.ValueKind);
        }
        else
        {
            Assert.Equal(expected, property.GetGuid());
        }
    }

    private void AssertGetOnly(Guid expectedId)
    {
        Assert.Equal(0, _factory.CreateCount);
        Assert.Equal(1, _factory.GetCount);
        Assert.Equal([expectedId], _factory.ReceivedIds);
        AssertNoLifecycleMutation();
    }

    private void AssertNoLifecycleMutation()
    {
        Assert.Equal(0, _factory.ClaimCount);
        Assert.Equal(0, _factory.CompleteCount);
        Assert.Equal(0, _factory.FailCount);
    }
}