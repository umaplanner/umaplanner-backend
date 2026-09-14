using Microsoft.Data.Sqlite;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Mdb;

public static class MdbReader
{
    private const int TextCategoryCardFullName = 4;
    private const int TextCategoryCardOutfitTitle = 5;
    private const int TextCategoryCharacterName = 6;

    public static async Task<(IReadOnlyList<BaseUma> BaseUmas, IReadOnlyList<VariantUma> Variants)> ReadAllUmasAsync(string mdbPath)
    {
        using var conn = OpenReadOnly(mdbPath);

        var availableTables = ListTables(conn).ToHashSet();
        var tablesToExport = new[] { "card_data", "chara_data", "text_data" };
        var targetTables = GetTables(tablesToExport, availableTables);

        // We only need chara_data rows and text_data lookup; card_data is not used for base/variant lists
        // but we keep the same table check as the Python script.

        var charaRows = await ReadTableRowsAsync(conn, targetTables["chara_data"]);
        var textLookup = await LoadTextDataAsync(conn, targetTables["text_data"]);

        var baseUmas = BuildBaseCharacters(
            charaPayload: charaRows,
            names: textLookup[TextCategoryCharacterName]
        );

        var knownCharaIds = baseUmas.Select(b => b.Id).ToHashSet();

        var variants = BuildTraineeVariants(
            fullNames: textLookup[TextCategoryCardFullName],
            outfitTitles: textLookup[TextCategoryCardOutfitTitle],
            baseNames: textLookup[TextCategoryCharacterName],
            knownCharaIds: knownCharaIds
        );

        return (baseUmas, variants);
    }

    private static SqliteConnection OpenReadOnly(string databasePath)
    {
        var uriPath = Uri.EscapeDataString(Path.GetFullPath(databasePath));
        var connString = $"Data Source=file:{uriPath};Mode=ReadOnly;";
        var conn = new SqliteConnection(connString);
        conn.Open();
        return conn;
    }

    private static IReadOnlyList<string> ListTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name
        """;
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static Dictionary<string, string> GetTables(IEnumerable<string> targetTables, HashSet<string> availableTables)
    {
        var mapping = new Dictionary<string, string>();
        foreach (var table in targetTables)
        {
            if (availableTables.Contains(table))
            {
                mapping[table] = table;
            }
            else
            {
                throw new InvalidOperationException($"Could not find {table} in master.mdb.");
            }
        }
        return mapping;
    }

    private static async Task<IReadOnlyList<IDictionary<string, object?>>> ReadTableRowsAsync(
        SqliteConnection conn,
        string table)
    {
        var columns = GetTableColumns(conn, table);
        var rows = new List<IDictionary<string, object?>>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\"";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var ordinal = reader.GetOrdinal(col);
                    var value = reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
                    row[col] = value;
                }
                rows.Add(row);
            }
        }

        return rows;
    }

    private static IReadOnlyList<string> GetTableColumns(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
            columns.Add(reader.GetString(1)); // name
        return columns;
    }

    private static async Task<Dictionary<int, Dictionary<int, string>>> LoadTextDataAsync(
        SqliteConnection conn,
        string textTable)
    {
        var columns = GetTableColumns(conn, textTable);

        var categoryColumn = FindColumn(columns, "category");
        var indexColumn = FindColumn(columns, "index");
        var textColumn = FindColumn(columns, "text");

        if (categoryColumn is null || indexColumn is null || textColumn is null)
        {
            throw new InvalidOperationException(
                $"{textTable} does not have recognizable category/index/text columns. " +
                $"Found columns: {string.Join(", ", columns)}");
        }

        var result = new Dictionary<int, Dictionary<int, string>>
        {
            [TextCategoryCardFullName] = new(),
            [TextCategoryCardOutfitTitle] = new(),
            [TextCategoryCharacterName] = new()
        };

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                    "{categoryColumn.Replace("\"", "\"\"")}" AS category,
                    "{indexColumn.Replace("\"", "\"\"")}" AS text_index,
                    "{textColumn.Replace("\"", "\"\"")}" AS text
                FROM "{textTable.Replace("\"", "\"\"")}"
                WHERE "{categoryColumn.Replace("\"", "\"\"")}" IN ($cat1, $cat2, $cat3)
            """;

            cmd.Parameters.AddWithValue("$cat1", TextCategoryCardFullName);
            cmd.Parameters.AddWithValue("$cat2", TextCategoryCardOutfitTitle);
            cmd.Parameters.AddWithValue("$cat3", TextCategoryCharacterName);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var category = reader.GetInt32(0);
                var index = reader.GetInt32(1);
                if (reader.IsDBNull(2)) continue;
                var text = reader.GetString(2);

                result[category][index] = text;
            }
        }

        return result;
    }

    private static string? FindColumn(IReadOnlyList<string> columns, string name)
    {
        return columns.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<BaseUma> BuildBaseCharacters(
        IReadOnlyList<IDictionary<string, object?>> charaPayload,
        Dictionary<int, string> names)
    {
        var characters = new List<BaseUma>();

        foreach (var row in charaPayload)
        {
            if (!row.TryGetValue("id", out var idObj) || idObj is null)
                continue;

            var charaId = Convert.ToInt32(idObj);
            characters.Add(new BaseUma
            {
                Id = charaId,
                Name = names.TryGetValue(charaId, out var name) ? name : null,
                Raw = row
            });
        }

        return characters.OrderBy(c => c.Id).ToList();
    }

    private static List<VariantUma> BuildTraineeVariants(
        Dictionary<int, string> fullNames,
        Dictionary<int, string> outfitTitles,
        Dictionary<int, string> baseNames,
        HashSet<int> knownCharaIds)
    {
        var variants = new List<VariantUma>();

        foreach (var (cardId, fullName) in fullNames)
        {
            var charaId = Math.DivRem(cardId, 100, out var variantNumber);
            if (!baseNames.ContainsKey(charaId))
                continue;

            variants.Add(new VariantUma
            {
                Id = cardId,
                CharaId = charaId,
                OutfitTitle = outfitTitles.TryGetValue(cardId, out var ot) ? ot : null,
                BaseCharacterName = baseNames.TryGetValue(charaId, out var bn) ? bn : null,
            });
        }

        return variants.OrderBy(v => v.Id).ToList();
    }
}
