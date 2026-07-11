using CustomRadioPanel.Core.Hardware;

namespace CustomRadioPanel.Tests;

public class SevenSegmentTests
{
    [Fact]
    public void Digits_AreRightAligned_AndPaddedWithBlanks()
    {
        var cells = SevenSegment.EncodeDisplay("123");
        Assert.Equal(new byte[] { 0x0F, 0x0F, 0x01, 0x02, 0x03 }, cells);
    }

    [Fact]
    public void DecimalPoint_AttachesToPrecedingDigit_WithoutConsumingACell()
    {
        // "118.30" -> 5 cells: 1,1,8., 3,0  (8. == 0xD8)
        var cells = SevenSegment.EncodeDisplay("118.30");
        Assert.Equal(new byte[] { 0x01, 0x01, 0xD8, 0x03, 0x00 }, cells);
    }

    [Fact]
    public void Dash_And_Blank_AreEncoded()
    {
        var cells = SevenSegment.EncodeDisplay("-");
        Assert.Equal(new byte[] { 0x0F, 0x0F, 0x0F, 0x0F, 0xEF }, cells);
    }

    [Fact]
    public void EmptyString_IsAllBlank()
    {
        var cells = SevenSegment.EncodeDisplay("");
        Assert.All(cells, b => Assert.Equal(SevenSegment.Blank, b));
    }

    [Fact]
    public void BuildBuffer_Is22Bytes_WithDisplaysInOrder()
    {
        var buf = SevenSegment.BuildBuffer("1", "2", "3", "4");
        Assert.Equal(SevenSegment.BufferLength, buf.Length);
        Assert.Equal(0x01, buf[4]);   // last cell of top-left
        Assert.Equal(0x02, buf[9]);   // last cell of top-right
        Assert.Equal(0x03, buf[14]);  // last cell of bottom-left
        Assert.Equal(0x04, buf[19]);  // last cell of bottom-right
    }

    [Fact]
    public void AllOff_IsAll0xFF()
    {
        Assert.All(SevenSegment.AllOff(), b => Assert.Equal(0xFF, b));
    }
}
