using Microsoft.AspNetCore.Mvc;

using SingularFlow.Api.Contracts.Simulations;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Controllers;

[ApiController]
[Route("api/simulations")]
public sealed class SimulationsController : ControllerBase
{
    private readonly RunSimulationHandler _handler;

    public SimulationsController(
        RunSimulationHandler handler)
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
    public ActionResult<RunSimulationApiResponse> Run(
        RunSimulationApiRequest request)
    {
        RunSimulationRequest applicationRequest =
            SimulationContractMapper.ToApplication(request);

        RunSimulationResult applicationResult =
            _handler.Handle(applicationRequest);

        RunSimulationApiResponse response =
            SimulationContractMapper.ToApi(
                applicationResult);

        return Ok(response);
    }
}