namespace wwosik.DocLibs.DocumentGeneration.Xlsx.Tests;

public class FormulaTests
{
    private static string? Letter(string header) => header switch
    {
        "Price" => "B",
        "Quantity" => "C",
        _ => null,
    };

    [Theory]
    [InlineData("1+2*3", "1+2*3")]
    [InlineData("[Price]*[Quantity]", "B7*C7")]
    [InlineData("SUM(B{row}:D{row})", "SUM(B7:D7)")]
    [InlineData("$A$1+A{row}+{row}", "$A$1+A7+7")]
    [InlineData("sum(a1:b2, 3)", "SUM(a1:b2, 3)")]
    [InlineData("-2^2%", "-2^2%")]
    [InlineData("IF([Price]>=10,\"big \"\"one\"\"\",#N/A)", "IF(B7>=10,\"big \"\"one\"\"\",#N/A)")]
    [InlineData("SUM({1,2;3,-4})", "SUM({1,2;3,-4})")]
    [InlineData("SUM(A:A,3:3)", "SUM(A:A,3:3)")]
    [InlineData("IF(A1,,1)", "IF(A1,,1)")]
    [InlineData("true<>FALSE", "TRUE<>FALSE")]
    [InlineData("XLOOKUP([Price],A1:A9,B1:B9)", "_xlfn.XLOOKUP(B7,A1:A9,B1:B9)")]
    [InlineData("_xlfn.xlookup(1,A1:A9,B1:B9)", "_xlfn.XLOOKUP(1,A1:A9,B1:B9)")]
    [InlineData("FILTER(A1:A9,B1:B9>0)", "_xlfn._xlws.FILTER(A1:A9,B1:B9>0)")]
    [InlineData("LET(x,[Price],x*2)", "_xlfn.LET(_xlpm.x,B7,_xlpm.x*2)")]
    [InlineData("TEXTJOIN(\", \",TRUE,A1:C1)&STDEV.S(A1:A3)", "_xlfn.TEXTJOIN(\", \",TRUE,A1:C1)&_xlfn.STDEV.S(A1:A3)")]
    [InlineData("LOG10(100)+LOG10", "LOG10(100)+LOG10")]
    [InlineData("SUM((A1:B2,D4))", "SUM((A1:B2,D4))")]
    [InlineData("[Price']x]", "[Price']x]")]
    public void Parses_AndWritesForARow(string source, string written)
    {
        var formula = XlsxFormula.Parse(source);

        Assert.Null(formula.Error);
        Assert.Equal(written, formula.Write(7, Letter));
    }

    [Fact]
    public void Prefixes_AreLeftOutAsExcelShowsTheFormula()
    {
        Assert.Equal("XLOOKUP(B3,A1:A9,B1:B9)+SUM(1)", XlsxFormula.Parse("XLOOKUP([Price],A1:A9,B1:B9)+SUM(1)").Write(3, Letter, prefixes: false));
    }

    [Theory]
    [InlineData("1+", 3, "ends where a value is expected")]
    [InlineData("SUM(1,2", 8, "not closed")]
    [InlineData("(1+2", 5, "')' is missing")]
    [InlineData("1+2)", 4, "')' closes no '('")]
    [InlineData("\"open", 1, "string is not closed")]
    [InlineData("1 2", 3, "an operator is missing")]
    [InlineData("#BAD", 1, "not an error value")]
    [InlineData("[Price", 1, "'[' is not closed")]
    [InlineData("[]", 1, "names no column")]
    [InlineData("IF(1)", 1, "IF takes 2 to 3 arguments, and is given 1")]
    [InlineData("ROUND(1)", 1, "ROUND takes 2 arguments")]
    [InlineData("SUM()", 1, "SUM takes at least 1 argument")]
    [InlineData("1+Sheet2!A1", 3, "another sheet")]
    [InlineData("'My sheet'!A1", 1, "another sheet")]
    [InlineData("Sales[Price]", 1, "Excel table")]
    [InlineData("{1,2;3}", 7, "differ in length")]
    [InlineData("{A1}", 2, "only numbers")]
    [InlineData("1:A1", 1, "joins two references")]
    [InlineData("A1:2", 4, "joins two references")]
    [InlineData("A1048577", 2, "outside Excel's rows")]
    [InlineData("1+?", 3, "has no meaning")]
    [InlineData("  ", 1, "empty")]
    public void Invalid_NamesThePosition(string source, int position, string message)
    {
        var error = XlsxFormula.Parse(source).Error;

        Assert.NotNull(error);
        Assert.Contains(message, error.Value.Message, StringComparison.Ordinal);
        Assert.Equal(position, error.Value.Position);
    }

    [Fact]
    public void Nesting_StopsAt64()
    {
        var deep = string.Concat(Enumerable.Repeat("ABS(", 65)) + "1" + new string(')', 65);

        Assert.Contains("64 deep", XlsxFormula.Parse(deep).Error!.Value.Message, StringComparison.Ordinal);
        Assert.Null(XlsxFormula.Parse(string.Concat(Enumerable.Repeat("ABS(", 64)) + "1" + new string(')', 64)).Error);
    }

    [Fact]
    public void UnknownFunctions_AndNames_AreCollected()
    {
        var formula = XlsxFormula.Parse("MYADDIN([Price])+Price");

        Assert.Null(formula.Error);
        Assert.Equal([("MYADDIN", 1)], formula.UnknownFunctions);
        Assert.Equal([("Price", 18)], formula.Names);
        Assert.Equal([("Price", 9)], formula.Headers);
        Assert.Equal("MYADDIN(B1)+Price", formula.Write(1, Letter));
    }

    [Fact]
    public void TheTable_KnowsAbout450Functions()
    {
        Assert.InRange(XlsxFormula.Functions.Count, 400, 500);
        Assert.Equal(string.Empty, XlsxFormula.Functions["SUM"].Prefix);
        Assert.Equal("_xlfn.", XlsxFormula.Functions["IFS"].Prefix);
        Assert.Equal("_xlfn.", XlsxFormula.Functions["CONCAT"].Prefix);
        Assert.Equal("_xlfn._xlws.", XlsxFormula.Functions["SORT"].Prefix);
    }

    [Fact]
    public void Closest_SuggestsSimilarNames()
    {
        Assert.Equal(["Price"], XlsxFormula.Closest("Prise", ["Price", "Quantity", "Region"]));
        Assert.Equal(["Unit price"], XlsxFormula.Closest("price", ["Unit price", "Quantity"]));
        Assert.Empty(XlsxFormula.Closest("Zebra", ["Price", "Quantity"]));
    }
}
