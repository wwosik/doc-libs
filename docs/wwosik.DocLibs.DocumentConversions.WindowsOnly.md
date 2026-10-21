# wwosik.DocLibs.DocumentConversions.WindowsOnly

Converts `.doc`, `.rtf`, `.xls` and `.xlsx` into Markdown through Office automation. It needs
Windows with Word and Excel installed, and depends on
[wwosik.DocLibs.DocumentConversions](wwosik.DocLibs.DocumentConversions.md).

```csharp
using wwosik.DocLibs.DocumentConversions;
using wwosik.DocLibs.DocumentConversions.WindowsOnly;

using var office = new OfficeConverter();
var registry = DocumentConverterRegistry.CreateDefault(office);
using var input = File.OpenRead("old.doc");
var document = registry.Convert(input, "old.doc");
```

## How it works

- **`.doc` and `.rtf`:** Word opens the file and saves it as `.docx`, and `DocxConverter` converts
  that. The result therefore looks like a `.docx` conversion, images included.
- **`.xls` and `.xlsx`:** Excel reads the cell values of each worksheet's used range. Each sheet
  becomes `## <sheet name>` with a table whose first row is the header. Empty rows and columns
  are dropped, and an empty sheet shows `*(empty)*`. Numbers use the invariant culture, booleans
  read `TRUE`/`FALSE`, and error values `#ERROR`. Formatting, formulas, charts and images are not
  read.
- **Metadata:** title and author, plus the sheet count for workbooks.

## Things to know

- **Files on disk.** Office only opens files from a path, so each call writes the input into a
  fresh folder below `ConversionOptions.WorkingDirectory`, then deletes it. The default is
  `%TEMP%\wwosik.DocLibs.DocumentConversions`. If Office still holds a lock, the folder is left
  behind rather than failing the call.
- **The file name matters for OLE files.** Their content does not tell `.doc` from `.xls`, so
  pass the file name to the registry or set `ConversionOptions.FileName`. Without it, an OLE
  file throws `DocumentConversionException`.
- **One instance, one conversion at a time.** Word and Excel start on first use and keep running
  until `Dispose`. Calls to an instance are serialized, so it is safe to share, but not parallel.
  If Office crashed in between, the next call restarts it once.
- **Safeguards:** Office runs invisible, with no alerts and macros forced off. Password-protected
  files fail with an error instead of prompting.
- **Platform.** The package builds everywhere, but `new OfficeConverter()` throws
  `PlatformNotSupportedException` off Windows. Converting throws `NotSupportedException` when
  Word or Excel is missing.
- **Desktop only.** Microsoft does not support Office automation in unattended or server
  processes (services, ASP.NET, scheduled tasks without a user session). Use this package on an
  interactive desktop.
- **Not Native AOT compatible:** late-bound COM needs the built-in COM interop.
