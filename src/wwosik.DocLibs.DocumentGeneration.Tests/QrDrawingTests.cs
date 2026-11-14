using System.Text;
using QRCoder;
using wwosik.DocLibs.DocumentConversions;

namespace wwosik.DocLibs.DocumentGeneration.Tests;

/// <summary><c>qr:…</c> images as drawings: QRCoder's module matrix, a quiet zone, one merged path, the colour.</summary>
public class QrDrawingTests
{
    private static Drawing Draw(string text, Brand? brand = null, string? caption = null, double scale = 1) =>
        QrDrawing.Draw(text, ResolvedBrand.Resolve(brand ?? Brand.Neutral), caption, scale, 1, DocumentFormat.Pdf);

    // The dark modules of a drawing, read back from the rectangles of its path.
    private static bool[,] Modules(Drawing drawing, double unit)
    {
        var size = (int)Math.Round(drawing.Width / unit);
        var modules = new bool[size, size];
        var path = ((DrawingShape)drawing.Items[1]).Path.Segments;
        for (var i = 0; i < path.Count; i += 5)
        {
            var (start, end) = ((MoveTo)path[i], (LineTo)path[i + 2]);
            for (var x = (int)Math.Round(start.X / unit); x < (int)Math.Round(end.X / unit); x++)
            {
                modules[(int)Math.Round(start.Y / unit), x] = true;
            }
        }

        return modules;
    }

    [Theory]
    [InlineData("HELLO")]
    [InlineData("https://example.com/survey?id=3")]
    [InlineData("WIFI:S:Guest;T:WPA;P:secret;;")]
    [InlineData("Zürich – 東京")]
    public void TheModules_AreQRCodersMatrix_InByteMode_ErrorCorrectionM(string text)
    {
        var drawing = Draw(text);

        using var generator = new QRCodeGenerator();
        using var expected = generator.CreateQrCode(Encoding.UTF8.GetBytes(text), QRCodeGenerator.ECCLevel.M);
        var modules = Modules(drawing, QrDrawing.ModuleSize);
        Assert.Equal(expected.ModuleMatrix.Count, modules.GetLength(0));
        for (var row = 0; row < expected.ModuleMatrix.Count; row++)
        {
            for (var column = 0; column < expected.ModuleMatrix.Count; column++)
            {
                Assert.Equal(expected.ModuleMatrix[row][column], modules[row, column]);
            }
        }
    }

    [Fact]
    public void AShortText_IsVersion1_WithAQuietZoneOfFourModules()
    {
        var drawing = Draw("HELLO");

        var modules = Modules(drawing, 4);
        Assert.Equal(21 + 8, modules.GetLength(0));
        Assert.Equal((116.0, 116.0), (drawing.Width, drawing.Height));
        for (var i = 0; i < 29; i++)
        {
            for (var q = 0; q < 4; q++)
            {
                Assert.False(modules[q, i] || modules[i, q] || modules[28 - q, i] || modules[i, 28 - q], "the quiet zone is light");
            }
        }

        // The finder pattern's dark outer ring in the top left corner.
        Assert.True(modules[4, 4] && modules[4, 10] && modules[10, 4] && modules[10, 10]);
        Assert.False(modules[5, 5]);
    }

    [Fact]
    public void TheCode_IsTwoShapes_AWhiteSquareAndOnePathOfAllModules()
    {
        var drawing = Draw("https://example.com");

        Assert.Equal(2, drawing.Items.Count);
        var square = Assert.IsType<DrawingShape>(drawing.Items[0]);
        Assert.Equal((DrawingShapeKind.Rectangle, "FFFFFF", (string?)null), (square.Kind, square.Fill, square.Stroke));
        Assert.Equal((0.0, 0.0, drawing.Width, drawing.Width), square.Path.Bounds);
        var modules = Assert.IsType<DrawingShape>(drawing.Items[1]);
        Assert.Equal(DrawingFillRule.NonZero, modules.FillRule);
        Assert.Null(modules.Stroke);
        Assert.True(modules.Path.Segments.Count(s => s is MoveTo) > 50, "one subpath per run of dark modules");
        drawing.Validate();
    }

    [Fact]
    public void ACaption_GoesUnderTheCode_AndScaleMultipliesTheModules()
    {
        var drawing = Draw("HELLO", caption: "Survey", scale: 2);

        Assert.Equal(29 * 8, drawing.Width);
        var caption = Assert.IsType<DrawingText>(drawing.Items[2]);
        Assert.Equal("Survey", caption.Text);
        Assert.Equal(TextAnchor.Middle, caption.Anchor);
        Assert.True(caption.Baseline > drawing.Width && caption.Baseline <= drawing.Height);
    }

    [Fact]
    public void TheColour_IsThePrimary_UnlessItIsTooLightForAScanner()
    {
        var dark = new Brand { Name = "dark", Colors = new BrandColors { Primary = "1F4E79" } };
        var light = new Brand { Name = "light", Colors = new BrandColors { Primary = "FFD60A", Diagram = new BrandDiagramColors { Text = "333333" } } };
        var pale = new Brand { Name = "pale", Colors = new BrandColors { Primary = "FFD60A", Diagram = new BrandDiagramColors { Text = "EEEEEE" } } };

        Assert.Equal("1F4E79", ((DrawingShape)Draw("x", dark).Items[1]).Fill);
        Assert.Equal("333333", ((DrawingShape)Draw("x", light).Items[1]).Fill);
        Assert.Equal("000000", ((DrawingShape)Draw("x", pale).Items[1]).Fill);
        Assert.Equal(21, QrDrawing.Contrast("000000", "FFFFFF"), 6);
    }

    [Fact]
    public void TooMuchContent_Throws()
    {
        var error = Assert.Throws<DocumentConversionException>(() => Draw(new string('a', 1001)));

        Assert.Contains("1001 bytes", error.Message, StringComparison.Ordinal);
        Draw(new string('a', 1000));
        Assert.Throws<DocumentConversionException>(() => Draw(string.Empty));
    }

    [Fact]
    public void OtherAttributes_ThanScaleAndCaption_AreErrors()
    {
        var brand = ResolvedBrand.Neutral;
        var round = new ImageAttributes { Round = true, Text = "{.round}" };

        var error = Assert.Throws<DocumentConversionException>(() => QrDrawing.ForImage("qr:x", "alt", null, round, brand, 4, DocumentFormat.Pptx));
        Assert.Contains("scale=<number> and .caption", error.Message, StringComparison.Ordinal);

        var (drawing, scaled) = QrDrawing.ForImage("qr:x", "Scan me", "caption", ImageAttributes.None, brand, 4, DocumentFormat.Pptx);
        Assert.False(scaled);
        Assert.Equal("Scan me", Assert.IsType<DrawingText>(drawing.Items[2]).Text);
        Assert.True(QrDrawing.ForImage("qr:x", "a", "scale=2", ImageAttributes.None, brand, 4, DocumentFormat.Pptx).Scaled);
    }
}
