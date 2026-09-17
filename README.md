# UmaPlanner Backend


## Project structure

```text
src/
├── UmaPlanner.Api/            # HTTP API and application startup
├── UmaPlanner.Core/           # Race-event entities and core types
└── UmaPlanner.Infrastructure/ # Google Sheets polling and in-memory cache
```

The dependency direction is:

```text
UmaPlanner.Api -> UmaPlanner.Infrastructure -> UmaPlanner.Core
```

## Requirements

- .NET 10 SDK
- A Google Sheets API key
- Access to the configured Google Spreadsheet

Docker can be used instead of installing the .NET SDK locally.

## Configuration

The application requires the following settings:

| Setting | Description | Default |
| --- | --- | --- |
| `Google:SpreadsheetId` | Google Spreadsheet ID | Configured in `appsettings.json` |
| `Google:SheetName` | Sheet tab to read | `PvP` |
| `Google:ApiKey` | Google Sheets API key | None |
| `Polling:IntervalHours` | Polling interval for refreshing race data | `12` |

For local development, use environment variables or an untracked configuration override. ASP.NET Core maps double underscores in environment variables to nested configuration keys:

## Running locally

Restore and build the solution:

```bash
dotnet restore UmaPlanner.slnx
dotnet build UmaPlanner.slnx
```

Start the API:

```bash
dotnet run --project src/UmaPlanner.Api/UmaPlanner.Api.csproj
```

The configured launch profiles use:

- HTTP: `http://localhost:5063`
- HTTPS: `https://localhost:7152`

The Google Sheets polling service fetches data immediately at startup and refreshes it every 12 hours by default. The API will fail during startup if the required Google settings are missing.

## Docker

Build the image from the repository root:

```bash
docker build -t umaplanner-api .
```

Run the API on port 8080:

```bash
docker run --rm -p 8080:8080 \
  -e Google__SpreadsheetId="your-spreadsheet-id" \
  -e Google__SheetName="PvP" \
  -e Google__ApiKey="your-api-key" \
  -e Polling__IntervalHours="12" \
  umaplanner-api
```

## API

- `GET /races`: Returns the latest race-event snapshot loaded from Google Sheets.

## Development

There are currently no test projects or configured lint commands. Run the solution build to validate changes:

```bash
dotnet build UmaPlanner.slnx
```
