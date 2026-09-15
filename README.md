# SingularFlow

[![Continuous Integration](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml/badge.svg)](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml)

SingularFlow is an educational .NET application for exploring the asymptotic scaling of a concentrating vortex near a theoretical finite-time singularity.

The project is inspired by the finite-time blow-up construction for the three-dimensional incompressible Navier–Stokes equations published by OpenAI in September 2026.

## Project status

SingularFlow is currently under active development.

The current version calculates individual vortex-core scaling states and generates time series using interchangeable uniform and logarithmic sampling strategies.

Application orchestration is separated from the mathematical domain through a dedicated use case. The command-line interface creates a simulation request, delegates execution to the application layer, and displays the structured result.

The command-line application currently uses logarithmic remaining-time sampling to provide greater resolution near the configured singular time.

The project is being developed incrementally with automated tests, continuous integration, protected branches, pull requests, and documented architectural decisions.

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
* Verify domain and application behavior with 47 automated test cases.
* Validate every pull request and push to `main` with GitHub Actions.

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
* xUnit
* Git
* GitHub
* GitHub Actions

## Planned technologies

Future versions are expected to introduce:

* ASP.NET Core Web API
* Entity Framework Core
* PostgreSQL
* Background services
* SignalR
* Blazor
* Interactive data visualization
* Docker
* Automated integration tests

Planned technologies will only be added when the project has a concrete requirement for them.

## Project structure

```text
singular-flow/
├── .github/
│   └── workflows/
│       └── ci.yml
├── src/
│   ├── SingularFlow.Application/
│   │   ├── Simulations/
│   │   │   ├── RunSimulationHandler.cs
│   │   │   ├── RunSimulationRequest.cs
│   │   │   ├── RunSimulationResult.cs
│   │   │   └── SamplingMode.cs
│   │   └── SingularFlow.Application.csproj
│   ├── SingularFlow.Cli/
│   │   ├── Program.cs
│   │   └── SingularFlow.Cli.csproj
│   └── SingularFlow.Domain/
│       ├── Calculations/
│       │   ├── BlowupScalingCalculator.cs
│       │   └── BlowupSeriesGenerator.cs
│       ├── Models/
│       │   ├── BlowupParameters.cs
│       │   ├── BlowupState.cs
│       │   └── TimeSeriesParameters.cs
│       ├── Sampling/
│       │   ├── ITimeSamplingStrategy.cs
│       │   ├── LogarithmicTimeSamplingStrategy.cs
│       │   ├── TimeSamplingValidation.cs
│       │   └── UniformTimeSamplingStrategy.cs
│       └── SingularFlow.Domain.csproj
├── tests/
│   ├── SingularFlow.Application.Tests/
│   │   ├── Simulations/
│   │   │   └── RunSimulationHandlerTests.cs
│   │   └── SingularFlow.Application.Tests.csproj
│   └── SingularFlow.Domain.Tests/
│       ├── BlowupParametersTests.cs
│       ├── BlowupScalingCalculatorTests.cs
│       ├── BlowupSeriesGeneratorTests.cs
│       ├── LogarithmicTimeSamplingStrategyTests.cs
│       ├── TimeSeriesParametersTests.cs
│       ├── UniformTimeSamplingStrategyTests.cs
│       └── SingularFlow.Domain.Tests.csproj
├── .editorconfig
├── .gitignore
├── global.json
├── SingularFlow.slnx
└── README.md
```

## Architecture

The solution currently contains five projects separated into production code and automated test projects.

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

### SingularFlow.Application

Contains the application use cases that coordinate domain behavior.

Responsibilities include:

* Receiving structured simulation requests.
* Selecting the requested sampling strategy.
* Creating validated domain configurations.
* Coordinating the scaling calculator and series generator.
* Executing a complete simulation use case.
* Returning structured simulation results.
* Preventing presentation concerns from entering the domain layer.

The application project depends on `SingularFlow.Domain`.

It does not depend on the command-line interface or test projects.

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

The current application tests verify:

* Rejection of a null simulation request.
* Execution with uniform sampling.
* Execution with logarithmic remaining-time sampling.
* Preservation of validated mathematical configuration.
* Preservation of validated time-series configuration.
* Preservation of the selected sampling mode.
* Rejection of unsupported sampling modes.
* Production of structured simulation results.

The test project depends directly on `SingularFlow.Application`.

## Dependency direction

The direct project dependencies are:

```text
SingularFlow.Cli ───────────────────> SingularFlow.Application
                                               │
                                               ▼
                                  SingularFlow.Domain

SingularFlow.Application.Tests ────> SingularFlow.Application
SingularFlow.Domain.Tests ─────────> SingularFlow.Domain
```

The dependency rules are:

* `SingularFlow.Domain` has no dependency on higher-level projects.
* `SingularFlow.Application` depends on the domain.
* `SingularFlow.Cli` depends on the application layer.
* Production projects do not depend on test projects.
* Presentation behavior does not enter the domain.
* Mathematical rules do not depend on the presentation mechanism.

The complete execution flow is:

```text
Program.cs
    │
    ▼
RunSimulationRequest
    │
    ▼
RunSimulationHandler
    │
    ├── Creates BlowupParameters
    ├── Creates TimeSeriesParameters
    └── Selects ITimeSamplingStrategy
                  │
                  ├── UniformTimeSamplingStrategy
                  └── LogarithmicTimeSamplingStrategy
                              │
                              ▼
                   BlowupSeriesGenerator
                              │
                              ▼
                  BlowupScalingCalculator
                              │
                              ▼
                  IReadOnlyList<BlowupState>
                              │
                              ▼
                  RunSimulationResult
                              │
                              ▼
                     CLI presentation
```

## Design decisions

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

## Requirements

Install the following software before building the project:

* .NET 10 SDK
* Git

Docker will be required in a future stage when database and infrastructure components are introduced.

Check the installed .NET SDK:

```bash
dotnet --version
```

The project currently uses:

```text
10.0.401
```

The required SDK family is declared in `global.json`.

## Restore dependencies

From the repository root, run:

```bash
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

## Test

Run all automated tests:

```bash
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

The solution currently contains 47 automated tests distributed across the domain and application test projects.

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

1. Check out the repository.
2. Install the .NET SDK declared in `global.json`.
3. Restore dependencies.
4. Verify code formatting.
5. Build the solution in Release mode.
6. Run all automated tests.

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
* [ ] Add cancellation support when asynchronous execution is introduced.
* [ ] Introduce additional use cases when required by the Web API.

### Phase 4 — Web API

* [ ] Create an ASP.NET Core Web API.
* [ ] Add REST endpoints.
* [ ] Add request validation.
* [ ] Generate OpenAPI documentation.
* [ ] Add integration tests.

### Phase 5 — Persistence

* [ ] Introduce PostgreSQL.
* [ ] Add Entity Framework Core.
* [ ] Store simulations and calculated snapshots.
* [ ] Add migrations and indexed queries.

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

* [ ] Containerize the application with Docker.
* [ ] Add the required infrastructure services.
* [ ] Deploy the application.
* [ ] Document the production environment.

## Development principles

SingularFlow follows these principles:

* Build features incrementally.
* Keep the domain independent of infrastructure.
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