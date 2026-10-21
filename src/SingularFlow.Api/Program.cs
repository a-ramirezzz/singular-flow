using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

using SingularFlow.Api.ErrorHandling;
using SingularFlow.Api.Workers;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<SingularFlowDbContext>(
        name: "postgresql",
        tags: ["ready"]);
builder.Services.AddProblemDetails();

builder.Services
    .AddExceptionHandler<
        InvalidSimulationRequestExceptionHandler>();

builder.Services.AddScoped<RunSimulationHandler>();

builder.Services.AddScoped<RunAndSaveSimulationHandler>();

builder.Services.AddScoped<GetSimulationHandler>();

builder.Services.AddScoped<ListSimulationsHandler>();

builder.Services.AddScoped<DeleteSimulationHandler>();

builder.Services.AddScoped<CreateSimulationJobHandler>();

builder.Services.AddScoped<GetSimulationJobHandler>();

builder.Services
    .AddOptions<SimulationJobWorkerOptions>()
    .BindConfiguration(
        SimulationJobWorkerOptions.SectionName)
    .Validate(
        options => options.PollingInterval > TimeSpan.Zero,
        "SimulationJobs:Worker:PollingInterval must be greater than 00:00:00.")
    .Validate(
        options => options.PollingInterval <= TimeSpan.FromMinutes(5),
        "SimulationJobs:Worker:PollingInterval must not exceed 00:05:00.")
    .Validate(
        options => options.FailureDelay > TimeSpan.Zero,
        "SimulationJobs:Worker:FailureDelay must be greater than 00:00:00.")
    .Validate(
        options => options.FailureDelay <= TimeSpan.FromMinutes(5),
        "SimulationJobs:Worker:FailureDelay must not exceed 00:05:00.")
    .Validate(
        options => options.LeaseDuration > TimeSpan.Zero,
        "SimulationJobs:Worker:LeaseDuration must be greater than 00:00:00.")
    .Validate(
        options => options.LeaseDuration <= TimeSpan.FromMinutes(5),
        "SimulationJobs:Worker:LeaseDuration must not exceed 00:05:00.")
    .Validate(
        options => options.HeartbeatInterval > TimeSpan.Zero,
        "SimulationJobs:Worker:HeartbeatInterval must be greater than 00:00:00.")
    .Validate(
        options => options.HeartbeatInterval <= TimeSpan.FromMinutes(5),
        "SimulationJobs:Worker:HeartbeatInterval must not exceed 00:05:00.")
    .Validate(
        options => options.HeartbeatInterval < options.LeaseDuration,
        "SimulationJobs:Worker:HeartbeatInterval must be less than LeaseDuration.")
    .Validate(
        options => options.RecoveryInterval > TimeSpan.Zero,
        "SimulationJobs:Worker:RecoveryInterval must be greater than 00:00:00.")
    .Validate(
        options => options.RecoveryInterval <= TimeSpan.FromMinutes(5),
        "SimulationJobs:Worker:RecoveryInterval must not exceed 00:05:00.")
    .Validate(
        options => options.RecoveryBatchSize >= 1,
        "SimulationJobs:Worker:RecoveryBatchSize must be at least 1.")
    .Validate(
        options => options.RecoveryBatchSize <= 1000,
        "SimulationJobs:Worker:RecoveryBatchSize must not exceed 1000.")
    .ValidateOnStart();

builder.Services.AddDbContext<SingularFlowDbContext>(options =>
{
    string connectionString =
        builder.Configuration.GetConnectionString("SingularFlow")
        ?? throw new InvalidOperationException(
            "Connection string 'SingularFlow' is required.");

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<
    ISimulationRepository,
    EfSimulationRepository>();

builder.Services.AddScoped<FailSimulationJobHandler>();

builder.Services.AddScoped<CompleteSimulationJobHandler>();

builder.Services.AddScoped<ClaimSimulationJobLeaseHandler>();

builder.Services.AddScoped<RenewSimulationJobLeaseHandler>();

builder.Services.AddScoped<RecoverExpiredSimulationJobsHandler>();

builder.Services.AddScoped<ProcessClaimedSimulationJobHandler>();

builder.Services.AddScoped<
    ISimulationJobRepository,
    EfSimulationJobRepository>();

builder.Services.AddScoped<
    ISimulationJobCompletionRepository,
    EfSimulationJobCompletionRepository>();

builder.Services.AddScoped<
    ISimulationJobLeaseRepository,
    EfSimulationJobLeaseRepository>();

builder.Services.AddSingleton<
    ISimulationJobWorkerDelay,
    SimulationJobWorkerDelay>();

builder.Services.AddSingleton<SimulationJobLeaseSupervisor>();

builder.Services.AddHostedService<SimulationJobWorker>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (builder.Configuration.GetValue(
        "HttpsRedirection:Enabled",
        defaultValue: true))
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false
    });

app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate =
            healthCheck =>
                healthCheck.Tags.Contains("ready")
    });

app.MapHealthChecks(
    "/health",
    new HealthCheckOptions
    {
        Predicate =
            healthCheck =>
                healthCheck.Tags.Contains("ready")
    });

app.Run();

public partial class Program;