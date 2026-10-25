using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Charts;

/// <summary>
/// Reads a <c>chart</c> code block: optional settings (<c>type</c>, <c>title</c>, <c>unit</c>, <c>stacked</c>), one
/// <c>key: value</c> per line, then a line <c>---</c>, then the data as CSV — a header row (the category column's name,
/// then one name per series) and a row per category. Without settings, the block is the CSV alone.
/// </summary>
internal static class ChartParser
{
    /// <summary>The chart <paramref name="code"/> describes.</summary>
    /// <exception cref="ArgumentException">A setting or the data is malformed; the message names the block's line.</exception>
    public static ChartSpec Parse(string code, int line)
    {
        var lines = code.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var separator = Array.FindIndex(lines, l => l.Trim() == "---");
        var settings = separator < 0 ? [] : lines[..separator];
        var data = (separator < 0 ? lines : lines[(separator + 1)..]).Where(l => l.Trim().Length > 0).ToList();

        var (kind, stacked, title, unit) = (ChartKind.Bar, false, (string?)null, (string?)null);
        foreach (var setting in settings.Select(s => StripComment(s).Trim()).Where(s => s.Length > 0))
        {
            var colon = setting.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                throw Error(line, $"'{setting}' is not a setting; write 'key: value' above the line '---'.");
            }

            var (key, value) = (setting[..colon].Trim(), setting[(colon + 1)..].Trim());
            switch (key.ToLowerInvariant())
            {
                case "type":
                    kind = value.ToLowerInvariant() switch
                    {
                        "bar" => ChartKind.Bar,
                        "line" => ChartKind.Line,
                        "area" => ChartKind.Area,
                        _ => throw Error(line, $"The type '{value}' is not bar, line or area."),
                    };
                    break;
                case "title":
                    title = value.Length > 0 ? value : null;
                    break;
                case "unit":
                    unit = value.Length > 0 ? value : null;
                    break;
                case "stacked":
                    stacked = value.ToLowerInvariant() switch
                    {
                        "true" => true,
                        "false" => false,
                        _ => throw Error(line, $"stacked is true or false, not '{value}'."),
                    };
                    break;
                default:
                    throw Error(line, $"Unknown setting '{key}'. Valid settings: type, title, unit, stacked.");
            }
        }

        if (data.Count < 2)
        {
            throw Error(line, "The data needs a header row and at least one row of values, as CSV below the line '---'.");
        }

        var header = Cells(data[0]);
        if (header.Count < 2)
        {
            throw Error(line, "The header row names the category column and at least one series: 'month, plan, actual'.");
        }

        var categories = new List<string>();
        var values = header.Skip(1).Select(_ => new List<double?>()).ToList();
        for (var row = 1; row < data.Count; row++)
        {
            var cells = Cells(data[row]);
            if (cells.Count > header.Count)
            {
                throw Error(line, $"Row {row + 1} of the data has {cells.Count} values; the header names {header.Count} columns.");
            }

            categories.Add(cells[0]);
            for (var series = 0; series < values.Count; series++)
            {
                var cell = series + 1 < cells.Count ? cells[series + 1] : string.Empty;
                values[series].Add(cell.Length == 0 ? null
                    : double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number
                    : throw Error(line, $"'{cell}' in row {row + 1} of the data is not a number; write numbers with a decimal point and no thousands separator."));
            }
        }

        return new ChartSpec(kind, categories, header.Skip(1).Select((name, i) => new ChartSeries(name, values[i])).ToList())
        {
            Stacked = stacked && kind != ChartKind.Line,
            Title = title,
            Unit = unit,
        };
    }

    // A comment after a setting: ` # …`.
    private static string StripComment(string line) =>
        line.IndexOf(" #", StringComparison.Ordinal) is var hash and >= 0 ? line[..hash] : line;

    // Comma-separated, each cell trimmed; a cell in double quotes may hold commas.
    private static List<string> Cells(string row)
    {
        var cells = new List<string>();
        var cell = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in row)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                cells.Add(cell.ToString().Trim());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString().Trim());
        return cells;
    }

    private static ArgumentException Error(int line, string reason) =>
        new(line > 0 ? $"The chart code block at line {line} cannot be drawn: {reason}" : $"The chart code block cannot be drawn: {reason}");
}
