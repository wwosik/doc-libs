using System.Text;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>Escaping for the XML parts, written as text.</summary>
internal static class XlsxXml
{
    /// <summary>Element text: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> escaped; characters XML 1.0 cannot hold left out.</summary>
    public static string Text(string text)
    {
        var xml = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&':
                    xml.Append("&amp;");
                    break;
                case '<':
                    xml.Append("&lt;");
                    break;
                case '>':
                    xml.Append("&gt;");
                    break;
                case '\t' or '\n':
                    xml.Append(c);
                    break;
                case '\r':
                    break;
                case < ' ' or '￾' or '￿':
                    break;
                default:
                    xml.Append(c);
                    break;
            }
        }

        return xml.ToString();
    }

    /// <summary>An attribute value in double quotes: as <see cref="Text"/>, and <c>"</c>, tabs and line breaks escaped.</summary>
    public static string Attribute(string text) =>
        Text(text).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\t", "&#9;", StringComparison.Ordinal).Replace("\n", "&#10;", StringComparison.Ordinal);
}
