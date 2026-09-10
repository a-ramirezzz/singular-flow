# SingularFlow

[![Continuous Integration](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml/badge.svg)](https://github.com/a-ramirezzz/singular-flow/actions/workflows/ci.yml)

SingularFlow is an educational .NET application for exploring the asymptotic scaling of a concentrating vortex near a theoretical finite-time singularity.

The project is inspired by the finite-time blow-up construction for the three-dimensional incompressible Navier–Stokes equations published by OpenAI in September 2026.

## Project status

SingularFlow is currently under active development.

The current version calculates individual vortex-core scaling states and generates uniformly sampled time series as time approaches a configurable singular time.

The project is being developed incrementally, with automated tests, continuous integration, protected branches, pull requests, and documented architectural decisions.

## Current functionality

* Calculate the remaining time before the theoretical singularity.
* Calculate radial and axial length scales.
* Calculate radial and angular velocity scales.
* Calculate core volume and energy scales.
* Represent mathematical configuration through immutable domain value objects.
* Validate singular time and concentration exponent values.
* Validate time-series start time, end time, and sample count.
* Reject non-finite, negative, incompatible, and out-of-range values.
* Generate uniformly spaced sequences of simulation states.
* Guarantee that generated sequences include both configured endpoints.
* Prevent a generated series from reaching or exceeding the singular time.
* Return generated states through a read-only collection.
* Display the active mathematical and sampling configuration.
* Display calculated states through a command-line interface.
* Verify domain behavior with 26 automated test cases.
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

## Uniform time-series sampling

A time series is configured with:

```text
t₀ = start time
t₁ = end time
N  = sample count
```

The sampling interval is calculated as:

```text
Δt = (t₁ - t₀) / (N - 1)
```

Each sample time is generated with:

```text
tᵢ = t₀ + iΔt
```

where:

```text
i = 0, 1, 2, ..., N - 1
```

The current sampling rules require:

```text
0 ≤ t₀ < t₁ < T
2 ≤ N ≤ 100,000
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

The final sample is explicitly assigned the configured end time to avoid accumulated floating-point rounding differences.

The current generator uses uniform sampling. Future versions may introduce logarithmic or adaptive sampling strategies to provide greater resolution near the singular time.

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
Sampling interval: [0.0000, 0.9999]
Sample count: 6

      Time      Remaining         Radius          Axial     Angular velocity      Core energy
------------------------------------------------------------------------------------------------
    0.0000    1.0000E+000    1.0000E+000    1.0000E+000          1.0000E+000      1.0000E+000
    0.2000    8.0002E-001    8.9444E-001    8.9544E-001          1.1193E+000      8.9744E-001
    0.4000    6.0004E-001    7.7462E-001    7.7660E-001          1.2943E+000      7.8058E-001
    0.5999    4.0006E-001    6.3250E-001    6.3541E-001          1.5883E+000      6.4125E-001
    0.7999    2.0008E-001    4.4730E-001    4.5092E-001          2.2537E+000      4.5823E-001
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
│       └── SingularFlow.Domain.csproj
├── tests/
│   └── SingularFlow.Domain.Tests/
│       ├── BlowupParametersTests.cs
│       ├── BlowupScalingCalculatorTests.cs
│       ├── BlowupSeriesGeneratorTests.cs
│       ├── TimeSeriesParametersTests.cs
│       └── SingularFlow.Domain.Tests.csproj
├── .editorconfig
├── .gitignore
├── global.json
├── SingularFlow.slnx
└── README.md
```

## Architecture

The solution currently contains three projects.

### SingularFlow.Domain

Contains the mathematical behavior, domain models, validation rules, and time-series generation.

Responsibilities include:

* Mathematical parameter validation.
* Time-series parameter validation.
* Individual scaling calculations.
* Uniform time-series generation.
* Cross-configuration validation.
* Mathematical result models.
* Domain rules independent of presentation and infrastructure.

The domain project does not depend on the command-line interface or the test project.

### SingularFlow.Cli

Provides the current command-line interface and acts as the composition point for the application.

Responsibilities include:

* Creating the mathematical configuration.
* Creating the sampling configuration.
* Constructing the domain calculator and series generator.
* Executing the series-generation operation.
* Formatting calculated results.
* Displaying the active configuration and output.

The CLI depends on `SingularFlow.Domain`.

### SingularFlow.Domain.Tests

Contains automated tests for domain behavior.

The current tests verify:

* Valid and invalid singular-time configuration.
* Valid and invalid concentration-exponent configuration.
* Angular velocity growth near the singular time.
* Core-energy decrease near the singular time.
* Rejection of the singular time as a calculation point.
* Valid and invalid time-series configuration.
* Minimum and maximum sample-count rules.
* Requested output count.
* Inclusion of both sampling endpoints.
* Uniform spacing between generated times.
* Rejection of series that reach or exceed the singular time.
* Rejection of required null dependencies and arguments.

The test project depends on `SingularFlow.Domain`.

## Dependency direction

```text
SingularFlow.Cli ──────────> SingularFlow.Domain
SingularFlow.Domain.Tests ─> SingularFlow.Domain
```

`SingularFlow.Domain` does not depend on the CLI or the tests.

Within the domain, the current execution flow is:

```text
TimeSeriesParameters ─┐
                      ├─> BlowupSeriesGenerator ─> BlowupScalingCalculator
BlowupParameters ─────┘                                  │
                                                        └─> BlowupState
```

## Design decisions

### Immutable configuration objects

`BlowupParameters` and `TimeSeriesParameters` are immutable records.

Their constructors validate all local invariants before assigning their properties. Once an instance has been created successfully, it cannot be changed into an invalid configuration.

### Single-state calculation

`BlowupScalingCalculator` is responsible only for validating a calculation time and producing one `BlowupState`.

It does not decide how many states should be generated or how sample times should be distributed.

### Series generation

`BlowupSeriesGenerator` is responsible for:

* Validating compatibility between mathematical and sampling configurations.
* Calculating the uniform time step.
* Generating sample times in chronological order.
* Delegating each mathematical calculation to `BlowupScalingCalculator`.
* Returning the resulting states through a read-only collection.

### Constructor injection

`BlowupSeriesGenerator` receives `BlowupScalingCalculator` through its constructor.

This makes the dependency explicit and keeps object creation in the application entry point.

### Bounded sample count

The time-series configuration accepts between 2 and 100,000 samples.

The lower limit guarantees that the series has a start and an end. The upper limit reduces the risk of accidental excessive in-memory allocation.

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
dotnet restore
```

## Build

Build the complete solution:

```bash
dotnet build SingularFlow.slnx --no-restore
```

Build using the Release configuration:

```bash
dotnet build SingularFlow.slnx \
  --configuration Release \
  --no-restore
```

## Run

Run the command-line application:

```bash
dotnet run \
  --project src/SingularFlow.Cli/SingularFlow.Cli.csproj
```

## Test

Run all automated tests:

```bash
dotnet test SingularFlow.slnx
```

Run tests without rebuilding after a successful build:

```bash
dotnet test SingularFlow.slnx --no-build
```

Run tests using the Release configuration:

```bash
dotnet test SingularFlow.slnx \
  --configuration Release \
  --no-build
```

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
feat(domain): add simulation series generation
test(domain): cover invalid sampling ranges
docs: document simulation workflow
fix(cli): correct displayed sampling values
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
* [x] Protect generated results through a read-only collection.
* [x] Expand automated test coverage.
* [ ] Introduce alternative sampling strategies.
* [ ] Add logarithmic sampling near the singular time.
* [ ] Add additional mathematical invariants when required.

### Phase 3 — Application layer

* [ ] Introduce use cases for creating and executing simulations.
* [ ] Separate application orchestration from mathematical calculations.
* [ ] Add cancellation support.
* [ ] Add structured result objects.

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