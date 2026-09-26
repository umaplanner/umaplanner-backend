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
| `Cors:AllowedOrigins` | Comma-separated exact origins allowed to make credentialed requests | None |
| `Cors:VercelProjectPrefix` | Prefix for allowed HTTPS Vercel preview hostnames | `umaplanner-` |

For local development, use environment variables or an untracked configuration override. ASP.NET Core maps double underscores in environment variables to nested configuration keys:

Production allows HTTPS Vercel preview origins whose host starts with
`Cors__VercelProjectPrefix` and ends with `.vercel.app`, so random preview URLs
do not need to be added individually. Set `Cors__AllowedOrigins` to exact
additional origins such as `https://umaplanner.app`. Development also allows
loopback origins such as `http://localhost:3000` and `http://localhost:5173`.

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

After successful authentication, the callback redirects to the configured
frontend base URL.

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

### Development PostgreSQL

Start the development database from the repository root:

```bash
docker compose -f docker-compose.dev.yml up -d --wait db
```

The database is available on `localhost:5432` with these development-only
credentials:

```text
Host=localhost;Port=5432;Database=umaplanner_dev;Username=postgres;Password=postgres
```

Check that PostgreSQL is ready:

```bash
docker compose -f docker-compose.dev.yml ps
```

Stop the database while keeping its data:

```bash
docker compose -f docker-compose.dev.yml down
```

Keep the database running while the API is running locally. If it has been
stopped, run the `up` command again before starting the API. Because the API
uses `localhost`, it must run on the host; an API container would instead use
the Compose service name `db` as its database host.

To remove the database volume and start fresh:

```bash
docker compose -f docker-compose.dev.yml down -v
```

## API

- `GET /races`: Returns the latest race-event snapshot loaded from Google Sheets.
- `GET /users`: Lists local users.
- `GET /users/{id}`: Gets one local user.
- `GET /users/me`: Gets the currently authenticated user, including `avatarUrl`.
- `PUT /users/{id}/trainer-id`: Sets or clears a user's manually verified trainer ID.
- `POST /builds`: Saves a batch of builds for the authenticated user. Each item
  has `event`, `id`, and an object-valued `data` property containing a numeric
  `lastUpdate` timestamp from `Date.now()`. Matching event/id entries are
  updated only when the incoming `lastUpdate` is newer.
- `GET /builds`: Returns all saved builds for the authenticated user. The response
  includes `event`, `id`, and `data`, but never `userId`.
- `GET /auth/logout?returnUrl=...`: Logs out the current user, clears the session, and redirects to the supplied frontend URL.

Log out from the frontend:

```javascript
window.location.href =
  `http://localhost:5063/auth/logout?returnUrl=${encodeURIComponent(window.location.href)}`;
```

After Discord OAuth redirects back to the frontend, request the logged-in user
with the session cookie:

```javascript
const response = await fetch("http://localhost:5063/users/me", {
  credentials: "include"
});
const user = await response.json();
```

Set a trainer ID after the user has been created:

```bash
curl -X PUT http://localhost:5063/users/<user-id>/trainer-id \
  -H "Content-Type: application/json" \
  -d '{"trainerId":"your-trainer-id"}'
```

To clear it again, send an empty value:

```bash
curl -X PUT http://localhost:5063/users/<user-id>/trainer-id \
  -H "Content-Type: application/json" \
  -d '{"trainerId":""}'
```

Save builds for the authenticated user:

```bash
curl -X POST http://localhost:5063/builds \
  -H "Content-Type: application/json" \
  -b cookies.txt \
  -d '[{"event":"CM 20","id":"42cb4e62-b8cf-4118-9a26-c634316a1a7d","data":{"outfitId":"100202","starCount":3,"uniqueLv":1,"speed":1200,"stamina":1200,"power":800,"guts":400,"wisdom":400,"strategy":"Senkou","distanceAptitude":"S","surfaceAptitude":"A","strategyAptitude":"A","mood":0,"skills":[],"forcedSkillPositions":{},"event":"CM 20","id":"42cb4e62-b8cf-4118-9a26-c634316a1a7d","name":"Silence Suzuka"}}]'
```

## Development

There are currently no test projects or configured lint commands. Run the solution build to validate changes:

```bash
dotnet build UmaPlanner.slnx
```
