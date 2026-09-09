using SingularFlow.Domain.Calculations;
using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Tests;

public sealed class BlowupScalingCalculatorTests
{
    private readonly BlowupScalingCalculator _calculator = new();

    [Fact]
    public void Calculate_WhenTimeApproachesSingularity_IncreasesAngularVelocity()
    {
        BlowupState earlierState = _calculator.Calculate(time: 0.9);
        BlowupState laterState = _calculator.Calculate(time: 0.99);

        Assert.True(
            laterState.AngularVelocityScale >
            earlierState.AngularVelocityScale);
    }

    [Fact]
    public void Calculate_WhenTimeApproachesSingularity_DecreasesCoreEnergy()
    {
        BlowupState earlierState = _calculator.Calculate(time: 0.9);
        BlowupState laterState = _calculator.Calculate(time: 0.99);

        Assert.True(
            laterState.CoreEnergyScale <
            earlierState.CoreEnergyScale);
    }

    [Fact]
    public void Calculate_WhenTimeEqualsSingularTime_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _calculator.Calculate(time: 1.0));
    }
}