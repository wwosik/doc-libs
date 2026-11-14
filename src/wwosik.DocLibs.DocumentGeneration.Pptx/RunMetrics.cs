namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>
/// <see cref="TextMetrics"/> on a slide's <see cref="TextRun"/>s: a run is measured bold when it is set bold (bold
/// itself, marked text, a status label, or everything in a header row), and a break run breaks the line.
/// </summary>
internal static class RunMetrics
{
    /// <summary>The width of the longest word of <paramref name="runs"/>, which no line break can split.</summary>
    public static double LongestWord(IReadOnlyList<TextRun> runs, int size, bool bold) =>
        TextMetrics.LongestWord(Pieces(runs, bold), size);

    /// <summary>How many lines <paramref name="runs"/> take at <paramref name="width"/>, wrapping between words.</summary>
    public static int Lines(IReadOnlyList<TextRun> runs, int size, bool bold, double width) =>
        TextMetrics.Lines(Pieces(runs, bold), size, width);

    /// <summary>Whether a run is set bold: bold itself, marked text, a status label, or everything in a header row.</summary>
    public static bool IsBold(TextRun run, bool header) =>
        header || run.Style.HasFlag(RunStyle.Bold) || run.Style.HasFlag(RunStyle.Mark) || run.Style.HasFlag(RunStyle.Label);

    // A break run is a line break whatever its style; any other run's text is measured as it is.
    private static IEnumerable<(string Text, bool Bold)> Pieces(IReadOnlyList<TextRun> runs, bool bold) =>
        runs.Select(run => (run.IsBreak ? TextMetrics.LineBreak : run.Text, IsBold(run, bold)));
}
