using Microsoft.AspNetCore.Mvc;

using SingularFlow.Api.Contracts.SimulationJobs;
using SingularFlow.Api.Contracts.Simulations;
using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Api.Controllers;

[ApiController]
[Route("api/simulation-jobs")]
public sealed class SimulationJobsController : ControllerBase
{
    private readonly CreateSimulationJobHandler _createHandler;
    private readonly GetSimulationJobHandler _getHandler;

    public SimulationJobsController(
        CreateSimulationJobHandler createHandler,
        GetSimulationJobHandler getHandler)
    {
        _createHandler = createHandler;
        _getHandler = getHandler;
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(SimulationJobApiResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SimulationJobApiResponse>> Create(
        RunSimulationApiRequest request,
        CancellationToken cancellationToken)
    {
        RunSimulationRequest applicationRequest =
            SimulationContractMapper.ToApplication(request);

        SimulationJobResult result =
            await _createHandler.HandleAsync(
                applicationRequest,
                cancellationToken);

        SimulationJobApiResponse response =
            SimulationJobContractMapper.ToApi(result);

        return AcceptedAtAction(
            actionName: nameof(GetById),
            routeValues: new
            {
                id = response.Id
            },
            value: response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(
        typeof(SimulationJobApiResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SimulationJobApiResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        SimulationJobResult? result =
            await _getHandler.HandleAsync(
                id,
                cancellationToken);

        if (result is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";

        return Ok(SimulationJobContractMapper.ToApi(result));
    }
}