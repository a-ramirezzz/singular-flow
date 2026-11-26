using Microsoft.Extensions.Options;

using SingularFlow.Application.SimulationJobs;

namespace SingularFlow.Api.Workers;

internal sealed class SimulationJobRetryOptionsValidator :
    IValidateOptions<SimulationJobRetryOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        SimulationJobRetryOptions options)
    {
        try
        {
            _ = new SimulationJobRetryPolicy(
                options.MaxAttempts,
                options.BaseDelay,
                options.MaximumDelay);

            return ValidateOptionsResult.Success;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return ValidateOptionsResult.Fail(
                $"{SimulationJobRetryOptions.SectionName} contains an " +
                $"invalid {exception.ParamName} value.");
        }
    }
}