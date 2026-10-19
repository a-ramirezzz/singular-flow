using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

namespace SingularFlow.Application.Simulations;

public sealed class RunSimulationHandler
{
    public RunSimulationResult Handle(
        RunSimulationRequest request)
    {
        ValidatedSimulationRequest validatedRequest =
            SimulationRequestValidator.Validate(request);

        BlowupScalingCalculator calculator = new();

        BlowupSeriesGenerator seriesGenerator = new(
            calculator,
            validatedRequest.SamplingStrategy);

        IReadOnlyList<BlowupState> states =
            seriesGenerator.Generate(
                validatedRequest.BlowupParameters,
                validatedRequest.TimeSeriesParameters);

        return new RunSimulationResult(
            BlowupParameters:
                validatedRequest.BlowupParameters,
            TimeSeriesParameters:
                validatedRequest.TimeSeriesParameters,
            SamplingMode: request.SamplingMode,
            States: states);
    }
}