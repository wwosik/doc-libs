using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace wwosik.DocLibs.DocumentGeneration.Xlsx;

/// <summary>
/// One formula of a workbook, parsed by Excel's grammar: numbers, strings, booleans, errors, A1 references and ranges,
/// <c>[Header]</c> and <c>{row}</c>, operators by precedence, <c>%</c>, unary signs, function calls with their argument
/// counts, and array constants. It never throws: a formula that does not parse has an <see cref="Error"/> with its
/// position. <see cref="Write"/> gives the formula as the file stores it, for one row.
/// </summary>
internal sealed partial class XlsxFormula
{
    /// <summary>The most characters Excel holds in a formula.</summary>
    public const int MaxLength = 8_192;

    // Excel nests functions 64 deep.
    private const int MaxDepth = 64;

    private static readonly string[] Errors = ["#NULL!", "#DIV/0!", "#VALUE!", "#REF!", "#NAME?", "#NUM!", "#N/A", "#GETTING_DATA", "#SPILL!", "#CALC!"];

    private readonly List<Token> tokens = [];
    private readonly List<(string Name, int Position)> headers = [];
    private readonly List<(string Name, int Position)> unknown = [];
    private readonly List<(string Name, int Position)> names = [];
    private readonly List<(int Column, int? Row, int Position)> cells = [];

    private XlsxFormula(string source) => this.Source = source;

    /// <summary>The formula as given, without its leading <c>=</c>.</summary>
    public string Source { get; }

    /// <summary>Why the formula does not parse, and where (from 1), or <see langword="null"/>.</summary>
    public (string Message, int Position)? Error { get; private set; }

    /// <summary>Every <c>[Header]</c>, unescaped and trimmed, with its position from 1.</summary>
    public IReadOnlyList<(string Name, int Position)> Headers => this.headers;

    /// <summary>Every function the table does not know, as written in upper case, with its position.</summary>
    public IReadOnlyList<(string Name, int Position)> UnknownFunctions => this.unknown;

    /// <summary>Every bare name outside <c>LET</c> and <c>LAMBDA</c>, which the workbook does not define.</summary>
    public IReadOnlyList<(string Name, int Position)> Names => this.names;

    /// <summary>Every single-cell A1 reference: its column from 0 and its row, <see langword="null"/> for <c>{row}</c>.</summary>
    public IReadOnlyList<(int Column, int? Row, int Position)> Cells => this.cells;

    /// <summary>Parses <paramref name="source"/>, a formula without its leading <c>=</c>.</summary>
    public static XlsxFormula Parse(string source)
    {
        var formula = new XlsxFormula(source);
        try
        {
            formula.Lex();
            new Parser(formula).Run();
        }
        catch (FormulaException ex)
        {
            formula.Error = (ex.Message, ex.Position + 1);
        }

        return formula;
    }

    /// <summary>
    /// The formula for row <paramref name="row"/>, as the file stores it (<paramref name="prefixes"/>: with <c>_xlfn.</c>
    /// where a function needs it) or as Excel shows it: <c>[Header]</c> as the cell of that column in the row,
    /// <c>{row}</c> as the row's number, function names in upper case.
    /// </summary>
    public string Write(int row, Func<string, string?> column, bool prefixes = true)
    {
        var text = new StringBuilder(this.Source.Length + 16);
        var number = row.ToString(CultureInfo.InvariantCulture);
        foreach (var token in this.tokens)
        {
            switch (token.Kind)
            {
                case Kind.Function:
                    text.Append(prefixes ? token.Prefix : string.Empty).Append(token.Value);
                    break;
                case Kind.Parameter:
                    text.Append(prefixes ? "_xlpm." : string.Empty).Append(token.Value);
                    break;
                case Kind.Header:
                    if (column(token.Value) is { } letter)
                    {
                        text.Append(letter).Append(number);
                    }
                    else
                    {
                        text.Append(token.Text);
                    }

                    break;
                case Kind.RowNumber:
                    text.Append(number);
                    break;
                case Kind.Cell when token.Row is null:
                    text.Append(token.Value).Append(number);
                    break;
                case Kind.Bool:
                    text.Append(token.Value);
                    break;
                default:
                    text.Append(token.Text);
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>The closest of <paramref name="candidates"/> to <paramref name="name"/>, at most three, closest first.</summary>
    public static IReadOnlyList<string> Closest(string name, IEnumerable<string> candidates) => [.. candidates
        .Select(c => (Name: c, Distance: Distance(name.ToUpperInvariant(), c.ToUpperInvariant())))
        .Where(c => c.Distance <= Math.Max(2, name.Length / 3) || c.Name.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(c.Name, StringComparison.OrdinalIgnoreCase))
        .OrderBy(c => c.Distance)
        .ThenBy(c => c.Name, StringComparer.Ordinal)
        .Take(3)
        .Select(c => c.Name)];

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private void Lex()
    {
        var s = this.Source;
        if (s.Trim().Length == 0)
        {
            throw new FormulaException("the formula is empty", 0);
        }

        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var start = i;
            if (char.IsWhiteSpace(c))
            {
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                {
                    i++;
                }

                this.Add(Kind.Space, start, i);
                continue;
            }

            switch (c)
            {
                case '"':
                    i++;
                    while (true)
                    {
                        if (i >= s.Length)
                        {
                            throw new FormulaException("the string is not closed with a \"", start);
                        }

                        if (s[i] == '"')
                        {
                            if (i + 1 < s.Length && s[i + 1] == '"')
                            {
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        i++;
                    }

                    this.Add(Kind.String, start, i);
                    continue;
                case '#':
                    var error = Errors.FirstOrDefault(e => string.Compare(s, i, e, 0, e.Length, StringComparison.OrdinalIgnoreCase) == 0);
                    if (error is null)
                    {
                        throw new FormulaException("this is not an error value such as #N/A or #DIV/0!", start);
                    }

                    i += error.Length;
                    this.Add(Kind.Error, start, i, error);
                    continue;
                case '[':
                    i = this.Header(start);
                    continue;
                case ']':
                    throw new FormulaException("']' closes no '['", start);
                case '{' when string.Compare(s, i, "{row}", 0, 5, StringComparison.OrdinalIgnoreCase) == 0:
                    i += 5;
                    this.Add(Kind.RowNumber, start, i);
                    continue;
                case '{':
                    this.Add(Kind.ArrayOpen, start, ++i);
                    continue;
                case '}':
                    this.Add(Kind.ArrayClose, start, ++i);
                    continue;
                case '(':
                    this.Add(Kind.Open, start, ++i);
                    continue;
                case ')':
                    this.Add(Kind.Close, start, ++i);
                    continue;
                case ',':
                    this.Add(Kind.Comma, start, ++i);
                    continue;
                case ';':
                    this.Add(Kind.Semicolon, start, ++i);
                    continue;
                case '%':
                    this.Add(Kind.Percent, start, ++i);
                    continue;
                case '<' or '>' when i + 1 < s.Length && (s[i + 1] == '=' || (c == '<' && s[i + 1] == '>')):
                    i += 2;
                    this.Add(Kind.Operator, start, i);
                    continue;
                case '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>' or ':':
                    this.Add(Kind.Operator, start, ++i);
                    continue;
                case '\'' or '!':
                    throw new FormulaException("this refers to another sheet; formulas refer only to their own sheet for now", start);
            }

            var rest = s.AsSpan(i);
            if (RowRange().Match(s, i, s.Length - i) is { Success: true } rows && rows.Index == i)
            {
                foreach (Group group in rows.Groups.Values.Skip(1))
                {
                    if (!int.TryParse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row is < 1 or > XlsxPlan.MaxRows)
                    {
                        throw new FormulaException($"row {group.Value} is outside Excel's rows 1 to 1,048,576", group.Index);
                    }
                }

                i += rows.Length;
                this.Add(Kind.Reference, start, i);
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && rest.Length > 1 && char.IsAsciiDigit(rest[1])))
            {
                var number = NumberPattern().Match(s, i, s.Length - i);
                i += number.Length;
                if (i < s.Length && (char.IsAsciiLetter(s[i]) || s[i] == '_'))
                {
                    throw new FormulaException("a number runs into a name; put an operator between them", start);
                }

                this.Add(Kind.Number, start, i);
                continue;
            }

            if (ColumnRange().Match(s, i, s.Length - i) is { Success: true } columns && columns.Index == i)
            {
                foreach (var group in new[] { columns.Groups[1], columns.Groups[2] })
                {
                    if (Column(group.Value) is < 0 or >= XlsxPlan.MaxColumns)
                    {
                        throw new FormulaException($"column {group.Value} is outside Excel's columns A to XFD", group.Index);
                    }
                }

                i += columns.Length;
                this.Add(Kind.Reference, start, i);
                continue;
            }

            if (CellPattern().Match(s, i, s.Length - i) is { Success: true } cell && cell.Index == i && Column(cell.Groups[2].Value) is >= 0 and < XlsxPlan.MaxColumns and var col)
            {
                var rowText = cell.Groups[4].Value;
                int? row = null;
                if (!rowText.StartsWith('{'))
                {
                    if (!int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out var r) || r is < 1 or > XlsxPlan.MaxRows)
                    {
                        throw new FormulaException($"row {rowText} is outside Excel's rows 1 to 1,048,576", cell.Groups[4].Index);
                    }

                    row = r;
                }

                i += cell.Length;
                this.cells.Add((col, row, start + 1));
                this.tokens.Add(new Token(Kind.Cell, start, s[start..i], cell.Groups[1].Value + cell.Groups[2].Value.ToUpperInvariant() + cell.Groups[3].Value, row));
                continue;
            }

            if (NamePattern().Match(s, i, s.Length - i) is { Success: true } name && name.Index == i)
            {
                i += name.Length;
                var text = name.Value;
                if (i < s.Length && s[i] == '!')
                {
                    throw new FormulaException("this refers to another sheet; formulas refer only to their own sheet for now", start);
                }

                if (i < s.Length && s[i] == '[')
                {
                    throw new FormulaException($"'{text}[' is a reference to an Excel table; refer to a column of this sheet as [Header]", start);
                }

                if (i < s.Length && s[i] == '(')
                {
                    this.tokens.Add(new Token(Kind.Function, start, text, text.ToUpperInvariant()));
                }
                else if (text.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || text.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    this.tokens.Add(new Token(Kind.Bool, start, text, text.ToUpperInvariant()));
                }
                else
                {
                    this.tokens.Add(new Token(Kind.Name, start, text, text));
                }

                continue;
            }

            throw new FormulaException($"'{c}' has no meaning in a formula", start);
        }
    }

    // [Header]: up to the next ']'; an apostrophe takes the next character as it is, as in Excel's table references.
    private int Header(int start)
    {
        var s = this.Source;
        var name = new StringBuilder();
        var i = start + 1;
        while (true)
        {
            if (i >= s.Length)
            {
                throw new FormulaException("'[' is not closed with ']'", start);
            }

            if (s[i] == '\'' && i + 1 < s.Length)
            {
                name.Append(s[i + 1]);
                i += 2;
                continue;
            }

            if (s[i] == ']')
            {
                break;
            }

            name.Append(s[i++]);
        }

        i++;
        var header = name.ToString().Trim();
        if (header.Length == 0)
        {
            throw new FormulaException("'[]' names no column; write the column's header between the brackets", start);
        }

        this.headers.Add((header, start + 1));
        this.tokens.Add(new Token(Kind.Header, start, s[start..i], header));
        return i;
    }

    private void Add(Kind kind, int start, int end, string? value = null) =>
        this.tokens.Add(new Token(kind, start, this.Source[start..end], value ?? this.Source[start..end]));

    // A column's index from 0: A is 0, XFD 16,383; -1 for more than three letters.
    private static int Column(string letters)
    {
        if (letters.Length is 0 or > 3)
        {
            return -1;
        }

        var n = 0;
        foreach (var c in letters.ToUpperInvariant())
        {
            n = (n * 26) + (c - 'A' + 1);
        }

        return n - 1;
    }

    [GeneratedRegex(@"\G\$?(\d+):\$?(\d+)(?![\w.(])", RegexOptions.CultureInvariant)]
    private static partial Regex RowRange();

    [GeneratedRegex(@"\G\$?([A-Za-z]{1,3}):\$?([A-Za-z]{1,3})(?![\w.({])", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnRange();

    [GeneratedRegex(@"\G(\$?)([A-Za-z]{1,3})(\$?)(\d+|\{[Rr][Oo][Ww]\})(?![\w.(\[!])", RegexOptions.CultureInvariant)]
    private static partial Regex CellPattern();

    [GeneratedRegex(@"\G(\d+(\.\d*)?|\.\d+)([Ee][+-]?\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\G[A-Za-z_\\][A-Za-z0-9_.\\]*", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    private enum Kind
    {
        Space,
        Number,
        String,
        Bool,
        Error,
        Cell,
        Reference,
        Header,
        RowNumber,
        Function,
        Name,
        Parameter,
        Operator,
        Percent,
        Comma,
        Semicolon,
        Open,
        Close,
        ArrayOpen,
        ArrayClose,
    }

    // What an operand is, for ':': a range joins references, never values.
    private enum Operand
    {
        Value,
        Reference,
        Any,
    }

    private sealed record Token(Kind Kind, int Start, string Text, string Value, int? Row = null)
    {
        public Kind Kind { get; set; } = Kind;

        public string Value { get; set; } = Value;

        public string Prefix { get; set; } = string.Empty;
    }

    private sealed class FormulaException(string message, int position) : Exception(message)
    {
        public int Position { get; } = position;
    }

    /// <summary>Recursive descent over the tokens, lowest precedence first: comparison, <c>&amp;</c>, <c>+ -</c>, <c>* /</c>, <c>^</c>, <c>%</c>, signs, <c>:</c>.</summary>
    private sealed class Parser(XlsxFormula formula)
    {
        private readonly List<Token> tokens = [.. formula.tokens.Where(t => t.Kind != Kind.Space)];
        private int at;
        private int depth;
        private int parameters;

        private Token? Current => this.at < this.tokens.Count ? this.tokens[this.at] : null;

        public void Run()
        {
            this.Expression();
            if (this.Current is { } extra)
            {
                throw new FormulaException(extra.Kind == Kind.Close ? "')' closes no '('" : $"'{extra.Text}' follows a complete formula; an operator is missing before it", extra.Start);
            }
        }

        private int End => formula.Source.TrimEnd().Length;

        private Operand Expression()
        {
            var operand = this.Concatenation();
            while (this.IsOperator("=", "<>", "<", ">", "<=", ">="))
            {
                this.at++;
                this.Concatenation();
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Concatenation()
        {
            var operand = this.Additive();
            while (this.IsOperator("&"))
            {
                this.at++;
                this.Additive();
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Additive()
        {
            var operand = this.Multiplicative();
            while (this.IsOperator("+", "-"))
            {
                this.at++;
                this.Multiplicative();
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Multiplicative()
        {
            var operand = this.Power();
            while (this.IsOperator("*", "/"))
            {
                this.at++;
                this.Power();
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Power()
        {
            var operand = this.Postfix();
            while (this.IsOperator("^"))
            {
                this.at++;
                this.Postfix();
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Postfix()
        {
            var operand = this.Prefix();
            while (this.Current is { Kind: Kind.Percent })
            {
                this.at++;
                operand = Operand.Value;
            }

            return operand;
        }

        private Operand Prefix()
        {
            if (this.IsOperator("+", "-"))
            {
                this.at++;
                this.Prefix();
                return Operand.Value;
            }

            return this.Range();
        }

        private Operand Range()
        {
            var start = this.Current;
            var operand = this.Primary();
            while (this.IsOperator(":"))
            {
                var colon = this.Current!;
                if (operand == Operand.Value)
                {
                    throw new FormulaException("':' joins two references, and what stands before it is a value", start!.Start);
                }

                this.at++;
                var next = this.Current;
                if (this.Primary() == Operand.Value)
                {
                    throw new FormulaException("':' joins two references, and what follows it is a value", next?.Start ?? colon.Start);
                }

                operand = Operand.Reference;
            }

            // Two references side by side are their intersection.
            while (operand != Operand.Value && this.Current is { Kind: Kind.Cell or Kind.Reference or Kind.Header } && this.Adjacent())
            {
                this.Range();
            }

            return operand;
        }

        private Operand Primary()
        {
            var token = this.Current ?? throw new FormulaException("the formula ends where a value is expected", this.End);
            switch (token.Kind)
            {
                case Kind.Number or Kind.String or Kind.Bool or Kind.Error or Kind.RowNumber:
                    this.at++;
                    return Operand.Value;
                case Kind.Cell or Kind.Reference or Kind.Header:
                    this.at++;
                    return Operand.Reference;
                case Kind.Name:
                    this.at++;
                    if (this.parameters > 0)
                    {
                        token.Kind = Kind.Parameter;
                        if (token.Value.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase))
                        {
                            token.Value = token.Value[6..];
                        }
                    }
                    else
                    {
                        formula.names.Add((token.Value, token.Start + 1));
                    }

                    return Operand.Any;
                case Kind.Function:
                    return this.Call(token);
                case Kind.Open:
                    this.at++;
                    var operand = this.Expression();
                    while (this.Current is { Kind: Kind.Comma })
                    {
                        // A union of references: (A1:B2,D4).
                        this.at++;
                        this.Expression();
                        operand = Operand.Reference;
                    }

                    this.Expect(Kind.Close, "')' is missing", this.End);
                    return operand;
                case Kind.ArrayOpen:
                    this.Array(token);
                    return Operand.Value;
                case Kind.Close:
                    throw new FormulaException("')' comes where a value is expected", token.Start);
                case Kind.Comma:
                    throw new FormulaException("',' comes where a value is expected; outside a function's arguments, put the formula in parentheses", token.Start);
                default:
                    throw new FormulaException($"'{token.Text}' comes where a value is expected", token.Start);
            }
        }

        private Operand Call(Token name)
        {
            this.at += 2;
            if (++this.depth > MaxDepth)
            {
                throw new FormulaException("functions are nested more than 64 deep, as far as Excel goes", name.Start);
            }

            var bare = Strip(name.Value);
            var known = Functions.TryGetValue(bare, out var function);
            name.Value = bare;
            if (known)
            {
                name.Prefix = function.Prefix;
            }
            else
            {
                // Written as given: an add-in's function, or one newer than the table.
                name.Value = name.Text;
                formula.unknown.Add((name.Text.ToUpperInvariant(), name.Start + 1));
            }

            var binds = bare is "LET" or "LAMBDA";
            if (binds)
            {
                this.parameters++;
            }

            var count = 0;
            if (this.Current is { Kind: Kind.Close })
            {
                this.at++;
            }
            else
            {
                while (true)
                {
                    count++;
                    if (this.Current is not { Kind: Kind.Comma or Kind.Close })
                    {
                        this.Expression();
                    }

                    if (this.Current is { Kind: Kind.Comma })
                    {
                        this.at++;
                        continue;
                    }

                    this.Expect(Kind.Close, $"the arguments of {bare} are not closed with ')'", this.End);
                    break;
                }
            }

            if (binds)
            {
                this.parameters--;
            }

            this.depth--;
            if (known && (count < function.Min || count > function.Max))
            {
                var takes = function.Min == function.Max
                    ? Count(function.Min)
                    : function.Max >= 254 ? $"at least {Count(function.Min)}" : $"{function.Min} to {function.Max} arguments";
                throw new FormulaException(string.Create(CultureInfo.InvariantCulture, $"{bare} takes {takes}, and is given {count}"), name.Start);
            }

            return Operand.Any;
        }

        private void Array(Token open)
        {
            this.at++;
            int? width = null;
            var columns = 0;
            while (true)
            {
                var token = this.Current ?? throw new FormulaException("the array is not closed with '}'", open.Start);
                if (token is { Kind: Kind.Operator, Text: "-" or "+" } && this.at + 1 < this.tokens.Count && this.tokens[this.at + 1].Kind == Kind.Number)
                {
                    this.at++;
                }
                else if (token.Kind is not (Kind.Number or Kind.String or Kind.Bool or Kind.Error))
                {
                    throw new FormulaException("an array holds only numbers, strings, TRUE, FALSE and error values", token.Start);
                }

                this.at++;
                columns++;
                var next = this.Current ?? throw new FormulaException("the array is not closed with '}'", open.Start);
                this.at++;
                if (next.Kind == Kind.Comma)
                {
                    continue;
                }

                if (next.Kind is Kind.Semicolon or Kind.ArrayClose)
                {
                    if (width is { } w && w != columns)
                    {
                        throw new FormulaException(string.Create(CultureInfo.InvariantCulture, $"the rows of the array differ in length: {w} and {columns}"), next.Start);
                    }

                    width = columns;
                    columns = 0;
                    if (next.Kind == Kind.ArrayClose)
                    {
                        return;
                    }

                    continue;
                }

                throw new FormulaException("an array separates values with ',' and rows with ';'", next.Start);
            }
        }

        private void Expect(Kind kind, string message, int position)
        {
            if (this.Current?.Kind != kind)
            {
                throw new FormulaException(message, this.Current?.Start ?? position);
            }

            this.at++;
        }

        private bool IsOperator(params string[] operators) => this.Current is { Kind: Kind.Operator } token && operators.Contains(token.Text);

        // Whether the current token is separated from the previous one by space alone: the intersection operator.
        private bool Adjacent()
        {
            var previous = this.tokens[this.at - 1];
            return this.tokens[this.at].Start > previous.Start + previous.Text.Length;
        }

        private static string Count(int n) => n == 1 ? "1 argument" : string.Create(CultureInfo.InvariantCulture, $"{n} arguments");

        private static string Strip(string name)
        {
            foreach (var prefix in (string[])["_XLFN._XLWS.", "_XLFN.", "_XLWS."])
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return name[prefix.Length..];
                }
            }

            return name;
        }
    }
}
