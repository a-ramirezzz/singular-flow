using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;

BlowupScalingCalculator calculator = new();
BlowupParameters parameters = BlowupParameters.Default;

double[] times =
[
    0.0,
    0.5,
    0.9,
    0.99,
    0.999,
    0.9999
];

Console.WriteLine("SingularFlow — Blow-up scaling model");
Console.WriteLine();
Console.WriteLine($"Singular time: {parameters.SingularTime:F4}");
Console.WriteLine(
    $"Concentration exponent: " +
    $"{parameters.ConcentrationExponent:F4}");

Console.WriteLine();

Console.WriteLine(
    $"{"Time",10} " +
    $"{"Remaining",14} " +
    $"{"Radius",14} " +
    $"{"Axial",14} " +
    $"{"Angular velocity",20} " +
    $"{"Core energy",16}");

Console.WriteLine(new string('-', 96));

foreach (double time in times)
{
    BlowupState state = calculator.Calculate(
        time,
        parameters);

    Console.WriteLine(
        $"{state.Time,10:F4} " +
        $"{state.RemainingTime,14:E4} " +
        $"{state.RadialLength,14:E4} " +
        $"{state.AxialLength,14:E4} " +
        $"{state.AngularVelocityScale,20:E4} " +
        $"{state.CoreEnergyScale,16:E4}");
}