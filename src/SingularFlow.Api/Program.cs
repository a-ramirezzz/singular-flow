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

builder.Services.AddScoped<ClaimSimulationJobHandler>();

builder.Services.AddScoped<FailSimulationJobHandler>();

builder.Services.AddScoped<ProcessNextSimulationJobHandler>();

builder.Services.AddScoped<
    ISimulationJobRepository,
    EfSimulationJobRepository>();

builder.Services.AddScoped<
    ISimulationJobCompletionRepository,
    EfSimulationJobCompletionRepository>();

builder.Services.AddSingleton<
    ISimulationJobWorkerDelay,
    SimulationJobWorkerDelay>();

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