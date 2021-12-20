using System.Collections;
using Zio;
using Zio.FileSystems;

namespace Test262Harness;

/// <summary>
/// An enumerable instance that can be used to traverse all Test262 ECMAScript Test Suite (ECMA TR/104) test cases.
/// </summary>
/// <remarks>
/// Single test case file can produce either one or two <see cref="Test262File"/> instances based on whether it's
/// a module (always one) or a script file (two if both strict and non-strict mode will be tested).
/// </remarks>
public sealed class Test262Stream : IEnumerable<Test262File>
{
    private readonly Test262StreamOptions _options;

    private Test262Stream(Test262StreamOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Creates an enumerable stream for test cases rooted to given base directory.
    /// </summary>
    /// <param name="baseDirectory">
    /// Base directory to use, this should have "harness" and "test" sub-directories like in https://github.com/tc39/test262 .
    /// </param>
    /// <param name="configure">
    /// Callback to call to configure options.
    /// </param>
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream FromDirectory(string baseDirectory, Action<Test262StreamOptions>? configure = null)
    {
        var fileSystem = new ReadOnlyFileSystem(new SubFileSystem(new PhysicalFileSystem(), baseDirectory));
        return FromFileSystem(fileSystem, configure);
    }

    /// <summary>
    /// Creates an enumerable stream for test cases from given zip archive.
    /// </summary>
    /// <param name="file">
    /// File to load archive from.
    /// </param>
    /// <param name="subDirectory">
    /// Sub-directory inside the archive to use.
    /// </param>
    /// <param name="configure">
    /// Callback to call to configure options.
    /// </param>
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream FromZipArchive(string file, string subDirectory, Action<Test262StreamOptions>? configure = null)
    {
        return FromFileSystem(ZipArchiveFileSystem.Create(file, subDirectory), configure);
    }

    /// <summary>
    /// Creates an enumerable stream for test cases from given file system abstraction.
    /// </summary>
    /// <param name="fileSystem">
    /// File system instance to use.
    /// </param>
    /// <param name="configure">
    /// Callback to call to configure options.
    /// </param>
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream FromFileSystem(IFileSystem fileSystem, Action<Test262StreamOptions>? configure = null)
    {
        var options = new Test262StreamOptions(fileSystem);
        configure?.Invoke(options);
        return Create(options);
    }

    /// <summary>
    /// Creates an enumerable stream for test cases.
    /// </summary>
    /// <param name="options">Options to use.</param>
    /// <returns>A stream that can be enumerated.</returns>
    private static Test262Stream Create(Test262StreamOptions options)
    {
        options.Validate();
        return new Test262Stream(options);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<Test262File> GetEnumerator()
    {
        var targetFiles = GetTestFiles();

        foreach (var filePath in targetFiles)
        {
            using var stream = _options.FileSystem.OpenFile(filePath.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var testCase in Test262File.FromStream(stream, filePath.FullName))
            {
                if (_options.TestCaseFilter(testCase))
                {
                    yield return testCase;
                }
            }
        }
    }

    private IEnumerable<FileSystemItem> GetTestFiles()
    {
        bool SearchPredicate(ref FileSystemItem item) => item.FullName.EndsWith(".js") && item.FullName.IndexOf("_FIXTURE", StringComparison.OrdinalIgnoreCase) == -1;

        IEnumerable<FileSystemItem> result = Array.Empty<FileSystemItem>();
        foreach (var subDirectory in _options.SubDirectories)
        {
            result = result.Concat(_options.FileSystem.EnumerateItems("/test/" + subDirectory, SearchOption.AllDirectories, SearchPredicate));
        }

        return result;
    }
}
