using System.Text;

namespace DoubleClickDOS.Tests;

public sealed class BinaryStringsTests
{
    [Fact]
    public void Scan_ReturnsPrintableRunsAndSkipsShortOnes()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MZ"));
        bytes.Add(0x00);
        bytes.Add(0x01);
        bytes.AddRange(Encoding.ASCII.GetBytes("Copyright (C) 1994 Acme"));
        bytes.Add(0x00);
        bytes.AddRange(Encoding.ASCII.GetBytes("abc"));
        bytes.Add(0xFF);

        var result = new List<string>();
        BinaryStrings.Scan(bytes.ToArray(), result, minLength: 5, maxStrings: 100);

        Assert.Equal(["Copyright (C) 1994 Acme"], result);
    }

    [Fact]
    public void Scan_StopsAtTheStringLimit()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("alpha\0bravo\0charlie\0delta\0");

        var result = new List<string>();
        BinaryStrings.Scan(bytes, result, minLength: 4, maxStrings: 2);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Scan_IgnoresRunsWithoutEnoughLetters()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("\u0001\u0002\u0003\u0004\u0005\u0006" + "1234");

        var result = new List<string>();
        BinaryStrings.Scan(bytes, result, minLength: 3, maxStrings: 100);

        Assert.Empty(result);
    }
}
