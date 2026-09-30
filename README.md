# SingularFlow

[![Continuous Integration](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml/badge.svg)](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml)

SingularFlow is an educational .NET application for exploring the asymptotic scaling of a concentrating vortex near a theoretical finite-time singularity.

The project is inspired by the finite-time blow-up construction for the three-dimensional incompressible Navier–Stokes equations published by OpenAI in September 2026.

## Project status

SingularFlow is currently under active development.

The current version calculates individual vortex-core scaling states and generates time series using interchangeable uniform and logarithmic sampling strategies.

Application orchestration is separated from the mathematical domain through a dedicated use case. The command-line interface creates a simulation request, delegates execution to the application layer, and displays the structured result.

The ASP.NET Core Web API supports creation, paginated listing, retrieval by ID, and deletion of persisted simulations. It executes simulations over HTTP with uniform or logarithmic sampling, exposes separate process-liveness and database-readiness health checks, and generates an OpenAPI document in Development. Invalid requests receive standardized HTTP errors.

The command-line application currently uses logarithmic remaining-time sampling to provide greater resolution near the configured singular time.

The complete local application stack can run through Docker Compose as separate ASP.NET Core API and PostgreSQL 18.6 services. Compose builds the API from the root `Dockerfile`, waits for PostgreSQL to become healthy before starting it, and can wait for both services to report healthy. The services communicate over the internal Compose network, where the API uses `postgres:5432` rather than PostgreSQL's host-published port.

`SingularFlow.Infrastructure` defines the EF Core schema, mappings, initial migration, and repository implementation. The containerized API uses the same Npgsql and EF Core persistence path as direct local execution: it creates, lists, retrieves, and deletes simulations while persisting their ordered states in the separate PostgreSQL service. This local Compose stack is a development environment, not a complete production deployment platform.

The project is being developed incrementally with Domain and Application unit tests, API integration tests, continuous integration, protected branches, pull requests, and documented architectural decisions.

## Current functionality

* Calculate the remaining time before the theoretical singularity.
* Calculate radial and axial length scales.
* Calculate radial and angular velocity scales.
* Calculate core volume and energy scales.
* Represent mathematical configuration through immutable domain value objects.
* Validate singular time and concentration exponent values.
* Validate time-series start time, end time, and sample count.
* Reject non-finite, negative, incompatible, and out-of-range values.
* Generate uniformly spaced time samples.
* Generate logarithmically spaced remaining-time samples.
* Select sampling behavior through the Strategy design pattern.
* Guarantee that generated sequences include both configured endpoints.
* Prevent a generated series from reaching or exceeding the singular time.
* Return generated times and states through read-only collections.
* Represent simulation input through a structured application request.
* Execute simulations through a dedicated application use case.
* Select the requested sampling mode inside the application layer.
* Return simulation configuration and generated states through a structured result.
* Keep command-line presentation separate from mathematical calculations.
* Display the active mathematical and sampling configuration.
* Display calculated states through a command-line interface.
* Verify domain, application, API, and Infrastructure behavior with 95 automated tests: 42 Domain, 14 Application, 23 API, and 16 Infrastructure tests.
* Validate every pull request and push to `main` with GitHub Actions, including a build of the API Docker image.
* Host an ASP.NET Core Web API.
* Expose `GET /health/live` for process liveness, `GET /health/ready` for PostgreSQL connectivity readiness, and `GET /health` as a backward-compatible readiness alias.
* Generate an OpenAPI 3.1.1 document in Development.
* Test the real ASP.NET Core HTTP pipeline in memory.
* Create persisted simulations through `POST /api/simulations` with `uniform` or `logarithmic` sampling.
* Persist each API simulation and its ordered states to PostgreSQL before returning the calculated series.
* Return `201 Created` with the persisted `id`, database-generated `createdAtUtc`, and a `Location` header.
* Retrieve persisted simulations through `GET /api/simulations/{id}` and return `404 Not Found` for an unknown ID.
* List persisted simulations through `GET /api/simulations` with optional `page` and `pageSize` parameters, defaults of `1` and `20`, and a maximum page size of `100`.
* Delete persisted simulations through `DELETE /api/simulations/{id}`, returning `204 No Content` for an existing ID and `404 Not Found` for an unknown ID.
* Return lightweight simulation summaries in stable newest-first order with `page`, `pageSize`, `totalCount`, and `totalPages` metadata, without returning state collections.
* Return `400 Bad Request` with `ProblemDetails` when pagination values fall outside their accepted ranges.
* Map dedicated API contracts to Application requests and results, and return structured JSON states.
* Return `400 Bad Request` with `ProblemDetails` for invalid simulation parameters and `ValidationProblemDetails` for model-binding or malformed-JSON failures.
* Document collection GET with pagination parameters and `200` and `400` responses, POST with `201` and `400`, and GET and DELETE by ID with their success and `404` responses through OpenAPI.
* Provide reusable HTTP requests in `SingularFlow.Api.http`.
* Build the API as a multi-stage Docker image from the root `Dockerfile`.
* Run the API and PostgreSQL together as separate Docker Compose services with readiness-based health checks.
* Persist local database data through a named Docker volume.
* Keep local database credentials outside version control.
* Model simulations and their generated states with EF Core persistence entities, PostgreSQL mappings, constraints, indexes, and a tracked initial migration.
* Keep the Application persistence contracts and orchestration independent of EF Core.
* Project collection summaries directly from PostgreSQL without loading `simulation_states` rows.
* Verify the complete HTTP-to-PostgreSQL POST, GET-by-ID, collection-listing, DELETE, and subsequent GET `404` lifecycle with a dedicated database integration test, including cascade removal of child states.
* Create the EF Core context at design time without storing a database password in the repository.

## Mathematical model

Let:

```text
T = singular time
t = current time
τ = T - t
h = concentration parameter
```

The current implementation evaluates the following asymptotic scaling relationships:

```text
Remaining time:          τ = T - t
Radial length:           ℓᵣ = τ^(1/2)
Axial length:            ℓ𝓏 = τ^(1/2-h)
Angular velocity scale:  Uθ = τ^(-1/2-h)
Radial velocity scale:   Uᵣ = τ^(-1/2)
Core volume scale:       V = τ^(3/2-h)
Core energy scale:       E = τ^(1/2-3h)
```

The concentration exponent `h` must satisfy:

```text
0 < h < 0.01
```

Every evaluated time must satisfy:

```text
0 ≤ t < T
```

As `t` approaches `T`:

* The remaining time approaches zero.
* The radial and axial length scales decrease.
* The vortex core becomes increasingly concentrated.
* The radial and angular velocity scales increase.
* The core volume decreases.
* The modeled core energy remains controlled and decreases for the permitted values of `h`.

This represents the mathematical idea that an unbounded velocity scale can be concentrated inside a sufficiently small region without requiring unbounded total energy.

## Time-series configuration

A time series is configured with:

```text
t₀ = start time
t₁ = end time
N  = sample count
```

The configuration rules require:

```text
0 ≤ t₀ < t₁ < T
2 ≤ N ≤ 100,000
```

The lower sample-count limit guarantees that a sequence has a beginning and an end.

The upper limit reduces the risk of accidentally requesting an excessive in-memory allocation.

## Sampling strategies

SingularFlow separates time generation from state calculation through `ITimeSamplingStrategy`.

The current implementations are:

* `UniformTimeSamplingStrategy`
* `LogarithmicTimeSamplingStrategy`

Both strategies receive the same mathematical and time-series configuration and return a read-only collection of sample times.

### Uniform sampling

The uniform strategy distributes absolute times with a constant interval.

The time step is:

```text
Δt = (t₁ - t₀) / (N - 1)
```

Each time is generated with:

```text
tᵢ = t₀ + iΔt
```

where:

```text
i = 0, 1, 2, ..., N - 1
```

For example:

```text
Start time:   0.0
End time:     0.8
Sample count: 5
```

produces:

```text
0.0
0.2
0.4
0.6
0.8
```

Uniform sampling is useful when approximately equal resolution is desired throughout the complete time range.

### Logarithmic remaining-time sampling

The logarithmic strategy distributes the remaining time:

```text
τ = T - t
```

Let:

```text
τ₀ = T - t₀
τ₁ = T - t₁
```

The geometric ratio is:

```text
r = (τ₁ / τ₀)^(1 / (N - 1))
```

Each remaining time is:

```text
τᵢ = τ₀ × r^i
```

Each absolute sample time is then recovered with:

```text
tᵢ = T - τᵢ
```

For:

```text
Singular time: 1.0
Start time:    0.0
End time:      0.9999
Sample count:  6
```

the remaining times are approximately:

```text
1.000000000000000
0.158489319246111
0.025118864315096
0.003981071705535
0.000630957344480
0.000100000000000
```

The resulting absolute times are approximately:

```text
0.000000
0.841511
0.974881
0.996019
0.999369
0.999900
```

This strategy provides increasingly fine resolution near the singular time, where the modeled scales change most rapidly.

Both strategies explicitly assign the configured first and last samples to reduce floating-point endpoint differences.

## Mathematical scope and disclaimer

The current implementation is an educational representation of selected asymptotic scaling relationships.

It is not:

* A complete Navier–Stokes solver.
* A computational fluid dynamics engine.
* A reproduction of the complete analytical proof.
* A formal verification of the published proof.
* A numerical demonstration that a singularity exists.
* A prediction of singularities in physical fluids.
* A claim about independent mathematical acceptance or prize adjudication.
* A substitute for independent mathematical review of the published result.

The application models selected scaling behavior described in the reference material. It does not implement the complete velocity field, pressure field, external force, oscillatory corrections, or analytical construction contained in the paper.

## Example output

```text
SingularFlow — Blow-up scaling model

Singular time: 1.0000
Concentration exponent: 0.0050
Sampling strategy: Logarithmic remaining time
Sampling range: [0.0000, 0.9999]
Sample count: 6

      Time      Remaining         Radius          Axial     Angular velocity      Core energy
------------------------------------------------------------------------------------------------
    0.0000    1.0000E+000    1.0000E+000    1.0000E+000          1.0000E+000      1.0000E+000
    0.8415    1.5849E-001    3.9811E-001    4.0179E-001          2.5351E+000      4.0926E-001
    0.9749    2.5119E-002    1.5849E-001    1.6144E-001          6.4269E+000      1.6749E-001
    0.9960    3.9811E-003    6.3096E-002    6.4863E-002          1.6293E+001      6.8549E-002
    0.9994    6.3096E-004    2.5119E-002    2.6062E-002          4.1305E+001      2.8054E-002
    0.9999    1.0000E-004    1.0000E-002    1.0471E-002          1.0471E+002      1.1482E-002
```

## Technologies currently used

* C#
* .NET 10
* ASP.NET Core Web API
* ASP.NET Core Health Checks
* EF Core `DbContext` health-check integration with tagged readiness and filtered liveness checks
* Microsoft.AspNetCore.OpenApi
* Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.12
* Microsoft.AspNetCore.Mvc.Testing
* xUnit
* Git
* GitHub
* GitHub Actions
* PostgreSQL 18.6 (`postgres:18.6-alpine3.24`)
* Docker
* Docker Compose
* Alpine-based .NET SDK and ASP.NET Core runtime container images
* Entity Framework Core 10.0.12 and Entity Framework Core Design 10.0.12
* Npgsql Entity Framework Core provider 10.0.3

## Planned technologies

Future versions are expected to introduce:

* Background services
* SignalR
* Blazor
* Interactive data visualization

Planned technologies will only be added when the project has a concrete requirement for them.

## Project structure

```text
singular-flow/
├── .dockerignore
├── .env.example
├── .github/
│   └── workflows/
│       └── ci.yml
├── Dockerfile
├── src/
│   ├── SingularFlow.Api/
│   │   ├── Contracts/
│   │   │   └── Simulations/
│   │   │       ├── PagedSimulationsResponse.cs
│   │   │       ├── RunSimulationApiRequest.cs
│   │   │       ├── RunSimulationApiResponse.cs
│   │   │       ├── SimulationContractMapper.cs
│   │   │       ├── SimulationStateResponse.cs
│   │   │       └── SimulationSummaryResponse.cs
│   │   ├── Controllers/
│   │   │   └── SimulationsController.cs
│   │   ├── ErrorHandling/
│   │   │   └── InvalidSimulationRequestExceptionHandler.cs
│   │   ├── Properties/
│   │   │   └── launchSettings.json
│   │   ├── Program.cs
│   │   ├── SingularFlow.Api.csproj
│   │   ├── SingularFlow.Api.http
│   │   ├── appsettings.Development.json
│   │   └── appsettings.json
│   ├── SingularFlow.Application/
│   │   ├── Simulations/
│   │   │   ├── DeleteSimulationHandler.cs
│   │   │   ├── GetSimulationHandler.cs
│   │   │   ├── ISimulationRepository.cs
│   │   │   ├── ListSimulationsHandler.cs
│   │   │   ├── PagedSimulationResult.cs
│   │   │   ├── PersistedSimulationResult.cs
│   │   │   ├── RunAndSaveSimulationHandler.cs
│   │   │   ├── RunSimulationHandler.cs
│   │   │   ├── RunSimulationRequest.cs
│   │   │   ├── RunSimulationResult.cs
│   │   │   ├── SamplingMode.cs
│   │   │   └── SimulationSummaryResult.cs
│   │   └── SingularFlow.Application.csproj
│   ├── SingularFlow.Cli/
│   │   ├── Program.cs
│   │   └── SingularFlow.Cli.csproj
│   ├── SingularFlow.Domain/
│   │   ├── Calculations/
│   │   │   ├── BlowupScalingCalculator.cs
│   │   │   └── BlowupSeriesGenerator.cs
│   │   ├── Models/
│   │   │   ├── BlowupParameters.cs
│   │   │   ├── BlowupState.cs
│   │   │   └── TimeSeriesParameters.cs
│   │   ├── Sampling/
│   │   │   ├── ITimeSamplingStrategy.cs
│   │   │   ├── LogarithmicTimeSamplingStrategy.cs
│   │   │   ├── TimeSamplingValidation.cs
│   │   │   └── UniformTimeSamplingStrategy.cs
│   │   └── SingularFlow.Domain.csproj
│   └── SingularFlow.Infrastructure/
│       ├── Persistence/
│       │   ├── Configurations/
│       │   ├── DesignTime/
│       │   │   └── SingularFlowDbContextFactory.cs
│       │   ├── Entities/
│       │   ├── EfSimulationRepository.cs
│       │   ├── Migrations/
│       │   │   └── 20260921055058_InitialPersistence.cs
│       │   └── SingularFlowDbContext.cs
│       └── SingularFlow.Infrastructure.csproj
├── tests/
│   ├── SingularFlow.Api.Tests/
│   │   ├── Health/
│   │   │   ├── DatabaseHealthEndpointTests.cs
│   │   │   └── HealthEndpointTests.cs
│   │   ├── OpenApi/
│   │   │   └── OpenApiEndpointTests.cs
│   │   ├── Simulations/
│   │   │   ├── SimulationDeletionEndpointTests.cs
│   │   │   ├── SimulationDatabaseEndpointTests.cs
│   │   │   ├── SimulationEndpointTests.cs
│   │   │   ├── SimulationEndpointFactory.cs
│   │   │   ├── SimulationListingEndpointTests.cs
│   │   │   ├── SimulationModelBindingTests.cs
│   │   │   ├── SimulationPersistenceEndpointTests.cs
│   │   │   ├── SimulationQueryEndpointTests.cs
│   │   │   └── SimulationValidationTests.cs
│   │   └── SingularFlow.Api.Tests.csproj
│   ├── SingularFlow.Application.Tests/
│   │   ├── Simulations/
│   │   │   ├── DeleteSimulationHandlerTests.cs
│   │   │   ├── GetSimulationHandlerTests.cs
│   │   │   ├── ListSimulationsHandlerTests.cs
│   │   │   ├── RunAndSaveSimulationHandlerTests.cs
│   │   │   └── RunSimulationHandlerTests.cs
│   │   └── SingularFlow.Application.Tests.csproj
│   ├── SingularFlow.Domain.Tests/
│   │   ├── BlowupParametersTests.cs
│   │   ├── BlowupScalingCalculatorTests.cs
│   │   ├── BlowupSeriesGeneratorTests.cs
│   │   ├── LogarithmicTimeSamplingStrategyTests.cs
│   │   ├── TimeSeriesParametersTests.cs
│   │   ├── UniformTimeSamplingStrategyTests.cs
│   │   └── SingularFlow.Domain.Tests.csproj
│   └── SingularFlow.Infrastructure.Tests/
│       ├── Persistence/
│       │   ├── DesignTime/
│       │   │   └── SingularFlowDbContextFactoryTests.cs
│       │   ├── EfSimulationRepositoryTests.cs
│       │   └── SingularFlowDbContextModelTests.cs
│       └── SingularFlow.Infrastructure.Tests.csproj
├── .editorconfig
├── .gitignore
├── compose.yaml
├── dotnet-tools.json
├── global.json
├── SingularFlow.slnx
└── README.md
```

`Dockerfile` defines the multi-stage API image, `.dockerignore` restricts its build context, and `compose.yaml` defines the local API and PostgreSQL services. `.env.example` is the safe local configuration template; the private `.env` file is ignored and excluded from the Docker build context. `.github/workflows/ci.yml` performs .NET validation, automated tests, and the API image build.

## Architecture

The solution currently contains nine projects separated into five production projects and four automated test projects.

### Local infrastructure

Docker Compose builds and runs the API alongside PostgreSQL through the root `compose.yaml` file. PostgreSQL remains a separate service, and the API waits for its `pg_isready` health check before starting. Infrastructure contains the EF Core PostgreSQL model, migration, and repository. The API registers the context and repository at runtime and requires an existing migrated schema for normal simulation requests; containerization does not apply migrations automatically.

### SingularFlow.Domain

Contains the mathematical behavior, domain models, validation rules, sampling strategies, and time-series generation.

Responsibilities include:

* Mathematical parameter validation.
* Time-series parameter validation.
* Individual scaling calculations.
* Uniform time sampling.
* Logarithmic remaining-time sampling.
* Cross-configuration validation.
* Mathematical result models.
* Domain rules independent of presentation and application orchestration.

The domain project does not depend on the application layer, command-line interface, or test projects.

### SingularFlow.Api

Hosts the ASP.NET Core HTTP application.

Current responsibilities include:

* Starting and configuring the web application.
* Receiving HTTP simulation requests through an MVC controller.
* Mapping API contracts to Application requests and Application results to HTTP response contracts.
* Executing `RunAndSaveSimulationHandler`, `GetSimulationHandler`, `ListSimulationsHandler`, and `DeleteSimulationHandler` through dependency injection.
* Translating the Boolean deletion result into `204 No Content` or `404 Not Found`.
* Registering `SingularFlowDbContext` with Npgsql from `ConnectionStrings:SingularFlow`.
* Binding `ISimulationRepository` to `EfSimulationRepository`.
* Producing standardized HTTP errors for invalid requests.
* Registering OpenAPI generation.
* Registering ASP.NET Core health-check services and the EF Core check for `SingularFlowDbContext` as `postgresql`, tagged `ready`.
* Exposing filtered process liveness at `GET /health/live`, database readiness at `GET /health/ready`, and the backward-compatible readiness alias `GET /health`.
* Generating an OpenAPI document in Development.
* Applying HTTPS redirection when `HttpsRedirection:Enabled` is true, which is the default.
* Providing local HTTP and HTTPS launch profiles.

The API depends directly on `SingularFlow.Application` and `SingularFlow.Infrastructure` as the composition root.

It does not contain mathematical formulas or domain validation rules.

### SingularFlow.Application

Contains the application use cases that coordinate domain behavior.

Responsibilities include:

* Receiving structured simulation requests.
* Selecting the requested sampling strategy.
* Creating validated domain configurations.
* Coordinating the scaling calculator and series generator.
* Executing a complete simulation use case.
* Defining `ISimulationRepository` as the persistence boundary.
* Coordinating calculation and awaited persistence through `RunAndSaveSimulationHandler`.
* Returning generated persistence identity through `PersistedSimulationResult`.
* Retrieving nullable persisted results through `GetSimulationHandler` and the repository abstraction.
* Validating pagination boundaries and delegating valid collection queries through `ListSimulationsHandler` and the repository abstraction.
* Owning the deletion use case through `DeleteSimulationHandler`, which delegates the simulation ID and cancellation token to the repository abstraction and returns its Boolean result.
* Returning `PagedSimulationResult` metadata and collection-specific `SimulationSummaryResult` items without state collections.
* Returning structured simulation results.
* Preventing presentation concerns from entering the domain layer.

The application project depends on `SingularFlow.Domain` and has no dependency on EF Core, PostgreSQL, Infrastructure, or ASP.NET Core.

It does not depend on the command-line interface or test projects.

### SingularFlow.Infrastructure

Contains the EF Core persistence foundation for PostgreSQL.

Responsibilities include:

* Defining `SingularFlowDbContext` and its `simulations` and `simulation_states` sets.
* Mapping separate Infrastructure persistence entities instead of mapping Domain records directly.
* Configuring generated keys, explicit PostgreSQL types, snake_case identifiers, constraints, indexes, and the required simulation-to-states relationship.
* Tracking the initial `20260921055058_InitialPersistence` migration.
* Implementing `ISimulationRepository` with `EfSimulationRepository`.
* Saving one simulation and all ordered state entities with one `SaveChangesAsync` call.
* Returning the generated simulation ID and database-generated creation timestamp after saving.
* Querying simulations by persisted ID with `AsNoTracking()` and loading their related states.
* Reconstructing Application and Domain results with states ordered by `Sequence`, or returning `null` when the ID does not exist.
* Listing simulations with a separate count query and an `AsNoTracking` summary projection ordered by creation time and ID in descending order.
* Applying offset pagination without including or loading related state rows.
* Physically deleting a simulation with a direct, ID-filtered `ExecuteDeleteAsync` command and using its affected-row count to report whether the resource existed.
* Relying on the existing required foreign key with `DeleteBehavior.Cascade` so PostgreSQL removes related `simulation_states` rows.
* Providing design-time context creation without a stored password.

Infrastructure depends directly on `SingularFlow.Application`, which in turn depends on Domain. EF Core remains confined to Infrastructure and the API composition root; Application and Domain do not depend on it.

The persisted query and deletion features use the existing schema and do not require a new migration.

### SingularFlow.Cli

Provides the current command-line presentation and composition entry point.

Responsibilities include:

* Creating a `RunSimulationRequest`.
* Invoking `RunSimulationHandler`.
* Receiving a `RunSimulationResult`.
* Translating the sampling mode into a user-facing description.
* Formatting calculated states.
* Displaying the active configuration and output.

The CLI currently requests logarithmic remaining-time sampling.

The CLI depends directly on `SingularFlow.Application`. It no longer constructs domain calculators, generators, or sampling strategies directly.

### SingularFlow.Domain.Tests

Contains automated tests for domain behavior.

The current domain tests verify:

* Valid and invalid singular-time configuration.
* Valid and invalid concentration-exponent configuration.
* Angular velocity growth near the singular time.
* Core-energy decrease near the singular time.
* Rejection of the singular time as a calculation point.
* Valid and invalid time-series configuration.
* Minimum and maximum sample-count rules.
* Required generator dependencies.
* Delegation from the generator to a sampling strategy.
* Requested output count.
* Inclusion of both sampling endpoints.
* Uniform spacing between absolute times.
* Geometric spacing between remaining times.
* Rejection of series that reach or exceed the singular time.
* Rejection of required null dependencies and arguments.

The test project depends directly on `SingularFlow.Domain`.

### SingularFlow.Application.Tests

Contains automated tests for application use-case behavior.

The fourteen application tests verify:

* Rejection of a null simulation request.
* Execution with uniform sampling.
* Execution with logarithmic remaining-time sampling.
* Preservation of validated mathematical configuration.
* Preservation of validated time-series configuration.
* Preservation of the selected sampling mode.
* Rejection of unsupported sampling modes.
* Production of structured simulation results.
* Delegation of calculated results to the repository and return of the persisted identity and creation timestamp.
* Delegation of existing and missing simulation queries through the repository abstraction.
* Delegation of valid collection pagination through the repository abstraction.
* Rejection of page numbers below `1` and page sizes outside `1` through `100`.
* Computation of total pages from the total count and page size.
* Deletion delegation, propagation of the repository's Boolean result, and forwarding of the simulation ID and cancellation token.

The test project depends directly on `SingularFlow.Application`.

### SingularFlow.Api.Tests

Contains integration tests for the ASP.NET Core host, including database-backed endpoint tests.

`HealthEndpointTests.cs` covers database-independent liveness. `DatabaseHealthEndpointTests.cs` covers healthy readiness and compatibility behavior against PostgreSQL, plus the separation between liveness and readiness when that dependency is unavailable.

The twenty-three API tests verify:

* `GET /health/live` returns `200 OK` and plain-text `Healthy` without connecting to PostgreSQL.
* `GET /health/ready` and the compatibility `GET /health` return `200 OK` and plain-text `Healthy` when the configured `singular_flow_tests` PostgreSQL database is available.
* Liveness remains `200 Healthy` when a deliberately unreachable local PostgreSQL port is configured, while readiness returns `503 Unhealthy` and the compatibility endpoint returns `503`.
* The Development OpenAPI document contains the API metadata, collection pagination parameters, and documented simulation operations and responses while excluding `/health`, `/health/live`, and `/health/ready`.
* Valid logarithmic and uniform simulation requests return `201 Created`, resource identity, creation timestamp, and a resource location.
* Unsupported sampling modes and invalid concentration exponents return `400 ProblemDetails`.
* Missing sampling modes and malformed JSON return `400 ValidationProblemDetails`.
* The persistence abstraction receives the calculated simulation before a successful response is returned.
* Existing resource queries return the persisted representation and missing queries return `404 Not Found`.
* Existing resource deletions return `204 No Content`, while missing IDs return `404 Not Found`.
* Collection queries return summary fields and pagination metadata, use default query values, omit states, and reject invalid pagination with `400 Bad Request`.
* The actual HTTP-to-PostgreSQL path creates a simulation, follows its `Location`, retrieves its configuration and ordered states, lists its summary, deletes it, observes a subsequent GET `404`, and verifies that neither the simulation nor its states remain.

The contract and validation tests use `WebApplicationFactory<Program>` with a test repository, so they execute the real HTTP pipeline without requiring PostgreSQL. The simulation database endpoint test retains the production repository, applies pending migrations, and deletes its inserted simulation afterward. The readiness tests use the dedicated `singular_flow_tests` connection and guard against another database name; the dependency-failure case substitutes an unreachable local port with short timeouts and does not stop the PostgreSQL container or mutate the test database.

The test project depends directly on `SingularFlow.Api`.

### SingularFlow.Infrastructure.Tests

Contains 16 automated tests for EF Core model metadata, design-time context creation, and repository persistence.

The tests verify table and column mappings, generated primary keys, PostgreSQL column types, required cascade relationships, indexes, check constraints, the `CURRENT_TIMESTAMP` default, Npgsql provider configuration, persistence of one simulation with ordered states, and query reconstruction in `Sequence` order. Missing IDs return `null`. Repository integration tests also verify total count, page metadata, stable descending ordering, first and later pages, summary projection, an out-of-range page with empty items and the original total count, deletion of an existing simulation and its states, and `false` for a missing deletion ID. The database tests apply pending migrations and use reversible transactions for inserted repository data.

The test project depends directly on `SingularFlow.Infrastructure`.

## Dependency direction

The direct production project dependencies are:

```text
SingularFlow.Api ────────────────> SingularFlow.Application
        │
        └────────────────────────> SingularFlow.Infrastructure
SingularFlow.Cli ────────────────> SingularFlow.Application
SingularFlow.Infrastructure ─────> SingularFlow.Application
                                             │
                                             ▼
                                  SingularFlow.Domain
```

The direct test project dependencies are:

```text
SingularFlow.Api.Tests ─────────> SingularFlow.Api
SingularFlow.Application.Tests ─> SingularFlow.Application
SingularFlow.Domain.Tests ──────> SingularFlow.Domain
SingularFlow.Infrastructure.Tests ─> SingularFlow.Infrastructure
```

The dependency rules are:

* `SingularFlow.Domain` has no dependency on higher-level projects.
* `SingularFlow.Application` depends on `SingularFlow.Domain`.
* `SingularFlow.Api` depends on `SingularFlow.Application` and `SingularFlow.Infrastructure`; `SingularFlow.Cli` depends on Application.
* `SingularFlow.Infrastructure` depends on `SingularFlow.Application`; Domain remains independent of Infrastructure.
* Production projects do not depend on test projects.
* Mathematical behavior is independent of HTTP and command-line presentation.
* `SingularFlow.Domain` and `SingularFlow.Application` do not depend on EF Core.

The simulation execution flow for the CLI and HTTP API is:

```text
CLI Program.cs → RunSimulationHandler ───────────────┐
                                                     │
HTTP JSON → SimulationsController                    │
    → SimulationContractMapper                       │
    → RunAndSaveSimulationHandler                    │
        ├── RunSimulationHandler ────────────────────┤
        └── ISimulationRepository                    │
            → EfSimulationRepository                 │
            → PostgreSQL                             │
                                                     ▼
                                          RunSimulationRequest
                                                     │
                                                     ▼
                                          Domain calculation
                                                     │
                                                     ▼
                                          RunSimulationResult
                                              │             │
                                              ▼             ▼
                                      CLI presentation   HTTP JSON
```

## Design decisions

### Containerized local infrastructure

The root `Dockerfile` uses a multi-stage build. Its build stage uses `mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.24`, matching `global.json`, and passes Docker's target architecture to restore and publish. It copies `global.json` and the API's required project files before the remaining `src` tree so dependency restoration can remain cached when source changes do not affect project dependencies. It restores the API and its referenced projects, then publishes the API in Release configuration to `/app/publish` with `UseAppHost=false`.

The runtime stage uses the pinned `mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.24` image rather than the full SDK and copies only the published application from the build stage. It installs `krb5-libs`, which supplies the `libgssapi_krb5.so.2` native library required by the PostgreSQL client runtime path on Alpine. The image exposes port `8080`, runs as the built-in non-root `$APP_UID` user, and starts with `dotnet SingularFlow.Api.dll`. Manual runtime verification confirmed the `app` user has UID and GID `1654`.

The selected Microsoft images are multi-platform, and the Dockerfile uses Docker build-platform and target-architecture arguments. The image has been built successfully in the project's ARM64 development environment; this does not imply that every supported architecture has been tested.

The root `.dockerignore` excludes repository content not required for the API build, admitting only the required `global.json` and `src` content and then excluding generated `bin` and `obj` directories and macOS `.DS_Store` files. The private `.env` file is therefore not included in the build context. This keeps the context small, improves build performance, and reduces the risk of copying local configuration into the image; it does not replace proper secret management.

The Compose `api` service uses the repository root as its build context and the root `Dockerfile`, produces the local `singular-flow-api:local` image, and uses the `singular-flow-api` container name with `restart: unless-stopped` and a 30-second stop grace period. It runs with `ASPNETCORE_ENVIRONMENT=Production`, listens for HTTP on container port `8080` through `ASPNETCORE_HTTP_PORTS`, depends on PostgreSQL reaching healthy status, and receives `ConnectionStrings__SingularFlow` through environment configuration rather than baking a connection string into the image.

The PostgreSQL service pins `postgres:18.6-alpine3.24` instead of using `latest`; the image supports the project's ARM64 development environment. It uses the container name `singular-flow-postgres` and checks its own container readiness with `pg_isready`. This check is separate from the API's EF Core readiness check: PostgreSQL health verifies the database service, while `/health/ready` verifies that the application can connect through `SingularFlowDbContext`.

PostgreSQL health runs every 5 seconds with a 5-second timeout, 10 retries, and a 10-second start period. The API image checks readiness every 10 seconds with a 3-second timeout, 3 retries, and a 10-second start period.

The default host bindings are loopback-only. `API_BIND_ADDRESS` and `API_PORT` control host-to-API publication, normally `127.0.0.1:5278` to `api:8080`. `POSTGRES_BIND_ADDRESS` and `POSTGRES_PORT` control host-to-PostgreSQL publication, normally `127.0.0.1:5432` to `postgres:5432`. Inside the Compose network, the API always reaches the database at `postgres:5432`; it does not use the host-published `POSTGRES_PORT`.

The tracked `.env.example` template is separate from the ignored local `.env` file, so local credentials stay out of version control. The named volume `singular-flow-postgres-data` preserves data across container recreation and mounts at `/var/lib/postgresql`, the PostgreSQL 18 data location.

### Configurable HTTPS redirection

HTTPS redirection remains enabled by default, preserving the existing behavior for direct local execution unless configuration overrides it. The Compose API service sets `HttpsRedirection__Enabled=false` because the local container exposes HTTP only; this prevents Kestrel from attempting to redirect to an HTTPS port that does not exist in the container. Manual verification confirmed that the previous `Failed to determine the https port for redirect` warning no longer appears in the container logs.

The local Compose stack does not terminate TLS, and the API container does not serve HTTPS. A future production deployment should normally terminate TLS at an appropriate reverse proxy, ingress, gateway, or hosting platform and must configure and trust forwarded headers correctly.

### EF Core persistence model

`SingularFlowDbContext` maps separate Infrastructure entities rather than coupling EF Core to Domain records. A simulation has a generated `uuid` key and many required state rows; each state has a generated `bigint` key, and deleting a simulation cascades to its states.

The mappings use snake_case PostgreSQL identifiers and explicit column types. The schema enforces the supported sample count, concentration exponent, time ordering, and non-negative state sequence with database check constraints. It also defines a unique `(simulation_id, sequence)` index, an index on `created_at_utc`, and a `CURRENT_TIMESTAMP` default for `created_at_utc`.

### Design-time database context

`SingularFlowDbContextFactory` lets the local EF tool inspect the model and manage migrations without starting the API. Its design-time connection string identifies the local host, port, database, and user but intentionally contains no password. Real local database credentials belong only in the ignored `.env` file and must never be committed.

### Immutable configuration objects

`BlowupParameters` and `TimeSeriesParameters` are immutable records.

Their constructors validate all local invariants before assigning their properties. Once an instance has been created successfully, it cannot be changed into an invalid configuration.

### Single-state calculation

`BlowupScalingCalculator` is responsible only for validating a calculation time and producing one `BlowupState`.

It does not decide how many states should be generated or how sample times should be distributed.

### Strategy pattern

`ITimeSamplingStrategy` defines the contract for generating sample times.

The uniform and logarithmic strategies implement this contract independently. Additional strategies can be introduced without placing conditional sampling logic inside `BlowupSeriesGenerator`.

### Shared sampling validation

`TimeSamplingValidation` centralizes the rules shared by all current sampling strategies.

It is `internal` because it is an implementation detail of the domain and is not intended to be called by the application layer, CLI, or future external consumers.

### Series generation

`BlowupSeriesGenerator` is responsible for:

* Receiving a sampling strategy.
* Requesting sample times from that strategy.
* Delegating every mathematical calculation to `BlowupScalingCalculator`.
* Returning the resulting states through a read-only collection.

The generator does not contain uniform or logarithmic sampling formulas.

### Constructor injection

`BlowupSeriesGenerator` receives both `BlowupScalingCalculator` and `ITimeSamplingStrategy` through its constructor.

This makes its dependencies explicit and allows the application layer to select the desired sampling behavior.

### Application use case

`RunSimulationHandler` represents the application use case for executing a simulation.

It coordinates the domain components without duplicating mathematical formulas or validation rules.

The handler:

* Receives a `RunSimulationRequest`.
* Selects a sampling strategy from `SamplingMode`.
* Creates validated domain configuration objects.
* Executes the domain series generator.
* Returns a `RunSimulationResult`.

`RunAndSaveSimulationHandler` decorates that calculation use case with persistence. It awaits `ISimulationRepository.SaveAsync` and returns a `PersistedSimulationResult` containing the generated identity, database creation timestamp, and simulation result. `ISimulationRepository.GetByIdAsync` returns a nullable persisted result, and `GetSimulationHandler` delegates retrieval through that abstraction.

`ListSimulationsHandler` rejects page numbers below `1` and page sizes outside `1` through `100`, then delegates valid requests to `ISimulationRepository.ListAsync`. `PagedSimulationResult` carries the items, requested page, page size, total count, and computed total pages. Its `SimulationSummaryResult` items form a collection-specific read model that intentionally excludes calculated states. These types belong to Application, so orchestration and collection models remain independent of EF Core and PostgreSQL.

POST calculates and persists a simulation, GET by ID returns its detailed persisted representation, collection GET returns lightweight persisted summaries, and DELETE by ID physically removes a persisted simulation.

### Structured request and result objects

`RunSimulationRequest` defines the complete input required to execute a simulation.

`RunSimulationResult` returns:

* The validated blow-up parameters.
* The validated time-series parameters.
* The selected sampling mode.
* The generated read-only collection of states.

These application models provide a stable boundary between presentation code and the mathematical domain.

### Centralized sampling selection

The conversion from `SamplingMode` to a concrete `ITimeSamplingStrategy` is centralized inside `RunSimulationHandler`.

The CLI does not need to know how sampling strategies are constructed. Unsupported modes are rejected explicitly instead of silently selecting a default behavior.

### Bounded sample count

The time-series configuration accepts between 2 and 100,000 samples.

The lower limit guarantees that the series has a start and an end. The upper limit reduces the risk of accidental excessive in-memory allocation.

### Floating-point endpoint protection

Both sampling strategies explicitly return `StartTime` and `EndTime` for the first and last positions.

This avoids exposing small accumulated floating-point differences at the configured boundaries.

### Web API foundation

`SingularFlow.Api` uses the ASP.NET Core Web SDK and acts as an additional presentation layer.

It hosts the simulation controller alongside the health and Development OpenAPI endpoints.

### Dedicated HTTP contracts

`RunSimulationApiRequest`, `RunSimulationApiResponse`, `SimulationStateResponse`, `PagedSimulationsResponse`, and `SimulationSummaryResponse` define the JSON boundary independently of Application and Domain models. HTTP representation can evolve without exposing mathematical domain types directly.

### HTTP contract mapper

`SimulationContractMapper` converts supported sampling strings to Application `SamplingMode` values and maps detailed and paged Application results to dedicated response contracts. Mathematical behavior remains in the Domain layer.

### Thin controller

`SimulationsController` delegates creation to `RunAndSaveSimulationHandler`, detailed retrieval to `GetSimulationHandler`, optional collection query parameters to `ListSimulationsHandler`, and deletion to `DeleteSimulationHandler`. It maps paged Application results to dedicated API contracts and returns `200 OK` for valid listings. Invalid pagination flows through the existing exception pipeline. POST returns `201 Created`; a successful GET by ID returns `200 OK`; and DELETE maps the handler's Boolean result to `204 No Content` or `404 Not Found`. The controller contains no mathematical calculations or EF Core query logic.

### Dependency injection

`RunSimulationHandler`, `RunAndSaveSimulationHandler`, `GetSimulationHandler`, `ListSimulationsHandler`, and `DeleteSimulationHandler` are registered with scoped lifetimes. The API registers `SingularFlowDbContext` with Npgsql, maps `ISimulationRepository` to `EfSimulationRepository`, and injects the handlers into `SimulationsController`.

### Global exception handling

`InvalidSimulationRequestExceptionHandler` handles known invalid-input `ArgumentOutOfRangeException` failures as `400 ProblemDetails`. Unexpected exception types continue through the general error pipeline instead of being classified as client errors. `Program.cs` registers problem details and the handler, then enables exception handling middleware.

### Model-binding validation

`[ApiController]` handles missing required values and malformed JSON before the controller action runs, returning `400 ValidationProblemDetails`. These responses are separate from the custom exception handler.

### HTTP status choice

POST creates a persisted simulation resource and returns `201 Created` with its representation. The response body contains the generated `id` and `createdAtUtc`, and `Location` identifies `GET /api/simulations/{id}`. GET by ID returns `200 OK` when the resource exists and `404 Not Found` when it does not. DELETE by ID returns `204 No Content` with no response body when a resource was deleted and `404 Not Found` when no simulation matched the ID. Collection GET returns `200 OK`; an empty database has empty `items`, `totalCount` and `totalPages` of `0`, while a valid page beyond the available range retains its requested metadata and total count with empty `items`. Neither case is a `404`. Invalid POST input and invalid collection pagination return `400 Bad Request`.

### Operational health check

The API uses ASP.NET Core Health Checks and the official `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.12 package to distinguish process liveness from database readiness. The `postgresql` EF Core check for `SingularFlowDbContext` is tagged `ready`; the readiness endpoints select that tag, while the liveness endpoint filters out registered checks and does not attempt to connect to PostgreSQL.

| Endpoint | Purpose | PostgreSQL check | Healthy response | Database unavailable |
|---|---|---:|---|---|
| `GET /health/live` | Process liveness | No | `200 Healthy` | Still `200 Healthy` if the process responds |
| `GET /health/ready` | Database readiness | Yes | `200 Healthy` | `503 Unhealthy` |
| `GET /health` | Backward-compatible readiness alias | Yes | `200 Healthy` | `503` |

These endpoints use the default plain-text health-check response, not JSON. Liveness answers whether the ASP.NET Core process can respond; it is not proof that database-dependent traffic can be served. A PostgreSQL outage therefore leaves liveness healthy because restarting a responsive process does not necessarily repair an external dependency. Readiness fails so a load balancer or orchestrator can stop routing database-dependent traffic. The compatibility endpoint is retained to avoid breaking existing users and scripts, but now has the same readiness semantics as `/health/ready`.

Readiness verifies database connectivity only. It does not validate that migrations are current or that the complete schema is correct.

The Dockerfile health check uses Alpine's available `wget` implementation to call `http://127.0.0.1:8080/health/ready`. Docker therefore marks the API container healthy only after the process is running and PostgreSQL connectivity succeeds, allowing `docker compose up --wait` to wait for both database and API readiness. Periodic evaluation can produce lightweight `SELECT 1` connectivity queries in EF Core logs; these are expected health-check activity, not simulation queries or persistence failures.

### Development OpenAPI document

OpenAPI generation is enabled only in the Development environment and currently produces an OpenAPI 3.1.1 document.

The document includes collection `GET /api/simulations` with `page` and `pageSize` query parameters and `200` and `400` responses, `POST /api/simulations` with `201` and `400`, `GET /api/simulations/{id}` with `200` and `404`, and `DELETE /api/simulations/{id}` with `204` and `404`. The operational endpoints `/health`, `/health/live`, and `/health/ready` are intentionally excluded because they are mapped health checks rather than controller operations.

### In-memory API testing

`SingularFlow.Api.Tests` uses `WebApplicationFactory<Program>`.

This starts the real ASP.NET Core pipeline in memory and verifies HTTP status codes, response bodies, content types, and generated OpenAPI metadata without requiring a separately running server. Contract, validation, query, and deletion tests replace `ISimulationRepository` with test implementations; a separate test retains `EfSimulationRepository` to verify the actual HTTP-to-PostgreSQL creation, detailed retrieval, collection listing, deletion, subsequent GET `404`, and cascade removal of child states.

### Paginated summary projection

`EfSimulationRepository.ListAsync` performs a read-only `AsNoTracking` query. It first uses `CountAsync` for collection metadata, calculates the offset with `long` arithmetic to avoid multiplication overflow for very large page numbers, and returns empty items without running the page query when the offset is beyond the total count.

For a page that can contain data, EF Core generates a separate query ordered by `CreatedAtUtc` descending and then `Id` descending as a stable tie-breaker. The query applies offset pagination through `Skip` and `Take` and projects directly to `SimulationSummaryResult`. It does not call `Include`, materialize full persistence entities for the response, or load `simulation_states`. Collection summaries keep payloads bounded instead of returning every calculated state for every simulation. The existing `simulations` table and `created_at_utc` index are reused, so no new migration is required.

### Direct physical deletion

`EfSimulationRepository.DeleteAsync` filters `simulations` by ID and uses `ExecuteDeleteAsync`, so EF Core emits a direct DELETE without first loading the simulation or its states. An affected-row count greater than zero means a resource was deleted; zero means the ID was missing. This is a physical deletion, not soft deletion. PostgreSQL removes the ordered `simulation_states` rows through the existing required foreign key with `DeleteBehavior.Cascade`; there is no application-level child-delete loop. The EF Core model and database schema are unchanged, so no new migration is required.

## Simulation API

The development base URL is `http://localhost:5278`. Send JSON to `POST /api/simulations`. The supported `samplingMode` strings are `uniform` and `logarithmic`.

Logarithmic request:

```json
{
  "singularTime": 1.0,
  "concentrationExponent": 0.005,
  "startTime": 0.0,
  "endTime": 0.9999,
  "sampleCount": 6,
  "samplingMode": "logarithmic"
}
```

Uniform request:

```json
{
  "singularTime": 1.0,
  "concentrationExponent": 0.005,
  "startTime": 0.0,
  "endTime": 0.8,
  "sampleCount": 5,
  "samplingMode": "uniform"
}
```

`singularTime` is the theoretical singular time; `concentrationExponent` is the model parameter `h`; `startTime` and `endTime` bound the sampled interval; `sampleCount` sets the number of returned states; `samplingMode` selects how times are spaced. The mathematical and time-series limits are described above.

Successful requests return `201 Created` with `application/json`. `Location` identifies the new resource at `GET /api/simulations/{id}`. A response has this structure (one representative state is shown; the example request returns six):

```json
{
  "id": "89e58894-f2ab-4528-8030-75c727887611",
  "createdAtUtc": "2026-09-23T12:00:00+00:00",
  "singularTime": 1.0,
  "concentrationExponent": 0.005,
  "startTime": 0.0,
  "endTime": 0.9999,
  "sampleCount": 6,
  "samplingMode": "logarithmic",
  "states": [
    {
      "time": 0.0,
      "remainingTime": 1.0,
      "radialLength": 1.0,
      "axialLength": 1.0,
      "angularVelocityScale": 1.0,
      "radialVelocityScale": 1.0,
      "coreVolumeScale": 1.0,
      "coreEnergyScale": 1.0
    }
  ]
}
```

Each state reports its sample time, remaining time, and the six scales defined in the mathematical model. Before returning this response, the endpoint stores the simulation configuration and all states in sequence order.

### List persisted simulations

List persisted simulations with offset pagination:

```http
GET /api/simulations?page=1&pageSize=20
```

Both query parameters are optional and default to `page=1` and `pageSize=20`. `page` must be at least `1`; `pageSize` must be between `1` and `100`. Results are ordered by newest creation time first, with ID descending as a stable tie-breaker. Each item is a summary and intentionally omits `states`:

```json
{
  "items": [
    {
      "id": "89e58894-f2ab-4528-8030-75c727887611",
      "createdAtUtc": "2026-09-24T12:00:00+00:00",
      "singularTime": 1.0,
      "concentrationExponent": 0.005,
      "startTime": 0.0,
      "endTime": 0.8,
      "sampleCount": 5,
      "samplingMode": "uniform"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1,
  "totalPages": 1
}
```

A valid request returns `200 OK`. An empty database returns empty `items`, `totalCount = 0`, and `totalPages = 0`. A page beyond the available range also returns `200 OK` with empty `items`, the original `totalCount`, and the requested page metadata. Invalid pagination returns `400 Bad Request` with `application/problem+json`.

Retrieve a persisted simulation using its returned `Location` or ID:

```http
@SimulationId = replace-with-a-simulation-id

GET {{SingularFlow.Api_HostAddress}}/api/simulations/{{SimulationId}}
Accept: application/json
```

`GET /api/simulations/{id}` returns the same persisted representation with `200 OK`, with states in their original sequence. An unknown ID returns `404 Not Found`.

Delete the same persisted simulation using its ID:

```http
DELETE {{SingularFlow.Api_HostAddress}}/api/simulations/{{SimulationId}}
```

`DELETE /api/simulations/{id}` returns `204 No Content` with no response body when deletion succeeds. An unknown ID returns `404 Not Found`. A typical persisted-resource workflow is to create a simulation, capture the returned `id` or `Location`, retrieve it or find its lightweight summary in the paginated list, delete it, and then observe `404 Not Found` from a subsequent GET.

Invalid sampling modes or mathematical and time-series parameters return `400 Bad Request` with `application/problem+json` and a `ProblemDetails` body, for example:

```json
{
  "title": "Invalid simulation request.",
  "status": 400,
  "detail": "Sampling mode must be either 'uniform' or 'logarithmic'.",
  "instance": "/api/simulations"
}
```

The `detail` text can include parameter information. Missing required values and malformed JSON instead receive `400 Bad Request` with `application/problem+json` and a `ValidationProblemDetails` body:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "SamplingMode": ["The SamplingMode field is required."]
  }
}
```

Validation error keys and messages depend on the failed input. The Development OpenAPI document at `/openapi/v1.json` includes collection GET with `page` and `pageSize` parameters and `200` and `400` responses, POST with `201` and `400`, GET by ID with `200` and `404`, and DELETE by ID with `204` and `404`.

## Requirements

Install the following software before building the project:

* .NET 10 SDK
* Git

Docker Desktop and Docker Compose are required to run the complete containerized stack. Docker-hosted PostgreSQL is also used by the documented full test-suite workflow, which includes database integration tests.

Check the installed .NET SDK:

```bash
dotnet --version
```

The project currently uses:

```text
10.0.401
```

The required SDK family is declared in `global.json`.

## Local Docker Compose stack

The root `compose.yaml` builds the ASP.NET Core API and runs it with the separate pinned PostgreSQL service. The complete stack has been manually verified with both containers healthy, the API running in Production as the non-root `app` user, HTTP listening on container port `8080`, and the host publishing the API at `127.0.0.1:5278`. The persisted simulation create, list, retrieve, delete, and subsequent GET `404` workflow was also verified through the containerized API, including clean removal of the temporary simulation.

Copy the environment template:

```bash
cp .env.example .env
```

Replace the example password in `.env` with a private local password. Never commit `.env`.

The safe template defines `API_BIND_ADDRESS=127.0.0.1` and `API_PORT=5278` for host-to-API traffic, plus `POSTGRES_BIND_ADDRESS=127.0.0.1` and `POSTGRES_PORT=5432` for host-to-database traffic. `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` configure PostgreSQL. Compose constructs `ConnectionStrings__SingularFlow` for the API with the internal hostname and port `postgres:5432`; ASP.NET Core maps the double underscore to `ConnectionStrings:SingularFlow`. The connection string is supplied at container runtime, not stored in the image.

Validate the Compose configuration:

```bash
docker compose config \
  --quiet
```

Build the API image directly:

```bash
docker build \
  --tag singular-flow-api:local \
  .
```

Build it through Compose:

```bash
docker compose build \
  api
```

Manual verification covered Compose configuration validation, both direct and Compose image builds, and successful multi-stage image construction. It also confirmed that installing `krb5-libs` removes the previous missing `libgssapi_krb5.so.2` runtime warning.

Start the complete stack and wait up to 60 seconds for PostgreSQL and API health:

```bash
docker compose up \
  --detach \
  --wait \
  --wait-timeout 60
```

Compose waits for PostgreSQL's `pg_isready` check before starting the API, then waits for the API's `/health/ready` check. Inspect both services and the API logs:

```bash
docker compose ps
docker compose logs \
  api \
  --tail 30
```

Verify liveness, readiness, and a representative persisted collection request:

```bash
curl \
  --silent \
  --show-error \
  --fail \
  http://127.0.0.1:5278/health/live

curl \
  --silent \
  --show-error \
  --fail \
  http://127.0.0.1:5278/health/ready

curl \
  --silent \
  --show-error \
  --fail \
  'http://127.0.0.1:5278/api/simulations?page=1&pageSize=1'
```

These URLs assume the `.env.example` defaults. If `API_BIND_ADDRESS` or `API_PORT` changes, use the corresponding host address. `POSTGRES_PORT` affects host-to-database tools only; it does not change the API's internal `postgres:5432` connection.

For direct, non-container API execution, load the private values and configure the connection for the current shell. This path uses the configured host-published `POSTGRES_PORT` rather than assuming port `5432`:

```bash
set -a
source .env
set +a
export ConnectionStrings__SingularFlow="Host=${POSTGRES_BIND_ADDRESS:-127.0.0.1};Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
```

Keep the value in the shell environment and the ignored `.env`; do not print it or commit `.env`.

Open `psql` inside the container:

```bash
docker compose exec postgres \
  psql \
  --username singular_flow \
  --dbname singular_flow
```

Stop the stack while preserving data in the named volume `singular-flow-postgres-data`:

```bash
docker compose down
```

Do not confuse that command with the following destructive variant, which removes the named volume and deletes locally persisted database data:

```bash
docker compose down \
  --volumes
```

`GET /health/live` does not require a successful database connection. `GET /health/ready` and the compatibility `GET /health` require the configured PostgreSQL endpoint to be reachable; a missing or unreachable connection causes readiness to fail. Normal persisted simulation operations still require the migrated schema. Startup registers the context but deliberately does not apply migrations automatically, and readiness does not apply migrations or validate that the schema is current.

API containerization and the database-aware health checks introduce no EF Core model or schema changes and require no migration. Neither Compose nor the Dockerfile creates the database schema or applies migrations. The API requires the same configured PostgreSQL connection and an applied schema for `POST /api/simulations`, `GET /api/simulations/{id}`, collection `GET /api/simulations`, and `DELETE /api/simulations/{id}`.

## EF Core migrations and database schema

The repository tracks `dotnet-ef` 10.0.12 in `dotnet-tools.json`. Restore the local tool from the repository root:

```bash
dotnet tool restore
```

The initial migration is `20260921055058_InitialPersistence`. It creates:

* `simulations`, containing the requested configuration, sampling mode, and creation timestamp.
* `simulation_states`, containing the ordered calculated states and a required foreign key to `simulations` with cascade deletion.

For design-time inspection, specify Infrastructure as both the target and startup project:

```bash
dotnet ef migrations list \
  --project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj \
  --startup-project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj
```

Check that the model still matches the latest migration:

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj \
  --startup-project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj
```

After configuring `ConnectionStrings__SingularFlow`, apply pending migrations to the development database before normal API use:

```bash
dotnet ef database update \
  --project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj \
  --startup-project src/SingularFlow.Infrastructure/SingularFlow.Infrastructure.csproj \
  --connection "$ConnectionStrings__SingularFlow"
```

These design-time commands use `SingularFlowDbContextFactory`. Applying or removing migrations remains a deliberate database operation; the API does not migrate the database during startup.

## Restore dependencies

From the repository root, run:

```bash
dotnet tool restore
dotnet restore SingularFlow.slnx
```

Restore should be executed after adding projects, project references, or package dependencies.

## Build

Build the complete solution:

```bash
dotnet build SingularFlow.slnx \
  --no-restore
```

Build using the Release configuration:

```bash
dotnet build SingularFlow.slnx \
  --configuration Release \
  --no-restore
```

Do not use `--no-restore` immediately after modifying project references unless a successful restore has already been completed.

## Run

See [Local Docker Compose stack](#local-docker-compose-stack) to run the complete containerized stack or configure PostgreSQL for direct execution. The schema must be applied before normal persistence operations; the CLI does not require PostgreSQL.

Run the command-line application:

```bash
dotnet run \
  --project src/SingularFlow.Cli/SingularFlow.Cli.csproj
```

Run without rebuilding after a successful build:

```bash
dotnet run \
  --project src/SingularFlow.Cli/SingularFlow.Cli.csproj \
  --no-build
```

Run the Web API:

```bash
dotnet run \
  --project src/SingularFlow.Api/SingularFlow.Api.csproj
```

The local development launch profiles use HTTP on port `5278` and HTTPS on port `7020`. These defaults are configured in `launchSettings.json`, and HTTPS redirection is enabled by default. The Production Compose service instead exposes HTTP on the configured host binding and disables HTTPS redirection.

Request process liveness over HTTP:

```bash
curl \
  --silent \
  --show-error \
  --include \
  http://localhost:5278/health/live
```

Request database readiness over HTTP:

```bash
curl \
  --silent \
  --show-error \
  --include \
  http://localhost:5278/health/ready
```

Both endpoints return a plain-text `Healthy` body when healthy. Liveness can remain healthy while readiness fails: readiness requires a valid configured PostgreSQL connection, whereas liveness only reports that the ASP.NET Core process responds. `GET /health` remains available as a backward-compatible readiness alias.

The expected healthy response body is:

```text
Healthy
```

The Development OpenAPI document is available at:

```text
http://localhost:5278/openapi/v1.json
```

OpenAPI is mapped only in the Development environment. The Compose API service runs in Production, so `/openapi/v1.json` is not exposed there.

Run a logarithmic simulation:

```bash
curl --include http://localhost:5278/api/simulations \
  --header 'Content-Type: application/json' \
  --data '{"singularTime":1.0,"concentrationExponent":0.005,"startTime":0.0,"endTime":0.9999,"sampleCount":6,"samplingMode":"logarithmic"}'
```

List persisted simulations:

```bash
curl --include \
  "http://localhost:5278/api/simulations?page=1&pageSize=20"
```

Quote URLs containing query strings because `&` has shell meaning.

Check an unsupported sampling mode (`400 Bad Request`):

```bash
curl --include http://localhost:5278/api/simulations \
  --header 'Content-Type: application/json' \
  --data '{"singularTime":1.0,"concentrationExponent":0.005,"startTime":0.0,"endTime":0.9999,"sampleCount":6,"samplingMode":"adaptive"}'
```

`src/SingularFlow.Api/SingularFlow.Api.http` contains reusable requests for liveness, readiness, the compatibility health endpoint, OpenAPI, logarithmic and uniform simulations, an unsupported sampling mode, malformed JSON, persisted simulation retrieval, and deletion based on `SimulationId`.

## Test

The database integration tests require the dedicated `singular_flow_tests` database and reject a connection whose database name is not exactly `singular_flow_tests`. Create it only when it does not already exist:

```bash
docker compose exec postgres sh -c \
  'psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --tuples-only --no-align --command "SELECT 1 FROM pg_database WHERE datname = '\''singular_flow_tests'\''" | grep -qx 1 || createdb --username "$POSTGRES_USER" singular_flow_tests'
```

The tests apply pending migrations to this test database. The HTTP database test deletes its inserted simulation, and the Infrastructure repository test rolls back its transaction.

Run all automated tests with the dedicated connection configured from `.env`:

```bash
set -a
source .env
set +a
export SINGULARFLOW_TEST_CONNECTION_STRING="Host=${POSTGRES_BIND_ADDRESS:-127.0.0.1};Port=${POSTGRES_PORT:-5432};Database=singular_flow_tests;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
dotnet test SingularFlow.slnx
```

Run tests without rebuilding after a successful build:

```bash
dotnet test SingularFlow.slnx \
  --no-build
```

Run tests using the Release configuration:

```bash
dotnet test SingularFlow.slnx \
  --configuration Release \
  --no-build
```

The solution currently contains 95 automated tests distributed across:

* 42 Domain unit tests.
* 14 Application unit tests.
* 23 API tests.
* 16 Infrastructure tests.

The Application unit tests cover collection-handler delegation, invalid page numbers and page sizes, total-page calculation, and deletion delegation with Boolean-result, ID, and cancellation-token propagation. The API tests cover healthy liveness, healthy readiness against `singular_flow_tests`, the compatibility `/health` endpoint, database-unavailable behavior, liveness remaining healthy during dependency failure, readiness returning `503`, health endpoints remaining outside OpenAPI, documented simulation operations, response codes and pagination parameter names, valid requests, request failures, persistence delegation, resource queries and deletions, pagination, and the real HTTP-to-PostgreSQL lifecycle. The unavailable-database test uses a deliberately unreachable local port with short timeouts; it does not stop Docker or mutate the test database. The simulation lifecycle test verifies creation, retrieval, listing, deletion, a subsequent GET `404`, and direct database confirmation that both the simulation and its child states are gone.

Most Infrastructure tests inspect EF Core metadata and design-time provider configuration without connecting to PostgreSQL. The repository integration tests connect to the dedicated test database and verify saving, detailed querying, ordered state reconstruction, a nullable result for a missing ID, total count, offset pagination, stable newest-first ordering, summary projection, later pages, out-of-range pages, physical deletion with cascade removal of states, and `false` for a missing deletion ID. Inserted repository data is contained in reversible transactions.

## Code formatting

Apply the formatting rules defined in `.editorconfig`:

```bash
dotnet format SingularFlow.slnx
```

Verify that no formatting changes are required:

```bash
dotnet format SingularFlow.slnx \
  --verify-no-changes \
  --no-restore
```

## Continuous integration

GitHub Actions validates every pull request targeting `main` and every push to `main`.

The workflow performs the following steps:

1. Start a temporary PostgreSQL service with a dedicated `singular_flow_tests` database and wait for its `pg_isready` health check.
2. Check out the repository.
3. Install the .NET SDK declared in `global.json`.
4. Display .NET information.
5. Restore the solution.
6. Verify code formatting.
7. Build the solution in Release mode.
8. Build the API Docker image from the root `Dockerfile` as `singular-flow-api:ci`.
9. Run all automated tests with `SINGULARFLOW_TEST_CONNECTION_STRING` configured from the service's assigned host port.

The temporary PostgreSQL service is used by the persistence and database-readiness integration tests. The Docker build step validates that the Dockerfile continues to build successfully. CI does not start the local Compose stack, run tests inside the API image, publish the image to a registry, or deploy the application.

The `main` branch is protected by a GitHub ruleset. Changes are integrated through pull requests after the required continuous-integration check succeeds.

## Development workflow

Development follows a feature-branch workflow:

1. Update the local `main` branch.
2. Create a focused feature branch.
3. Implement and test one incremental change.
4. Verify formatting, build, tests, and whitespace.
5. Create a descriptive Conventional Commit.
6. Push the feature branch.
7. Open a pull request targeting `main`.
8. Wait for the required continuous-integration check.
9. Merge the pull request.
10. Delete the merged feature branch.

Example:

```bash
git switch main
git pull --ff-only
git switch -c feat/example-feature
```

Commit messages follow the Conventional Commits style:

```text
feat(domain): add logarithmic sampling strategy
feat(application): add simulation execution use case
test(application): cover simulation handler behavior
docs: document application architecture
fix(cli): correct displayed sampling information
ci: update continuous integration workflow
```

## Development roadmap

The project is developed incrementally.

### Phase 1 — Foundation

* [x] Create the .NET solution.
* [x] Implement the initial scaling model.
* [x] Add a command-line interface.
* [x] Add automated domain tests.
* [x] Configure Git and publish the repository.
* [x] Configure continuous integration.
* [x] Protect the `main` branch.

### Phase 2 — Domain model

* [x] Introduce validated blow-up parameters.
* [x] Expand mathematical input validation.
* [x] Introduce validated time-series parameters.
* [x] Generate uniformly sampled simulation states.
* [x] Protect generated results through read-only collections.
* [x] Introduce interchangeable sampling strategies.
* [x] Add logarithmic sampling near the singular time.
* [x] Expand automated domain test coverage.
* [ ] Add additional mathematical invariants when required.
* [ ] Introduce adaptive sampling if justified by a concrete use case.

### Phase 3 — Application layer

* [x] Introduce a use case for executing simulations.
* [x] Separate application orchestration from mathematical calculations.
* [x] Introduce structured simulation request and result objects.
* [x] Move sampling-mode selection out of the CLI.
* [x] Add automated application tests.
* [x] Add cancellation support to asynchronous persistence.
* [x] Add a use case for retrieving a persisted simulation by ID.
* [x] Add a use case for deleting a persisted simulation by ID.
* [ ] Introduce additional use cases when required by the Web API.

### Phase 4 — Web API

* [x] Create an ASP.NET Core Web API.
* [x] Add database-aware process-liveness and PostgreSQL-readiness endpoints, retaining `/health` as a compatibility alias.
* [x] Generate an OpenAPI document in Development.
* [x] Add API integration-test infrastructure.
* [x] Add a simulation REST endpoint.
* [x] Add HTTP request validation.
* [x] Document simulation operations through OpenAPI.
* [x] Expand integration tests for simulation execution.
* [x] Return `201 Created` with persisted identity and `Location`.
* [x] Retrieve a persisted simulation by ID.
* [x] List persisted simulations with offset pagination, collection metadata, and summaries that omit states.
* [x] Delete persisted simulations by ID with `204` and `404` responses.

### Phase 5 — Persistence

* [x] Add local PostgreSQL infrastructure with Docker Compose.
* [x] Add persistent PostgreSQL storage and a container health check.
* [x] Add a tracked environment-variable template while excluding local secrets.
* [x] Introduce an Infrastructure project.
* [x] Add Entity Framework Core and the Npgsql provider.
* [x] Add a DbContext, separate persistence entities, and relational mappings.
* [x] Add the initial persistence migration.
* [x] Add automated EF Core model metadata tests.
* [x] Register Infrastructure and the database context in the API at runtime.
* [x] Introduce an application persistence abstraction and repository implementation.
* [x] Save simulation executions and calculated states.
* [x] Return generated persistence identity from simulation creation.
* [x] Add database-backed persistence integration tests.
* [x] Physically delete simulations and cascade deletion to their persisted states.

### Phase 6 — Background processing

* [ ] Execute simulations through background workers.
* [ ] Add a simulation queue.
* [ ] Report execution progress.
* [ ] Support cancellation and failure recovery.

### Phase 7 — Real-time communication

* [ ] Introduce SignalR.
* [ ] Stream simulation progress to connected clients.
* [ ] Display intermediate states without page reloads.

### Phase 8 — Interactive interface

* [ ] Create a Blazor frontend.
* [ ] Add charts and an interactive timeline.
* [ ] Visualize changes in velocity, energy, radius, and axial length.
* [ ] Add a pedagogical vortex visualization.

### Phase 9 — Deployment

The API and PostgreSQL are containerized as a complete local Docker Compose stack. Public deployment and production hosting remain future work.

* [x] Containerize the API with a multi-stage Docker build.
* [x] Run the API and PostgreSQL together through Docker Compose.
* [ ] Add the required infrastructure services.
* [ ] Deploy the application.
* [ ] Document the production environment.

## Development principles

SingularFlow follows these principles:

* Build features incrementally.
* Keep the domain independent of infrastructure.
* Keep persistence infrastructure separate from Domain and Application concerns.
* Exclude local credentials and secrets from version control.
* Keep application orchestration independent of presentation.
* Depend on abstractions when multiple behaviors are required.
* Write automated tests for meaningful behavior.
* Maintain zero build errors and warnings.
* Avoid unnecessary architectural complexity.
* Add dependencies only when justified.
* Keep commits small and descriptive.
* Protect the main branch.
* Require automated validation before merging.
* Document mathematical and technical limitations honestly.
* Distinguish educational models from complete physical simulations.

## References

* [On the Navier–Stokes Millennium Prize Problem](https://openai.com/index/navier-stokes-solution/)
* [Finite Time Blowup for Navier–Stokes](https://cdn.openai.com/pdf/32d9f210-8b73-45e0-91bc-82a30aef8a9a/navier-stokes.pdf)
* [Lean formalization repository](https://github.com/openai/NavierStokesAndEuler)
* [Clay Mathematics Institute — Navier–Stokes Equation](https://www.claymath.org/millennium/navier-stokes-equation/)
* [Official Navier–Stokes problem description](https://www.claymath.org/wp-content/uploads/2022/06/navierstokes.pdf)
