using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
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

        _sheetsService = new SheetsService(
            new Google.Apis.Services.BaseClientService.Initializer
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
            _options.IntervalHours);

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

            await Task.Delay(
                TimeSpan.FromHours(_options.IntervalHours),
                stoppingToken);
        }

        _logger.LogInformation("Uma race sheet polling service stopping");
    }

    private async Task FetchAndProcessSheetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Fetching sheet {Sheet} from spreadsheet {Id}",
            _options.SheetName,
            _options.SpreadsheetId);

        var request = _sheetsService.Spreadsheets.Get(_options.SpreadsheetId);
        request.Ranges = new[] { _options.SheetName };
        request.IncludeGridData = true;

        var spreadsheet = await request.ExecuteAsync(cancellationToken);

        var sheet = spreadsheet.Sheets.FirstOrDefault(s =>
            s.Properties != null &&
            s.Properties.Title == _options.SheetName);

        if (sheet?.Data == null || sheet.Data.Count == 0)
        {
            _logger.LogWarning(
                "No data found for sheet {Sheet}",
                _options.SheetName);

            return;
        }

        var gridData = sheet.Data[0];
        var rows = gridData.RowData;

        var headerRow = rows.FirstOrDefault();
        if (headerRow?.Values == null)
        {
            _logger.LogWarning(
                "No header row found in sheet {Sheet}",
                _options.SheetName);

            return;
        }

        var headers = headerRow.Values
            .Select((value, index) => new
            {
                Index = index,
                Text = (value.EffectiveValue?.StringValue ?? "").Trim()
            })
            .ToList();

        int FindCol(string name) =>
            headers.FindIndex(header =>
                header.Text.Equals(name, StringComparison.OrdinalIgnoreCase));

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
            string.Join(
                " | ",
                headers.Select(header =>
                    $"[{header.Index}] '{header.Text}'")));

        _logger.LogDebug(
            "Columns found - Champion Meet: {Champion}, Global Date: {GlobalDate}",
            championMeetCol,
            globalDateCol);

        if (globalDateCol < 0)
        {
            _logger.LogWarning(
                "Column 'Global Server Release Date' not found in sheet {Sheet}",
                _options.SheetName);

            return;
        }

        // Counters produce: CM 1, CM 2, LoH 1, LoH 2, etc.
        var eventCounters = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        var events = new List<UmaRaceEvent>();

        // Skip the header row.
        for (int r = 1; r < rows.Count; r++)
        {
            var rowData = rows[r];

            if (rowData.Values == null)
                continue;

            var values = rowData.Values;

            string? GetString(int col)
            {
                if (col < 0 || col >= values.Count)
                    return null;

                var cell = values[col];

                return cell.EffectiveValue?.StringValue
                    ?? cell.EffectiveValue?.NumberValue?.ToString(
                        CultureInfo.InvariantCulture);
            }

            var championMeetRaw = GetString(championMeetCol);

            if (string.IsNullOrWhiteSpace(championMeetRaw))
                continue;

            var eventTitle = ComputeEventTitle(
                championMeetRaw,
                eventCounters);

            var releaseDateRaw = GetString(globalDateCol);
            var releaseDate = ParseDate(releaseDateRaw);

            var (groundType, normalizedDistanceType) =
                ParseGroundAndDistanceType(GetString(distanceTypeCol));

            var globalDateCell = globalDateCol < values.Count
                ? values[globalDateCol]
                : null;

            var ev = new UmaRaceEvent
            {
                EventTitle = eventTitle,
                Name = championMeetRaw,
                GroundType = groundType,
                DistanceType = normalizedDistanceType,
                Racecourse = GetString(racecourseCol),
                Distance = GetString(distanceCol),
                GroundCondition = GetString(conditionCol),
                Weather = GetString(weatherCol),
                Direction = GetString(handedCol),
                Season = GetString(seasonCol),
                ReleaseDate = releaseDate,

                // Manually entered date = confirmed.
                // Formula-generated date = estimated / not confirmed.
                IsConfirmed = IsConfirmedDate(globalDateCell)
            };

            _logger.LogDebug(
                "Row {Row}: release date raw={ReleaseDateRaw}; entered string={StringValue}; entered number={NumberValue}; formula={FormulaValue}; confirmed={IsConfirmed}",
                r + 1,
                releaseDateRaw,
                globalDateCell?.UserEnteredValue?.StringValue,
                globalDateCell?.UserEnteredValue?.NumberValue,
                globalDateCell?.UserEnteredValue?.FormulaValue,
                ev.IsConfirmed);

            events.Add(ev);
        }

        await _cache.UpdateAsync(events);

        _logger.LogInformation(
            "Fetched and cached {Count} race events from public sheet",
            events.Count);
    }

    private static string ComputeEventTitle(
        string championMeet,
        Dictionary<string, int> counters)
    {
        if (string.IsNullOrWhiteSpace(championMeet))
            return "Unknown";

        var normalized = championMeet.Trim();

        if (normalized.Contains(
                "Monthly Match",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Monthly Match";
        }

        if (normalized.Contains(
                "League of Heroes",
                StringComparison.OrdinalIgnoreCase))
        {
            const string abbreviation = "LoH";

            counters.TryGetValue(abbreviation, out var count);
            count++;

            counters[abbreviation] = count;

            return $"{abbreviation} {count}";
        }

        const string defaultAbbreviation = "CM";

        counters.TryGetValue(defaultAbbreviation, out var defaultCount);
        defaultCount++;

        counters[defaultAbbreviation] = defaultCount;

        return $"{defaultAbbreviation} {defaultCount}";
    }

    private static bool IsConfirmedDate(CellData? dateCell)
    {
        var enteredValue = dateCell?.UserEnteredValue;

        if (enteredValue == null)
            return false;

        // A formula means the date is an estimate:
        // e.g. =K20+(L20+A$2)
        if (!string.IsNullOrWhiteSpace(enteredValue.FormulaValue))
            return false;

        // Manually entered date cells are usually stored as Google Sheets
        // serial date values, represented here as NumberValue.
        if (enteredValue.NumberValue.HasValue)
            return true;

        // Supports manually entered date text if the spreadsheet uses it.
        return !string.IsNullOrWhiteSpace(enteredValue.StringValue);
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDate))
        {
            return parsedDate;
        }

        if (DateTime.TryParse(
                trimmed,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out parsedDate))
        {
            return parsedDate;
        }

        // Handles Google Sheets serial date values such as 46280 or 46280.5.
        var normalized = trimmed.Replace(',', '.');

        if (double.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var serial))
        {
            if (serial < 1 || serial > 100000)
                return null;

            var googleSheetsEpoch = new DateTime(1899, 12, 30);

            return googleSheetsEpoch.AddDays(serial);
        }

        return null;
    }

    private static (string groundType, string? distanceType)
        ParseGroundAndDistanceType(string? distanceType)
    {
        if (string.IsNullOrWhiteSpace(distanceType))
            return ("Turf", null);

        var trimmed = distanceType.Trim();

        if (trimmed.StartsWith(
                "Dirt",
                StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed
                .Split(
                    new[] { ' ', '-' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();

            var lastWord = parts.Length > 0
                ? parts[^1]
                : trimmed;

            return ("Dirt", lastWord);
        }

        return ("Turf", trimmed);
    }
}
