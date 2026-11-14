using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// <c>::: steps</c>: a list of 2 to 7 steps, each <c>- **Title** – description</c>, drawn 960 px wide as a row of
/// chevrons with the titles inside and the descriptions below. <c>{.current}</c> at the end of one item marks it: the
/// steps before it are done (accent fill, white text), the current one is in the highlight colour with a bold title,
/// later ones are outlined. Without <c>{.current}</c> all are drawn alike in the accent colour.
/// </summary>
internal static partial class StepsDrawing
{
    /// <summary>The fewest and most steps.</summary>
    public const int MinSteps = 2;

    /// <inheritdoc cref="MinSteps"/>
    public const int MaxSteps = 7;

    /// <summary>The width in pixels.</summary>
    public const double Width = 960;

    /// <summary>The only attribute an item takes.</summary>
    public const string Current = ".current";

    private const double Top = 2;
    private const double ChevronHeight = 64;
    private const double Point = 22;
    private const double Gap = 6;
    private const double TitleSize = 16;
    private const double DescriptionSize = 14;
    private const double LineStep = 20;
    private const int TitleLines = 2;
    private const int DescriptionLines = 3;
    private const string White = "FFFFFF";

    private sealed record Step(List<DrawingSpan> Title, List<DrawingSpan>? Description);

    /// <summary>Draws the container's steps.</summary>
    /// <exception cref="DocumentConversionException">Not one list of 2 to 7 steps with bold titles, or more than one <c>{.current}</c>.</exception>
    public static Drawing Draw(CustomContainer container, VisualContext context)
    {
        var (steps, current) = Steps(container, context);
        var count = steps.Count;

        // Chevrons overlap by their point less the gap, so the row fills the width exactly.
        var chevron = (Width + ((count - 1) * (Point - Gap))) / count;
        var pitch = chevron - Point + Gap;
        var colors = context.Brand.Diagram;
        var highlight = context.Brand.Highlight;
        var items = new List<DrawingItem>();
        var descriptions = steps
            .Select(s => s.Description is null ? [] : DrawingTexts.Lines(s.Description, DescriptionSize, pitch - 12, DescriptionLines))
            .ToList();
        for (var i = 0; i < count; i++)
        {
            var left = i * pitch;
            var right = left + chevron;
            var (fill, stroke, ink, bold) = current is not { } at ? (colors.Accent, (string?)null, White, true)
                : i < at ? (colors.Accent, null, White, false)
                : i == at ? (highlight, highlight == colors.Accent ? colors.Text : null, White, true)
                : (colors.Surface, colors.Border, colors.Text, false);
            items.Add(new DrawingShape(Chevron(left, right, notched: i > 0), fill, stroke, stroke is null ? 0 : 1.5));

            // The middle of the chevron's body: the first has no notch to make room for.
            var middle = left + (chevron / 2) - (i == 0 ? Point / 4 : 0);
            var room = chevron - (i == 0 ? 1.5 : 2) * Point - 4;
            var title = DrawingTexts.Lines([.. steps[i].Title.Select(s => s with { Bold = bold })], TitleSize, room, TitleLines);
            var baseline = Top + (ChevronHeight / 2) + (TitleSize * 0.35) - ((title.Count - 1) * LineStep / 2);
            foreach (var line in title)
            {
                items.Add(new DrawingText(middle, baseline, line, TitleSize, ink) { Anchor = TextAnchor.Middle, MaxWidth = room });
                baseline += LineStep;
            }

            baseline = Top + ChevronHeight + 24;
            foreach (var line in descriptions[i])
            {
                items.Add(new DrawingText(middle, baseline, line, DescriptionSize, colors.Text) { Anchor = TextAnchor.Middle, MaxWidth = pitch - 12 });
                baseline += LineStep;
            }
        }

        var lines = descriptions.Max(d => d.Count);
        var height = Top + ChevronHeight + (lines == 0 ? Top : 24 + ((lines - 1) * LineStep) + 6);
        return new Drawing(Width, height, items);
    }

    // A chevron from `left` to `right`, its point on the right; all but the first notched on the left.
    private static DrawingPath Chevron(double left, double right, bool notched)
    {
        var (top, bottom, middle) = (Top, Top + ChevronHeight, Top + (ChevronHeight / 2));
        var segments = new List<PathSegment>
        {
            new MoveTo(left, top),
            new LineTo(right - Point, top),
            new LineTo(right, middle),
            new LineTo(right - Point, bottom),
            new LineTo(left, bottom),
        };
        if (notched)
        {
            segments.Add(new LineTo(left + Point, middle));
        }

        segments.Add(new ClosePath());
        return new DrawingPath(segments);
    }

    private static (List<Step> Steps, int? Current) Steps(CustomContainer container, VisualContext context)
    {
        var at = VisualContainers.At(container, context);
        var usage = "It holds one list of 2 to 7 steps, each starting with a bold title: '- **Design** – agree the target picture', and {.current} at the end of the current one.";
        if (container.Count != 1 || container[0] is not ListBlock list)
        {
            throw context.Error($"The '::: steps'{at} holds {(container.Count == 0 ? "nothing" : "something else than one list")}. {usage}");
        }

        var items = list.OfType<ListItemBlock>().ToList();
        if (items.Count is < MinSteps or > MaxSteps)
        {
            throw context.Error($"The '::: steps'{at} has {items.Count} {(items.Count == 1 ? "step" : "steps")}; it takes {MinSteps} to {MaxSteps}. {usage}");
        }

        var steps = new List<Step>();
        int? current = null;
        foreach (var item in items)
        {
            var itemAt = VisualContainers.At(item, context);
            if (item.Count != 1 || item[0] is not ParagraphBlock { Inline.FirstChild: EmphasisInline { DelimiterCount: 2 } title })
            {
                throw context.Error($"The step{itemAt} does not start with a bold title. {usage}");
            }

            var rest = DrawingTexts.Trim(DrawingTexts.Spans(title.NextSibling));
            if (rest.Count > 0 && !rest[^1].Bold && AttributePattern().Match(rest[^1].Text) is { Success: true } attribute)
            {
                if (attribute.Groups[1].Value.Trim() != Current)
                {
                    throw context.Error($"The step{itemAt} has the attribute '{{{attribute.Groups[1].Value.Trim()}}}'; a step takes only {{{Current}}}.");
                }

                if (current is not null)
                {
                    throw context.Error($"The step{itemAt} is the second one marked {{{Current}}}; mark only the current step.");
                }

                current = steps.Count;
                rest[^1] = rest[^1] with { Text = rest[^1].Text[..attribute.Index] };
                rest = DrawingTexts.Trim(rest);
            }

            if (rest.Count > 0 && !rest[0].Bold)
            {
                rest[0] = rest[0] with { Text = SeparatorPattern().Replace(rest[0].Text, string.Empty) };
                rest = DrawingTexts.Trim(rest);
            }

            var spans = DrawingTexts.Trim(DrawingTexts.Spans(title.FirstChild));
            if (spans.Count == 0)
            {
                throw context.Error($"The step{itemAt} has an empty title. {usage}");
            }

            steps.Add(new Step(spans, rest.Count > 0 ? rest : null));
        }

        return (steps, current);
    }

    // `{…}` at the very end of an item.
    [GeneratedRegex(@"\{([^{}]*)\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributePattern();

    // What separates a title from its description: a dash or colon, as in `::: cards`.
    [GeneratedRegex(@"^\s*(?:[–—:]|-(?=\s))?\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();
}
