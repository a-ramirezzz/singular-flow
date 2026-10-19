using System.Net;
using System.Runtime.ExceptionServices;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;

namespace SingularFlow.Api.Tests.Workers;

public sealed class SimulationJobWorkerCompositionTests
{
    [Fact]
    public void RuntimeServices_HaveApprovedTypesAndLifetimes()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> compositionFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        configuration.AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["ConnectionStrings:SingularFlow"] =
                                    "Host=127.0.0.1;Port=1;Database=singular_flow_tests;Username=inert;Password=inert"
                            });
                    });

                builder.ConfigureTestServices(services =>
                {
                    AssertScopedRegistration<RunSimulationHandler>(services);
                    AssertScopedRegistration<SingularFlowDbContext>(services);
                    AssertScopedRegistration<ISimulationRepository,
                        EfSimulationRepository>(services);
                    AssertScopedRegistration<ISimulationJobRepository,
                        EfSimulationJobRepository>(services);
                    AssertScopedRegistration<ISimulationJobCompletionRepository,
                        EfSimulationJobCompletionRepository>(services);
                    AssertScopedRegistration<ClaimSimulationJobHandler>(services);
                    AssertScopedRegistration<FailSimulationJobHandler>(services);
                    AssertScopedRegistration<ProcessNextSimulationJobHandler>(services);
                    AssertScopedRegistration<CreateSimulationJobHandler>(services);
                    AssertScopedRegistration<GetSimulationJobHandler>(services);
                    AssertSingletonRegistration<ISimulationJobWorkerDelay,
                        SimulationJobWorkerDelay>(services);
                    Assert.Single(
                        services,
                        service =>
                            service.ServiceType == typeof(IHostedService) &&
                            service.ImplementationType ==
                                typeof(SimulationJobWorker));
                    ServiceDescriptor configurationBinding = Assert.Single(
                        services,
                        service => service.ServiceType ==
                            typeof(IConfigureOptions<
                                SimulationJobWorkerOptions>));
                    Assert.NotNull(configurationBinding.ImplementationFactory);
                    ServiceDescriptor[] validationRegistrations = services
                        .Where(service => service.ServiceType ==
                            typeof(IValidateOptions<
                                SimulationJobWorkerOptions>))
                        .ToArray();
                    Assert.Equal(4, validationRegistrations.Length);
                    Assert.All(
                        validationRegistrations,
                        registration => Assert.NotNull(
                            registration.ImplementationInstance));
                    Assert.Contains(
                        services,
                        service => service.ServiceType ==
                            typeof(IStartupValidator));
                    Assert.DoesNotContain(
                        services,
                        service => service.ServiceType ==
                            typeof(CompleteSimulationJobHandler));
                    Assert.DoesNotContain(
                        services,
                        service => service.ServiceType ==
                            typeof(EfSimulationRepository));
                    Assert.DoesNotContain(
                        services,
                        service => service.ServiceType ==
                            typeof(EfSimulationJobRepository));
                    Assert.DoesNotContain(
                        services,
                        service => service.ServiceType ==
                            typeof(EfSimulationJobCompletionRepository));

                    services.RemoveAll<ISimulationJobRepository>();
                    services.RemoveAll<ISimulationJobCompletionRepository>();
                    services.AddSingleton(probe);
                    services.AddScoped<ISimulationJobRepository,
                        ControlledSimulationJobRepository>();
                    services.AddScoped<ISimulationJobCompletionRepository,
                        ControlledSimulationJobCompletionRepository>();
                });
            });
        IServiceProvider services = compositionFactory.Services;

        SimulationJobWorkerOptions options = services
            .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
            .Value;
        Assert.False(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), options.PollingInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.FailureDelay);
        Assert.Single(
            services.GetServices<IHostedService>(),
            service => service is SimulationJobWorker);
        ISimulationJobWorkerDelay delay =
            services.GetRequiredService<ISimulationJobWorkerDelay>();
        Assert.IsType<SimulationJobWorkerDelay>(delay);

        using IServiceScope firstScope = services.CreateScope();
        IServiceProvider first = firstScope.ServiceProvider;
        RunSimulationHandler run =
            first.GetRequiredService<RunSimulationHandler>();
        ClaimSimulationJobHandler claim =
            first.GetRequiredService<ClaimSimulationJobHandler>();
        FailSimulationJobHandler fail =
            first.GetRequiredService<FailSimulationJobHandler>();
        ProcessNextSimulationJobHandler process =
            first.GetRequiredService<ProcessNextSimulationJobHandler>();
        CreateSimulationJobHandler create =
            first.GetRequiredService<CreateSimulationJobHandler>();
        GetSimulationJobHandler get =
            first.GetRequiredService<GetSimulationJobHandler>();
        ISimulationJobRepository jobs =
            first.GetRequiredService<ISimulationJobRepository>();
        ISimulationJobCompletionRepository completion =
            first.GetRequiredService<ISimulationJobCompletionRepository>();
        SingularFlowDbContext context =
            first.GetRequiredService<SingularFlowDbContext>();
        Assert.NotNull(run);
        Assert.NotNull(claim);
        Assert.NotNull(fail);
        Assert.NotNull(process);
        Assert.NotNull(create);
        Assert.NotNull(get);
        Assert.IsType<ControlledSimulationJobRepository>(jobs);
        Assert.IsType<ControlledSimulationJobCompletionRepository>(completion);
        Assert.Equal(
            System.Data.ConnectionState.Closed,
            context.Database.GetDbConnection().State);
        Assert.Same(run, first.GetRequiredService<RunSimulationHandler>());
        Assert.Same(claim, first.GetRequiredService<ClaimSimulationJobHandler>());
        Assert.Same(fail, first.GetRequiredService<FailSimulationJobHandler>());
        Assert.Same(
            process,
            first.GetRequiredService<ProcessNextSimulationJobHandler>());
        Assert.Same(
            create,
            first.GetRequiredService<CreateSimulationJobHandler>());
        Assert.Same(
            get,
            first.GetRequiredService<GetSimulationJobHandler>());
        Assert.Same(jobs, first.GetRequiredService<ISimulationJobRepository>());
        Assert.Same(
            completion,
            first.GetRequiredService<ISimulationJobCompletionRepository>());
        Assert.Same(context, first.GetRequiredService<SingularFlowDbContext>());
        ISimulationJobWorkerDelay firstDelay =
            first.GetRequiredService<ISimulationJobWorkerDelay>();
        Assert.Same(delay, firstDelay);

        using IServiceScope secondScope = services.CreateScope();
        IServiceProvider second = secondScope.ServiceProvider;
        Assert.NotSame(run, second.GetRequiredService<RunSimulationHandler>());
        Assert.NotSame(
            claim,
            second.GetRequiredService<ClaimSimulationJobHandler>());
        Assert.NotSame(
            fail,
            second.GetRequiredService<FailSimulationJobHandler>());
        Assert.NotSame(
            process,
            second.GetRequiredService<ProcessNextSimulationJobHandler>());
        Assert.NotSame(
            create,
            second.GetRequiredService<CreateSimulationJobHandler>());
        Assert.NotSame(
            get,
            second.GetRequiredService<GetSimulationJobHandler>());
        Assert.NotSame(
            jobs,
            second.GetRequiredService<ISimulationJobRepository>());
        Assert.NotSame(
            completion,
            second.GetRequiredService<ISimulationJobCompletionRepository>());
        SingularFlowDbContext secondContext =
            second.GetRequiredService<SingularFlowDbContext>();
        Assert.NotSame(context, secondContext);
        Assert.Equal(
            System.Data.ConnectionState.Closed,
            secondContext.Database.GetDbConnection().State);
        Assert.Same(
            firstDelay,
            second.GetRequiredService<ISimulationJobWorkerDelay>());
        Assert.Null(first.GetService<CompleteSimulationJobHandler>());
        Assert.Null(first.GetService<EfSimulationRepository>());
        Assert.Null(first.GetService<EfSimulationJobRepository>());
        Assert.Null(first.GetService<EfSimulationJobCompletionRepository>());
        Assert.Equal(0, probe.ClaimCount);
        Assert.Equal(0, probe.CompletionCount);
        Assert.Equal(0, probe.FailureCount);
    }

    [Fact]
    public async Task SharedFactory_StartsDisabledInDevelopmentWithoutProcessing()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> controlledFactory =
            ConfigureControlledRepositories(factory, probe);
        using HttpClient client = controlledFactory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            Environments.Development,
            controlledFactory.Services
                .GetRequiredService<IHostEnvironment>()
                .EnvironmentName);
        Assert.False(
            controlledFactory.Services
                .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
                .Value.Enabled);
        Assert.Contains(
            controlledFactory.Services.GetServices<IHostedService>(),
            service => service is SimulationJobWorker);
        Assert.Equal(0, probe.ClaimCount);
        Assert.Equal(0, probe.CompletionCount);
        Assert.Equal(0, probe.FailureCount);
    }

    [Fact]
    public async Task EnabledHost_ProcessesNoWorkOnceWithoutOpeningDatabase()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        WebApplicationFactory<Program> enabledFactory =
            ConfigureControlledRepositories(
                factory,
                probe,
                enabled: true);
        using CancellationTokenSource safetyCancellation =
            new(TimeSpan.FromSeconds(30));
        Exception? testFailure = null;

        try
        {
            _ = enabledFactory.Services;
            CancellationToken processingToken =
                await probe.WaitForClaimAsync();

            Assert.True(
                enabledFactory.Services
                    .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
                    .Value.Enabled);
            Assert.False(processingToken.IsCancellationRequested);
            Assert.Equal(1, probe.ClaimCount);
            Assert.Equal(0, probe.CompletionCount);
            Assert.Equal(0, probe.FailureCount);
        }
        catch (Exception exception)
        {
            testFailure = exception;
        }

        try
        {
            await enabledFactory
                .DisposeAsync()
                .AsTask()
                .WaitAsync(safetyCancellation.Token);
        }
        catch (Exception disposalFailure) when (testFailure is not null)
        {
            throw new AggregateException(testFailure, disposalFailure);
        }

        if (testFailure is not null)
        {
            ExceptionDispatchInfo.Capture(testFailure).Throw();
        }
    }

    [Fact]
    public void InvalidOptions_FailApiStartupBeforeProcessing()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> invalidFactory =
            ConfigureControlledRepositories(
                factory,
                probe,
                pollingInterval: "00:00:00");

        Exception exception = Assert.ThrowsAny<Exception>(
            () => _ = invalidFactory.Services);

        Assert.Contains(
            "SimulationJobs:Worker:PollingInterval must be greater than 00:00:00.",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.Equal(0, probe.ClaimCount);
    }

    private static WebApplicationFactory<Program> ConfigureControlledRepositories(
        SingularFlowApiFactory factory,
        SimulationJobProcessingProbe probe,
        bool? enabled = null,
        string? pollingInterval = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            if (enabled is not null || pollingInterval is not null)
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) =>
                    {
                        Dictionary<string, string?> values = new();
                        if (enabled is not null)
                        {
                            values["SimulationJobs:Worker:Enabled"] =
                                enabled.Value.ToString();
                        }

                        if (pollingInterval is not null)
                        {
                            values["SimulationJobs:Worker:PollingInterval"] =
                                pollingInterval;
                        }

                        configuration.AddInMemoryCollection(values);
                    });
            }

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
    }

    private static void AssertScopedRegistration<TService,
        TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(TService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static void AssertScopedRegistration<TService>(
        IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(TService));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TService), descriptor.ImplementationType);
    }

    private static void AssertSingletonRegistration<TService,
        TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            service => service.ServiceType == typeof(TService));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }
}