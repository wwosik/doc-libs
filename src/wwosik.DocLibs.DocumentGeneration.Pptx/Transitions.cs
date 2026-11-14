using DocumentFormat.OpenXml;
using P = DocumentFormat.OpenXml.Presentation;

namespace wwosik.DocLibs.DocumentGeneration.Pptx;

/// <summary>Writes a slide's <c>p:transition</c>, with PowerPoint's default direction for each.</summary>
internal static class Transitions
{
    private const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string PowerPoint2015 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";
    private const string PresentationML = "http://schemas.openxmlformats.org/presentationml/2006/main";

    /// <summary>The value of a name in <see cref="LayoutSyntax.Transitions"/>.</summary>
    public static SlideTransition Parse(string name) => Enum.Parse<SlideTransition>(name, ignoreCase: true);

    /// <summary>
    /// Adds the transition after <c>p:clrMapOvr</c>, as the schema orders it; <see cref="SlideTransition.None"/> adds
    /// nothing. Morph is written for PowerPoint 2019 and later, with a fade for viewers without it.
    /// </summary>
    public static void Write(P.Slide slide, SlideTransition transition)
    {
        if (transition == SlideTransition.None)
        {
            return;
        }

        OpenXmlElement element = transition switch
        {
            SlideTransition.Morph => new AlternateContent(
                $"<mc:AlternateContent xmlns:mc=\"{MarkupCompatibility}\" xmlns:p=\"{PresentationML}\">"
                + $"<mc:Choice xmlns:p159=\"{PowerPoint2015}\" Requires=\"p159\"><p:transition spd=\"slow\"><p159:morph option=\"byObject\"/></p:transition></mc:Choice>"
                + "<mc:Fallback><p:transition spd=\"slow\"><p:fade/></p:transition></mc:Fallback>"
                + "</mc:AlternateContent>"),
            _ => new P.Transition(Effect(transition)) { Speed = Speed(transition) },
        };

        slide.InsertAfter(element, slide.ColorMapOverride);
    }

    private static OpenXmlElement Effect(SlideTransition transition) => transition switch
    {
        SlideTransition.Fade => new P.FadeTransition(),
        SlideTransition.Push => new P.PushTransition(),
        SlideTransition.Wipe => new P.WipeTransition(),
        SlideTransition.Split => new P.SplitTransition(),
        SlideTransition.Cover => new P.CoverTransition(),
        SlideTransition.Uncover => new P.PullTransition(),
        SlideTransition.Zoom => new P.ZoomTransition(),
        _ => new P.CutTransition(),
    };

    // PowerPoint's default durations, to the nearest of the three speeds: fast 0.5 s, medium 0.75 s, slow 1 s.
    private static P.TransitionSpeedValues Speed(SlideTransition transition) => transition switch
    {
        SlideTransition.Fade or SlideTransition.Zoom => P.TransitionSpeedValues.Medium,
        SlideTransition.Cut => P.TransitionSpeedValues.Fast,
        _ => P.TransitionSpeedValues.Slow,
    };
}
