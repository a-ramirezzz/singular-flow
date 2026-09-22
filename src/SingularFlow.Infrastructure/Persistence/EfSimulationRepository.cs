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

    public async Task SaveAsync(
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
    }
}