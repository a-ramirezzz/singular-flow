using Microsoft.EntityFrameworkCore;

using SingularFlow.Api.ErrorHandling;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

builder.Services
    .AddExceptionHandler<
        InvalidSimulationRequestExceptionHandler>();

builder.Services.AddScoped<RunSimulationHandler>();

builder.Services.AddScoped<RunAndSaveSimulationHandler>();

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

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;