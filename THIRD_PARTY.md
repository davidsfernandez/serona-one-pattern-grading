# Third-Party Components

This repository contains or references external libraries used by the Pattern Grading implementation.

## Rhino3dm

The grading module uses **Rhino3dm** behind the internal `ICubicKernel` abstraction for selected cubic Bézier operations.

Observed version in the SERONA One project graph:

`8.35.0`

The grading domain is designed so that Rhino-specific types do not become authoritative domain or persistence contracts.

Rhino3dm remains subject to its own license and terms.

## Entity Framework Core

Used for persistence and database migrations.

Observed version:

`8.0.2`

## Pomelo.EntityFrameworkCore.MySql

Used as the Entity Framework Core provider for MySQL.

Observed version:

`8.0.0`

## MySqlConnector

Used by the MySQL persistence stack.

Observed transitive version:

`2.3.5`

## xUnit

Used for automated testing.

Observed version in the broader solution:

`2.5.3`

## Other dependencies

Additional transitive dependencies may be restored through the original SERONA One project graph.

This repository is a portfolio snapshot and does not claim authorship of third-party components.
