using System.Net;

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
    public void RuntimeServices_HaveLeaseAwareTypesAndLifetimes()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> compositionFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration(
                    (_, configuration) => configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:SingularFlow"] =
                                "Host=127.0.0.1;Port=1;Database=singular_flow_tests;Username=inert;Password=inert"
                        }));
                builder.ConfigureTestServices(services =>
                {
                    AssertScoped<RunSimulationHandler>(services);
                    AssertScoped<CreateSimulationJobHandler>(services);
                    AssertScoped<GetSimulationJobHandler>(services);
                    AssertScoped<FailSimulationJobHandler>(services);
                    AssertScoped<CompleteSimulationJobHandler>(services);
                    AssertScoped<ClaimSimulationJobLeaseHandler>(services);
                    AssertScoped<RenewSimulationJobLeaseHandler>(services);
                    AssertScoped<RecoverExpiredSimulationJobsHandler>(services);
                    AssertScoped<ProcessClaimedSimulationJobHandler>(services);
                    Type[] registeredJobHandlers = services
                        .Where(descriptor =>
                            descriptor.ServiceType.Namespace ==
                            typeof(CreateSimulationJobHandler).Namespace &&
                            descriptor.ServiceType.Name.EndsWith(
                                "Handler",
                                StringComparison.Ordinal))
                        .Select(descriptor => descriptor.ServiceType)
                        .OrderBy(type => type.FullName)
                        .ToArray();
                    Type[] expectedJobHandlers =
                    [
                        typeof(ClaimSimulationJobLeaseHandler),
                        typeof(CompleteSimulationJobHandler),
                        typeof(CreateSimulationJobHandler),
                        typeof(FailSimulationJobHandler),
                        typeof(GetSimulationJobHandler),
                        typeof(ProcessClaimedSimulationJobHandler),
                        typeof(RecoverExpiredSimulationJobsHandler),
                        typeof(RenewSimulationJobLeaseHandler)
                    ];
                    Assert.Equal(
                        expectedJobHandlers.OrderBy(type => type.FullName),
                        registeredJobHandlers);
                    AssertScoped<SingularFlowDbContext>(services);
                    AssertScoped<ISimulationJobRepository, EfSimulationJobRepository>(services);
                    AssertScoped<ISimulationJobCompletionRepository, EfSimulationJobCompletionRepository>(services);
                    AssertScoped<ISimulationJobLeaseRepository, EfSimulationJobLeaseRepository>(services);
                    AssertSingleton<ISimulationJobWorkerDelay, SimulationJobWorkerDelay>(services);
                    AssertSingleton<SimulationJobLeaseSupervisor>(services);
                    Assert.Single(
                        services,
                        descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                            descriptor.ImplementationType == typeof(SimulationJobWorker));
                    Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(SimulationJobWorker));
                    Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfSimulationJobRepository));
                    Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfSimulationJobCompletionRepository));
                    Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(EfSimulationJobLeaseRepository));

                    services.RemoveAll<ISimulationJobRepository>();
                    services.RemoveAll<ISimulationJobCompletionRepository>();
                    services.RemoveAll<ISimulationJobLeaseRepository>();
                    services.AddSingleton(probe);
                    services.AddScoped<ISimulationJobRepository, ControlledSimulationJobRepository>();
                    services.AddScoped<ISimulationJobCompletionRepository, ControlledSimulationJobCompletionRepository>();
                    services.AddScoped<ISimulationJobLeaseRepository, ControlledSimulationJobLeaseRepository>();
                });
            });

        IServiceProvider root = compositionFactory.Services;
        ISimulationJobWorkerDelay delay = root.GetRequiredService<ISimulationJobWorkerDelay>();
        SimulationJobLeaseSupervisor supervisor = root.GetRequiredService<SimulationJobLeaseSupervisor>();
        using IServiceScope firstScope = root.CreateScope();
        using IServiceScope secondScope = root.CreateScope();
        IServiceProvider first = firstScope.ServiceProvider;
        IServiceProvider second = secondScope.ServiceProvider;

        Assert.Same(delay, first.GetRequiredService<ISimulationJobWorkerDelay>());
        Assert.Same(delay, second.GetRequiredService<ISimulationJobWorkerDelay>());
        Assert.Same(supervisor, first.GetRequiredService<SimulationJobLeaseSupervisor>());
        Assert.Same(supervisor, second.GetRequiredService<SimulationJobLeaseSupervisor>());
        AssertScopedIdentity<ClaimSimulationJobLeaseHandler>(first, second);
        AssertScopedIdentity<RenewSimulationJobLeaseHandler>(first, second);
        AssertScopedIdentity<RecoverExpiredSimulationJobsHandler>(first, second);
        AssertScopedIdentity<ProcessClaimedSimulationJobHandler>(first, second);
        Assert.IsType<ControlledSimulationJobRepository>(first.GetRequiredService<ISimulationJobRepository>());
        Assert.IsType<ControlledSimulationJobCompletionRepository>(first.GetRequiredService<ISimulationJobCompletionRepository>());
        Assert.IsType<ControlledSimulationJobLeaseRepository>(first.GetRequiredService<ISimulationJobLeaseRepository>());
        SingularFlowDbContext context = first.GetRequiredService<SingularFlowDbContext>();
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(0, probe.ClaimCount);
        Assert.Equal(0, probe.RecoveryCount);
        Assert.Equal(0, probe.CompletionCount);
        Assert.Equal(0, probe.FailureCount);
    }

    [Fact]
    public async Task SharedFactory_StartsDisabledWithoutWorkerActivity()
    {
        SimulationJobProcessingProbe probe = new();
        using SingularFlowApiFactory factory = new();
        using WebApplicationFactory<Program> controlledFactory =
            factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ISimulationJobRepository>();
                    services.RemoveAll<ISimulationJobCompletionRepository>();
                    services.RemoveAll<ISimulationJobLeaseRepository>();
                    services.AddSingleton(probe);
                    services.AddScoped<ISimulationJobRepository, ControlledSimulationJobRepository>();
                    services.AddScoped<ISimulationJobCompletionRepository, ControlledSimulationJobCompletionRepository>();
                    services.AddScoped<ISimulationJobLeaseRepository, ControlledSimulationJobLeaseRepository>();
                });
            });
        using HttpClient client = controlledFactory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(controlledFactory.Services
            .GetRequiredService<IOptions<SimulationJobWorkerOptions>>()
            .Value.Enabled);
        Assert.Equal(0, probe.ClaimCount);
        Assert.Equal(0, probe.RecoveryCount);
        Assert.Equal(0, probe.CompletionCount);
        Assert.Equal(0, probe.FailureCount);
    }

    private static void AssertScoped<TService>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TService), descriptor.ImplementationType);
    }

    private static void AssertScoped<TService, TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static void AssertSingleton<TService>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TService), descriptor.ImplementationType);
    }

    private static void AssertSingleton<TService, TImplementation>(IServiceCollection services)
    {
        ServiceDescriptor descriptor = Assert.Single(
            services,
            item => item.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static void AssertScopedIdentity<TService>(IServiceProvider first, IServiceProvider second)
        where TService : class
    {
        TService firstValue = first.GetRequiredService<TService>();
        Assert.Same(firstValue, first.GetRequiredService<TService>());
        Assert.NotSame(firstValue, second.GetRequiredService<TService>());
    }
}