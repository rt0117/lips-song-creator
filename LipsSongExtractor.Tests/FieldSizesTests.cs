namespace LipsSongExtractor.Tests;

public class FieldSizesTests
{
    // ── DetermineFieldSize ──────────────────────────────────────

    [Theory]
    [InlineData("bool", 1)]
    [InlineData("unsigned char", 1)]
    [InlineData("char", 1)]
    [InlineData("short", 2)]
    [InlineData("unsigned short", 2)]
    [InlineData("int", 4)]
    [InlineData("unsigned int", 4)]
    [InlineData("float", 4)]
    [InlineData("double", 8)]
    [InlineData("long", 8)]
    public void DetermineFieldSize_PrimitiveTypes(string type, int expected)
    {
        Assert.Equal(expected, FieldSizes.DetermineFieldSize(type));
    }

    [Theory]
    [InlineData("ixPackage *", 4)]
    [InlineData("ixAsset*", 4)]
    [InlineData("pointer", 4)]
    public void DetermineFieldSize_PointerTypes_Return4(string type, int expected)
    {
        Assert.Equal(expected, FieldSizes.DetermineFieldSize(type));
    }

    [Theory]
    [InlineData("ixVector<char,1,ixAllocator<char,1>,ixIterator<char> >", 16)]
    [InlineData("ixList<ixPackage *,ixAllocator<ixDblCnt<ixPackage *>,1> >", 16)]
    [InlineData("ixArray<int>", 16)]
    public void DetermineFieldSize_ContainerTypes_Return16(string type, int expected)
    {
        Assert.Equal(expected, FieldSizes.DetermineFieldSize(type));
    }

    [Fact]
    public void DetermineFieldSize_NullOrEmpty_Returns4()
    {
        Assert.Equal(4, FieldSizes.DetermineFieldSize(null));
        Assert.Equal(4, FieldSizes.DetermineFieldSize(""));
        Assert.Equal(4, FieldSizes.DetermineFieldSize("   "));
    }

    // ── ReadBE ──────────────────────────────────────────────────

    [Fact]
    public void ReadBE_UInt32_BigEndian()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x17 };
        Assert.Equal(23u, FieldSizes.ReadBE<uint>(bytes));
    }

    [Fact]
    public void ReadBE_UInt32_BigEndian_Large()
    {
        var bytes = new byte[] { 0x3A, 0x38, 0x06, 0xA0 };
        Assert.Equal(0x3A3806A0u, FieldSizes.ReadBE<uint>(bytes));
    }

    [Fact]
    public void ReadBE_Int32_Negative()
    {
        var bytes = new byte[] { 0xFF, 0xFF, 0xFF, 0xFE };
        Assert.Equal(-2, FieldSizes.ReadBE<int>(bytes));
    }

    [Fact]
    public void ReadBE_Float()
    {
        // IEEE 754: 120.0f = 0x42F00000
        var bytes = new byte[] { 0x42, 0xF0, 0x00, 0x00 };
        Assert.Equal(120.0f, FieldSizes.ReadBE<float>(bytes));
    }

    [Fact]
    public void ReadBE_Bool_True()
    {
        Assert.True(FieldSizes.ReadBE<bool>(new byte[] { 0x01 }));
    }

    [Fact]
    public void ReadBE_Bool_False()
    {
        Assert.False(FieldSizes.ReadBE<bool>(new byte[] { 0x00 }));
    }

    [Fact]
    public void ReadBE_DoesNotMutateInput()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x17 };
        var copy = (byte[])bytes.Clone();
        FieldSizes.ReadBE<uint>(bytes);
        Assert.Equal(copy, bytes);
    }

    // ── ReadCString ─────────────────────────────────────────────

    [Fact]
    public void ReadCString_ReadsUntilNull()
    {
        var buf = "Hello\0World"u8.ToArray();
        // ptr=0 gibt "" zurück (null-pointer), also ptr=1 verwenden um ab Index 1 zu lesen
        // oder wir nutzen einen nicht-null offset
        var result = FieldSizes.ReadCString(buf, 6, false);
        Assert.Equal("World", result);
    }

    [Fact]
    public void ReadCString_FromOffset()
    {
        var buf = new byte[] { 0x00, 0x48, 0x69, 0x00 }; // \0 H i \0
        var result = FieldSizes.ReadCString(buf, 1, false);
        Assert.Equal("Hi", result);
    }

    [Fact]
    public void ReadCString_ZeroPtr_ReturnsEmpty()
    {
        var buf = new byte[] { 0x41, 0x42, 0x00 };
        Assert.Equal("", FieldSizes.ReadCString(buf, 0, false));
    }

    [Fact]
    public void ReadCString_PtrBeyondBuffer_ReturnsInvalid()
    {
        var buf = new byte[] { 0x41, 0x42, 0x00 };
        Assert.StartsWith("<invalid", FieldSizes.ReadCString(buf, 99, false));
    }

    // ── ConvertByType ───────────────────────────────────────────

    [Fact]
    public void ConvertByType_Bool()
    {
        Assert.Equal(true, FieldSizes.ConvertByType("bool", "m_bFlag", new byte[] { 0x01 }, true));
        Assert.Equal(false, FieldSizes.ConvertByType("bool", "m_bFlag", new byte[] { 0x00 }, true));
    }

    [Fact]
    public void ConvertByType_Float()
    {
        var bytes = new byte[] { 0x42, 0xF0, 0x00, 0x00 };
        var result = FieldSizes.ConvertByType("float", "m_fValue", bytes, true);
        Assert.IsType<float>(result);
        Assert.Equal(120.0f, (float)result);
    }

    [Fact]
    public void ConvertByType_UnsignedInt()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x2A };
        var result = FieldSizes.ConvertByType("unsigned int", "m_uiCount", bytes, true);
        Assert.IsType<uint>(result);
        Assert.Equal(42u, (uint)result);
    }

    [Fact]
    public void ConvertByType_FallbackToNameHeuristic()
    {
        // Kein Typ angegeben, Name beginnt mit m_f -> float
        var bytes = new byte[] { 0x42, 0xF0, 0x00, 0x00 };
        var result = FieldSizes.ConvertByType(null, "m_fSpeed", bytes, true);
        Assert.IsType<float>(result);
        Assert.Equal(120.0f, (float)result);
    }
}
