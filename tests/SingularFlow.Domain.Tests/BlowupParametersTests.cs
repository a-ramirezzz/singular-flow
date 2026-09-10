using SingularFlow.Domain.Models;

namespace SingularFlow.Domain.Tests;

public sealed class BlowupParametersTests
{
    [Fact]
    public void Constructor_WithValidValues_CreatesParameters()
    {
        BlowupParameters parameters = new(
            singularTime: 2.0,
            concentrationExponent: 0.005);

        Assert.Equal(2.0, parameters.SingularTime);
        Assert.Equal(
            0.005,
            parameters.ConcentrationExponent);
    }

    [Fact]
    public void Constructor_WithNonPositiveSingularTime_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BlowupParameters(
                singularTime: 0.0,
                concentrationExponent: 0.005));
    }

    [Fact]
    public void Constructor_WithExponentAtMaximum_ThrowsException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BlowupParameters(
                singularTime: 1.0,
                concentrationExponent:
                    BlowupParameters.MaximumConcentrationExponent));
    }
}