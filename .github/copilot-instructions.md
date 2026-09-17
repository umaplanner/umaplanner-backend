# Copilot instructions for UmaPlanner backend

## Project overview

This repository is a .NET 10 solution (`UmaPlanner.slnx`) with three projects:

- `UmaPlanner.Api` is the ASP.NET Core minimal API host. Startup and dependency injection are in `Program.cs`; HTTP routes are grouped into endpoint-mapping extension classes under `Endpoints/`.
- `UmaPlanner.Core` contains the application-facing race-event entities. Keep this project independent of ASP.NET Core, database providers, and Google APIs.
- `UmaPlanner.Infrastructure` implements external-data access. `UmaRaceSheetPollingService` is a hosted background service that fetches and normalizes race events from Google Sheets; `RaceEventCache` provides the in-memory handoff to the API.

The API currently exposes:

- `GET /races` for the latest race-event snapshot loaded from Google Sheets.

The service starts the Google Sheets hosted service during application startup. Its constructor validates `Google:SpreadsheetId`, `Google:SheetName`, and `Google:ApiKey`, so those settings must be available even when working on unrelated endpoints.

## Build, run, test, and lint

Run commands from the repository root:

```bash
dotnet restore UmaPlanner.slnx
dotnet build UmaPlanner.slnx
dotnet run --project src/UmaPlanner.Api/UmaPlanner.Api.csproj
```

The launch profiles use `http://localhost:5063` and `https://localhost:7152`. The API needs valid Google Sheets settings.

There are currently no test projects or configured lint/format commands in this repository. If a test project is added, run the full suite with:

```bash
dotnet test UmaPlanner.slnx
```

Run one test (or a filtered group) with:

```bash
dotnet test path/to/TestProject.csproj --filter 'FullyQualifiedName~Namespace.Class.TestName'
```

## Architecture and implementation conventions

- Preserve the dependency direction: `Api -> Infrastructure -> Core`; Core must not reference either outer layer. Register infrastructure implementations in `Api/Program.cs` rather than constructing them in endpoint handlers.
- Add API routes as mapping extensions in `src/UmaPlanner.Api/Endpoints/` and call the extension from `Program.cs`. Keep handlers thin: inject a Core interface or an Infrastructure cache and return its result.
- Preserve nullable fields in the race-event model because source data may be incomplete.
- The race polling service fetches the sheet immediately, then waits for `Polling:IntervalHours` (defaulting to 12 when non-positive). It skips malformed/incomplete rows, logs warnings for missing sheet data or required columns, and replaces the cache with a complete snapshot rather than incrementally mutating it.
- Preserve the sheet normalization rules: event titles are numbered as `CM 1`, `CM 2`, etc. or `LoH 1`, `LoH 2`, while Monthly Match remains unnumbered; missing/non-Dirt distance types default to Turf, and Dirt uses the final token as its normalized distance type.
- `RaceEventCache` protects both replacement and reads with `SemaphoreSlim` and returns a copy from `GetAllAsync`; retain that snapshot/thread-safety behavior when changing cache access.
- Configuration is split between `Google` and `Polling`. Do not hard-code credentials or add secrets to tracked configuration; use environment variables or local development overrides for sensitive values.
- The project enables nullable reference types and implicit usings in every project. Target `net10.0` and follow the existing file-scoped namespace and primary minimal-API style.
