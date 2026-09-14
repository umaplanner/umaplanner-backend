using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public class UmaRaceSheetPollingService : BackgroundService
{
    private readonly ILogger<UmaRaceSheetPollingService> _logger;
    private readonly UmaSheetOptions _options;
    private readonly SheetsService _sheetsService;
    private readonly RaceEventCache _cache;

    // Tune these after inspecting actual colors in logs
    private static readonly Color PredictedColor = new Color { Red = 1f, Green = 1f, Blue = 0.6f, Alpha = 1f };
    private static readonly Color ConfirmedColor = new Color { Green = 1f, Blue = 1f, Alpha = 1f };
    private const double ColorTolerance = 0.15;

    public UmaRaceSheetPollingService(
        ILogger<UmaRaceSheetPollingService> logger,
        IConfiguration configuration,
        RaceEventCache cache)
    {
        _logger = logger;
        _cache = cache;

        var googleSection = configuration.GetSection("Google");
        _options = googleSection.Get<UmaSheetOptions>() ?? new UmaSheetOptions();

        _options.IntervalHours = configuration.GetValue<int>("Polling:IntervalHours");
        if (_options.IntervalHours <= 0)
            _options.IntervalHours = 12;

        if (string.IsNullOrWhiteSpace(_options.SpreadsheetId))
            throw new InvalidOperationException("Google:SpreadsheetId is missing.");
        if (string.IsNullOrWhiteSpace(_options.SheetName))
            throw new InvalidOperationException("Google:SheetName is missing.");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Google:ApiKey is missing.");

        _sheetsService = new SheetsService(new Google.Apis.Services.BaseClientService.Initializer
        {
            ApiKey = _options.ApiKey,
            ApplicationName = "UmaPlanner"
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Uma race sheet polling service starting (spreadsheet={Id}, sheet={Sheet}, interval={Hours}h)",
            _options.SpreadsheetId,
            _options.SheetName,
            _options.IntervalHours
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchAndProcessSheetAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching public Google Sheet");
            }

            await Task.Delay(TimeSpan.FromHours(_options.IntervalHours), stoppingToken);
        }

        _logger.LogInformation("Uma race sheet polling service stopping");
    }

    private async Task FetchAndProcessSheetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching sheet {Sheet} from spreadsheet {Id}", _options.SheetName, _options.SpreadsheetId);

        var request = _sheetsService.Spreadsheets.Get(_options.SpreadsheetId);
        request.Ranges = new[] { _options.SheetName };
        request.IncludeGridData = true;

        var spreadsheet = await request.ExecuteAsync(cancellationToken);
        var sheet = spreadsheet.Sheets.FirstOrDefault(s =>
            s.Properties != null && s.Properties.Title == _options.SheetName);

        if (sheet == null || sheet.Data == null || sheet.Data.Count == 0)
        {
            _logger.LogWarning("No data found for sheet {Sheet}", _options.SheetName);
            return;
        }

        var gridData = sheet.Data[0];
        var rows = gridData.RowData;

        var headerRow = rows.FirstOrDefault();
        if (headerRow == null || headerRow.Values == null)
        {
            _logger.LogWarning("No header row found in sheet {Sheet}", _options.SheetName);
            return;
        }

        var headers = headerRow.Values
            .Select((v, i) => new
            {
                Index = i,
                Text = (v.EffectiveValue?.StringValue ?? "").Trim()
            })
            .ToList();

        int FindCol(string name) =>
            headers.FindIndex(h => h.Text.Equals(name, StringComparison.OrdinalIgnoreCase));

        int championMeetCol = FindCol("Champion Meet");
        int distanceTypeCol = FindCol("Distance Type");
        int racecourseCol = FindCol("Racecourse");
        int distanceCol = FindCol("Distance");
        int conditionCol = FindCol("Condition");
        int weatherCol = FindCol("Weather");
        int handedCol = FindCol("Handed");
        int seasonCol = FindCol("Season");
        int globalDateCol = FindCol("Global Server Release Date");

        _logger.LogDebug(
            "Headers in sheet: {Headers}",
            string.Join(" | ", headers.Select(h => $"[{h.Index}] '{h.Text}'"))
        );
        _logger.LogDebug(
            "Columns found - Champion Meet: {Champion}, Global Date: {GlobalDate}",
            championMeetCol,
            globalDateCol
        );

        if (globalDateCol < 0)
        {
            _logger.LogWarning("Column 'Global Server Release Date' not found in sheet {Sheet}", _options.SheetName);
            return;
        }

        // Counters for event titles: CM 1, CM 2, LoH 1, LoH 2, ...
        var eventCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var events = new List<UmaRaceEvent>();

        // Skip header row
        for (int r = 1; r < rows.Count; r++)
        {
            var rowData = rows[r];
            if (rowData.Values == null)
                continue;

            var values = rowData.Values;

            string? GetString(int col)
            {
                if (col < 0 || col >= values.Count) return null;
                var cell = values[col];
                return cell.EffectiveValue?.StringValue ??
                       cell.EffectiveValue?.NumberValue?.ToString();
            }

            var championMeetRaw = GetString(championMeetCol);
            if (string.IsNullOrWhiteSpace(championMeetRaw))
                continue;

            var globalDateCell = globalDateCol < values.Count ? values[globalDateCol] : null;

            var eventTitle = ComputeEventTitle(championMeetRaw, eventCounters);

            var releaseDateRaw = GetString(globalDateCol);
            var releaseDate = ParseDate(releaseDateRaw);

            var ev = new UmaRaceEvent
            {
                EventTitle = eventTitle,
                Name = championMeetRaw,
                DistanceType = GetString(distanceTypeCol),
                Racecourse = GetString(racecourseCol),
                Distance = GetString(distanceCol),
                Condition = GetString(conditionCol),
                Weather = GetString(weatherCol),
                Handed = GetString(handedCol),
                Season = GetString(seasonCol),
                ReleaseDate = releaseDate,
                IsConfirmed = false
            };

            var dateColor = globalDateCell?.EffectiveFormat?.BackgroundColor;
            ev.IsConfirmed = GetIsConfirmedFromColor(dateColor ?? new Color());

            // Optional debug logging to help tune color thresholds
            if (dateColor != null &&
                (dateColor.Red == null || dateColor.Green == null || dateColor.Blue == null))
            {
                _logger.LogDebug("Row {Row}: date cell has no color info", r + 1);
            }
            else if (dateColor != null)
            {
                var type = ev.IsConfirmed ? "Confirmed" : "Predicted/Unknown";
                _logger.LogDebug(
                    "Row {Row}: color R={R}, G={G}, B={B} → {Type}",
                    r + 1,
                    dateColor.Red,
                    dateColor.Green,
                    dateColor.Blue,
                    type
                );
            }

            events.Add(ev);
        }

        // TODO: persist `events` to DB / update in-memory cache / pass to IUmaService
        _logger.LogInformation("Fetched {Count} race events from public sheet", events.Count);

        // Persist to cache
        await _cache.UpdateAsync(events);

        _logger.LogInformation("Fetched {Count} race events from public sheet", events.Count);

    }

    private static string ComputeEventTitle(
        string championMeet,
        Dictionary<string, int> counters)
    {
        if (string.IsNullOrWhiteSpace(championMeet))
            return "Unknown";

        string normalized = championMeet.Trim();

        if (normalized.Contains("Monthly Match", StringComparison.OrdinalIgnoreCase))
            return "Monthly Match";

        if (normalized.Contains("League of Heroes", StringComparison.OrdinalIgnoreCase))
        {
            const string abbreviation = "LoH";
            if (!counters.TryGetValue(abbreviation, out var count))
                count = 0;

            count++;
            counters[abbreviation] = count;
            return $"{abbreviation} {count}";
        }

        const string defaultAbbreviation = "CM";
        if (!counters.TryGetValue(defaultAbbreviation, out var defaultCount))
            defaultCount = 0;

        defaultCount++;
        counters[defaultAbbreviation] = defaultCount;
        return $"{defaultAbbreviation} {defaultCount}";
    }

    private bool GetIsConfirmedFromColor(Color color)
    {
        if (!IsColorMatch(color, ConfirmedColor))
            return false;

        return true;
    }

    private bool IsColorMatch(Color actual, Color target)
    {
        if (target.Red != null)
        {
            if (actual.Red == null) return false;
            if (Math.Abs(actual.Red.Value - target.Red.Value) >= ColorTolerance) return false;
        }

        if (target.Green != null)
        {
            if (actual.Green == null) return false;
            if (Math.Abs(actual.Green.Value - target.Green.Value) >= ColorTolerance) return false;
        }

        if (target.Blue != null)
        {
            if (actual.Blue == null) return false;
            if (Math.Abs(actual.Blue.Value - target.Blue.Value) >= ColorTolerance) return false;
        }

        if (target.Alpha != null)
        {
            if (actual.Alpha == null) return false;
            if (Math.Abs(actual.Alpha.Value - target.Alpha.Value) >= ColorTolerance) return false;
        }

        return true;
    }

  private static DateTime? ParseDate(string? value)
  {
      if (string.IsNullOrWhiteSpace(value))
          return null;

      // Try normal parsing first
      if (DateTime.TryParse(value, out var dt))
          return dt;

      // Normalize comma to dot for numeric serials
      var normalized = value.Replace(',', '.');

      if (double.TryParse(
              normalized,
              System.Globalization.NumberStyles.Number,
              System.Globalization.CultureInfo.InvariantCulture,
              out var serial))
      {
          if (serial < 1 || serial > 100000)
              return null;

          var epoch = new DateTime(1899, 12, 30);
          return epoch.AddDays(serial);
      }

      return null;
  }
}
