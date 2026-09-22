using Microsoft.AspNetCore.Mvc;

using SingularFlow.Api.Contracts.Simulations;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Controllers;

[ApiController]
[Route("api/simulations")]
public sealed class SimulationsController : ControllerBase
{
    private readonly RunAndSaveSimulationHandler _handler;

    public SimulationsController(
        RunAndSaveSimulationHandler handler)
    {
        _handler = handler;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(RunSimulationApiResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RunSimulationApiResponse>> Run(
    RunSimulationApiRequest request,
    CancellationToken cancellationToken)
    {
        RunSimulationRequest applicationRequest =
            SimulationContractMapper.ToApplication(request);

        RunSimulationResult applicationResult =
            await _handler.HandleAsync(
                applicationRequest,
                cancellationToken);

        RunSimulationApiResponse response =
            SimulationContractMapper.ToApi(
                applicationResult);

        return Ok(response);
    }
}