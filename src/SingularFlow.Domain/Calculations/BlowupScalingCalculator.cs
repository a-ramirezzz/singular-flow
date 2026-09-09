using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Calculations;

public sealed class BlowupScalingCalculator
{
    public BlowupState Calculate(
        double time,
        double singularTime = 1.0,
        double h = 0.005)
    {
        ValidateParameters(time, singularTime, h);

        double remainingTime = singularTime - time;

        double radialLength = Math.Pow(remainingTime, 0.5);
        double axialLength = Math.Pow(remainingTime, 0.5 - h);

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

    private static void ValidateParameters(
        double time,
        double singularTime,
        double h)
    {
        if (!double.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                "Time must be a finite number.");
        }

        if (!double.IsFinite(singularTime) || singularTime <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(singularTime),
                "Singular time must be a finite positive number.");
        }

        if (time < 0 || time >= singularTime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                "Time must be greater than or equal to zero and less than singular time.");
        }

        if (!double.IsFinite(h) || h <= 0 || h >= 0.01)
        {
            throw new ArgumentOutOfRangeException(
                nameof(h),
                "The parameter h must satisfy 0 < h < 0.01.");
        }
    }
}