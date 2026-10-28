using System.Globalization;
using PhotoRecull.Core;

namespace PhotoRecull.Tests;

public class FileSizeTextTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(274_726_912L, "262 MB")]
    [InlineData(1_159_641_169L, "1.08 GB")]
    public void Format_UsesBinaryUnitsWithTwoSignificantDecimals(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeText.Format(bytes, CultureInfo.InvariantCulture));
    }
}
