using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using wwosik.DocLibs.DocumentGeneration.Pptx.Rendering;

namespace wwosik.DocLibs.DocumentGeneration.Pptx.Tests;

/// <summary>
/// Presenting the HTML deck (E07-S06): the reader's speaker notes, build steps and transitions, and what the page
/// carries of them. The script's keys, effects and window sync are checked in browsers, not here.
/// </summary>
public partial class HtmlDeckPresentingTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static byte[] Presenting() =>
        Samples.Generate(Samples.Markdown(File.ReadAllText(Samples.Path("presenting.md")).ReplaceLineEndings("\n")));

    private static DeckScene Read(byte[] pptx, out GenerationResult result)
    {
        var (deck, notes) = DeckReader.Read(new MemoryStream(pptx));
        result = notes.Result(deck.Slides.Count);
        return deck;
    }

    private static SlideScene Slide(DeckScene deck, string title, int which = 0) => deck.Slides.Where(s => s.Title == title).ElementAt(which);

    private static (string Html, GenerationResult Result) Html(byte[] pptx, HtmlDeckOptions? options = null)
    {
        using var output = new MemoryStream();
        var result = HtmlDeck.Write(new MemoryStream(pptx), output, options);
        return (Encoding.UTF8.GetString(output.ToArray()), result);
    }

    private static string Text(NoteParagraph paragraph) => string.Concat(paragraph.Runs.Select(r => r.Text));

    [Fact]
    public void SpeakerNotes_AreReadWithFormattingLinksAndLists()
    {
        var deck = Read(Presenting(), out _);
        var notes = Slide(deck, "Where we stand").Notes;

        Assert.Equal(
            [
                (0, NoteList.None, "Keep this short: the audience knows the old planner."),
                (0, NoteList.Number, "Name the two changes"),
                (0, NoteList.Number, "Point to the project page"),
                (1, NoteList.Bullet, "only if someone asks"),
                (0, NoteList.Bullet, "Mention the planner.json format"),
                (0, NoteList.None, "Then move on."),
            ],
            notes.Select(p => (p.Level, p.List, Text(p))));
        Assert.Contains(notes[1].Runs, r => r is { Text: "two", Italic: true, Bold: false });
        Assert.Contains(notes[2].Runs, r => r is { Text: "project page", Link: "https://example.com/harbour" });
        Assert.Contains(Slide(deck, "Presenting the harbour plan").Notes[0].Runs, r => r is { Text: "three", Bold: true });

        // Split slides repeat the notes; a slide without notes has none.
        Assert.Equal(Slide(deck, "Every stop on the route").Notes, Slide(deck, "Every stop on the route", 1).Notes);
        Assert.Empty(Slide(deck, "Questions first").Notes);
    }

    [Fact]
    public void Transitions_AreReadWithKindDirectionAndSpeed()
    {
        var deck = Read(Presenting(), out var result);

        Assert.Equal(
            [
                ("Presenting the harbour plan", "fade", null, 750),
                ("Where we stand", "push", "l", 1000),
                ("Every stop on the route", "wipe", "l", 1000),
                ("Every stop on the route", "wipe", "l", 1000),
                ("The numbers", "split", "horz-out", 1000),
                ("The numbers", "split", "horz-out", 1000),
                ("What changes", "cover", "l", 1000),
                ("Questions first", "uncover", "l", 1000),
                ("Timeline", "zoom", "in", 750),
                ("Straight to the demo", "cut", null, 0),
                ("The same shapes, moved", "morph", null, 1000),
            ],
            deck.Slides.Take(11).Select(s => (s.Title, s.Transition!.Kind, s.Transition.Direction, s.Transition.Duration)));
        Assert.Null(Slide(deck, "No transition here").Transition);
        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
    }

    [Fact]
    public void AlternateContent_TakesTheFirstKnownChoice_ElseTheFallback()
    {
        var p14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
        var pptx = Samples.Generate(Samples.Markdown("## One\n\nA\n\n## Two\n\nB\n\n## Three\n\nC\n"));
        pptx = HtmlDeckTests.Edit(pptx, (slides, _) =>
        {
            // An unknown choice first, then Morph: Morph is taken.
            slides[0].Root!.Add(XElement.Parse(
                $"<mc:AlternateContent xmlns:mc='{Mc}' xmlns:p='{P}' xmlns:p14='{p14}' xmlns:p159='http://schemas.microsoft.com/office/powerpoint/2015/09/main'>"
                + "<mc:Choice Requires='p14'><p:transition spd='slow' p14:dur='1600'><p14:vortex dir='r'/></p:transition></mc:Choice>"
                + "<mc:Choice Requires='p159'><p:transition spd='fast'><p159:morph option='byObject'/></p:transition></mc:Choice>"
                + "<mc:Fallback><p:transition spd='slow'><p:fade/></p:transition></mc:Fallback></mc:AlternateContent>"));

            // Only an unknown choice: the fallback, with p14:dur.
            slides[1].Root!.Add(XElement.Parse(
                $"<mc:AlternateContent xmlns:mc='{Mc}' xmlns:p='{P}' xmlns:p14='{p14}'>"
                + "<mc:Choice Requires='p14'><p:transition spd='slow' p14:dur='1600'><p14:vortex dir='r'/></p:transition></mc:Choice>"
                + "<mc:Fallback><p:transition spd='slow' p14:dur='1200'><p:push dir='u'/></p:transition></mc:Fallback></mc:AlternateContent>"));

            // A transition the reader does not know: a fade, and a note.
            slides[2].Root!.Add(XElement.Parse($"<p:transition xmlns:p='{P}' spd='med'><p:randomBar dir='vert'/></p:transition>"));
        });
        var deck = Read(pptx, out var result);

        Assert.Equal(new SceneTransition("morph", null, 500), deck.Slides[0].Transition);
        Assert.Equal(new SceneTransition("push", "u", 1200), deck.Slides[1].Transition);
        Assert.Equal(new SceneTransition("fade", null, 750), deck.Slides[2].Transition);
        var note = Assert.Single(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
        Assert.Contains("randomBar", note.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_BecomeSteps_InTheOrderOfTheClicks()
    {
        var deck = Read(Presenting(), out _);
        var plan = Slide(deck, "Building the plan");
        var body = plan.Steps[0].Effects[0].Id;

        // Items one by one (the second with its sub-item), the flying line, a line in and out, the pulse.
        Assert.Equal(
            [
                [new SceneEffect(body, (0, 0), EffectClass.Entrance, EffectKind.Appear, null, 0)],
                [new SceneEffect(body, (1, 2), EffectClass.Entrance, EffectKind.Appear, null, 0)],
                [new SceneEffect(body, (3, 3), EffectClass.Entrance, EffectKind.Appear, null, 0)],
                [new SceneEffect(body, (4, 4), EffectClass.Entrance, EffectKind.Fly, "b", 500)],
                [new SceneEffect(body, (5, 5), EffectClass.Entrance, EffectKind.Appear, null, 0)],
                [new SceneEffect(body, (5, 5), EffectClass.Exit, EffectKind.Appear, null, 0)],
                [new SceneEffect(body, (6, 6), EffectClass.Emphasis, EffectKind.Pulse, null, 500)],
            ],
            plan.Steps.Select(s => s.Effects.ToArray()));

        // Wipes from the left; several shapes on one click (a card is its box and its text); a whole shape leaving.
        Assert.All(Slide(deck, "Every berth, one by one").Steps, s => Assert.Equal((EffectKind.Wipe, "l"), (s.Effects.Single().Kind, s.Effects.Single().Direction)));
        var cards = Slide(deck, "Who we talked to").Steps;
        Assert.Equal([3, 3, 3, 1, 3], cards.Select(s => s.Effects.Count));
        Assert.All(cards[0].Effects, e => Assert.Null(e.Paragraphs));
        var after = Slide(deck, "Before and after").Steps;
        Assert.Equal(EffectClass.Exit, after[^1].Effects.Single().Class);
        Assert.Null(after[^1].Effects.Single().Paragraphs);

        Assert.Empty(Slide(deck, "Where we stand").Steps);
    }

    [Fact]
    public void UnknownEffects_PlayAsAppearOrPulse_AndAreReported()
    {
        var deck = Read(Presenting(), out _);
        var plan = deck.Slides.ToList().FindIndex(s => s.Title == "Building the plan");
        var pptx = HtmlDeckTests.Edit(Presenting(), (slides, _) =>
        {
            var effects = slides[plan].Descendants(P + "cTn").Where(c => c.Attribute("presetClass") is not null).ToList();
            effects[0].SetAttributeValue("presetID", "53"); // Grow & Turn
            effects[^1].SetAttributeValue("presetID", "8"); // Spin
        });
        var edited = Read(pptx, out var result);
        var steps = edited.Slides[plan].Steps;

        Assert.Equal((EffectClass.Entrance, EffectKind.Appear), (steps[0].Effects[0].Class, steps[0].Effects[0].Kind));
        Assert.Equal((EffectClass.Emphasis, EffectKind.Pulse), (steps[^1].Effects[0].Class, steps[^1].Effects[0].Kind));
        Assert.Equal(2, result.Notes.Count(n => n.Code == NoteCodes.DrawingUnsupported));
    }

    [Fact]
    public void ThePage_CarriesStepsTransitionsAndNotes_ForTheScript()
    {
        var (html, result) = Html(Presenting());

        Assert.DoesNotContain(result.Notes, n => n.Code == NoteCodes.DrawingUnsupported);
        Assert.Contains("data-transition=\"push\" data-direction=\"l\" data-duration=\"1000\" aria-label=\"Where we stand\"", html, StringComparison.Ordinal);
        Assert.Contains("data-transition=\"morph\" data-duration=\"1000\"", html, StringComparison.Ordinal);
        Assert.Contains("<aside class=\"notes\" aria-label=\"Speaker notes\"><p>Keep this short: the audience knows the old planner.</p><ol><li>Name the <em>two</em> changes</li><li>Point to the <a href=\"https://example.com/harbour\" target=\"_blank\" rel=\"noopener noreferrer\">project page</a><ul><li>only if someone asks</li></ul></li></ol><ul><li>Mention the planner.json format</li></ul><p>Then move on.</p></aside>", html, StringComparison.Ordinal);
        Assert.Contains("@page withnotes { size: 960pt 1357.632pt; margin: 0; }", html, StringComparison.Ordinal);
        Assert.Contains("<kbd>N</kbd></td><td>notes view", html, StringComparison.Ordinal);
        Assert.Contains("<kbd>P</kbd></td><td>presenter window", html, StringComparison.Ordinal);

        // Every effect's item is in the slide's own group, every paragraph in that item.
        var sections = Regex.Matches(html, "<section [^>]*data-steps=\"([^\"]*)\"[^>]*>(.*?)</section>", RegexOptions.Singleline);
        Assert.Equal(6, sections.Count);
        foreach (Match section in sections)
        {
            var own = section.Groups[2].Value[section.Groups[2].Value.IndexOf("<g class=\"own\">", StringComparison.Ordinal)..];
            using var steps = JsonDocument.Parse(System.Net.WebUtility.HtmlDecode(section.Groups[1].Value));
            foreach (var effect in steps.RootElement.EnumerateArray().SelectMany(s => s.EnumerateArray()))
            {
                var id = effect.GetProperty("id").GetInt32();
                var item = own.IndexOf($"<g data-id=\"{id}\"", StringComparison.Ordinal);
                Assert.True(item >= 0, $"item {id} is not in the slide's own group");
                if (effect.TryGetProperty("p", out var range))
                {
                    for (var p = range[0].GetInt32(); p <= range[1].GetInt32(); p++)
                    {
                        Assert.Contains($"<g data-p=\"{p}\">", own[item..], StringComparison.Ordinal);
                    }
                }
            }
        }
    }

    [Fact]
    public void ADeckWithoutBuildsTransitionsOrNotes_HasNoneOfThem()
    {
        var (html, _) = Html(Samples.Generate(Samples.Deck()));

        Assert.DoesNotContain("data-steps=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-transition=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<aside", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-notes=", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesOff_LeavesTheNotesOutOfTheFile()
    {
        var (html, _) = Html(Presenting(), new HtmlDeckOptions { Notes = false });

        Assert.Contains("<main class=\"deck\" id=\"deck\" data-notes=\"off\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<aside", html, StringComparison.Ordinal);
        Assert.DoesNotContain("audience knows", html, StringComparison.Ordinal);
        Assert.DoesNotContain("say the totals", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesText_StaysText()
    {
        var pptx = Samples.Generate(Samples.Markdown("## Slide\n\nText\n\n::: notes\nSay `</script><img src=x onerror=alert(1)>` & [go](javascript:alert(1)) or [mail](mailto:ann@example.com).\n:::\n"));
        var (html, _) = Html(pptx);
        var notes = Regex.Match(html, "<aside class=\"notes\".*?</aside>").Value;

        Assert.Single(Regex.Matches(html, "<script"));
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/script&gt;&lt;img src=x onerror=alert(1)&gt;", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"mailto:ann@example.com\"", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSlidePreviews_AreUnchanged_ByTheOwnGroup()
    {
        // The group of the slide's own items is the page's only; the painting SVG stays as E07-S03 paints it.
        var markdown = Samples.Markdown(File.ReadAllText(Samples.Path("presenting.md")).ReplaceLineEndings("\n"));
        var deck = Read(Samples.Generate(markdown, new PptxOptions { Template = Samples.BuiltIn("sample") }), out _);
        var slide = Slide(deck, "Building the plan");
        var html = SceneSvg.Write(deck, slide, SvgTarget.Html);

        Assert.DoesNotContain("class=\"own\"", SceneSvg.Write(deck, slide, SvgTarget.Painting), StringComparison.Ordinal);
        Assert.True(slide.Decorations > 0);
        Assert.Contains("<g class=\"own\">", html, StringComparison.Ordinal);
        Assert.Equal(slide.Decorations, Regex.Matches(html[..html.IndexOf("<g class=\"own\">", StringComparison.Ordinal)], "<g data-id=").Count);
    }
}
