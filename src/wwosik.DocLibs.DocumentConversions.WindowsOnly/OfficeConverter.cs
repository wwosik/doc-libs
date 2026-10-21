using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using wwosik.DocLibs.DocumentConversions.OpenXml;

namespace wwosik.DocLibs.DocumentConversions.WindowsOnly;

/// <summary>
/// Converts <c>.doc</c> and <c>.rtf</c> (Word saves them as <c>.docx</c>, then <see cref="DocxConverter"/>
/// converts that) and <c>.xls</c> and <c>.xlsx</c> (Excel reads the cell values, one table per worksheet)
/// through Office automation.
/// </summary>
/// <remarks>
/// Word and Excel start on first use and keep running until <see cref="Dispose"/>. Calls are
/// serialized: one conversion at a time per instance. Office only opens files from a path, so every
/// call writes the input into a fresh working directory below <see cref="ConversionOptions.WorkingDirectory"/>
/// and deletes it afterwards. Microsoft does not support Office automation in unattended or server
/// processes; use this on an interactive desktop only.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OfficeConverter : IDocumentConverter, IDisposable
{
    private const int WordFormatDocumentDefault = 16;
    private const int WordAlertsNone = 0;
    private const int WordDoNotSaveChanges = 0;
    private const int AutomationSecurityForceDisable = 3;

    // A dummy password makes protected files fail fast instead of prompting.
    private const string NoPassword = "doclibs-no-password";

    private static readonly object Missing = System.Reflection.Missing.Value;

    private readonly Lock gate = new();
    private readonly DocxConverter docx = new();
    private object? word;
    private object? excel;
    private bool disposed;

    /// <summary>Creates the converter. Office starts only when the first document is converted.</summary>
    /// <exception cref="PlatformNotSupportedException">Not running on Windows.</exception>
    public OfficeConverter()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Office automation is only available on Windows.");
        }
    }

    /// <inheritdoc/>
    public bool CanConvert(DocumentFormat format) =>
        format is DocumentFormat.Doc or DocumentFormat.Rtf or DocumentFormat.Xls or DocumentFormat.Xlsx;

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Word or Excel, whichever the format needs, is not installed.</exception>
    public MarkdownDocument Convert(Stream input, ConversionOptions options)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);

        var format = DocumentFormatDetector.Detect(input, options.FileName);
        if (format == DocumentFormat.Ole)
        {
            throw new DocumentConversionException(format, "An OLE file needs its file name to tell .doc from .xls; set ConversionOptions.FileName.");
        }

        if (!this.CanConvert(format))
        {
            throw new DocumentConversionException(format, "This converter handles .doc, .rtf, .xls and .xlsx only.");
        }

        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            var directory = Path.Combine(
                options.WorkingDirectory ?? Path.Combine(Path.GetTempPath(), "wwosik.DocLibs.DocumentConversions"),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var source = Path.Combine(directory, "input" + Extension(format));
                using (var file = File.Create(source))
                {
                    input.CopyTo(file);
                }

                return format is DocumentFormat.Doc or DocumentFormat.Rtf
                    ? this.ConvertWithWord(format, source, Path.Combine(directory, "output.docx"), options)
                    : this.ConvertWithExcel(format, source);
            }
            catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidComObjectException)
            {
                throw new DocumentConversionException(format, "Office could not convert the document: " + Unwrap(ex).Message, ex);
            }
            finally
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    /// <summary>Quits Word and Excel if they were started.</summary>
    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            Quit(ref this.word, WordDoNotSaveChanges);
            Quit(ref this.excel);
        }
    }

    private MarkdownDocument ConvertWithWord(DocumentFormat format, string source, string target, ConversionOptions options)
    {
        this.Retry(() =>
        {
            var documents = Get(this.Word, "Documents")!;
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, PasswordDocument, PasswordTemplate,
            //      Revert, WritePasswordDocument, WritePasswordTemplate, Format, Encoding, Visible, OpenAndRepair,
            //      DocumentDirection, NoEncodingDialog)
            var document = Call(documents, "Open", source, false, true, false, NoPassword, NoPassword,
                false, Missing, Missing, Missing, Missing, false, false, Missing, true)!;
            try
            {
                Call(document, "SaveAs2", target, WordFormatDocumentDefault);
            }
            finally
            {
                TryCall(document, "Close", WordDoNotSaveChanges);
            }

            return 0;
        }, () => Quit(ref this.word, WordDoNotSaveChanges));

        using var docxStream = File.OpenRead(target);
        try
        {
            return this.docx.Convert(docxStream, options);
        }
        catch (DocumentConversionException ex)
        {
            throw new DocumentConversionException(format, ex.Reason, ex);
        }
    }

    private MarkdownDocument ConvertWithExcel(DocumentFormat format, string source) => this.Retry(() =>
    {
        var workbooks = Get(this.Excel, "Workbooks")!;
        // Open(Filename, UpdateLinks, ReadOnly, Format, Password, WriteResPassword, IgnoreReadOnlyRecommended)
        var workbook = Call(workbooks, "Open", source, 0, true, Missing, NoPassword, Missing, true)!;
        try
        {
            var builder = new StringBuilder();
            var sheets = Get(workbook, "Worksheets")!;
            var count = System.Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                var sheet = Get(sheets, "Item", index)!;
                var name = Get(sheet, "Name") as string ?? "Sheet " + index.ToString(CultureInfo.InvariantCulture);
                var values = Get(Get(sheet, "UsedRange")!, "Value2");
                builder.Append("## ").Append(MarkdownText.Escape(name)).Append("\n\n");
                var grid = MarkdownText.Compact(ToGrid(values));
                builder.Append(grid.Count == 0 ? "*(empty)*\n\n" : MarkdownText.Table(grid));
            }

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MetadataKeys.SheetCount] = count.ToString(CultureInfo.InvariantCulture),
            };
            AddBuiltInProperty(workbook, "Title", MetadataKeys.Title, metadata);
            AddBuiltInProperty(workbook, "Author", MetadataKeys.Author, metadata);
            return new MarkdownDocument(MarkdownText.Normalize(builder.ToString()), [], metadata);
        }
        finally
        {
            TryCall(workbook, "Close", false);
        }
    }, () => Quit(ref this.excel));

    private object Word
    {
        get
        {
            if (this.word is null)
            {
                var application = Start("Word.Application");
                TrySet(application, "Visible", false);
                TrySet(application, "DisplayAlerts", WordAlertsNone);
                TrySet(application, "AutomationSecurity", AutomationSecurityForceDisable);
                TrySet(application, "ScreenUpdating", false);
                this.word = application;
            }

            return this.word;
        }
    }

    private object Excel
    {
        get
        {
            if (this.excel is null)
            {
                var application = Start("Excel.Application");
                TrySet(application, "Visible", false);
                TrySet(application, "DisplayAlerts", false);
                TrySet(application, "AutomationSecurity", AutomationSecurityForceDisable);
                TrySet(application, "AskToUpdateLinks", false);
                TrySet(application, "EnableEvents", false);
                this.excel = application;
            }

            return this.excel;
        }
    }

    private static object Start(string progId)
    {
        var type = Type.GetTypeFromProgID(progId) ?? throw new NotSupportedException($"{progId} is not installed.");
        return Activator.CreateInstance(type)!;
    }

    // One restart when Office crashed or was closed between calls.
    private T Retry<T>(Func<T> action, Action reset)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (IsServerGone(ex))
        {
            reset();
            return action();
        }
    }

    private static bool IsServerGone(Exception ex)
    {
        var inner = Unwrap(ex);
        return inner is InvalidComObjectException
            || (inner is COMException com && (uint)com.HResult is 0x800706BA or 0x800706BE or 0x80010108);
    }

    private static List<IReadOnlyList<string>> ToGrid(object? values)
    {
        var grid = new List<IReadOnlyList<string>>();
        if (values is object[,] cells)
        {
            for (var r = cells.GetLowerBound(0); r <= cells.GetUpperBound(0); r++)
            {
                var row = new List<string>();
                for (var c = cells.GetLowerBound(1); c <= cells.GetUpperBound(1); c++)
                {
                    row.Add(CellText(cells[r, c]));
                }

                grid.Add(row);
            }
        }
        else if (values is not null)
        {
            grid.Add([CellText(values)]);
        }

        return grid;
    }

    private static string CellText(object? value) => value switch
    {
        null => string.Empty,
        double d => d.ToString("G15", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        int => "#ERROR", // Excel delivers error values (CVErr) as Int32.
        _ => MarkdownText.Escape(System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static void AddBuiltInProperty(object workbook, string name, string key, Dictionary<string, string> metadata)
    {
        try
        {
            var properties = Get(workbook, "BuiltinDocumentProperties")!;
            if (Get(Get(properties, "Item", name)!, "Value") is string { Length: > 0 } value && value.Trim().Length > 0)
            {
                metadata[key] = value.Trim();
            }
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException)
        {
            // Unset built-in properties throw instead of returning nothing.
        }
    }

    private static string Extension(DocumentFormat format) => format switch
    {
        DocumentFormat.Doc => ".doc",
        DocumentFormat.Rtf => ".rtf",
        DocumentFormat.Xls => ".xls",
        _ => ".xlsx",
    };

    // No culture argument: Excel rejects calls made with the invariant culture's LCID (TYPE_E_INVDATAREAD).
    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, args);

    private static void TrySet(object target, string name, object? value)
    {
        try
        {
            target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException)
        {
            // Older Office versions lack some of these settings.
        }
    }

    private static void TryCall(object target, string name, params object?[] args)
    {
        try
        {
            Call(target, name, args);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidComObjectException)
        {
            // Closing a document that Office already dropped.
        }
    }

    private static void Quit(ref object? application, params object?[] args)
    {
        if (application is null)
        {
            return;
        }

        TryCall(application, "Quit", args);
        try
        {
            Marshal.FinalReleaseComObject(application);
        }
        catch (ArgumentException)
        {
            // Not a COM object any more.
        }

        application = null;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException or AggregateException && ex.InnerException is not null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Office may still hold a lock; the folder is left behind rather than failing the call.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
