using System.Globalization;
using System.Text;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>What an animation can be attached to: a shape with text, a graphic frame (table, chart), or another shape.</summary>
internal enum TargetKind
{
    /// <summary>A <c>p:sp</c>: text box, placeholder or box.</summary>
    Shape,

    /// <summary>A <c>p:graphicFrame</c>: a table or a chart, built as one.</summary>
    Graphic,

    /// <summary>A picture or a group, such as a drawing.</summary>
    Other,
}

/// <summary>
/// What one build marker covers on a slide: a whole shape, or one paragraph of a shape's text
/// (<see cref="Paragraph"/>, from 0). <see cref="Item"/> is the list item, card, row, column or image it belongs to.
/// </summary>
internal sealed record BuildTarget(BuildMark Mark, int Item, uint Shape, TargetKind Kind, int? Paragraph);

/// <summary>
/// Writes a slide's <c>p:timing</c>: the main sequence, one click per build step in the order of the Markdown, each
/// step's shapes started together, and the build list. Every effect is one of PowerPoint's presets, written as
/// PowerPoint writes it, so the Animation pane names it.
/// </summary>
internal static class TimingWriter
{
    private const string PresentationML = "http://schemas.openxmlformats.org/presentationml/2006/main";

    /// <summary>
    /// Appends the timing to the slide, after its transition, as the schema orders them; a slide without build targets
    /// gets none.
    /// </summary>
    public static void Write(P.Slide slide, IReadOnlyList<BuildTarget> targets)
    {
        if (targets.Count > 0)
        {
            slide.Append(new P.Timing(Xml(targets)));
        }
    }

    /// <summary>
    /// The steps of the targets: the marked blocks in the order of the Markdown, each marker's builds in their written
    /// order, and a build that goes item by item one step per item.
    /// </summary>
    internal static List<(Build Build, List<BuildTarget> Targets)> Steps(IReadOnlyList<BuildTarget> targets)
    {
        var steps = new List<(Build, List<BuildTarget>)>();
        foreach (var block in targets.GroupBy(t => t.Mark.Block).OrderBy(g => g.Key))
        {
            foreach (var build in block.First().Mark.Builds)
            {
                if (build.ByItem)
                {
                    steps.AddRange(block.GroupBy(t => t.Item).OrderBy(g => g.Key).Select(item => (build, item.ToList())));
                }
                else
                {
                    steps.Add((build, block.ToList()));
                }
            }
        }

        return steps;
    }

    internal static string Xml(IReadOnlyList<BuildTarget> targets)
    {
        // Per shape, in order of appearance: whether some step builds it by paragraph, and whether some step builds it whole.
        var shapes = targets.GroupBy(t => t.Shape)
            .Select(g => (Id: g.Key, g.First().Kind, ByParagraph: g.Any(t => t.Paragraph is not null), Whole: g.Any(t => t.Paragraph is null)))
            .ToList();
        int Group(uint shape, bool whole) => whole && shapes.First(s => s.Id == shape).ByParagraph ? 1 : 0;

        var id = 0;
        string Next() => (++id).ToString(CultureInfo.InvariantCulture);
        var xml = new StringBuilder();
        xml.Append($"<p:timing xmlns:p=\"{PresentationML}\"><p:tnLst><p:par><p:cTn id=\"{Next()}\" dur=\"indefinite\" restart=\"never\" nodeType=\"tmRoot\"><p:childTnLst>");
        xml.Append($"<p:seq concurrent=\"1\" nextAc=\"seek\"><p:cTn id=\"{Next()}\" dur=\"indefinite\" nodeType=\"mainSeq\"><p:childTnLst>");
        foreach (var (build, stepTargets) in Steps(targets))
        {
            xml.Append($"<p:par><p:cTn id=\"{Next()}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"indefinite\"/></p:stCondLst><p:childTnLst>");
            xml.Append($"<p:par><p:cTn id=\"{Next()}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>");
            var first = true;
            foreach (var (shape, paragraphs) in Effects(stepTargets))
            {
                var target = Target(shape, paragraphs);
                Effect(xml, build, target, Group(shape, paragraphs is null), first ? "clickEffect" : "withEffect", Next);
                first = false;
            }

            xml.Append("</p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par>");
        }

        xml.Append("</p:childTnLst></p:cTn>");
        xml.Append("<p:prevCondLst><p:cond evt=\"onPrev\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst>");
        xml.Append("<p:nextCondLst><p:cond evt=\"onNext\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst>");
        xml.Append("</p:seq></p:childTnLst></p:cTn></p:par></p:tnLst>");

        // Pictures and groups have no entry in the build list.
        var list = new StringBuilder();
        foreach (var shape in shapes)
        {
            var spid = shape.Id.ToString(CultureInfo.InvariantCulture);
            if (shape.Kind == TargetKind.Graphic)
            {
                list.Append($"<p:bldGraphic spid=\"{spid}\" grpId=\"0\"><p:bldAsOne/></p:bldGraphic>");
            }
            else if (shape.Kind == TargetKind.Shape)
            {
                if (shape.ByParagraph)
                {
                    list.Append($"<p:bldP spid=\"{spid}\" grpId=\"0\" build=\"p\"/>");
                }

                if (shape.Whole)
                {
                    list.Append($"<p:bldP spid=\"{spid}\" grpId=\"{Group(shape.Id, whole: true)}\" animBg=\"1\"/>");
                }
            }
        }

        if (list.Length > 0)
        {
            xml.Append("<p:bldLst>").Append(list).Append("</p:bldLst>");
        }

        return xml.Append("</p:timing>").ToString();
    }

    // The effects of one step: per shape in order, the whole shape, or its paragraphs as ranges of neighbours.
    private static IEnumerable<(uint Shape, (int Start, int End)? Paragraphs)> Effects(List<BuildTarget> targets)
    {
        foreach (var shape in targets.GroupBy(t => t.Shape))
        {
            if (shape.Any(t => t.Paragraph is null))
            {
                yield return (shape.Key, null);
                continue;
            }

            var indices = shape.Select(t => t.Paragraph!.Value).Distinct().Order().ToList();
            var start = indices[0];
            for (var i = 1; i <= indices.Count; i++)
            {
                if (i == indices.Count || indices[i] != indices[i - 1] + 1)
                {
                    yield return (shape.Key, (start, indices[i - 1]));
                    if (i < indices.Count)
                    {
                        start = indices[i];
                    }
                }
            }
        }
    }

    private static string Target(uint shape, (int Start, int End)? paragraphs)
    {
        var spid = shape.ToString(CultureInfo.InvariantCulture);
        return paragraphs is var (start, end)
            ? $"<p:tgtEl><p:spTgt spid=\"{spid}\"><p:txEl><p:pRg st=\"{start.ToString(CultureInfo.InvariantCulture)}\" end=\"{end.ToString(CultureInfo.InvariantCulture)}\"/></p:txEl></p:spTgt></p:tgtEl>"
            : $"<p:tgtEl><p:spTgt spid=\"{spid}\"/></p:tgtEl>";
    }

    // One effect node, as PowerPoint writes its preset: Appear (1), Fly In from the bottom (2, subtype 4), Wipe from the
    // left (22, subtype 8), Disappear (exit 1) and Pulse (emphasis 26).
    private static void Effect(StringBuilder xml, Build build, string target, int group, string nodeType, Func<string> next)
    {
        var (preset, presetClass, subtype) = build.Effect switch
        {
            BuildEffect.Fly => (2, "entr", 4),
            BuildEffect.Wipe => (22, "entr", 8),
            BuildEffect.Disappear => (1, "exit", 0),
            BuildEffect.Pulse => (26, "emph", 0),
            _ => (1, "entr", 0),
        };
        xml.Append($"<p:par><p:cTn id=\"{next()}\" presetID=\"{preset}\" presetClass=\"{presetClass}\" presetSubtype=\"{subtype}\" fill=\"hold\" grpId=\"{group}\" nodeType=\"{nodeType}\">");
        xml.Append("<p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>");
        switch (build.Effect)
        {
            case BuildEffect.Appear:
                Visibility(xml, target, "visible", next);
                break;
            case BuildEffect.Fly:
                Visibility(xml, target, "visible", next);
                Move(xml, target, "ppt_x", "#ppt_x", "#ppt_x", next);
                Move(xml, target, "ppt_y", "1+#ppt_h/2", "#ppt_y", next);
                break;
            case BuildEffect.Wipe:
                Visibility(xml, target, "visible", next);
                xml.Append($"<p:animEffect transition=\"in\" filter=\"wipe(left)\"><p:cBhvr><p:cTn id=\"{next()}\" dur=\"500\"/>{target}</p:cBhvr></p:animEffect>");
                break;
            case BuildEffect.Disappear:
                Visibility(xml, target, "hidden", next);
                break;
            case BuildEffect.Pulse:
                xml.Append($"<p:animEffect transition=\"out\" filter=\"fade\"><p:cBhvr><p:cTn id=\"{next()}\" dur=\"500\" tmFilter=\"0, 0; .2, .5; .8, .5; 1, 0\"/>{target}</p:cBhvr></p:animEffect>");
                xml.Append($"<p:animScale><p:cBhvr><p:cTn id=\"{next()}\" dur=\"250\" autoRev=\"1\" fill=\"hold\"/>{target}</p:cBhvr><p:by x=\"105000\" y=\"105000\"/></p:animScale>");
                break;
        }

        xml.Append("</p:childTnLst></p:cTn></p:par>");
    }

    private static void Visibility(StringBuilder xml, string target, string value, Func<string> next) => xml.Append(
        $"<p:set><p:cBhvr><p:cTn id=\"{next()}\" dur=\"1\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst></p:cTn>{target}"
        + $"<p:attrNameLst><p:attrName>style.visibility</p:attrName></p:attrNameLst></p:cBhvr><p:to><p:strVal val=\"{value}\"/></p:to></p:set>");

    private static void Move(StringBuilder xml, string target, string attribute, string from, string to, Func<string> next) => xml.Append(
        $"<p:anim calcmode=\"lin\" valueType=\"num\"><p:cBhvr additive=\"base\"><p:cTn id=\"{next()}\" dur=\"500\" fill=\"hold\"/>{target}"
        + $"<p:attrNameLst><p:attrName>{attribute}</p:attrName></p:attrNameLst></p:cBhvr>"
        + $"<p:tavLst><p:tav tm=\"0\"><p:val><p:strVal val=\"{from}\"/></p:val></p:tav><p:tav tm=\"100000\"><p:val><p:strVal val=\"{to}\"/></p:val></p:tav></p:tavLst></p:anim>");
}
