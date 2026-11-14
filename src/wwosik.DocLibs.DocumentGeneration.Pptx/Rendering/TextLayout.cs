using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

/// <summary>
/// Where a text body's properties come from, nearest first: list styles (each with <c>lvl1pPr</c>…<c>lvl9pPr</c>) such
/// as the shape's own <c>lstStyle</c>, the layout's and master's placeholder's, the master's title, body or other style
/// and the presentation's <c>defaultTextStyle</c>; <c>bodyPr</c> elements merged attribute by attribute.
/// </summary>
internal sealed record TextSources(IReadOnlyList<XElement?> Lists, IReadOnlyList<XElement?> Bodies);

/// <summary>Text laid out in a box: the lines, and the height they take (for table rows that grow to fit).</summary>
internal sealed record LaidOutText(SceneText Text, double Height);

/// <summary>
/// Lays out a DrawingML text body with real font metrics: properties resolved through <see cref="TextSources"/>,
/// lines broken between words at spaces and after hyphens (a word wider than the line between characters), bullets
/// and numbering, line and paragraph spacing, insets, anchoring and autofit. No kerning, ligatures or shaping.
/// </summary>
internal sealed class TextLayout(Palette palette, FontLibrary fonts, Func<string, string?> links, int slideNumber)
{
    private const double EmuPerPoint = 12700;
    private const double DefaultInsetX = 91440;
    private const double DefaultInsetY = 45720;

    // Wingdings bullets the generator and templates use, as Unicode, so they draw without the font.
    private static readonly Dictionary<char, string> Wingdings = new()
    {
        [''] = "☐", [''] = "☑", [''] = "✓", [''] = "▪", [''] = "■",
        [''] = "➢", [''] = "❖", [''] = "•", ['§'] = "▪", ['Ø'] = "➢",
        ['ü'] = "✓", ['n'] = "■", ['q'] = "❏", ['v'] = "❖",
    };

    /// <summary>
    /// Lays out <paramref name="body"/> (a <c>txBody</c>) in a box of <paramref name="width"/> × <paramref name="height"/> EMU;
    /// <see langword="null"/> when it has no text to draw.
    /// </summary>
    public LaidOutText? Layout(XElement? body, TextSources sources, double width, double height)
    {
        if (body is null)
        {
            return null;
        }

        var bodies = new List<XElement?> { body.Element(Ns.A + "bodyPr") };
        bodies.AddRange(sources.Bodies);
        string? Body(string attribute) => bodies.Select(b => b?.Attribute(attribute)?.Value).FirstOrDefault(v => v is not null);
        double Inset(string attribute, double fallback) => double.TryParse(Body(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        if (Body("vert") is { } vertical && vertical != "horz")
        {
            palette.Notes.Unsupported("vertical text (drawn horizontally)");
        }

        if (int.TryParse(Body("numCol"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns) && columns > 1)
        {
            palette.Notes.Unsupported("text columns (drawn as one column)");
        }

        if (bodies.Any(b => b?.Element(Ns.A + "prstTxWarp") is { } warp && warp.Attribute("prst")?.Value != "textNoShape"))
        {
            palette.Notes.Unsupported("text warps (drawn straight)");
        }

        if (bodies.Any(b => b?.Element(Ns.A + "scene3d") is not null || b?.Element(Ns.A + "sp3d") is not null))
        {
            palette.Notes.Unsupported("3-D text");
        }

        var autofit = bodies.Select(b => b?.Elements().FirstOrDefault(e => e.Name.LocalName is "normAutofit" or "noAutofit" or "spAutoFit")).FirstOrDefault(e => e is not null);
        var fontScale = autofit?.Name.LocalName == "normAutofit" && autofit.Attribute("fontScale") is not null ? Palette.Number(autofit, "fontScale") / 100000 : 1;
        var spacingReduction = autofit?.Name.LocalName == "normAutofit" ? Palette.Number(autofit, "lnSpcReduction") / 100000 : 0;
        var (left, top, right, bottom) = (Inset("lIns", DefaultInsetX), Inset("tIns", DefaultInsetY), Inset("rIns", DefaultInsetX), Inset("bIns", DefaultInsetY));
        var wrap = Body("wrap") != "none";
        var room = Math.Max(1, width - left - right);

        var paragraphs = new List<(int Index, List<LineDraft> Lines, double Before, double After)>();
        var counters = new Dictionary<int, int>();
        var index = 0;
        var any = false;
        foreach (var paragraph in body.Elements(Ns.A + "p"))
        {
            var laid = this.Paragraph(paragraph, sources, room, wrap, fontScale, spacingReduction, counters);
            any |= laid.Lines.Any(l => l.Runs.Any(r => r.Text.Trim().Length > 0));
            paragraphs.Add((index++, laid.Lines, laid.Before, laid.After));
        }

        if (!any)
        {
            return null;
        }

        // Paragraph spacing before the first paragraph is not applied, as in PowerPoint.
        var total = 0.0;
        for (var p = 0; p < paragraphs.Count; p++)
        {
            total += (p > 0 ? paragraphs[p].Before : 0) + paragraphs[p].Lines.Sum(l => l.Pitch) + (p < paragraphs.Count - 1 ? paragraphs[p].After : 0);
        }

        var anchor = Body("anchor") ?? "t";
        var y = anchor switch
        {
            "ctr" => top + ((height - top - bottom - total) / 2),
            "b" => height - bottom - total,
            _ => top,
        };
        var scene = new List<SceneParagraph>();
        for (var p = 0; p < paragraphs.Count; p++)
        {
            y += p > 0 ? paragraphs[p].Before : 0;
            var lines = new List<SceneTextLine>();
            foreach (var line in paragraphs[p].Lines)
            {
                lines.Add(new SceneTextLine(y + line.Baseline, [.. line.Runs.Where(r => r.Text.Length > 0).Select(r => r with { X = r.X + left })]));
                y += line.Pitch;
            }

            y += paragraphs[p].After;
            scene.Add(new SceneParagraph(paragraphs[p].Index, lines));
        }

        return new LaidOutText(new SceneText(scene), total + top + bottom);
    }

    private sealed record LineDraft(List<SceneRun> Runs, double Pitch, double Baseline);

    private sealed record Cluster(string Text, Style Style, double Width, bool Space, bool Break);

    private sealed record Style(SceneFont Font, double Size, Rgba Color, bool Underline, bool Strike, double Shift, Rgba? Highlight, string? Link);

    private (List<LineDraft> Lines, double Before, double After) Paragraph(
        XElement paragraph,
        TextSources sources,
        double room,
        bool wrap,
        double fontScale,
        double spacingReduction,
        Dictionary<int, int> counters)
    {
        var properties = paragraph.Element(Ns.A + "pPr");
        var level = Math.Clamp((int)Palette.Number(properties ?? new XElement("x"), "lvl"), 0, 8);
        var chain = new List<XElement?> { properties };
        chain.AddRange(sources.Lists.Select(list => list?.Element(Ns.A + $"lvl{level + 1}pPr")));
        string? Attribute(string name) => chain.Select(e => e?.Attribute(name)?.Value).FirstOrDefault(v => v is not null);
        XElement? Child(params string[] names) => chain.Select(e => e?.Elements().FirstOrDefault(c => names.Contains(c.Name.LocalName))).FirstOrDefault(c => c is not null);
        double Emu(string name) => double.TryParse(Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

        if (Attribute("rtl") is "1" or "true")
        {
            palette.Notes.Unsupported("right-to-left text (drawn left to right)");
        }

        var marL = Emu("marL");
        var indent = Emu("indent");
        var alignment = Attribute("algn") ?? "l";
        var defaults = chain.Select(e => e?.Element(Ns.A + "defRPr")).ToList();

        // Runs, breaks and fields in order.
        var clusters = new List<Cluster>();
        Style? first = null;
        foreach (var element in paragraph.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "r" or "fld":
                    var style = this.RunStyle(element.Element(Ns.A + "rPr"), defaults, fontScale);
                    first ??= style;
                    var text = element.Element(Ns.A + "t")?.Value ?? string.Empty;
                    if (element.Name.LocalName == "fld" && element.Attribute("type")?.Value == "slidenum")
                    {
                        text = slideNumber.ToString(CultureInfo.InvariantCulture);
                    }

                    this.Add(clusters, text, style);
                    break;
                case "br":
                    var breakStyle = this.RunStyle(element.Element(Ns.A + "rPr"), defaults, fontScale);
                    first ??= breakStyle;
                    clusters.Add(new Cluster(string.Empty, breakStyle, 0, false, true));
                    break;
            }
        }

        var end = this.RunStyle(paragraph.Element(Ns.A + "endParaRPr"), defaults, fontScale);
        var hasText = clusters.Any(c => !c.Break);

        // Bullets and numbering, on paragraphs with text.
        SceneRun? bullet = null;
        var bulletKind = Child("buNone", "buChar", "buAutoNum", "buBlip");
        if (bulletKind?.Name.LocalName != "buAutoNum")
        {
            counters.Remove(level);
        }

        foreach (var deeper in counters.Keys.Where(k => k > level).ToList())
        {
            counters.Remove(deeper);
        }

        if (hasText && bulletKind is not null && bulletKind.Name.LocalName != "buNone" && first is not null)
        {
            string? symbol = null;
            if (bulletKind.Name.LocalName == "buChar")
            {
                symbol = bulletKind.Attribute("char")?.Value;
            }
            else if (bulletKind.Name.LocalName == "buAutoNum")
            {
                var start = bulletKind.Attribute("startAt") is { } at ? int.Parse(at.Value, CultureInfo.InvariantCulture) : 1;
                var number = counters.TryGetValue(level, out var previous) ? previous + 1 : start;
                counters[level] = number;
                symbol = AutoNumber(bulletKind.Attribute("type")?.Value ?? "arabicPeriod", number);
            }
            else
            {
                palette.Notes.Unsupported("picture bullets (drawn as •)");
                symbol = "•";
            }

            if (!string.IsNullOrEmpty(symbol))
            {
                var bulletFont = Child("buFontTx", "buFont");
                var family = bulletFont?.Name.LocalName == "buFont" ? palette.Theme.Font(bulletFont.Attribute("typeface")?.Value ?? first.Font.Family) : first.Font.Family;
                if (family.Equals("Wingdings", StringComparison.OrdinalIgnoreCase) || family.Equals("Symbol", StringComparison.OrdinalIgnoreCase))
                {
                    symbol = string.Concat(symbol.Select(c => Wingdings.TryGetValue(c, out var u) ? u : c.ToString()));
                    family = first.Font.Family;
                }

                var sizeElement = Child("buSzTx", "buSzPct", "buSzPts");
                var size = sizeElement?.Name.LocalName switch
                {
                    "buSzPct" => first.Size * Palette.Number(sizeElement, "val") / 100000,
                    "buSzPts" => Palette.Number(sizeElement, "val") / 100 * EmuPerPoint * fontScale,
                    _ => first.Size,
                };
                var colourElement = Child("buClrTx", "buClr");
                var colour = colourElement?.Name.LocalName == "buClr" ? palette.Color(colourElement) ?? first.Color : first.Color;
                var font = new SceneFont(family, false, false, fonts.Face(family, false, false));
                bullet = new SceneRun(0, FontLibrary.Width(font, symbol, size), symbol, font, size, colour, false, false, 0, null, null);
            }
        }

        // Spacing.
        var spacing = Child("lnSpc");
        var percent = spacing?.Element(Ns.A + "spcPct") is { } pct ? Palette.Number(pct, "val") / 100000 : 1.0;
        double? points = spacing?.Element(Ns.A + "spcPts") is { } pts ? Palette.Number(pts, "val") / 100 * EmuPerPoint : null;
        percent = Math.Max(0.1, percent - spacingReduction);
        double Space(XElement? element, double size) =>
            element?.Element(Ns.A + "spcPts") is { } p ? Palette.Number(p, "val") / 100 * EmuPerPoint
            : element?.Element(Ns.A + "spcPct") is { } q ? Palette.Number(q, "val") / 100000 * size * 1.2
            : 0;
        var firstSize = first?.Size ?? end.Size;
        var before = Space(Child("spcBef"), firstSize);
        var after = Space(Child("spcAft"), firstSize);

        // Line breaking.
        var bulletX = marL + indent;
        var firstStart = bullet is null ? marL + indent : indent < 0 ? Math.Max(marL, bulletX + bullet.Width) : bulletX + bullet.Width;
        var lines = new List<List<Cluster>>();
        var current = new List<Cluster>();
        var currentWidth = 0.0;
        double Room(int line) => Math.Max(1, room - (line == 0 ? firstStart : marL));
        foreach (var token in Tokens(clusters))
        {
            if (token.Count == 1 && token[0].Break)
            {
                current.Add(token[0]);
                lines.Add(current);
                (current, currentWidth) = ([], 0);
                continue;
            }

            var wordWidth = token.Where(c => !c.Space).Sum(c => c.Width);
            var tokenWidth = token.Sum(c => c.Width);
            if (wrap && current.Count > 0 && currentWidth + wordWidth > Room(lines.Count) + 1)
            {
                lines.Add(current);
                (current, currentWidth) = ([], 0);
            }

            if (wrap && current.Count == 0 && wordWidth > Room(lines.Count) + 1)
            {
                // A word wider than the line breaks between characters.
                foreach (var cluster in token)
                {
                    if (!cluster.Space && current.Count > 0 && currentWidth + cluster.Width > Room(lines.Count) + 1)
                    {
                        lines.Add(current);
                        (current, currentWidth) = ([], 0);
                    }

                    current.Add(cluster);
                    currentWidth += cluster.Width;
                }

                continue;
            }

            current.AddRange(token);
            currentWidth += tokenWidth;
        }

        if (current.Count > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }

        var drafts = new List<LineDraft>();
        for (var l = 0; l < lines.Count; l++)
        {
            var line = lines[l];
            var visible = line.Where(c => !c.Break).ToList();
            var start = l == 0 ? firstStart : marL;
            var trimmed = visible.Count;
            while (trimmed > 0 && visible[trimmed - 1].Space)
            {
                trimmed--;
            }

            var lineWidth = visible.Take(trimmed).Sum(c => c.Width);
            var lineRoom = room - start;
            var x = start + alignment switch
            {
                "ctr" => (lineRoom - lineWidth) / 2,
                "r" => lineRoom - lineWidth,
                _ => 0,
            };
            if (l == 0 && bullet is not null && alignment is "ctr" or "r")
            {
                x = Math.Max(x, bulletX + bullet.Width);
            }

            var runs = new List<SceneRun>();
            if (l == 0 && bullet is not null)
            {
                runs.Add(bullet with { X = alignment is "ctr" or "r" ? x - bullet.Width : bulletX });
            }

            var text = new StringBuilder();
            Style? style = null;
            double runX = x, runWidth = 0;
            void Flush()
            {
                if (style is not null && text.Length > 0)
                {
                    runs.Add(new SceneRun(runX, runWidth, text.ToString(), style.Font, style.Size, style.Color, style.Underline, style.Strike, style.Shift, style.Highlight, style.Link));
                }

                text.Clear();
                runX += runWidth;
                runWidth = 0;
            }

            foreach (var cluster in visible)
            {
                if (!ReferenceEquals(cluster.Style, style))
                {
                    Flush();
                    style = cluster.Style;
                }

                text.Append(cluster.Text);
                runWidth += cluster.Width;
            }

            Flush();

            var largest = line.Count > 0 ? line.Max(c => c.Style.Size) : end.Size;
            if (bullet is not null && l == 0)
            {
                largest = Math.Max(largest, bullet.Size);
            }

            var ascentFace = line.Where(c => c.Style.Size == largest).Select(c => c.Style.Font.Face).FirstOrDefault(f => f is not null);
            var ascent = (ascentFace?.Metrics.Ascent ?? 0.905) * largest;
            var natural = 1.2 * largest;
            var pitch = points ?? (natural * percent);
            var baseline = Math.Max(pitch - (natural - ascent), 0.5 * largest);
            drafts.Add(new LineDraft(runs, pitch, baseline));
        }

        return (drafts, before, after);
    }

    // Words with their trailing spaces, split after hyphens; a line break is a token of its own.
    private static IEnumerable<List<Cluster>> Tokens(List<Cluster> clusters)
    {
        var token = new List<Cluster>();
        for (var i = 0; i < clusters.Count; i++)
        {
            var cluster = clusters[i];
            if (cluster.Break)
            {
                if (token.Count > 0)
                {
                    yield return token;
                }

                yield return [cluster];
                token = [];
                continue;
            }

            if (!cluster.Space && token.Count > 0 && token[^1].Space)
            {
                yield return token;
                token = [];
            }

            token.Add(cluster);
            if (cluster.Text is "-" or "‐" or "–" && i + 1 < clusters.Count && !clusters[i + 1].Space && !clusters[i + 1].Break)
            {
                yield return token;
                token = [];
            }
        }

        if (token.Count > 0)
        {
            yield return token;
        }
    }

    private void Add(List<Cluster> clusters, string text, Style style)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var piece = char.IsSurrogatePair(text, i) ? text.Substring(i++, 2) : text[i].ToString();
            if (piece == "\t")
            {
                piece = " ";
            }

            if (piece is "\n" or "\v")
            {
                clusters.Add(new Cluster(string.Empty, style, 0, false, true));
                continue;
            }

            clusters.Add(new Cluster(piece, style, FontLibrary.Width(style.Font, piece, style.Size), piece == " ", false));
        }
    }

    private Style RunStyle(XElement? properties, List<XElement?> defaults, double fontScale)
    {
        var chain = new List<XElement?> { properties };
        chain.AddRange(defaults);
        string? Attribute(string name) => chain.Select(e => e?.Attribute(name)?.Value).FirstOrDefault(v => v is not null);
        XElement? Child(params string[] names) => chain.Select(e => e?.Elements().FirstOrDefault(c => names.Contains(c.Name.LocalName))).FirstOrDefault(c => c is not null);
        static bool On(string? value) => value is "1" or "true" or "on";

        var size = (double.TryParse(Attribute("sz"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sz) ? sz : 1800) / 100 * EmuPerPoint * fontScale;
        var baseline = double.TryParse(Attribute("baseline"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var shift) ? shift / 100000 : 0;
        var shiftUp = baseline * size;
        if (baseline != 0)
        {
            size *= 2.0 / 3;
        }

        var bold = On(Attribute("b"));
        var italic = On(Attribute("i"));
        var family = palette.Theme.Font(Child("latin")?.Attribute("typeface")?.Value ?? "+mn-lt");
        var fill = Child("solidFill", "noFill", "gradFill");
        var colour = fill?.Name.LocalName switch
        {
            "solidFill" => palette.Color(fill) ?? Rgba.Black,
            "noFill" => new Rgba(0, 0, 0, 0),
            "gradFill" => palette.Color(fill.Descendants(Ns.A + "gs").FirstOrDefault()) ?? Rgba.Black,
            _ => Rgba.Black,
        };
        var highlight = properties?.Element(Ns.A + "highlight") is { } mark ? palette.Color(mark) : null;
        var link = properties?.Element(Ns.A + "hlinkClick")?.Attribute(Ns.R + "id")?.Value is { } id ? links(id) : null;
        if (properties?.Element(Ns.A + "effectLst")?.HasElements == true)
        {
            palette.Notes.Unsupported("text effects such as shadows and glow");
        }

        var font = new SceneFont(family, bold, italic, fonts.Face(family, bold, italic));
        return new Style(font, size, colour, Attribute("u") is { } u && u != "none", Attribute("strike") is { } s && s != "noStrike", shiftUp, highlight, link);
    }

    private static string AutoNumber(string scheme, int number)
    {
        var (format, suffix) = scheme switch
        {
            var s when s.EndsWith("ParenBoth", StringComparison.Ordinal) => (s[..^9], "()"),
            var s when s.EndsWith("ParenR", StringComparison.Ordinal) => (s[..^6], ")"),
            var s when s.EndsWith("Period", StringComparison.Ordinal) => (s[..^6], "."),
            var s when s.EndsWith("Plain", StringComparison.Ordinal) => (s[..^5], string.Empty),
            var s => (s, "."),
        };
        var value = format switch
        {
            "alphaLc" => Letters(number).ToLowerInvariant(),
            "alphaUc" => Letters(number),
            "romanLc" => Roman(number).ToLowerInvariant(),
            "romanUc" => Roman(number),
            _ => number.ToString(CultureInfo.InvariantCulture),
        };
        return suffix == "()" ? $"({value})" : value + suffix;
    }

    private static string Letters(int number)
    {
        var letter = (char)('A' + ((number - 1) % 26));
        return new string(letter, ((number - 1) / 26) + 1);
    }

    private static string Roman(int number)
    {
        var text = new StringBuilder();
        foreach (var (value, numeral) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
        {
            while (number >= value)
            {
                text.Append(numeral);
                number -= value;
            }
        }

        return text.ToString();
    }
}
