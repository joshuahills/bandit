using Bandit.UI.Rendering;
using Xunit;

namespace Bandit.Tests;

public class BrailleCanvasTests
{
    [Fact]
    public void Empty_canvas_returns_null_for_every_cell()
    {
        var canvas = new BrailleCanvas(3, 2);

        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 3; x++)
                Assert.Null(canvas.GetCell(x, y));
    }

    [Theory]
    [InlineData(0, 0, '⠁')] // dot 1
    [InlineData(0, 1, '⠂')] // dot 2
    [InlineData(0, 2, '⠄')] // dot 3
    [InlineData(1, 0, '⠈')] // dot 4
    [InlineData(1, 1, '⠐')] // dot 5
    [InlineData(1, 2, '⠠')] // dot 6
    [InlineData(0, 3, '⡀')] // dot 7
    [InlineData(1, 3, '⢀')] // dot 8
    public void SetDot_maps_to_canonical_unicode_braille_bit(int dotX, int dotY, char expected)
    {
        var canvas = new BrailleCanvas(1, 1);
        canvas.SetDot(dotX, dotY);

        Assert.Equal(expected, canvas.GetCell(0, 0));
    }

    [Fact]
    public void All_eight_dots_set_in_one_cell_renders_full_braille_block()
    {
        var canvas = new BrailleCanvas(1, 1);
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 4; dy++)
                canvas.SetDot(dx, dy);

        Assert.Equal('⣿', canvas.GetCell(0, 0));
    }

    [Fact]
    public void FillBelow_sets_every_dot_in_column_from_top_down()
    {
        var canvas = new BrailleCanvas(1, 2);
        canvas.FillBelow(0, 2); // top dot at row 2 (third row of 8 dots)

        // Dots in left column at rows 2, 3, 4, 5, 6, 7 should be set:
        // - rows 2-3 are in cell (0, 0), left column → bits for dot3 (0b100) and dot7 (0b1000000)
        // - rows 4-7 are in cell (0, 1), left column → all left-column bits
        Assert.Equal((char)(0x2800 | 0b01000100), canvas.GetCell(0, 0));
        Assert.Equal((char)(0x2800 | 0b01000111), canvas.GetCell(0, 1));
    }

    [Fact]
    public void Out_of_bounds_SetDot_silently_ignored()
    {
        var canvas = new BrailleCanvas(2, 1);

        canvas.SetDot(-1, 0);
        canvas.SetDot(0, -1);
        canvas.SetDot(canvas.DotWidth, 0);
        canvas.SetDot(0, canvas.DotHeight);
        canvas.SetDot(int.MaxValue, int.MaxValue);

        Assert.Null(canvas.GetCell(0, 0));
        Assert.Null(canvas.GetCell(1, 0));
    }

    [Fact]
    public void Out_of_bounds_FillBelow_silently_ignored()
    {
        var canvas = new BrailleCanvas(2, 1);

        canvas.FillBelow(-1, 0);
        canvas.FillBelow(canvas.DotWidth, 0);

        Assert.Null(canvas.GetCell(0, 0));
        Assert.Null(canvas.GetCell(1, 0));
    }

    [Fact]
    public void FillBelow_with_negative_top_clamps_to_full_column()
    {
        var canvas = new BrailleCanvas(1, 1);
        canvas.FillBelow(0, -10);

        // Whole left column lit → dots 1, 2, 3, 7 (bits 0, 1, 2, 6) = 0b01000111
        Assert.Equal((char)(0x2800 | 0b01000111), canvas.GetCell(0, 0));
    }

    [Theory]
    [InlineData(1, 1, 2, 4)]
    [InlineData(10, 5, 20, 20)]
    [InlineData(100, 50, 200, 200)]
    public void Dot_dimensions_are_two_per_column_and_four_per_row(
        int cellWidth, int cellHeight, int expectedDotWidth, int expectedDotHeight)
    {
        var canvas = new BrailleCanvas(cellWidth, cellHeight);

        Assert.Equal(expectedDotWidth, canvas.DotWidth);
        Assert.Equal(expectedDotHeight, canvas.DotHeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_clamps_non_positive_dimensions_to_one(int dim)
    {
        var canvas = new BrailleCanvas(dim, dim);

        Assert.Equal(1, canvas.CellWidth);
        Assert.Equal(1, canvas.CellHeight);
    }
}
