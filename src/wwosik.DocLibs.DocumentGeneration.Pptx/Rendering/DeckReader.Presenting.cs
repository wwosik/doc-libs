using System.Globalization;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

// What a slide needs for presenting (E07-S06): its speaker notes, the build steps of its main click sequence, and its
// transition.
internal sealed partial class DeckReader
{
    private static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    private static readonly XNamespace P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";

    private static readonly HashSet<string> Transitions = ["fade", "push", "wipe", "cover", "pull", "split", "zoom", "cut"];

    /// <summary>The body placeholder of the slide's notes page as paragraphs; the slide image and decorations are not notes.</summary>
    private List<NoteParagraph> SpeakerNotes(SlidePart slidePart)
    {
        if (slidePart.NotesSlidePart is not { } notesPart)
        {
            return [];
        }

        var notes = this.Xml(notesPart);
        var body = notes.Root.Descendants(Ns.P + "sp").FirstOrDefault(sp => Placeholder(sp) is { } ph
            && (ph.Attribute("type")?.Value == "body" || (ph.Attribute("type") is null && ph.Attribute("idx")?.Value == "1")));
        var paragraphs = new List<NoteParagraph>();
        foreach (var p in body?.Element(Ns.P + "txBody")?.Elements(Ns.A + "p") ?? [])
        {
            var runs = new List<NoteRun>();
            foreach (var child in p.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "r" or "fld":
                        var text = child.Element(Ns.A + "t")?.Value ?? string.Empty;
                        if (text.Length == 0)
                        {
                            break;
                        }

                        var properties = child.Element(Ns.A + "rPr");
                        var link = properties?.Element(Ns.A + "hlinkClick")?.Attribute(Ns.R + "id")?.Value is { } id ? notes.Link(id) : null;
                        runs.Add(new NoteRun(text, On(properties, "b"), On(properties, "i"), link));
                        break;
                    case "br":
                        runs.Add(new NoteRun("\n", false, false, null));
                        break;
                }
            }

            if (!runs.Any(r => r.Text.Trim().Length > 0))
            {
                continue;
            }

            var pPr = p.Element(Ns.A + "pPr");
            var level = pPr?.Attribute("lvl") is { } lvl && int.TryParse(lvl.Value, CultureInfo.InvariantCulture, out var l) ? Math.Clamp(l, 0, 8) : 0;
            var number = pPr?.Element(Ns.A + "buAutoNum");
            var list = number is not null ? NoteList.Number : pPr?.Element(Ns.A + "buChar") is not null ? NoteList.Bullet : NoteList.None;
            var start = number?.Attribute("startAt") is { } at && int.TryParse(at.Value, CultureInfo.InvariantCulture, out var s) ? s : 1;
            paragraphs.Add(new NoteParagraph(level, list, start, runs));
        }

        return paragraphs;
    }

    private static bool On(XElement? properties, string name) => properties?.Attribute(name)?.Value is "1" or "true";

    /// <summary>
    /// The main click sequence of <c>p:timing</c> as steps: each click a step, effects that start by themselves joining
    /// the step before. Effects on items the slide does not draw are dropped.
    /// </summary>
    private List<SceneStep> Steps(XElement slide, IReadOnlyList<SceneItem> own)
    {
        var root = slide.Element(Ns.P + "timing")?.Element(Ns.P + "tnLst")?.Element(Ns.P + "par")?.Element(Ns.P + "cTn");
        if (root is null)
        {
            return [];
        }

        var ids = new HashSet<int>();
        void Collect(SceneItem item)
        {
            ids.Add(item.Id);
            if (item is SceneGroup group)
            {
                foreach (var child in group.Items)
                {
                    Collect(child);
                }
            }
        }

        foreach (var item in own)
        {
            Collect(item);
        }

        var steps = new List<List<SceneEffect>>();
        foreach (var sequence in root.Element(Ns.P + "childTnLst")?.Elements(Ns.P + "seq") ?? [])
        {
            var node = sequence.Element(Ns.P + "cTn");
            if (node?.Attribute("nodeType")?.Value != "mainSeq")
            {
                this.notes.Unsupported("animations started by clicking a shape");
                continue;
            }

            foreach (var click in node.Element(Ns.P + "childTnLst")?.Elements(Ns.P + "par") ?? [])
            {
                var onClick = click.Element(Ns.P + "cTn")?.Element(Ns.P + "stCondLst")?.Elements(Ns.P + "cond")
                    .Any(c => c.Attribute("delay")?.Value == "indefinite") ?? false;
                var effects = click.Descendants(Ns.P + "cTn")
                    .Where(c => c.Attribute("presetClass") is not null)
                    .Select(this.Effect)
                    .OfType<SceneEffect>()
                    .Where(e => ids.Contains(e.Id))
                    .ToList();
                if (onClick || steps.Count == 0)
                {
                    steps.Add(effects);
                }
                else
                {
                    steps[^1].AddRange(effects);
                }
            }
        }

        return [.. steps.Where(s => s.Count > 0).Select(s => new SceneStep(s))];
    }

    // One effect node (a cTn with presetClass). Appear, Fade, Fly and Wipe in and out, and Pulse are known; another
    // entrance plays as appear, another exit as disappear, another emphasis as a pulse, and each is reported.
    private SceneEffect? Effect(XElement node)
    {
        var target = node.Descendants(Ns.P + "spTgt").FirstOrDefault();
        if (target is null || !int.TryParse(target.Attribute("spid")?.Value, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var presetClass = node.Attribute("presetClass")?.Value;
        var preset = Integer(node, "presetID");
        var subtype = Integer(node, "presetSubtype");
        EffectClass effectClass;
        switch (presetClass)
        {
            case "entr":
                effectClass = EffectClass.Entrance;
                break;
            case "exit":
                effectClass = EffectClass.Exit;
                break;
            case "emph":
                effectClass = EffectClass.Emphasis;
                break;
            case "mediacall":
                return null;
            default:
                this.notes.Unsupported($"motion paths and other {presetClass} animations");
                return null;
        }

        var kind = (effectClass, preset) switch
        {
            (EffectClass.Emphasis, 26) => EffectKind.Pulse,
            (EffectClass.Emphasis, _) => (EffectKind?)null,
            (_, 1) => EffectKind.Appear,
            (_, 10) => EffectKind.Fade,
            (_, 2) => EffectKind.Fly,
            (_, 22) => EffectKind.Wipe,
            _ => null,
        };
        if (kind is null)
        {
            this.notes.Unsupported(effectClass switch
            {
                EffectClass.Entrance => $"the entrance effect {preset} (shown as appear)",
                EffectClass.Exit => $"the exit effect {preset} (shown as disappear)",
                _ => $"the emphasis effect {preset} (shown as a pulse)",
            });
            kind = effectClass == EffectClass.Emphasis ? EffectKind.Pulse : EffectKind.Appear;
        }

        (int, int)? paragraphs = target.Element(Ns.P + "txEl")?.Element(Ns.P + "pRg") is { } range
            ? (Integer(range, "st"), Integer(range, "end"))
            : null;
        var direction = kind is EffectKind.Fly or EffectKind.Wipe ? Side(subtype) : null;
        var duration = node.Descendants(Ns.P + "cTn").Select(c => Integer(c, "dur")).DefaultIfEmpty(0).Max();
        if (kind == EffectKind.Appear)
        {
            duration = 0;
        }
        else if (duration <= 1)
        {
            duration = 500;
        }

        return new SceneEffect(id, paragraphs, effectClass, kind.Value, direction, duration);
    }

    // PowerPoint's preset subtypes for fly and wipe: the side the effect comes from.
    private static string Side(int subtype) => subtype switch
    {
        1 => "t",
        2 => "r",
        3 => "tr",
        6 => "br",
        8 => "l",
        9 => "tl",
        12 => "bl",
        _ => "b",
    };

    private static int Integer(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>
    /// The slide's <c>p:transition</c>, or in <c>mc:AlternateContent</c> the first choice with a transition the reader
    /// knows, else the fallback. A transition it does not know plays as a fade and is reported.
    /// </summary>
    private SceneTransition? Transition(XElement slide)
    {
        foreach (var element in slide.Elements())
        {
            if (element.Name == Ns.P + "transition")
            {
                return this.TransitionOf(element);
            }

            if (element.Name == Ns.Mc + "AlternateContent" && element.Descendants(Ns.P + "transition").Any())
            {
                var choices = element.Elements(Ns.Mc + "Choice").Concat(element.Elements(Ns.Mc + "Fallback")).ToList();
                for (var i = 0; i < choices.Count; i++)
                {
                    if (choices[i].Element(Ns.P + "transition") is { } transition
                        && (i == choices.Count - 1 || Known(transition)))
                    {
                        return this.TransitionOf(transition);
                    }
                }
            }
        }

        return null;
    }

    private static XElement? TransitionEffect(XElement transition) =>
        transition.Elements().FirstOrDefault(e => e.Name.LocalName is not ("sndAc" or "extLst"));

    private static bool Known(XElement transition) => TransitionEffect(transition) is not { } effect
        || (effect.Name.Namespace == Ns.P && Transitions.Contains(effect.Name.LocalName))
        || effect.Name == P159 + "morph";

    private SceneTransition? TransitionOf(XElement transition)
    {
        var duration = transition.Attribute(P14 + "dur") is { } dur && int.TryParse(dur.Value, CultureInfo.InvariantCulture, out var ms)
            ? ms
            : transition.Attribute("spd")?.Value switch
            {
                "slow" => 1000,
                "med" => 750,
                _ => 500,
            };
        if (TransitionEffect(transition) is not { } effect)
        {
            return null;
        }

        if (!Known(transition))
        {
            this.notes.Unsupported($"the transition {effect.Name.LocalName} (shown as a fade)");
            return new SceneTransition("fade", null, duration);
        }

        var kind = effect.Name.LocalName switch
        {
            "pull" => "uncover",
            var name => name,
        };
        var direction = kind switch
        {
            "push" or "wipe" or "cover" or "uncover" => effect.Attribute("dir")?.Value ?? "l",
            "split" => $"{effect.Attribute("orient")?.Value ?? "horz"}-{effect.Attribute("dir")?.Value ?? "out"}",
            "zoom" => effect.Attribute("dir")?.Value ?? "in",
            _ => null,
        };
        return new SceneTransition(kind, direction, kind == "cut" ? 0 : duration);
    }
}
