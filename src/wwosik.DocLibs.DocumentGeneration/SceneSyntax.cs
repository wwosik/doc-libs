using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Syntax;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>What a scene shape is: PowerPoint's rectangle, rounded rectangle or ellipse, or a text without fill and line.</summary>
internal enum SceneKind
{
    Box,
    Round,
    Ellipse,
    Text,
}

/// <summary>
/// The colour of a scene item: a palette role or a label category. <see cref="Default"/> is a light surface with a thin
/// line in the primary colour; <see cref="Outline"/> the line alone.
/// </summary>
internal enum SceneRole
{
    Default,
    Primary,
    Accent,
    Highlight,
    Muted,
    Dark,
    Outline,
    Success,
    Info,
    Warning,
    Danger,
    Neutral,
}

/// <summary>The size of a label: the flow text size (16 pt), <c>.small</c> (12 pt) or <c>.large</c> (24 pt).</summary>
internal enum SceneTextSize
{
    Normal,
    Small,
    Large,
}

/// <summary>
/// How a scene item looks and builds: its colour, its label's weight and size, the click it comes in on
/// (<see cref="Step"/>, with <see cref="Effect"/>) and the click it leaves on (<see cref="Exit"/>); <see langword="null"/>:
/// shown from the start, or until the end.
/// </summary>
internal sealed record SceneStyle(SceneRole Role, bool Bold, SceneTextSize Size, int? Step, int? Exit, BuildEffect Effect)
{
    public static SceneStyle Plain { get; } = new(SceneRole.Default, false, SceneTextSize.Normal, null, null, BuildEffect.Appear);

    /// <summary>The label size in points.</summary>
    public int Points => this.Size switch { SceneTextSize.Small => 12, SceneTextSize.Large => 24, _ => 16 };

    /// <summary>Whether the item is still shown after the last click.</summary>
    public bool VisibleAtEnd => this.Exit is null;

    /// <summary>The category of a label-category role, else <see langword="null"/>.</summary>
    public LabelCategory? Category => this.Role switch
    {
        SceneRole.Success => LabelCategory.Success,
        SceneRole.Info => LabelCategory.Info,
        SceneRole.Warning => LabelCategory.Warning,
        SceneRole.Danger => LabelCategory.Danger,
        SceneRole.Neutral => LabelCategory.Neutral,
        _ => null,
    };
}

/// <summary>A shape or connector of a scene, with its name (unique in the scene), label (<c>\n</c> breaks it) and line in the Markdown.</summary>
internal abstract record SceneElement(string Name, string? Label, SceneStyle Style, int Line);

/// <summary>A shape at (<paramref name="X"/>, <paramref name="Y"/>) of <paramref name="W"/> × <paramref name="H"/> grid units.</summary>
internal sealed record SceneNode(string Name, SceneKind Kind, double X, double Y, double W, double H, string? Label, SceneStyle Style, int Line)
    : SceneElement(Name, Label, Style, Line)
{
    public (double X, double Y) Centre => (this.X + (this.W / 2), this.Y + (this.H / 2));
}

/// <summary>An arrow (head at <paramref name="To"/>) or a line between two shapes, by their names.</summary>
internal sealed record SceneConnector(string Name, string From, string To, bool Arrow, string? Label, SceneStyle Style, int Line)
    : SceneElement(Name, Label, Style, Line);

/// <summary>The side of a shape a connector meets, in the order of PowerPoint's connection sites of a rectangle.</summary>
internal enum SceneSide
{
    Top,
    Left,
    Bottom,
    Right,
}

/// <summary>
/// A <c>::: scene</c>: shapes in the order of their lines (later ones cover earlier ones), then the connectors between
/// them. Positions are in units of a 160 × 90 grid over the whole slide.
/// </summary>
internal sealed record Scene(IReadOnlyList<SceneNode> Shapes, IReadOnlyList<SceneConnector> Connectors)
{
    /// <summary>The grid's columns and rows.</summary>
    public const int Columns = 160;

    /// <inheritdoc cref="Columns"/>
    public const int Rows = 90;

    public SceneNode Shape(string name) => this.Shapes.First(s => s.Name == name);

    /// <summary>The distinct click numbers of the scene's steps and exits, in order: the n-th is the n-th click.</summary>
    public IReadOnlyList<int> Clicks =>
        [.. this.Shapes.Concat<SceneElement>(this.Connectors).SelectMany(e => new[] { e.Style.Step, e.Style.Exit }).OfType<int>().Distinct().Order()];

    /// <summary>
    /// The sides a connector runs between: the facing sides of the two shapes, left and right where they are further
    /// apart across than up and down, else top and bottom.
    /// </summary>
    public (SceneSide From, SceneSide To) Sides(SceneConnector connector)
    {
        var (from, to) = (this.Shape(connector.From), this.Shape(connector.To));
        var across = Math.Max(to.X - (from.X + from.W), from.X - (to.X + to.W));
        var down = Math.Max(to.Y - (from.Y + from.H), from.Y - (to.Y + to.H));
        if (across == down)
        {
            var (fc, tc) = (from.Centre, to.Centre);
            (across, down) = (Math.Abs(tc.X - fc.X), Math.Abs(tc.Y - fc.Y));
        }

        return across >= down
            ? to.Centre.X >= from.Centre.X ? (SceneSide.Right, SceneSide.Left) : (SceneSide.Left, SceneSide.Right)
            : to.Centre.Y >= from.Centre.Y ? (SceneSide.Bottom, SceneSide.Top) : (SceneSide.Top, SceneSide.Bottom);
    }

    /// <summary>The middle of a shape's side, in grid units.</summary>
    public static (double X, double Y) Point(SceneNode shape, SceneSide side) => side switch
    {
        SceneSide.Top => (shape.X + (shape.W / 2), shape.Y),
        SceneSide.Left => (shape.X, shape.Y + (shape.H / 2)),
        SceneSide.Bottom => (shape.X + (shape.W / 2), shape.Y + shape.H),
        _ => (shape.X + shape.W, shape.Y + (shape.H / 2)),
    };

    /// <summary>
    /// Where a connector's label goes, in the caller's units: above the middle of a connector that runs across, to the
    /// right of one that runs up or down. Returns the label box's top left corner for a label of <paramref name="width"/>
    /// × <paramref name="height"/>, <paramref name="gap"/> away from the line.
    /// </summary>
    public static (double X, double Y) LabelAt((double X, double Y) start, (double X, double Y) end, double width, double height, double gap)
    {
        var (mx, my) = ((start.X + end.X) / 2, (start.Y + end.Y) / 2);
        return Math.Abs(end.X - start.X) >= Math.Abs(end.Y - start.Y)
            ? (mx - (width / 2), my - gap - height)
            : (mx + gap, my - (height / 2));
    }
}

/// <summary>
/// <c>::: scene</c>: one shape or connector per line, read from the Markdown source itself (so <c>*</c>, <c>_</c> and
/// <c>[</c> in a label stay as written). Blank lines are allowed; a line starting with <c>%</c> is a comment.
/// <list type="bullet">
/// <item><c>box|round|ellipse|text name x y w h ["label"] [attributes]</c>, in units of a 160 × 90 grid;</item>
/// <item><c>arrow|line from to ["label"] [attributes]</c>, between the facing sides of two named shapes.</item>
/// </list>
/// Attributes: a colour (<see cref="Roles"/>), <c>.bold</c>, <c>.small</c>, <c>.large</c>, <c>step=N</c>, <c>exit=N</c>,
/// <c>effect=appear|fly|wipe</c>, and <c>name=</c> on connectors. A scene is a slide of its own: under a <c>##</c>
/// heading, with nothing else but <c>::: notes</c> (<see cref="Check"/>). Every generator parses it here, so all reject
/// the same mistakes.
/// </summary>
internal static partial class SceneSyntax
{
    /// <summary>The container's name.</summary>
    public const string Name = "scene";

    /// <summary>The shape kinds, then the connector kinds, as they are written.</summary>
    public const string Kinds = "box, round, ellipse, text, arrow, line";

    /// <summary>The colour attributes, as they are written.</summary>
    public static readonly IReadOnlyList<string> Roles =
        [".primary", ".accent", ".highlight", ".muted", ".dark", ".outline", ".success", ".info", ".warning", ".danger", ".neutral"];

    private const int MaxStep = 99;

    /// <summary>Whether a block is a <c>::: scene</c>.</summary>
    public static bool IsScene(Block block) => block is CustomContainer { Info: Name };

    /// <summary>
    /// Checks where the document's scenes are: each alone with its <c>##</c> heading and any <c>::: notes</c>, at most
    /// one per section, never inside another block, and without a build marker before it.
    /// </summary>
    /// <exception cref="DocumentConversionException">A scene is misplaced, or shares its slide.</exception>
    public static void Check(Markdig.Syntax.MarkdownDocument document, DocumentFormat format, Func<MarkdownObject, int> lineOf)
    {
        HeadingBlock? heading = null;
        var blocks = new List<Block>();
        foreach (var block in document)
        {
            if (block is ContainerBlock container && !IsScene(block) && container.Descendants<CustomContainer>().FirstOrDefault(IsScene) is { } nested)
            {
                throw new DocumentConversionException(
                    format,
                    $"The '::: scene' at line {lineOf(nested)} is inside another block. A scene is a slide of its own: put it at the top level, right under its '##' heading.");
            }

            if (block is HeadingBlock { Level: <= 2 } next)
            {
                CheckSection(heading, blocks, format, lineOf);
                (heading, blocks) = (next, []);
            }
            else if (block is not Markdig.Extensions.Footnotes.FootnoteGroup)
            {
                blocks.Add(block);
            }
        }

        CheckSection(heading, blocks, format, lineOf);
    }

    private static void CheckSection(HeadingBlock? heading, List<Block> blocks, DocumentFormat format, Func<MarkdownObject, int> lineOf)
    {
        var scenes = blocks.Where(IsScene).ToList();
        if (scenes.Count == 0)
        {
            return;
        }

        var at = lineOf(scenes[0]);
        if (heading is not { Level: 2 })
        {
            throw new DocumentConversionException(
                format,
                $"The '::: scene' at line {at} is {(heading is null ? "before the first heading" : "under a '#' heading")}. A scene is a slide of its own: put it right under a '##' heading.");
        }

        if (scenes.Count > 1)
        {
            throw new DocumentConversionException(
                format,
                $"The slide at line {lineOf(heading)} has {scenes.Count} scenes (lines {string.Join(", ", scenes.Select(lineOf))}). A slide holds one '::: scene'; give each its own '##' heading.");
        }

        foreach (var block in blocks.Where(b => !IsScene(b) && b is not CustomContainer { Info: LayoutSyntax.Notes } && b is not LinkReferenceDefinitionGroup))
        {
            var what = block is ParagraphBlock paragraph && BuildMarker.IsMarker(paragraph)
                ? "a build marker; the scene's step= and exit= say what comes in and goes on which click"
                : "more than the scene";
            throw new DocumentConversionException(
                format,
                $"The slide with the '::: scene' at line {at} has {what} (line {lineOf(block)}). A scene slide holds only its heading, the scene and '::: notes'.");
        }
    }

    /// <summary>Reads a scene from the container's lines in <paramref name="markdown"/>, the text Markdig parsed it from.</summary>
    /// <exception cref="DocumentConversionException">A line is not a shape or connector, or the scene is empty; the message names the line.</exception>
    public static Scene Parse(CustomContainer container, string markdown, Func<MarkdownObject, int> lineOf, DocumentFormat format)
    {
        var first = lineOf(container);
        var start = Math.Clamp(container.Span.Start, 0, markdown.Length);
        var end = Math.Clamp(container.Span.End + 1, start, markdown.Length);
        var lines = markdown[start..end].Split('\n');

        // The opening fence, and the closing one if the container has it.
        var count = lines.Length;
        while (count > 1 && lines[count - 1].Trim().Length == 0)
        {
            count--;
        }

        if (count > 1 && FencePattern().IsMatch(lines[count - 1]))
        {
            count--;
        }

        var shapes = new List<SceneNode>();
        var connectors = new List<SceneConnector>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 1; i < count; i++)
        {
            var text = lines[i].Trim();
            if (text.Length == 0 || text.StartsWith('%'))
            {
                continue;
            }

            var line = new Line(text, first + i, format);
            var element = Element(line);
            if (!names.Add(element.Name))
            {
                throw line.Error($"repeats the name '{element.Name}'{(element is SceneConnector ? "; give one of the connectors its own with name=" : string.Empty)}. Names are unique in a scene");
            }

            if (element is SceneNode shape)
            {
                shapes.Add(shape);
            }
            else
            {
                connectors.Add((SceneConnector)element);
            }
        }

        if (shapes.Count == 0)
        {
            throw new DocumentConversionException(
                format,
                $"The '::: scene' at line {first} has no shapes. Write one per line, such as 'box api 65 34 30 22 \"API\"'.");
        }

        var byName = shapes.ToDictionary(s => s.Name, StringComparer.Ordinal);
        return new Scene(shapes, [.. connectors.Select(c => Resolve(c, byName, format))]);
    }

    // A connector's ends must be shapes; without step= it comes in with the later of them, without exit= it leaves with the
    // earlier one that leaves.
    private static SceneConnector Resolve(SceneConnector connector, Dictionary<string, SceneNode> shapes, DocumentFormat format)
    {
        foreach (var end in new[] { connector.From, connector.To })
        {
            if (!shapes.ContainsKey(end))
            {
                throw new DocumentConversionException(
                    format,
                    $"The connector '{connector.Name}' at line {connector.Line} joins '{end}', which is no shape of the scene. Connectors join shapes by their names: box, round, ellipse or text.");
            }
        }

        var (from, to) = (shapes[connector.From], shapes[connector.To]);
        var step = connector.Style.Step ?? Later(from.Style.Step, to.Style.Step);
        var exit = connector.Style.Exit ?? new[] { from.Style.Exit, to.Style.Exit }.OfType<int>().Cast<int?>().Min();
        if (step is { } s && exit is { } x && x <= s)
        {
            throw new DocumentConversionException(
                format,
                $"The connector '{connector.Name}' at line {connector.Line} would leave on step {x}, before it comes in on step {s}: one of its shapes leaves by then. Give it an exit= after its step=, or a step= before.");
        }

        return connector with { Style = connector.Style with { Step = step, Exit = exit } };
    }

    private static int? Later(int? a, int? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);

    private static SceneElement Element(Line line)
    {
        var tokens = Tokens(line);
        var kind = tokens[0].Text;
        if (tokens[0].Quoted || kind is not ("box" or "round" or "ellipse" or "text" or "arrow" or "line"))
        {
            throw line.Error($"starts with '{kind}', which is no kind of shape. Kinds: {Kinds}");
        }

        var connector = kind is "arrow" or "line";
        var positional = connector ? 3 : 6;
        if (tokens.Count < positional || tokens.Take(positional).Any(t => t.Quoted || t.Text.StartsWith('.') || t.Text.Contains('=', StringComparison.Ordinal)))
        {
            throw line.Error(connector
                ? $"needs the names of the two shapes it joins: '{kind} from to [\"label\"] [attributes]'"
                : $"needs a name and its place on the grid: '{kind} name x y w h [\"label\"] [attributes]'");
        }

        string? label = null;
        string? name = connector ? null : CheckName(line, tokens[1].Text);
        var role = (SceneRole?)null;
        var (bold, size, step, exit, effect) = (false, (SceneTextSize?)null, (int?)null, (int?)null, (BuildEffect?)null);
        foreach (var token in tokens.Skip(positional))
        {
            if (token.Quoted)
            {
                label = label is null ? token.Text : throw line.Error("has two labels in quotes; a shape has one label, '\\n' breaks it");
                continue;
            }

            var text = token.Text;
            var equals = text.IndexOf('=', StringComparison.Ordinal);
            if (text.StartsWith('.') && Roles.Contains(text, StringComparer.Ordinal))
            {
                role = role is null ? Enum.Parse<SceneRole>(text[1..], ignoreCase: true) : throw line.Error($"has two colours, '.{role.Value.ToString().ToLowerInvariant()}' and '{text}'; keep one");
            }
            else if (text == ".bold")
            {
                bold = true;
            }
            else if (text is ".small" or ".large")
            {
                size = size is null ? text == ".small" ? SceneTextSize.Small : SceneTextSize.Large : throw line.Error("has two text sizes; keep one of .small and .large");
            }
            else if (equals > 0 && text[..equals] is "step" or "exit")
            {
                var value = Step(line, text, text[(equals + 1)..]);
                if (text.StartsWith("step", StringComparison.Ordinal))
                {
                    step = step is null ? value : throw line.Error("has step= twice; keep one");
                }
                else
                {
                    exit = exit is null ? value : throw line.Error("has exit= twice; keep one");
                }
            }
            else if (equals > 0 && text[..equals] == "effect")
            {
                var value = text[(equals + 1)..];
                effect = effect is null && BuildMarker.Effects.Contains(value, StringComparer.Ordinal)
                    ? Enum.Parse<BuildEffect>(value, ignoreCase: true)
                    : throw line.Error(effect is null ? $"has '{text}'; effect= takes {string.Join(", ", BuildMarker.Effects)}" : "has effect= twice; keep one");
            }
            else if (connector && equals > 0 && text[..equals] == "name")
            {
                name = name is null ? CheckName(line, text[(equals + 1)..]) : throw line.Error("has name= twice; keep one");
            }
            else
            {
                throw line.Error(
                    $"has '{text}', which a {(connector ? "connector" : "shape")} does not take. Attributes: {string.Join(" ", Roles)} .bold .small .large step=N exit=N effect={string.Join("|", BuildMarker.Effects)}"
                    + (connector ? " name=…" : string.Empty)
                    + (text.StartsWith('"') ? "; a label is one quoted text, with \\\" for a quote" : string.Empty));
            }
        }

        if (effect is not null && step is null)
        {
            throw line.Error("has effect= without step=: the effect is how it comes in on its step");
        }

        if (step is { } s && exit is { } x && x <= s)
        {
            throw line.Error($"has exit={x}, which is not after its step={s}");
        }

        var style = new SceneStyle(role ?? SceneRole.Default, bold, size ?? SceneTextSize.Normal, step, exit, effect ?? BuildEffect.Appear);
        if (connector)
        {
            var (from, to) = (CheckName(line, tokens[1].Text), CheckName(line, tokens[2].Text));
            if (from == to)
            {
                throw line.Error($"joins '{from}' to itself; a connector joins two shapes");
            }

            return new SceneConnector(name ?? $"{from}-{to}", from, to, kind == "arrow", label, style, line.Number);
        }

        var (gx, gy, gw, gh) = (Number(line, tokens[2].Text, "x"), Number(line, tokens[3].Text, "y"), Number(line, tokens[4].Text, "w"), Number(line, tokens[5].Text, "h"));
        if (gw <= 0 || gh <= 0)
        {
            throw line.Error("has a width or height of 0; both are more than 0 grid units");
        }

        if (gx + gw > Scene.Columns || gy + gh > Scene.Rows)
        {
            throw line.Error(Invariant($"runs outside the grid of {Scene.Columns} × {Scene.Rows} units (to {gx + gw} across and {gy + gh} down); keep x + w ≤ {Scene.Columns} and y + h ≤ {Scene.Rows}"));
        }

        if (kind == "text" && label is null)
        {
            throw line.Error("is a text without its text; write it in quotes after the place");
        }

        var shapeKind = kind switch { "box" => SceneKind.Box, "round" => SceneKind.Round, "ellipse" => SceneKind.Ellipse, _ => SceneKind.Text };
        return new SceneNode(name!, shapeKind, gx, gy, gw, gh, label, style, line.Number);
    }

    private static string CheckName(Line line, string name) =>
        NamePattern().IsMatch(name)
            ? name
            : throw line.Error($"has the name '{name}'; a name is letters, digits and '-', starting with a letter");

    private static int Step(Line line, string token, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var step) && step is >= 1 and <= MaxStep
            ? step
            : throw line.Error($"has '{token}'; a step is a whole number from 1 to {MaxStep}");

    private static double Number(Line line, string text, string what) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : throw line.Error($"has '{text}' as its {what}; expected a number of grid units, such as 34 or 12.5");

    // Words and quoted labels; in quotes `\n` breaks the line, `\"` is a quote and `\\` a backslash.
    private static List<(string Text, bool Quoted)> Tokens(Line line)
    {
        var tokens = new List<(string, bool)>();
        var text = line.Text;
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '"')
            {
                var label = new StringBuilder();
                i++;
                var closed = false;
                while (i < text.Length)
                {
                    var c = text[i++];
                    if (c == '"')
                    {
                        closed = true;
                        break;
                    }

                    if (c == '\\' && i < text.Length)
                    {
                        var next = text[i++];
                        label.Append(next switch { 'n' => '\n', _ => next });
                        continue;
                    }

                    label.Append(c);
                }

                if (!closed)
                {
                    throw line.Error("has a label without its closing quote");
                }

                tokens.Add((label.ToString(), true));
                continue;
            }

            var startAt = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"')
            {
                i++;
            }

            tokens.Add((text[startAt..i], false));
        }

        return tokens;
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^\s*:{3,}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FencePattern();

    private sealed record Line(string Text, int Number, DocumentFormat Format)
    {
        public DocumentConversionException Error(string problem) =>
            new(this.Format, $"The scene line '{GenerationNotes.Quote(this.Text)}' at line {this.Number.ToString(CultureInfo.InvariantCulture)} {problem}.");
    }
}
