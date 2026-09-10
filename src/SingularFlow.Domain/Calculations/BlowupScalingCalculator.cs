using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Calculations;

public sealed class BlowupScalingCalculator
{
    public BlowupState Calculate(
        double time,
        BlowupParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        ValidateTime(time, parameters.SingularTime);

        double remainingTime =
            parameters.SingularTime - time;

        double h = parameters.ConcentrationExponent;

        double radialLength =
            Math.Pow(remainingTime, 0.5);

        double axialLength =
            Math.Pow(remainingTime, 0.5 - h);

        double angularVelocityScale =
            Math.Pow(remainingTime, -0.5 - h);

        double radialVelocityScale =
            Math.Pow(remainingTime, -0.5);

        double coreVolumeScale =
            Math.Pow(remainingTime, 1.5 - h);

        double coreEnergyScale =
            Math.Pow(remainingTime, 0.5 - (3.0 * h));

        return new BlowupState(
            Time: time,
            RemainingTime: remainingTime,
            RadialLength: radialLength,
            AxialLength: axialLength,
            AngularVelocityScale: angularVelocityScale,
            RadialVelocityScale: radialVelocityScale,
            CoreVolumeScale: coreVolumeScale,
            CoreEnergyScale: coreEnergyScale);
    }

    private static void ValidateTime(
        double time,
        double singularTime)
    {
        if (!double.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                "Time must be a finite number.");
        }

        if (time < 0 || time >= singularTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                "Time must be greater than or equal to zero " +
                "and less than singular time.");
        }
    }
}