using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Tests.Simulations;

public sealed class SimulationListingEndpointTests
{
    [Fact]
    public async Task GetSimulations_ValidPagination_ReturnsSummaries()
    {
        SimulationSummaryResult summary = new(
            Id: Guid.Parse(
                "89e58894-f2ab-4528-8030-75c727887611"),
            CreatedAtUtc: new DateTimeOffset(
                2026,
                9,
                24,
                12,
                0,
                0,
                TimeSpan.Zero),
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 5,
            SamplingMode: SamplingMode.Uniform);

        PagedSimulationResult applicationResult = new(
            Items: new[] { summary },
            Page: 2,
            PageSize: 2,
            TotalCount: 3);

        ListingSimulationRepository repository = new(
            applicationResult);

        using WebApplicationFactory<Program> factory =
            new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<
                            ISimulationRepository>();

                        services.AddSingleton<
                            ISimulationRepository>(
                                repository);
                    });
                });

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        using HttpResponseMessage response =
            await client.GetAsync(
                "/api/simulations?page=2&pageSize=2");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            2,
            root.GetProperty("page").GetInt32());

        Assert.Equal(
            2,
            root.GetProperty("pageSize").GetInt32());

        Assert.Equal(
            3,
            root.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            2,
            root.GetProperty("totalPages").GetInt32());

        JsonElement items =
            root.GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());

        JsonElement item = items[0];

        Assert.Equal(
            summary.Id,
            item.GetProperty("id").GetGuid());

        Assert.Equal(
            "uniform",
            item.GetProperty(
                "samplingMode").GetString());

        Assert.Equal(
            5,
            item.GetProperty(
                "sampleCount").GetInt32());

        Assert.False(
            item.TryGetProperty(
                "states",
                out _));

        Assert.Equal(
            2,
            repository.RequestedPage);

        Assert.Equal(
            2,
            repository.RequestedPageSize);
    }

    [Fact]
    public async Task GetSimulations_WithoutPagination_UsesDefaults()
    {
        using SimulationEndpointFactory factory = new();

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        using HttpResponseMessage response =
            await client.GetAsync(
                "/api/simulations");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        await using Stream contentStream =
            await response.Content.ReadAsStreamAsync();

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                contentStream);

        JsonElement root = document.RootElement;

        Assert.Equal(
            ListSimulationsHandler.DefaultPage,
            root.GetProperty("page").GetInt32());

        Assert.Equal(
            ListSimulationsHandler.DefaultPageSize,
            root.GetProperty("pageSize").GetInt32());

        Assert.Equal(
            0,
            root.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            0,
            root.GetProperty("totalPages").GetInt32());

        Assert.Empty(
            root.GetProperty("items")
                .EnumerateArray());
    }

    [Theory]
    [InlineData("/api/simulations?page=0&pageSize=20")]
    [InlineData("/api/simulations?page=1&pageSize=0")]
    [InlineData("/api/simulations?page=1&pageSize=101")]
    public async Task GetSimulations_InvalidPagination_ReturnsBadRequest(
        string requestUri)
    {
        using SimulationEndpointFactory factory = new();

        WebApplicationFactoryClientOptions options = new()
        {
            BaseAddress = new Uri(
                "https://localhost")
        };

        using HttpClient client =
            factory.CreateClient(options);

        using HttpResponseMessage response =
            await client.GetAsync(
                requestUri);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers
                .ContentType?.MediaType);
    }

    private sealed class ListingSimulationRepository :
        ISimulationRepository
    {
        private readonly PagedSimulationResult _result;

        public ListingSimulationRepository(
            PagedSimulationResult result)
        {
            _result = result;
        }

        public int? RequestedPage { get; private set; }

        public int? RequestedPageSize { get; private set; }

        public Task<PersistedSimulationResult> SaveAsync(
            RunSimulationResult result,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PersistedSimulationResult?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PagedSimulationResult> ListAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            RequestedPage = page;
            RequestedPageSize = pageSize;

            return Task.FromResult(_result);
        }
    }
}