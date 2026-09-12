using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;
using SingularFlow.Domain.Sampling;

const string samplingStrategyName =
    "Logarithmic remaining time";

BlowupScalingCalculator calculator = new();

ITimeSamplingStrategy samplingStrategy =
    new LogarithmicTimeSamplingStrategy();

BlowupSeriesGenerator seriesGenerator = new(
    calculator,
    samplingStrategy);

BlowupParameters blowupParameters =
    BlowupParameters.Default;

TimeSeriesParameters seriesParameters = new(
    startTime: 0.0,
    endTime: 0.9999,
    sampleCount: 6);

IReadOnlyList<BlowupState> states =
    seriesGenerator.Generate(
        blowupParameters,
        seriesParameters);

Console.WriteLine(
    "SingularFlow — Blow-up scaling model");

Console.WriteLine();

Console.WriteLine(
    $"Singular time: " +
    $"{blowupParameters.SingularTime:F4}");

Console.WriteLine(
    $"Concentration exponent: " +
    $"{blowupParameters.ConcentrationExponent:F4}");

Console.WriteLine(
    $"Sampling strategy: " +
    $"{samplingStrategyName}");

Console.WriteLine(
    $"Sampling range: " +
    $"[{seriesParameters.StartTime:F4}, " +
    $"{seriesParameters.EndTime:F4}]");

Console.WriteLine(
    $"Sample count: " +
    $"{seriesParameters.SampleCount}");

Console.WriteLine();

Console.WriteLine(
    $"{"Time",10} " +
    $"{"Remaining",14} " +
    $"{"Radius",14} " +
    $"{"Axial",14} " +
    $"{"Angular velocity",20} " +
    $"{"Core energy",16}");

Console.WriteLine(new string('-', 96));

foreach (BlowupState state in states)
{
    Console.WriteLine(
        $"{state.Time,10:F4} " +
        $"{state.RemainingTime,14:E4} " +
        $"{state.RadialLength,14:E4} " +
        $"{state.AxialLength,14:E4} " +
        $"{state.AngularVelocityScale,20:E4} " +
        $"{state.CoreEnergyScale,16:E4}");
}