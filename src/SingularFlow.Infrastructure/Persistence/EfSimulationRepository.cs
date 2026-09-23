using Microsoft.EntityFrameworkCore;

using SingularFlow.Application.Simulations;
using SingularFlow.Domain.Models;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationRepository : ISimulationRepository
{
    private readonly SingularFlowDbContext _context;

    public EfSimulationRepository(
        SingularFlowDbContext context)
    {
        _context = context;
    }

    public async Task<PersistedSimulationResult> SaveAsync(
        RunSimulationResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        SimulationEntity simulation = new()
        {
            SingularTime =
                result.BlowupParameters.SingularTime,

            ConcentrationExponent =
                result.BlowupParameters.ConcentrationExponent,

            StartTime =
                result.TimeSeriesParameters.StartTime,

            EndTime =
                result.TimeSeriesParameters.EndTime,

            SampleCount =
                result.TimeSeriesParameters.SampleCount,

            SamplingMode = result.SamplingMode
        };

        for (int index = 0; index < result.States.Count; index++)
        {
            BlowupState state = result.States[index];

            simulation.States.Add(
                new SimulationStateEntity
                {
                    Sequence = index,
                    Time = state.Time,
                    RemainingTime = state.RemainingTime,
                    RadialLength = state.RadialLength,
                    AxialLength = state.AxialLength,
                    AngularVelocityScale =
                        state.AngularVelocityScale,
                    RadialVelocityScale =
                        state.RadialVelocityScale,
                    CoreVolumeScale =
                        state.CoreVolumeScale,
                    CoreEnergyScale =
                        state.CoreEnergyScale
                });
        }

        _context.Simulations.Add(simulation);

        await _context.SaveChangesAsync(
            cancellationToken);

        return new PersistedSimulationResult(
            Id: simulation.Id,
            CreatedAtUtc: simulation.CreatedAtUtc,
            Simulation: result);
    }

    public async Task<PersistedSimulationResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        SimulationEntity? simulation =
            await _context.Simulations
                .AsNoTracking()
                .Include(
                    entity => entity.States)
                .SingleOrDefaultAsync(
                    entity => entity.Id == id,
                    cancellationToken);

        if (simulation is null)
        {
            return null;
        }

        BlowupState[] states =
            simulation.States
                .OrderBy(state => state.Sequence)
                .Select(state => new BlowupState(
                    Time: state.Time,
                    RemainingTime: state.RemainingTime,
                    RadialLength: state.RadialLength,
                    AxialLength: state.AxialLength,
                    AngularVelocityScale:
                        state.AngularVelocityScale,
                    RadialVelocityScale:
                        state.RadialVelocityScale,
                    CoreVolumeScale:
                        state.CoreVolumeScale,
                    CoreEnergyScale:
                        state.CoreEnergyScale))
                .ToArray();

        BlowupParameters blowupParameters = new(
            singularTime: simulation.SingularTime,
            concentrationExponent:
                simulation.ConcentrationExponent);

        TimeSeriesParameters timeSeriesParameters = new(
            startTime: simulation.StartTime,
            endTime: simulation.EndTime,
            sampleCount: simulation.SampleCount);

        RunSimulationResult result = new(
            BlowupParameters: blowupParameters,
            TimeSeriesParameters: timeSeriesParameters,
            SamplingMode: simulation.SamplingMode,
            States: states);

        return new PersistedSimulationResult(
            Id: simulation.Id,
            CreatedAtUtc: simulation.CreatedAtUtc,
            Simulation: result);
    }
}