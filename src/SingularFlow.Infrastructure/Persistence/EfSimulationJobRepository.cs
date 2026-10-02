using Microsoft.EntityFrameworkCore;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobRepository :
    ISimulationJobRepository
{
    private readonly SingularFlowDbContext _context;

    public EfSimulationJobRepository(
        SingularFlowDbContext context)
    {
        _context = context;
    }

    public async Task<SimulationJobResult> CreateAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SimulationJobEntity job = new()
        {
            SingularTime = request.SingularTime,
            ConcentrationExponent =
                request.ConcentrationExponent,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SampleCount = request.SampleCount,
            SamplingMode = request.SamplingMode,
            Status = SimulationJobStatus.Pending
        };

        _context.SimulationJobs.Add(job);

        await _context.SaveChangesAsync(
            cancellationToken);

        return Map(job);
    }

    public async Task<SimulationJobResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        SimulationJobEntity? job =
            await _context.SimulationJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == id,
                    cancellationToken);

        return job is null
            ? null
            : Map(job);
    }

    private static SimulationJobResult Map(
        SimulationJobEntity job)
    {
        RunSimulationRequest request = new(
            SingularTime: job.SingularTime,
            ConcentrationExponent:
                job.ConcentrationExponent,
            StartTime: job.StartTime,
            EndTime: job.EndTime,
            SampleCount: job.SampleCount,
            SamplingMode: job.SamplingMode);

        return new SimulationJobResult(
            Id: job.Id,
            Request: request,
            Status: job.Status,
            CreatedAtUtc: job.CreatedAtUtc);
    }
}