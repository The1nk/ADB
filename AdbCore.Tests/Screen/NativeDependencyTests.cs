using OpenCvSharp;
using Xunit;

namespace AdbCore.Tests.Screen;

public class NativeDependencyTests
{
    [Fact]
    public void OpenCvSharp_NativeRuntime_Loads()
    {
        // Constructing a Mat forces the native runtime to load; if the runtime package is missing
        // or incompatible with net10.0-windows, this throws (DllNotFound/TypeInitialization).
        using var mat = new Mat(2, 2, MatType.CV_8UC3, Scalar.All(0));
        Assert.Equal(2, mat.Rows);
        Assert.Equal(2, mat.Cols);
    }
}
