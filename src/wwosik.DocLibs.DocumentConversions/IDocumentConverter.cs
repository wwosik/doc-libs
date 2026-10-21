namespace wwosik.DocLibs.DocumentConversions;

/// <summary>Converts documents of one or more formats into Markdown.</summary>
public interface IDocumentConverter
{
    /// <summary>Whether this converter handles <paramref name="format"/>.</summary>
    bool CanConvert(DocumentFormat format);

    /// <summary>Converts <paramref name="input"/>, read from its current position, into Markdown.</summary>
    /// <exception cref="DocumentConversionException">The document could not be converted.</exception>
    MarkdownDocument Convert(Stream input, ConversionOptions options);
}

/// <summary>A document could not be converted.</summary>
public sealed class DocumentConversionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public DocumentConversionException(DocumentFormat format, string reason, Exception? innerException = null)
        : base($"{format}: {reason}", innerException)
    {
        this.Format = format;
        this.Reason = reason;
    }

    /// <summary>The format of the document that failed.</summary>
    public DocumentFormat Format { get; }

    /// <summary>Why it failed.</summary>
    public string Reason { get; }
}
