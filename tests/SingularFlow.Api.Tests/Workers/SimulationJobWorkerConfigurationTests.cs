using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobWorkerConfigurationTests
{
    [Fact]
    public void RuntimeAppSettings_ContainApprovedDefaults()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();
        SimulationJobWorkerOptions options = new();

        configuration
            .GetSection(SimulationJobWorkerOptions.SectionName)
            .Bind(options);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
    }

    [Fact]
    public void SharedFactory_DisablesWorkerAndRetainsRuntimeDelays()
    {
        using SingularFlowApiFactory factory = new();

        SimulationJobWorkerOptions options = ResolveOptions(factory);
        IHostedService[] hostedServices = factory.Services
            .GetServices<IHostedService>()
            .ToArray();

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
        Assert.Contains(hostedServices, service => service is SimulationJobWorker);
    }

    [Fact]
    public void LaterCloneOverride_DoesNotMutateSharedFactory()
    {
        using SingularFlowApiFactory factory = new();
        SimulationJobProcessingProbe probe = new();
        using WebApplicationFactory<Program> enabledFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["SimulationJobs:Worker:Enabled"] =
                                    bool.TrueString
                            });
                    });

                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISimulationJobRepository>();
                    services.RemoveAll<ISimulationJobCompletionRepository>();
                    services.AddSingleton(probe);
                    services.AddScoped<
                        ISimulationJobRepository,
                        ControlledSimulationJobRepository>();
                    services.AddScoped<
                        ISimulationJobCompletionRepository,
                        ControlledSimulationJobCompletionRepository>();
                });
            });

        SimulationJobWorkerOptions original = ResolveOptions(factory);

        Assert.Equal(
            Environments.Development,
            factory.Services
                .GetRequiredService<IHostEnvironment>()
                .EnvironmentName);
        Assert.False(original.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), original.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), original.FailureDelay);

        SimulationJobWorkerOptions clone = ResolveOptions(enabledFactory);

        Assert.Equal(
            Environments.Development,
            enabledFactory.Services
                .GetRequiredService<IHostEnvironment>()
                .EnvironmentName);
        Assert.True(clone.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), clone.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), clone.FailureDelay);
        Assert.False(ResolveOptions(factory).Enabled);
    }

    [Theory]
    [InlineData("PollingInterval", "00:00:00", "SimulationJobs:Worker:PollingInterval must be greater than 00:00:00.")]
    [InlineData("PollingInterval", "-00:00:01", "SimulationJobs:Worker:PollingInterval must be greater than 00:00:00.")]
    [InlineData("PollingInterval", "00:05:01", "SimulationJobs:Worker:PollingInterval must not exceed 00:05:00.")]
    [InlineData("FailureDelay", "00:00:00", "SimulationJobs:Worker:FailureDelay must be greater than 00:00:00.")]
    [InlineData("FailureDelay", "-00:00:01", "SimulationJobs:Worker:FailureDelay must be greater than 00:00:00.")]
    [InlineData("FailureDelay", "00:05:01", "SimulationJobs:Worker:FailureDelay must not exceed 00:05:00.")]
    public void InvalidDuration_FailsValidationWhileDisabled(
        string optionName,
        string value,
        string expectedMessage)
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> invalidFactory =
            Configure(factory, optionName, value);

        Exception exception = Assert.ThrowsAny<Exception>(
            () => _ = invalidFactory.Services);

        Assert.Contains(expectedMessage, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedDuration_FailsBindingWhileDisabled()
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> invalidFactory =
            Configure(factory, "PollingInterval", "not-a-duration");

        Exception exception = Assert.ThrowsAny<Exception>(
            () => _ = invalidFactory.Services);

        Assert.Contains("PollingInterval", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryDurations_AreValid()
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> validFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["SimulationJobs:Worker:PollingInterval"] = "00:05:00",
                                ["SimulationJobs:Worker:FailureDelay"] = "00:05:00"
                            });
                    });
            });

        SimulationJobWorkerOptions options = ResolveOptions(validFactory);

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromMinutes(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), options.FailureDelay);
    }

    [Fact]
    public void MissingValues_RetainCSharpDefaults()
    {
        ConfigurationManager configuration = new();
        configuration["SimulationJobs:Worker:Enabled"] = bool.FalseString;
        using ServiceProvider services = BuildOptionsServices(configuration);

        SimulationJobWorkerOptions options = services
            .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
            .Value;

        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
    }

    [Fact]
    public void MissingSection_RetainsCSharpDefaults()
    {
        ConfigurationManager configuration = new();
        using ServiceProvider services = BuildOptionsServices(configuration);

        SimulationJobWorkerOptions options = services
            .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
            .Value;

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
    }

    private static SimulationJobWorkerOptions ResolveOptions(
        WebApplicationFactory<Program> factory)
    {
        return factory.Services
            .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
            .Value;
    }

    private static WebApplicationFactory<Program> Configure(
        SingularFlowApiFactory factory,
        string optionName,
        string value)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                {
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            [$"SimulationJobs:Worker:{optionName}"] = value
                        });
                });
        });
    }

    private static ServiceProvider BuildOptionsServices(
        IConfiguration configuration)
    {
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services
            .AddOptions<SimulationJobWorkerOptions>()
            .BindConfiguration(SimulationJobWorkerOptions.SectionName);
        return services.BuildServiceProvider();
    }
}