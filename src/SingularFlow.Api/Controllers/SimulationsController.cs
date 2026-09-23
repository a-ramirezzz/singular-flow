using Microsoft.AspNetCore.Mvc;

using SingularFlow.Api.Contracts.Simulations;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Controllers;

[ApiController]
[Route("api/simulations")]
public sealed class SimulationsController : ControllerBase
{
    private readonly RunAndSaveSimulationHandler _runHandler;
    private readonly GetSimulationHandler _getHandler;

    public SimulationsController(
        RunAndSaveSimulationHandler runHandler,
        GetSimulationHandler getHandler)
    {
        _runHandler = runHandler;
        _getHandler = getHandler;
    }

    [HttpGet(
        "{id:guid}",
        Name = nameof(GetById))]
    [ProducesResponseType(
        typeof(RunSimulationApiResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunSimulationApiResponse>>
        GetById(
            Guid id,
            CancellationToken cancellationToken)
    {
        PersistedSimulationResult? result =
            await _getHandler.HandleAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        RunSimulationApiResponse response =
            SimulationContractMapper.ToApi(
                result);

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(RunSimulationApiResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RunSimulationApiResponse>> Run(
        RunSimulationApiRequest request,
        CancellationToken cancellationToken)
    {
        RunSimulationRequest applicationRequest =
            SimulationContractMapper.ToApplication(request);

        PersistedSimulationResult applicationResult =
            await _runHandler.HandleAsync(
                applicationRequest,
                cancellationToken);

        RunSimulationApiResponse response =
            SimulationContractMapper.ToApi(
                applicationResult);

        return CreatedAtRoute(
            routeName: nameof(GetById),
            routeValues: new
            {
                id = response.Id
            },
            value: response);
    }
}