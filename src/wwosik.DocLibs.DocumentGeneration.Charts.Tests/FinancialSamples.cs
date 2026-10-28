using System.Globalization;

namespace wwosik.DocLibs.DocumentGeneration.Charts.Tests;

/// <summary>Made-up figures for the six financial charts: a year of orders in millions, actuals through July.</summary>
internal static class FinancialSamples
{
    public static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static readonly FinancialChartOptions Options = new()
    {
        Culture = CultureInfo.GetCultureInfo("pl-PL"),
        Scale = 1_000_000,
        Unit = "M",
        Labels = new FinancialChartLabels { Previous = "Last year", Current = "This year", Reference = "Target", Projection = "Forecast", Estimate = "Estimate" },
    };

    private static readonly double[] PreviousYear = [14.2, 15.1, 17.8, 16.4, 17.0, 18.3, 16.9, 13.5, 17.7, 18.9, 19.4, 21.2];
    private static readonly double[] CurrentYear = [15.0, 15.9, 18.1, 17.9, 18.2, 19.6, 18.4];
    private static readonly double[] Forecast = [14.8, 15.6, 18.0, 17.5, 18.6, 19.1, 18.0, 14.6, 19.0, 20.1, 20.5, 22.4];
    private const double TargetRate = 0.05;

    public static DevelopmentChart Development(DevelopmentPanel panel = DevelopmentPanel.Cumulative, int lastActual = 6)
    {
        var months = new List<DevelopmentMonth>();
        double previous = 0, current = 0, forecast = 0, target = 0;
        for (var i = 0; i < 12; i++)
        {
            previous += PreviousYear[i];
            forecast += Forecast[i];
            target += PreviousYear[i] * (1 + TargetRate);
            double? actual = i <= lastActual && i < CurrentYear.Length ? CurrentYear[i] : null;
            if (actual is { } a)
            {
                current += a;
            }

            var growth = actual is not null ? (current / previous) - 1 : (forecast / previous) - 1;
            months.Add(new DevelopmentMonth(
                Months[i],
                M(PreviousYear[i]),
                actual is { } value ? M(value) : null,
                M(Forecast[i]),
                M(previous),
                actual is not null ? M(current) : null,
                M(forecast),
                M(PreviousYear[i] * (1 + TargetRate)),
                M(target),
                growth));
        }

        return new DevelopmentChart
        {
            Title = "Orders",
            Period = "July 2026",
            Months = months,
            LastActual = lastActual,
            ProjectionComparedFrom = 5,
            ReferenceRate = TargetRate,
            ChangeAtPeriod = months[lastActual].Growth ?? 0,
            ChangeAtYearEnd = months[^1].Growth,
            Estimates = [new EstimateMark(9, "Estimate", M(166.0))],
            LowerPanel = panel,
        };
    }

    public static GrowthByRowChart GrowthByRow() => new()
    {
        Title = "Orders growth by region",
        Period = "July 2026",
        Rows =
        [
            new GrowthRow("North", 0.081, 0.05),
            new GrowthRow("South and the islands", 0.032, 0.05, Emphasised: true),
            new GrowthRow("East", -0.021, 0.04),
            new GrowthRow("West", 0.124, 0.06),
            new GrowthRow("Central and metropolitan areas", 0.049, 0.05),
        ],
        Overall = new GrowthFigures(0.052, 0.05),
    };

    public static ActualVsReferenceChart ActualVsReference(ReferenceDisplay display = ReferenceDisplay.Growth) => new()
    {
        Title = "Orders and sales against target",
        Period = "July 2026",
        Display = display,
        Rows =
        [
            Row("All regions", true, 0.031, 0.052, 0.05, 0.044, 0.061, 0.06),
            Row("North", false, 0.12, 0.081, 0.05, 0.09, 0.07, 0.06),
            Row("South and the islands", false, -0.04, 0.032, 0.05, 2.4, 0.05, 0.06),
            Row("East", false, -0.08, -0.021, 0.04, -0.02, -0.01, 0.05),
        ],
    };

    public static GrowthTrendChart GrowthTrend() => new()
    {
        Title = "Orders growth",
        Period = "July 2026",
        LastActual = 6,
        Reference = TargetRate,
        Months = [.. Development().Months.Select(m => new TrendMonth(m.Label, m.Growth))],
    };

    public static TwoYearChart TwoYear(int categories = 5)
    {
        string[] names = ["Machines", "Spare parts", "Service contracts", "Software", "Training", "Rentals"];
        double[] bases = [42, 18, 25, 9, 4, 12];
        var series = Enumerable.Range(0, categories).Select(c => new TwoYearSeries(
            names[c],
            [.. Enumerable.Range(0, 24).Select(i => i >= 19 ? (double?)null : M(bases[c] * (1 + (0.12 * Math.Sin((i + c) * 0.7))) * (i >= 12 ? 1.06 : 1)))])).ToList();
        return new TwoYearChart
        {
            Title = "Sales by product line",
            Period = "July 2026",
            Months = [.. Months, .. Months],
            PreviousYear = "2025",
            CurrentYear = "2026",
            Series = series,
        };
    }

    public static SummaryTable Summary() => new()
    {
        Title = "Summary",
        Period = "July 2026",
        Columns = [new SummaryColumn("Total", Total: true), new SummaryColumn("Machines"), new SummaryColumn("Parts"), new SummaryColumn("Service"), new SummaryColumn("Other", Rest: true)],
        Blocks =
        [
            new SummaryBlock("Orders", 0.05, [Cell(118.3, 112.5, 0.05), Cell(64.2, 60.1, 0.05), Cell(30.5, 31.0, 0.04), Cell(23.4, 21.2, 0.06), Cell(0.2, 0.2, 0.05)]),
            new SummaryBlock("Sales", 0.04, [Cell(109.2, 104.8, 0.04), Cell(58.0, 55.9, 0.04), Cell(29.1, 28.4, 0.03), Cell(22.0, 20.4, 0.05), Cell(0.1, 0.1, 0.04)]),
            new SummaryBlock("Margin", 0.01, [Rate(46.6, 44.9, 109.2, 104.8), Rate(25.1, 24.7, 58.0, 55.9), Rate(12.4, 12.0, 29.1, 28.4), Rate(9.1, 8.2, 22.0, 20.4), Rate(0.0, 0.0, 0.1, 0.1)], RateOf: "sales"),
        ],
    };

    private static MeasureRow Row(string label, bool aggregate, double month, double ytd, double target, double salesMonth, double salesYtd, double salesTarget) => new(
        label,
        [
            new Measure("orders", "Orders (month)", false, M(18.4), month, target, M(18.0)),
            new Measure("orders", "Orders (YTD)", true, M(122.5), ytd, target, M(121.8)),
            new Measure("sales", "Sales (month)", false, M(16.2), salesMonth, salesTarget, M(16.0)),
            new Measure("sales", "Sales (YTD)", true, M(109.2), salesYtd, salesTarget, M(110.4)),
        ],
        aggregate);

    private static SummaryCell Cell(double current, double previous, double reference) => new(M(current), M(previous), (current / previous) - 1, reference);

    private static SummaryCell Rate(double margin, double previousMargin, double sales, double previousSales)
    {
        var (rate, previousRate) = (margin / sales, previousMargin / previousSales);
        return new SummaryCell(M(margin), M(previousMargin), previousMargin == 0 ? 0 : (margin / previousMargin) - 1, 0)
        {
            Rate = sales > 0.5 ? rate : null,
            PreviousRate = previousSales > 0.5 ? previousRate : null,
            ReferenceRate = previousSales > 0.5 ? previousRate + 0.01 : null,
        };
    }

    private static double M(double millions) => millions * 1_000_000;
}
