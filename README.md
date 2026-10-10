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
| `Admin:AdminDiscordIds` | Comma-separated Discord IDs with access to the admin API | None |
| `EventSummary:WriteLocalJson` | Write summary JSON files in Development | `true` |
| `EventSummary:IntervalMinutes` | Interval between event summary runs | `10` |
| `R2Storage:AccountId` | Cloudflare account ID; used to derive the R2 S3 API endpoint | None |
| `R2Storage:Endpoint` | Optional Cloudflare R2 S3 API endpoint (`https://<account-id>.r2.cloudflarestorage.com`) | Derived from account ID; account ID takes precedence |
| `R2Storage:AccessKeyId` | R2 access key ID | None |
| `R2Storage:SecretAccessKey` | R2 secret access key | None |
| `R2Storage:BucketName` | R2 bucket receiving event summaries | None |
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
In the `Production` environment, the event summary service performs one R2 upload during application startup, so
invalid credentials, an incorrect endpoint, or insufficient bucket permissions
prevent the application from starting. It then runs every 10 minutes by default,
configurable with `EventSummary__IntervalMinutes`. It
aligns runs to interval boundaries (for example, at `:00`, `:10`, `:20`, etc.
for a 10-minute interval). It reads each user's teams and referenced builds,
then writes summaries to
`data/overview/<event>.json` in R2. The JSON contains `sha256` and `data`
properties, plus `nextUpdate`, an ISO-8601 UTC timestamp for the next scheduled
summary. The summary data includes `userCount`, the number of distinct users
with at least one valid referenced build included in the summary.
`runningStyles` is an array of style summaries;
each includes its build `count`, per-skill and per-support-card counts, and the
per-style outfit counts. Each also includes the average of each available stat
(`speed`, `stamina`, `power`, `guts`, and `wisdom`). The hash covers both the
summary data and `nextUpdate`, so summaries are refreshed on every interval.
Configure R2 credentials through
environment variables such as `R2Storage__AccessKeyId` or the same
`R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, and
`R2_BUCKET_NAME` names used by the repository's Python tooling rather than
committing secrets to `appsettings.json`. In Development, summaries are instead
written to `event-summary-<event>.json` files in the repository root. Set
`EventSummary__WriteLocalJson=false` to disable these files; the default is
`true` in Development. The interval setting also applies in Development and
can be overridden with `EventSummary__IntervalMinutes`.
On startup, the API applies pending database migrations, including the daily
admin statistics table.

After successful authentication, the callback redirects to the configured
frontend base URL. The persistent session cookie survives browser restarts.
Both the cookie and server-side session remain valid for up to 400 days;
activity refreshes the server-side session, and `/auth/logout` clears it.
Configure admin access with `Admin__AdminDiscordIds` in the API environment.
Only users whose Discord IDs are in this list can view administrative
statistics.

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
  `isAdmin` is `true` for admins and omitted for regular users.
- `GET /admin/stats?days=1`: Returns daily overall user, build, and team counts
  for the requested number of days (1-3650, default 1). A team is counted only
  when at least one of `uma1`, `uma2`, or `uma3` has a non-empty build selection.
- `GET /admin/stats/max-days`: Returns the inclusive date span available in
  stored overall snapshots as `maxDays`, with the earliest and latest dates.
  Before the first snapshot, `maxDays` is `0` and both dates are `null`.
- `GET /admin/stats/events?days=1`: Returns daily user, build, and team counts
  grouped by event for the requested number of days (1-3650, default 1). Add
  `event=<saved event name>` to return only that event, for example
  `/admin/stats/events?event=CM%201`. The value must match the saved event name;
  any `compareTo` comparison is filtered to that same event.
  Both endpoints read the same snapshots stored in PostgreSQL, captured at
  each UTC midnight; if none exist, the service records an initial baseline on
  startup. Both require admin access. Add `compareTo=N` (1-3650) to
  either endpoint to compare the newest snapshot with the one from N days
  earlier. `comparison` contains only `usersPercentChange`,
  `buildsPercentChange`, and `teamsPercentChange`. On the events endpoint with
  an `event` filter, those three fields are returned directly; without a filter,
  `comparison` maps each event name to those three fields. If the earlier
  snapshot is unavailable, or its count is zero while the current count is
  nonzero, the corresponding percentage is `null`.
  For example,
  `/admin/stats?compareTo=3` compares the newest snapshot with the one from
  three days earlier.
- `PUT /users/{id}/trainer-id`: Sets or clears a user's manually verified trainer ID.
- `POST /builds`: Saves a batch of builds for the authenticated user. Each item
  has `event`, `id`, and an object-valued `data` property containing a numeric
  `lastUpdate` timestamp from `Date.now()`. Matching event/id entries are
  updated only when the incoming `lastUpdate` is newer.
- `GET /builds`: Returns all saved builds for the authenticated user, including
  soft-deleted builds. The response includes `event`, `id`, `data`, and nullable
  `deletedAt`, but never `userId`.
- `DELETE /builds/delete`: Soft-deletes one build for the authenticated user.
  Send `event` and the build UUID in the JSON body. Deleted builds are hidden
  from normal active-build use by the frontend and permanently removed after
  14 days. A newer POST sync for the same build restores it.
- `POST /teams`: Saves a batch of event teams for the authenticated user. Each
  team contains `event`, `uma1`, `uma2`, `uma3`, and a numeric `lastUpdate`;
  newer timestamps replace older data.
- `GET /teams`: Returns all saved teams in the same shape, without exposing
  `userId`.
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

Delete a build for the authenticated user:

```bash
curl -X DELETE http://localhost:5063/builds/delete \
  -H "Content-Type: application/json" \
  -b cookies.txt \
  -d '{"event":"CM 20","id":"42cb4e62-b8cf-4118-9a26-c634316a1a7d"}'
```

## Development

There are currently no test projects or configured lint commands. Run the solution build to validate changes:

```bash
dotnet build UmaPlanner.slnx
```
