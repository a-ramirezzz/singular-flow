namespace SingularFlow.Api.Contracts.Simulations;

public sealed record SimulationStateResponse(
    double Time,
    double RemainingTime,
    double RadialLength,
    double AxialLength,
    double AngularVelocityScale,
    double RadialVelocityScale,
    double CoreVolumeScale,
    double CoreEnergyScale);