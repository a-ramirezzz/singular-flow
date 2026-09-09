namespace SingularFlow.Domain.Models;

public sealed record BlowupState(
    double Time,
    double RemainingTime,
    double RadialLength,
    double AxialLength,
    double AngularVelocityScale,
    double RadialVelocityScale,
    double CoreVolumeScale,
    double CoreEnergyScale);