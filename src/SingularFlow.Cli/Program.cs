using SingularFlow.Application.Simulations;

RunSimulationRequest request = new(
    SingularTime: 1.0,
    ConcentrationExponent: 0.005,
    StartTime: 0.0,
    EndTime: 0.9999,
    SampleCount: 6,
    SamplingMode: SamplingMode.Logarithmic);

RunSimulationHandler handler = new();

RunSimulationResult result =
    handler.Handle(request);

string samplingStrategyName =
    result.SamplingMode switch
    {
        SamplingMode.Uniform =>
            "Uniform time",

        SamplingMode.Logarithmic =>
            "Logarithmic remaining time",

        _ => throw new InvalidOperationException(
            "The simulation returned an unsupported " +
            "sampling mode.")
    };

Console.WriteLine(
    "SingularFlow — Blow-up scaling model");

Console.WriteLine();

Console.WriteLine(
    $"Singular time: " +
    $"{result.BlowupParameters.SingularTime:F4}");

Console.WriteLine(
    $"Concentration exponent: " +
    $"{result.BlowupParameters.ConcentrationExponent:F4}");

Console.WriteLine(
    $"Sampling strategy: " +
    $"{samplingStrategyName}");

Console.WriteLine(
    $"Sampling range: " +
    $"[{result.TimeSeriesParameters.StartTime:F4}, " +
    $"{result.TimeSeriesParameters.EndTime:F4}]");

Console.WriteLine(
    $"Sample count: " +
    $"{result.TimeSeriesParameters.SampleCount}");

Console.WriteLine();

Console.WriteLine(
    $"{"Time",10} " +
    $"{"Remaining",14} " +
    $"{"Radius",14} " +
    $"{"Axial",14} " +
    $"{"Angular velocity",20} " +
    $"{"Core energy",16}");

Console.WriteLine(new string('-', 96));

foreach (var state in result.States)
{
    Console.WriteLine(
        $"{state.Time,10:F4} " +
        $"{state.RemainingTime,14:E4} " +
        $"{state.RadialLength,14:E4} " +
        $"{state.AxialLength,14:E4} " +
        $"{state.AngularVelocityScale,20:E4} " +
        $"{state.CoreEnergyScale,16:E4}");
}