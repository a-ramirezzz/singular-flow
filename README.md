# SingularFlow

SingularFlow is an educational .NET application for exploring the asymptotic scaling of a concentrating vortex near a theoretical finite-time singularity.

The project is inspired by the finite-time blow-up construction for the three-dimensional incompressible Navier–Stokes equations published by OpenAI in September 2026.

## Project status

SingularFlow is currently in its initial development stage.

The current version calculates and displays how selected vortex-core scales change as time approaches a configurable singular time.

## Current functionality

* Calculate the remaining time before the theoretical singularity.
* Calculate radial and axial length scales.
* Calculate radial and angular velocity scales.
* Calculate core volume and energy scales.
* Validate mathematical input parameters.
* Reject non-finite and out-of-range values.
* Display calculated results through a command-line interface.
* Verify domain behavior with automated tests.

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

The parameter `h` must satisfy:

```text
0 < h < 0.01
```

As `t` approaches `T`:

* The remaining time approaches zero.
* The radial and axial length scales decrease.
* The vortex core becomes increasingly concentrated.
* The angular velocity scale increases.
* The core volume decreases.
* The modeled core energy remains controlled and decreases for the permitted values of `h`.

This demonstrates the mathematical idea that an unbounded velocity scale can be concentrated inside a sufficiently small region without requiring unbounded total energy.

## Mathematical scope and disclaimer

The current implementation is an educational representation of selected asymptotic scaling relationships.

It is not:

* A complete Navier–Stokes solver.
* A computational fluid dynamics engine.
* A reproduction or formal verification of the complete analytical proof.
* A numerical demonstration that a singularity exists.
* A prediction of singularities in physical fluids.
* A substitute for independent mathematical review of the published result.

The application models scaling behavior described in the reference material. It does not implement the complete velocity field, pressure field, external force, oscillatory corrections, or analytical construction contained in the paper.

## Example output

```text
SingularFlow — Blow-up scaling model

      Time      Remaining         Radius          Axial     Angular velocity      Core energy
------------------------------------------------------------------------------------------------
    0.0000    1.0000E+000    1.0000E+000    1.0000E+000          1.0000E+000      1.0000E+000
    0.5000    5.0000E-001    7.0711E-001    7.0956E-001          1.4191E+000      7.1450E-001
    0.9000    1.0000E-001    3.1623E-001    3.1989E-001          3.1989E+000      3.2734E-001
    0.9900    1.0000E-002    1.0000E-001    1.0233E-001          1.0233E+001      1.0715E-001
    0.9990    1.0000E-003    3.1623E-002    3.2734E-002          3.2734E+001      3.5075E-002
    0.9999    1.0000E-004    1.0000E-002    1.0471E-002          1.0471E+002      1.1482E-002
```

## Technologies currently used

* C#
* .NET 10
* xUnit
* Git

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
* GitHub Actions
* Automated integration tests

Planned technologies will only be added when the project has a concrete requirement for them.

## Project structure

```text
singular-flow/
├── src/
│   ├── SingularFlow.Cli/
│   │   ├── Program.cs
│   │   └── SingularFlow.Cli.csproj
│   └── SingularFlow.Domain/
│       ├── Calculations/
│       │   └── BlowupScalingCalculator.cs
│       ├── Models/
│       │   ├── BlowupParameters.cs
│       │   └── BlowupState.cs
│       └── SingularFlow.Domain.csproj
├── tests/
│   └── SingularFlow.Domain.Tests/
│       ├── BlowupScalingCalculatorTests.cs
│       └── SingularFlow.Domain.Tests.csproj
├── .editorconfig
├── .gitignore
├── global.json
├── SingularFlow.slnx
└── README.md
```

## Architecture

The initial solution contains three projects.

### SingularFlow.Domain

Contains the mathematical behavior and domain models.

Responsibilities include:

* Parameter validation.
* Scaling calculations.
* Mathematical result models.
* Domain rules independent of presentation or infrastructure.

The domain project does not depend on the command-line interface or the test project.

### SingularFlow.Cli

Provides the current command-line interface.

Responsibilities include:

* Selecting the times to evaluate.
* Calling the domain calculator.
* Formatting results.
* Displaying the output in the terminal.

The CLI depends on `SingularFlow.Domain`.

### SingularFlow.Domain.Tests

Contains automated tests for the domain behavior.

The current tests verify that:

* Angular velocity increases as time approaches the singular time.
* Core energy decreases as time approaches the singular time.
* The singular time itself is rejected as an invalid calculation point.

The test project depends on `SingularFlow.Domain`.

## Dependency direction

```text
SingularFlow.Cli ──────────> SingularFlow.Domain
SingularFlow.Domain.Tests ─> SingularFlow.Domain
```

`SingularFlow.Domain` does not depend on the CLI or the tests.

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
dotnet build --no-restore
```

Build using the Release configuration:

```bash
dotnet build --configuration Release
```

## Run

Run the command-line application:

```bash
dotnet run --project src/SingularFlow.Cli/SingularFlow.Cli.csproj
```

## Test

Run all automated tests:

```bash
dotnet test
```

Run the tests using the Release configuration:

```bash
dotnet test --configuration Release
```

## Format

Apply the formatting rules defined by `.editorconfig`:

```bash
dotnet format SingularFlow.slnx
```

Verify that no formatting changes are required:

```bash
dotnet format SingularFlow.slnx --verify-no-changes
```

## Development roadmap

The project will be developed incrementally.

### Phase 1 — Foundation

* Create the .NET solution.
* Implement the initial scaling model.
* Add a command-line interface.
* Add automated domain tests.
* Configure Git and publish the repository.

### Phase 2 — Domain model

* Introduce a dedicated parameter model.
* Expand input validation.
* Add additional mathematical invariants.
* Improve test coverage.
* Generate sequences of simulation states.

### Phase 3 — Application layer

* Introduce use cases for creating and executing simulations.
* Separate orchestration from mathematical calculations.
* Add cancellation support.
* Add structured result objects.

### Phase 4 — Web API

* Create an ASP.NET Core Web API.
* Add REST endpoints.
* Add request validation.
* Generate OpenAPI documentation.
* Add integration tests.

### Phase 5 — Persistence

* Introduce PostgreSQL.
* Add Entity Framework Core.
* Store simulations and calculated snapshots.
* Add migrations and indexed queries.

### Phase 6 — Background processing

* Execute simulations through background workers.
* Add a simulation queue.
* Report execution progress.
* Support cancellation and failure recovery.

### Phase 7 — Real-time communication

* Introduce SignalR.
* Stream simulation progress to connected clients.
* Display intermediate states without page reloads.

### Phase 8 — Interactive interface

* Create a Blazor frontend.
* Add charts and an interactive timeline.
* Visualize changes in velocity, energy, radius, and axial length.
* Add a pedagogical vortex visualization.

### Phase 9 — Deployment

* Containerize the application with Docker.
* Add GitHub Actions.
* Run automated builds and tests.
* Deploy the application.
* Document the production environment.

## Development principles

SingularFlow follows these principles:

* Build features incrementally.
* Keep the domain independent of infrastructure.
* Write automated tests for meaningful behavior.
* Maintain zero build errors.
* Avoid unnecessary architectural complexity.
* Add dependencies only when justified.
* Keep commits small and descriptive.
* Document mathematical and technical limitations honestly.
* Distinguish educational models from complete physical simulations.

## References

* [On the Navier–Stokes Millennium Prize Problem](https://openai.com/index/navier-stokes-solution/)
* [Finite Time Blowup for Navier–Stokes](https://cdn.openai.com/pdf/32d9f210-8b73-45e0-91bc-82a30aef8a9a/navier-stokes.pdf)
* [Lean formalization repository](https://github.com/openai/NavierStokesAndEuler)
* [Official Navier–Stokes problem description](https://www.claymath.org/wp-content/uploads/2022/06/navierstokes.pdf)

