# SERONA One — Pattern Grading

Pattern Grading is a technical module developed within the **SERONA One** ecosystem for generating size variants from authoritative 2D pattern geometry while preserving geometric identity, provenance, persistence, auditability, and deterministic downstream integration.

> **Portfolio snapshot**
>
> This repository contains a sanitized snapshot of the Pattern Grading module as it exists inside the larger SERONA One codebase.
>
> It intentionally preserves original project references and module boundaries and is **not intended to build as a standalone application**.

## Engineering problem

Pattern grading transforms a base pattern into ordered size variants while preserving explicit geometric and business rules.

The implementation addresses more than simple coordinate scaling. It includes:

- authoritative polygon and Bézier geometry
- ordered size systems
- interior base sizes
- consecutive grading increments
- per-piece size availability
- distinction between missing rules and explicit zero increments
- curved geometry handling
- immutable revisions and results
- durable background execution
- optimistic concurrency
- auditable approval workflows
- deterministic adaptation into Automatic Nesting
- independent geometric certification
- health, metrics, and operational diagnostics

## Geometry model

The module uses its own authoritative geometry model.

Core concepts include:

- stable geometry identities
- provenance
- polygons
- cubic Bézier curves
- millimeter units
- X axis increasing to the right
- Y axis increasing upward

The grading model supports an ordered size sequence where the `BASE` size may appear inside the sequence rather than only at one end.

Derived sizes are computed by accumulating consecutive grading increments relative to the base size.

## Grading semantics

Implemented behavior includes:

- ordered sizes
- interior base size
- consecutive increment accumulation
- per-piece size availability
- explicit zero increments
- missing-rule distinction
- stable identity propagation
- deterministic derived geometry

Curved grading follows observed rule behavior by recalculating curve handles and intermediate control geometry.

Unsupported rule variants are rejected instead of silently approximated.

## Persistence and auditability

The persistence layer supports durable modeling and grading workflows.

Implemented concepts include:

- private persistence
- drafts
- optimistic concurrency
- immutable revisions
- immutable grading results
- idempotent requests
- auditable approval
- archive and reactivation
- durable lifecycle state
- grading-to-nesting handoffs

The database schema is managed through Entity Framework Core migrations.

## Durable worker

Grading execution is handled by a dedicated background worker.

Operational behavior includes:

- durable claims
- lease ownership
- fencing
- lease renewal
- recovery
- durable cancellation
- completion race protection
- idempotent publication
- cooperative shutdown
- admission control
- health reporting

This allows grading operations to survive process restarts and protects current work from stale workers.

## Cubic geometry kernel

The grading implementation uses **Rhino3dm** behind an internal abstraction named `ICubicKernel`.

The kernel is used for selected cubic-curve operations such as:

- construction
- evaluation
- derivatives
- splitting
- bounds

Rhino-specific types are intentionally kept out of the core domain, persistence contracts, and public grading model.

## Grading to Automatic Nesting

The module integrates directly with the SERONA One Automatic Nesting pipeline.

The handoff does **not** require a PLT round trip.

For nesting adaptation, cubic Bézier geometry is deterministically converted to polygonal geometry using adaptive subdivision.

The final quantized chain is independently checked against a rational/Bernstein-based geometric bound.

The validated adapter tolerance is:

```text
0.025 mm
```

This tolerance applies specifically to the **Grading → Nesting adapter** and is not a general accuracy claim for all grading operations.

## Validation evidence

Recorded test milestones include:

```text
Phase 2: 47 / 47
Phase 3: 74 / 74
Phase 4: 105 / 105
Phase 5: 162 / 162
Phase 6: 399 distinct tests
Phase 7: 223 distinct tests
Phase 8: 215 / 215
```

A limited comparison corpus against observed external grading behavior recorded a maximum deviation of approximately:

```text
2.9653e-9 mm
```

That result applies only to the validated comparison cases and must not be interpreted as complete contour equivalence across all possible grading inputs.

## Performance-oriented workloads

The accepted performance phase included multiple workloads covering different scales of grading activity.

The largest validated workload included:

- 32 pieces
- 9 sizes
- 288 derived geometries
- 1,024 grading rules

A separate preparation workload included:

- 100 definitions
- 1,000 instances
- 2,900 vertices

## Observability

The module uses standard .NET observability mechanisms including:

- `ILogger`
- `Meter`
- health checks
- low-cardinality metrics

Private geometry and sensitive customer data are not intended to be emitted into operational logs.

## Technologies

- C#
- .NET 8
- Entity Framework Core
- MySQL 8
- Pomelo.EntityFrameworkCore.MySql
- MySqlConnector
- Rhino3dm
- ASP.NET Core integration
- .NET Worker Services
- xUnit

## Project structure

```text
Core/
Persistence/
Worker/
Tests/
```

### Core

Authoritative grading geometry, rule semantics, deterministic transformations, and grading-domain behavior.

### Persistence

Entity Framework Core persistence, migrations, durable state, immutable revisions, lifecycle data, and grading-to-nesting handoff persistence.

### Worker

Dedicated grading worker and operational lifecycle behavior.

### Tests

Automated tests covering geometry, grading semantics, persistence, migrations, worker behavior, durability, and integration-oriented flows.

## Relationship with SERONA One

This module was developed as part of the **SERONA One** software ecosystem.

The public repository is intended to demonstrate engineering work related to:

- C#/.NET
- software architecture
- computational geometry
- Bézier geometry
- domain modeling
- durable background processing
- concurrency
- database persistence
- immutable revisions
- auditability
- integration boundaries
- automated testing

The complete SERONA One application and customer-specific integrations are not included.

## Repository scope

Some project references point to other SERONA One assemblies that are intentionally not part of this public portfolio snapshot.

This repository therefore serves as a **technical portfolio and code architecture reference**, rather than a standalone distribution.

## Current status

Pattern Grading is an **advanced in-progress module** inside SERONA One.

Core grading, persistence, durable worker, API behavior, cubic-kernel integration, grading-to-nesting adaptation, UI workflow logic, observability, and performance-oriented validation have already passed accepted implementation phases.

Remaining work includes representative runtime validation and final end-to-end acceptance for the complete Grading → Nesting → certified output workflow.

The repository should therefore be read as a substantial implemented engineering module, not as a claim of fully completed production validation on every target runtime.

## Data and privacy

Customer-specific fixtures, identifiers, credentials, and private production artifacts were removed from this public snapshot.

Tests retained in this repository use synthetic or local-only configuration values.

## Third-party components

See [THIRD_PARTY.md](THIRD_PARTY.md).
