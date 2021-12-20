using Zio;
using Zio.FileSystems;

namespace Test262Harness.TestSuite;

/// <summary>
/// Access point to self-contained test suite packaged to this DLL file.
/// </summary>
public class TestSuiteFileSystem
{
    private static readonly Lazy<IFileSystem> _instance = new(() => new ReadOnlyFileSystem(new PhysicalFileSystem()));

    /// <summary>
    /// Returns the shared read-only file system instance.
    /// </summary>
    public static IFileSystem Instance => _instance.Value;
}
