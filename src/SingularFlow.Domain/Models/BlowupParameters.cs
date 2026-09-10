namespace SingularFlow.Domain.Models;

public sealed record BlowupParameters
{
    public const double MaximumConcentrationExponent = 0.01;

    public static BlowupParameters Default { get; } = new(
        singularTime: 1.0,
        concentrationExponent: 0.005);

    public double SingularTime { get; }

    public double ConcentrationExponent { get; }

    public BlowupParameters(
        double singularTime,
        double concentrationExponent)
    {
        ValidateSingularTime(singularTime);
        ValidateConcentrationExponent(concentrationExponent);

        SingularTime = singularTime;
        ConcentrationExponent = concentrationExponent;
    }

    private static void ValidateSingularTime(double singularTime)
    {
        if (!double.IsFinite(singularTime) || singularTime <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(singularTime),
                "Singular time must be a finite positive number.");
        }
    }

    private static void ValidateConcentrationExponent(
        double concentrationExponent)
    {
        if (!double.IsFinite(concentrationExponent) ||
            concentrationExponent <= 0 ||
            concentrationExponent >= MaximumConcentrationExponent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concentrationExponent),
                $"Concentration exponent must satisfy " +
                $"0 < h < {MaximumConcentrationExponent}.");
        }
    }
}