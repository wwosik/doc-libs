using DocumentFormat.OpenXml.Packaging;
using wwosik.DocLibs.DocumentConversions;
using Format = wwosik.DocLibs.DocumentConversions.DocumentFormat;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>The layout roles: further kinds of slide a template can have a layout for.</summary>
internal enum LayoutRole
{
    TwoContent,
    TextAndPicture,
    Picture,
    Agenda,
    Quote,
    Statement,
    Contact,
    Chart,
    TwoCharts,
    Dark,
}

/// <summary>
/// The roles of a template, found once per deck: the layout of each layout role, and per layout what its placeholders
/// are for. A declared name wins; else the OOXML type decides where it can; roles without a type are found only when
/// declared. Declared names the template lacks throw <see cref="DocumentConversionException"/> listing what it has.
/// </summary>
internal sealed class TemplateRoles
{
    private readonly Dictionary<LayoutRole, SlideLayoutPart> layouts = [];
    private readonly Dictionary<SlideLayoutPart, LayoutPlaceholders> placeholders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SlideLayoutPart, List<PptxPlaceholderRole>> declared = new(ReferenceEqualityComparer.Instance);

    private TemplateRoles(PptxTemplateRoles roles) => this.Declared = roles;

    /// <summary>What the template declares: text levels, footer pattern and content area among them.</summary>
    public PptxTemplateRoles Declared { get; }

    /// <summary>Resolves <paramref name="roles"/> (none: <see langword="null"/>) against the template's <paramref name="layouts"/>.</summary>
    public static TemplateRoles Resolve(IReadOnlyList<SlideLayoutPart> layouts, PptxTemplateRoles? roles)
    {
        roles ??= new PptxTemplateRoles();
        var result = new TemplateRoles(roles);
        var names = roles.Layouts ?? new PptxLayoutRoles();
        foreach (var role in Enum.GetValues<LayoutRole>())
        {
            var name = role switch
            {
                LayoutRole.TwoContent => names.TwoContent,
                LayoutRole.TextAndPicture => names.TextAndPicture,
                LayoutRole.Picture => names.Picture,
                LayoutRole.Agenda => names.Agenda,
                LayoutRole.Quote => names.Quote,
                LayoutRole.Statement => names.Statement,
                LayoutRole.Contact => names.Contact,
                LayoutRole.Chart => names.Chart,
                LayoutRole.TwoCharts => names.TwoCharts,
                _ => names.Dark,
            };
            var layout = name is not null ? Named(layouts, name, $"the {role} layout")
                : role switch
                {
                    LayoutRole.TwoContent => ByType(layouts, P.SlideLayoutValues.TwoObjects) ?? ByType(layouts, P.SlideLayoutValues.TwoColumnText),
                    LayoutRole.TextAndPicture => ByType(layouts, P.SlideLayoutValues.PictureText),
                    LayoutRole.Chart => ByType(layouts, P.SlideLayoutValues.Chart),
                    LayoutRole.Dark => layouts.FirstOrDefault(l => string.Equals(Name(l), TemplateBuilder.DarkLayoutName, StringComparison.Ordinal)),
                    _ => null,
                };
            if (layout is not null)
            {
                result.layouts[role] = layout;
            }
        }

        // Every declared placeholder is checked now, so that a mistake shows whatever the deck uses.
        foreach (var group in (roles.Placeholders ?? []).GroupBy(p => p.Layout, StringComparer.Ordinal))
        {
            var layout = Named(layouts, group.Key, "a layout with placeholder roles");
            var list = result.declared.TryGetValue(layout, out var existing) ? existing : result.declared[layout] = [];
            list.AddRange(group);
            result.Placeholders(layout);
        }

        return result;
    }

    /// <summary>The layout of <paramref name="role"/>, or <see langword="null"/> when the template has none.</summary>
    public SlideLayoutPart? Layout(LayoutRole role) => this.layouts.GetValueOrDefault(role);

    /// <summary>What the placeholders of <paramref name="layout"/> are for.</summary>
    public LayoutPlaceholders Placeholders(SlideLayoutPart layout)
    {
        if (this.placeholders.TryGetValue(layout, out var known))
        {
            return known;
        }

        var shapes = (layout.SlideLayout?.CommonSlideData?.ShapeTree?.Descendants<P.PlaceholderShape>() ?? []).ToList();
        var roles = new Dictionary<P.PlaceholderShape, PlaceholderRole>(ReferenceEqualityComparer.Instance);
        var declaredBodies = new List<P.PlaceholderShape>();
        foreach (var role in this.declared.GetValueOrDefault(layout) ?? [])
        {
            var shape = shapes.FirstOrDefault(s => string.Equals(ShapeName(s), role.Placeholder, StringComparison.OrdinalIgnoreCase))
                ?? throw new DocumentConversionException(
                    Format.Pptx,
                    $"The layout '{Name(layout)}' has no placeholder named '{role.Placeholder}' (given the role {role.Role}). "
                    + $"Its placeholders: {string.Join(", ", shapes.Select(s => $"'{ShapeName(s)}'"))}.");
            roles[shape] = role.Role;
            if (role.Role == PlaceholderRole.Body)
            {
                declaredBodies.Add(shape);
            }
        }

        P.PlaceholderShape? Declared(PlaceholderRole role) => roles.FirstOrDefault(r => r.Value == role).Key;
        IEnumerable<P.PlaceholderShape> Undeclared(Func<P.PlaceholderShape, bool> match) => shapes.Where(s => !roles.ContainsKey(s) && match(s));
        var master = layout.SlideMasterPart?.SlideMaster?.CommonSlideData?.ShapeTree;
        List<P.PlaceholderShape> InReadingOrder(IEnumerable<P.PlaceholderShape> found) =>
            [.. found.Select(s => (Shape: s, Box: Box(s, master))).OrderBy(s => s.Box, ReadingOrder.Instance).Select(s => s.Shape)];

        var result = new LayoutPlaceholders(
            [.. declaredBodies, .. InReadingOrder(Undeclared(IsBody))],
            Declared(PlaceholderRole.Subtitle) ?? Undeclared(s => s.Type?.Value == P.PlaceholderValues.SubTitle).FirstOrDefault(),
            Declared(PlaceholderRole.Byline),
            Declared(PlaceholderRole.ChapterNumber),
            [.. roles.Where(r => r.Value == PlaceholderRole.Picture).Select(r => r.Key), .. InReadingOrder(Undeclared(s => s.Type?.Value == P.PlaceholderValues.Picture))],
            InReadingOrder(Undeclared(s => s.Type?.Value == P.PlaceholderValues.Chart)),
            Declared(PlaceholderRole.ChartTitle),
            Declared(PlaceholderRole.ChartUnit),
            Declared(PlaceholderRole.ChartSource),
            new HashSet<P.PlaceholderShape>(roles.Where(r => r.Value != PlaceholderRole.Body).Select(r => r.Key), ReferenceEqualityComparer.Instance));
        this.placeholders[layout] = result;
        return result;
    }

    /// <summary>
    /// Whether <paramref name="placeholder"/> of <paramref name="layout"/> may take body text: never when it is
    /// declared decorative or with another role than body.
    /// </summary>
    public bool IsExcluded(SlideLayoutPart layout, P.PlaceholderShape placeholder) => this.Placeholders(layout).Excluded.Contains(placeholder);

    /// <summary>A body placeholder by type: <c>body</c>, <c>obj</c> or untyped.</summary>
    public static bool IsBody(P.PlaceholderShape placeholder) =>
        placeholder.Type is null || placeholder.Type.Value == P.PlaceholderValues.Body || placeholder.Type.Value == P.PlaceholderValues.Object;

    /// <summary>The name of the shape a placeholder belongs to, as the Selection Pane shows it.</summary>
    public static string? ShapeName(P.PlaceholderShape placeholder) =>
        placeholder.Parent?.Parent?.GetFirstChild<P.NonVisualDrawingProperties>()?.Name?.Value;

    private static string? Name(SlideLayoutPart layout) => layout.SlideLayout?.CommonSlideData?.Name?.Value;

    private static SlideLayoutPart? ByType(IReadOnlyList<SlideLayoutPart> layouts, P.SlideLayoutValues type) =>
        layouts.FirstOrDefault(l => l.SlideLayout?.Type?.Value == type);

    private static SlideLayoutPart Named(IReadOnlyList<SlideLayoutPart> layouts, string name, string kind) =>
        layouts.FirstOrDefault(l => string.Equals(Name(l), name, StringComparison.Ordinal))
        ?? throw new DocumentConversionException(
            Format.Pptx,
            $"The template has no layout named '{name}' (asked for as {kind}). Its layouts: {string.Join(", ", layouts.Select(l => $"'{Name(l)}'"))}.");

    // The placeholder's box: its own, else that of the master's placeholder it inherits from; empty when neither has one.
    private static (long X, long Y, long W, long H) Box(P.PlaceholderShape placeholder, P.ShapeTree? master)
    {
        static (long, long, long, long)? Of(P.PlaceholderShape? p) =>
            p?.Parent?.Parent?.Parent?.GetFirstChild<P.ShapeProperties>()?.Transform2D is { Offset: { X: { } x, Y: { } y }, Extents: { Cx: { } cx, Cy: { } cy } }
                ? (x.Value, y.Value, cx.Value, cy.Value)
                : null;

        return Of(placeholder)
            ?? Of(master?.Descendants<P.PlaceholderShape>().FirstOrDefault(m =>
                placeholder.Index is { } index ? m.Index?.Value == index.Value : m.Type?.Value == (placeholder.Type?.Value ?? P.PlaceholderValues.Body)))
            ?? (0, 0, 0, 0);
    }

    // Left to right when the tops are within half the height of the smaller one; otherwise top to bottom.
    private sealed class ReadingOrder : IComparer<(long X, long Y, long W, long H)>
    {
        public static readonly ReadingOrder Instance = new();

        public int Compare((long X, long Y, long W, long H) a, (long X, long Y, long W, long H) b) =>
            Math.Abs(a.Y - b.Y) <= Math.Min(a.H, b.H) / 2 ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y);
    }
}

/// <summary>
/// What the placeholders of one layout are for. <see cref="Bodies"/> are in order: declared ones first, then the others
/// in reading order. <see cref="Excluded"/> are those never filled with body text: decorative ones and those declared with
/// another role.
/// </summary>
internal sealed record LayoutPlaceholders(
    IReadOnlyList<P.PlaceholderShape> Bodies,
    P.PlaceholderShape? Subtitle,
    P.PlaceholderShape? Byline,
    P.PlaceholderShape? ChapterNumber,
    IReadOnlyList<P.PlaceholderShape> Pictures,
    IReadOnlyList<P.PlaceholderShape> Charts,
    P.PlaceholderShape? ChartTitle,
    P.PlaceholderShape? ChartUnit,
    P.PlaceholderShape? ChartSource,
    IReadOnlySet<P.PlaceholderShape> Excluded);
