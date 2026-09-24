# SingularFlow

[![Continuous Integration](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml/badge.svg)](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml)

SingularFlow is an educational .NET application for exploring the asymptotic scaling of a concentrating vortex near a theoretical finite-time singularity.

The project is inspired by the finite-time blow-up construction for the three-dimensional incompressible Navier–Stokes equations published by OpenAI in September 2026.

## Project status

SingularFlow is currently under active development.

The current version calculates individual vortex-core scaling states and generates time series using interchangeable uniform and logarithmic sampling strategies.

Application orchestration is separated from the mathematical domain through a dedicated use case. The command-line interface creates a simulation request, delegates execution to the application layer, and displays the structured result.

The ASP.NET Core Web API executes simulations over HTTP with uniform or logarithmic sampling. It also exposes a health-check endpoint and generates an OpenAPI document in Development. Invalid requests receive standardized HTTP errors.

The command-line application currently uses logarithmic remaining-time sampling to provide greater resolution near the configured singular time.

Local PostgreSQL 18.6 infrastructure is available through Docker Compose. `SingularFlow.Infrastructure` defines the EF Core schema, mappings, initial migration, and repository implementation. The API connects to PostgreSQL through Npgsql and persists each simulation and its ordered states before returning `201 Created` with the database-generated `id` and `createdAtUtc`. The `Location` header identifies the GET-by-ID endpoint, while a separate paginated collection endpoint lists persisted simulations.

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
* Verify domain, application, API, and Infrastructure behavior with 86 automated tests: 42 Domain, 12 Application, 18 API, and 14 Infrastructure tests.
* Validate every pull request and push to `main` with GitHub Actions.
* Host an ASP.NET Core Web API.
* Expose an operational health-check endpoint.
* Generate an OpenAPI 3.1.1 document in Development.
* Test the real ASP.NET Core HTTP pipeline in memory.
* Create persisted simulations through `POST /api/simulations` with `uniform` or `logarithmic` sampling.
* Persist each API simulation and its ordered states to PostgreSQL before returning the calculated series.
* Return `201 Created` with the persisted `id`, database-generated `createdAtUtc`, and a `Location` header.
* Retrieve persisted simulations through `GET /api/simulations/{id}` and return `404 Not Found` for an unknown ID.
* List persisted simulations through `GET /api/simulations` with optional `page` and `pageSize` parameters, defaults of `1` and `20`, and a maximum page size of `100`.
* Return lightweight simulation summaries in stable newest-first order with `page`, `pageSize`, `totalCount`, and `totalPages` metadata, without returning state collections.
* Return `400 Bad Request` with `ProblemDetails` when pagination values fall outside their accepted ranges.
* Map dedicated API contracts to Application requests and results, and return structured JSON states.
* Return `400 Bad Request` with `ProblemDetails` for invalid simulation parameters and `ValidationProblemDetails` for model-binding or malformed-JSON failures.
* Document collection GET with pagination parameters and `200` and `400` responses, POST with `201` and `400`, and GET by ID with `200` and `404` through OpenAPI.
* Provide reusable HTTP requests in `SingularFlow.Api.http`.
* Start PostgreSQL locally with Docker Compose and check the database container's health.
* Persist local database data through a named Docker volume.
* Keep local database credentials outside version control.
* Model simulations and their generated states with EF Core persistence entities, PostgreSQL mappings, constraints, indexes, and a tracked initial migration.
* Keep the Application persistence contracts and orchestration independent of EF Core.
* Project collection summaries directly from PostgreSQL without loading `simulation_states` rows.
* Verify the complete HTTP-to-PostgreSQL POST, GET-by-ID, and collection-listing path with a dedicated database integration test.
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
* Microsoft.AspNetCore.OpenApi
* Microsoft.AspNetCore.Mvc.Testing
* xUnit
* Git
* GitHub
* GitHub Actions
* PostgreSQL 18.6 (`postgres:18.6-alpine3.24`)
* Docker
* Docker Compose
* Entity Framework Core 10.0.12 and Entity Framework Core Design 10.0.12
* Npgsql Entity Framework Core provider 10.0.3

## Planned technologies

Future versions are expected to introduce:

* Background services
* SignalR
* Blazor
* Interactive data visualization
* Docker for future application containerization; currently only PostgreSQL is containerized.

Planned technologies will only be added when the project has a concrete requirement for them.

## Project structure

```text
singular-flow/
├── .github/
│   └── workflows/
│       └── ci.yml
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
│   │   │   └── HealthEndpointTests.cs
│   │   ├── OpenApi/
│   │   │   └── OpenApiEndpointTests.cs
│   │   ├── Simulations/
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
├── .env.example
├── .gitignore
├── compose.yaml
├── dotnet-tools.json
├── global.json
├── SingularFlow.slnx
└── README.md
```

`compose.yaml` defines the local PostgreSQL service. `.env.example` is the configuration template; the private `.env` file is ignored and is not part of the project structure.

## Architecture

The solution currently contains nine projects separated into five production projects and four automated test projects.

### Local infrastructure

Docker Compose provisions PostgreSQL through the root `compose.yaml` file. Infrastructure contains the EF Core PostgreSQL model, migration, and repository. The API registers the context and repository at runtime and requires an existing migrated schema for normal simulation requests.

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
* Executing `RunAndSaveSimulationHandler`, `GetSimulationHandler`, and `ListSimulationsHandler` through dependency injection and returning persisted simulations as JSON.
* Registering `SingularFlowDbContext` with Npgsql from `ConnectionStrings:SingularFlow`.
* Binding `ISimulationRepository` to `EfSimulationRepository`.
* Producing standardized HTTP errors for invalid requests.
* Registering OpenAPI generation.
* Registering ASP.NET Core health-check services.
* Exposing `GET /health`.
* Generating an OpenAPI document in Development.
* Applying HTTPS redirection.
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
* Returning `PagedSimulationResult` metadata and collection-specific `SimulationSummaryResult` items without state collections.
* Returning structured simulation results.
* Preventing presentation concerns from entering the domain layer.

The application project depends on `SingularFlow.Domain` and has no EF Core dependency.

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
* Providing design-time context creation without a stored password.

Infrastructure depends directly on `SingularFlow.Application`, which in turn depends on Domain. EF Core remains confined to Infrastructure and the API composition root; Application and Domain do not depend on it.

The persisted query features use the existing schema and do not require a new migration.

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

The twelve application tests verify:

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

The test project depends directly on `SingularFlow.Application`.

### SingularFlow.Api.Tests

Contains integration tests for the ASP.NET Core host, including one database-backed end-to-end test.

The eighteen API tests verify:

* `GET /health` returns `200 OK` and `Healthy`.
* The Development OpenAPI document contains the API metadata, collection pagination parameters, and documented simulation operations and responses.
* Valid logarithmic and uniform simulation requests return `201 Created`, resource identity, creation timestamp, and a resource location.
* Unsupported sampling modes and invalid concentration exponents return `400 ProblemDetails`.
* Missing sampling modes and malformed JSON return `400 ValidationProblemDetails`.
* The persistence abstraction receives the calculated simulation before a successful response is returned.
* Existing resource queries return the persisted representation and missing queries return `404 Not Found`.
* Collection queries return summary fields and pagination metadata, use default query values, omit states, and reject invalid pagination with `400 Bad Request`.
* The actual HTTP-to-PostgreSQL path creates a simulation, follows its `Location`, retrieves its configuration and ordered states, lists its summary, and removes the database record afterward.

The contract and validation tests use `WebApplicationFactory<Program>` with a test repository, so they execute the real HTTP pipeline without requiring PostgreSQL. The dedicated database endpoint test retains the production repository, applies pending migrations, and deletes its inserted simulation afterward.

The test project depends directly on `SingularFlow.Api`.

### SingularFlow.Infrastructure.Tests

Contains 14 automated tests for EF Core model metadata, design-time context creation, and repository persistence.

The tests verify table and column mappings, generated primary keys, PostgreSQL column types, required cascade relationships, indexes, check constraints, the `CURRENT_TIMESTAMP` default, Npgsql provider configuration, persistence of one simulation with ordered states, and query reconstruction in `Sequence` order. Missing IDs return `null`. Repository integration tests also verify total count, page metadata, stable descending ordering, first and later pages, summary projection, and an out-of-range page with empty items and the original total count. The database tests apply pending migrations and use reversible transactions for inserted repository data.

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

### Local PostgreSQL infrastructure

The Compose service pins `postgres:18.6-alpine3.24` instead of using `latest`; the image supports the project's ARM64 development environment. The `postgres` service uses the container name `singular-flow-postgres` and checks readiness with `pg_isready`.

The default host binding is loopback-only (`POSTGRES_BIND_ADDRESS=127.0.0.1`). `POSTGRES_PORT` defaults to `5432` and can be changed when that host port is occupied. The database always listens on port `5432` inside the container.

The tracked `.env.example` template is separate from the ignored local `.env` file, so local credentials stay out of version control. The named volume `singular-flow-postgres-data` preserves data across container recreation and mounts at `/var/lib/postgresql`, the PostgreSQL 18 data location.

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

POST calculates and persists a simulation, GET by ID returns its detailed persisted representation, and collection GET returns lightweight persisted summaries.

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

`SimulationsController` delegates creation to `RunAndSaveSimulationHandler`, detailed retrieval to `GetSimulationHandler`, and optional collection query parameters to `ListSimulationsHandler`. It maps paged Application results to dedicated API contracts and returns `200 OK` for valid listings. Invalid pagination flows through the existing exception pipeline. POST returns `201 Created`; a successful GET by ID returns `200 OK`, and a missing resource becomes `404 Not Found`. The controller contains no mathematical calculations or EF Core query logic.

### Dependency injection

`RunSimulationHandler`, `RunAndSaveSimulationHandler`, `GetSimulationHandler`, and `ListSimulationsHandler` are registered with scoped lifetimes. The API registers `SingularFlowDbContext` with Npgsql, maps `ISimulationRepository` to `EfSimulationRepository`, and injects the handlers into `SimulationsController`.

### Global exception handling

`InvalidSimulationRequestExceptionHandler` handles known invalid-input `ArgumentOutOfRangeException` failures as `400 ProblemDetails`. Unexpected exception types continue through the general error pipeline instead of being classified as client errors. `Program.cs` registers problem details and the handler, then enables exception handling middleware.

### Model-binding validation

`[ApiController]` handles missing required values and malformed JSON before the controller action runs, returning `400 ValidationProblemDetails`. These responses are separate from the custom exception handler.

### HTTP status choice

POST creates a persisted simulation resource and returns `201 Created` with its representation. The response body contains the generated `id` and `createdAtUtc`, and `Location` identifies `GET /api/simulations/{id}`. GET by ID returns `200 OK` when the resource exists and `404 Not Found` when it does not. Collection GET returns `200 OK`; an empty database has empty `items`, `totalCount` and `totalPages` of `0`, while a valid page beyond the available range retains its requested metadata and total count with empty `items`. Neither case is a `404`. Invalid POST input and invalid collection pagination return `400 Bad Request`.

### Operational health check

The `/health` endpoint uses the built-in ASP.NET Core health-check infrastructure.

It returns `200 OK` with `Healthy` while all registered checks report a healthy status.

### Development OpenAPI document

OpenAPI generation is enabled only in the Development environment and currently produces an OpenAPI 3.1.1 document.

The document includes collection `GET /api/simulations` with `page` and `pageSize` query parameters and `200` and `400` responses, `POST /api/simulations` with `201` and `400`, and `GET /api/simulations/{id}` with `200` and `404`. The infrastructure health endpoint is intentionally not included as a controller operation.

### In-memory API testing

`SingularFlow.Api.Tests` uses `WebApplicationFactory<Program>`.

This starts the real ASP.NET Core pipeline in memory and verifies HTTP status codes, response bodies, content types, and generated OpenAPI metadata without requiring a separately running server. Contract, validation, and query tests replace `ISimulationRepository` with test implementations; a separate test retains `EfSimulationRepository` to verify the actual HTTP-to-PostgreSQL creation, detailed retrieval, and collection listing path before cleaning up the created record.

### Paginated summary projection

`EfSimulationRepository.ListAsync` performs a read-only `AsNoTracking` query. It first uses `CountAsync` for collection metadata, calculates the offset with `long` arithmetic to avoid multiplication overflow for very large page numbers, and returns empty items without running the page query when the offset is beyond the total count.

For a page that can contain data, EF Core generates a separate query ordered by `CreatedAtUtc` descending and then `Id` descending as a stable tie-breaker. The query applies offset pagination through `Skip` and `Take` and projects directly to `SimulationSummaryResult`. It does not call `Include`, materialize full persistence entities for the response, or load `simulation_states`. Collection summaries keep payloads bounded instead of returning every calculated state for every simulation. The existing `simulations` table and `created_at_utc` index are reused, so no new migration is required.

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

Validation error keys and messages depend on the failed input. The Development OpenAPI document at `/openapi/v1.json` includes collection GET with `page` and `pageSize` parameters and `200` and `400` responses, POST with `201` and `400`, and GET by ID with `200` and `404`.

## Requirements

Install the following software before building the project:

* .NET 10 SDK
* Git

Docker Desktop and Docker Compose are required to run the API locally and to execute the full test suite, which includes database integration tests.

Check the installed .NET SDK:

```bash
dotnet --version
```

The project currently uses:

```text
10.0.401
```

The required SDK family is declared in `global.json`.

## Local PostgreSQL

The root `compose.yaml` provisions PostgreSQL only. Its pinned `postgres:18.6-alpine3.24` image supports ARM64. Docker Compose configuration has been validated, PostgreSQL has been verified healthy, data persistence across container recreation has been manually verified, and the EF Core schema has been manually applied and verified locally.

Copy the environment template:

```bash
cp .env.example .env
```

Replace the example password in `.env` with a private local password. Never commit `.env`.

Validate the Compose configuration, download the pinned image, and start PostgreSQL:

```bash
docker compose config --quiet
docker compose pull
docker compose up -d
```

Inspect service health and recent logs:

```bash
docker compose ps
docker compose logs postgres --tail 20
```

The default port mapping is `127.0.0.1:${POSTGRES_PORT}` on the host to PostgreSQL port `5432` inside the container. `POSTGRES_BIND_ADDRESS` controls the host binding and defaults to `127.0.0.1`; `POSTGRES_PORT` defaults to `5432`. If host port `5432` is occupied, set `POSTGRES_PORT=5433` in the private `.env` file. Port `5433` is an optional local setting, not a project default.

Load the private values and configure the API connection for the current shell. This uses the configured `POSTGRES_PORT` rather than assuming port `5432`:

```bash
set -a
source .env
set +a
export ConnectionStrings__SingularFlow="Host=${POSTGRES_BIND_ADDRESS:-127.0.0.1};Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
```

ASP.NET Core maps `ConnectionStrings__SingularFlow` to `ConnectionStrings:SingularFlow`. Keep the value in the shell environment and the ignored `.env`; do not print it or commit `.env`.

Open `psql` inside the container:

```bash
docker compose exec postgres \
  psql \
  --username singular_flow \
  --dbname singular_flow
```

Stop the container while preserving data in the named volume `singular-flow-postgres-data`:

```bash
docker compose down
```

The following command is destructive to local database data because it removes the volume:

```bash
docker compose down --volumes
```

The API requires PostgreSQL and an applied schema for `POST /api/simulations`, `GET /api/simulations/{id}`, and collection `GET /api/simulations`. Startup registers the context but deliberately does not apply migrations automatically. Collection listing reuses the existing `simulations` table and its `created_at_utc` index, so it requires no new migration.

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

See [Local PostgreSQL](#local-postgresql) to start PostgreSQL, configure `ConnectionStrings__SingularFlow`, and apply the schema before running the API. The CLI does not require PostgreSQL.

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

The local development launch profiles use HTTP on port `5278` and HTTPS on port `7020`. These defaults are configured in `launchSettings.json`.

Request the operational health endpoint over HTTP:

```bash
curl \
  --include \
  http://localhost:5278/health
```

The expected response body is:

```text
Healthy
```

The Development OpenAPI document is available at:

```text
http://localhost:5278/openapi/v1.json
```

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

`src/SingularFlow.Api/SingularFlow.Api.http` contains reusable requests for health, OpenAPI, logarithmic and uniform simulations, an unsupported sampling mode, malformed JSON, and persisted simulation retrieval.

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

The solution currently contains 86 automated tests distributed across:

* 42 Domain unit tests.
* 12 Application unit tests.
* 18 API tests.
* 14 Infrastructure tests.

The Application unit tests cover collection-handler delegation, invalid page numbers and page sizes, and total-page calculation. The API tests cover health, OpenAPI operations, response codes and pagination parameter names, valid logarithmic and uniform requests, request failures, persistence delegation, existing and missing resource queries, default and explicit pagination, summary fields and metadata, omission of state collections, invalid pagination responses, and the real HTTP-to-PostgreSQL creation, detailed retrieval, and collection listing path. HTTP contract, validation, and query tests substitute a test repository; only the dedicated database endpoint test uses PostgreSQL and cleans up its created record.

Most Infrastructure tests inspect EF Core metadata and design-time provider configuration without connecting to PostgreSQL. The repository integration tests connect to the dedicated test database and verify saving, detailed querying, ordered state reconstruction, a nullable result for a missing ID, total count, offset pagination, stable newest-first ordering, summary projection, later pages, and out-of-range pages. Inserted repository data is contained in reversible transactions.

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

1. Start a temporary PostgreSQL service with a dedicated `singular_flow_tests` database.
2. Check out the repository.
3. Install the .NET SDK declared in `global.json`.
4. Restore dependencies.
5. Verify code formatting.
6. Build the solution in Release mode.
7. Run all automated tests with `SINGULARFLOW_TEST_CONNECTION_STRING` configured from the service's assigned host port.

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
* [ ] Introduce additional use cases when required by the Web API.

### Phase 4 — Web API

* [x] Create an ASP.NET Core Web API.
* [x] Add an operational health-check endpoint.
* [x] Generate an OpenAPI document in Development.
* [x] Add API integration-test infrastructure.
* [x] Add a simulation REST endpoint.
* [x] Add HTTP request validation.
* [x] Document simulation operations through OpenAPI.
* [x] Expand integration tests for simulation execution.
* [x] Return `201 Created` with persisted identity and `Location`.
* [x] Retrieve a persisted simulation by ID.
* [x] List persisted simulations with offset pagination, collection metadata, and summaries that omit states.

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

PostgreSQL is containerized for local development; the .NET application is not yet containerized.

* [ ] Containerize the application with Docker.
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
