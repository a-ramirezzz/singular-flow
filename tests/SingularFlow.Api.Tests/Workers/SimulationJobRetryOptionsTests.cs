using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobRetryOptionsTests
{
    [Fact]
    public void SectionName_IsApproved()
    {
        Assert.Equal(
            "SimulationJobs:Retry",
            SimulationJobRetryOptions.SectionName);
    }

    [Fact]
    public void Defaults_AreApproved()
    {
        SimulationJobRetryOptions options = new();

        Assert.Equal(3, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), options.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaximumDelay);
    }

    [Theory]
    [InlineData("MaxAttempts", "0")]
    [InlineData("MaxAttempts", "11")]
    [InlineData("BaseDelay", "00:00:00")]
    [InlineData("BaseDelay", "-00:00:01")]
    [InlineData("MaximumDelay", "00:00:00")]
    [InlineData("MaximumDelay", "00:05:01")]
    public void InvalidValue_FailsHostStartupWhileWorkerDisabled(
        string optionName,
        string value)
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> invalidFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                [$"SimulationJobs:Retry:{optionName}"] = value
                            });
                    });
            });

        Exception exception = Assert.ThrowsAny<Exception>(
            () => _ = invalidFactory.Services);

        Assert.Contains(
            SimulationJobRetryOptions.SectionName,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MaximumDelayBeforeBaseDelay_FailsHostStartup()
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> invalidFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["SimulationJobs:Retry:BaseDelay"] = "00:00:06",
                                ["SimulationJobs:Retry:MaximumDelay"] = "00:00:05"
                            });
                    });
            });

        Exception exception = Assert.ThrowsAny<Exception>(
            () => _ = invalidFactory.Services);

        Assert.Contains(
            SimulationJobRetryOptions.SectionName,
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CustomConfiguration_CreatesOneSharedPolicyWithExactValues()
    {
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> configuredFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["SimulationJobs:Retry:MaxAttempts"] = "8",
                                ["SimulationJobs:Retry:BaseDelay"] = "00:00:03",
                                ["SimulationJobs:Retry:MaximumDelay"] = "00:01:00"
                            });
                    });
            });

        IServiceProvider root = configuredFactory.Services;
        SimulationJobRetryPolicy policy =
            root.GetRequiredService<SimulationJobRetryPolicy>();
        using IServiceScope scope = root.CreateScope();

        Assert.Equal(8, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(3), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(1), policy.MaximumDelay);
        Assert.Same(
            policy,
            scope.ServiceProvider
                .GetRequiredService<SimulationJobRetryPolicy>());
    }
}