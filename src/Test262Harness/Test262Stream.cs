using System.Collections;

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
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream Create(string baseDirectory)
    {
        return Create(new Test262StreamOptions(baseDirectory));
    }

    /// <summary>
    /// Creates an enumerable stream for test cases from given list of files.
    /// </summary>
    /// <param name="files"> Files to use. </param>
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream Create(IEnumerable<string> files)
    {
        return Create(new Test262StreamOptions(files));
    }

    /// <summary>
    /// Creates an enumerable stream for test cases.
    /// </summary>
    /// <param name="options">Options to use.</param>
    /// <returns>A stream that can be enumerated.</returns>
    public static Test262Stream Create(Test262StreamOptions options)
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
        var targetFiles = _options.Files ?? GetTargetFiles();

        targetFiles = targetFiles
            .Where(x => x.IndexOf("_FIXTURE", StringComparison.OrdinalIgnoreCase) == -1);

        foreach (var filePath in targetFiles)
        {
            foreach (var testCase in Test262File.FromFile(filePath))
            {
                if (_options.Filter(testCase))
                {
                    yield return testCase;
                }
            }
        }
    }

    private IEnumerable<string> GetTargetFiles()
    {
        var testDirectory = Path.Combine(_options.BaseDirectory!, "test");
        var subDirectories = _options.SubDirectories;

        IEnumerable<string> targetFiles = Array.Empty<string>();
        foreach (var subDirectory in subDirectories)
        {
            targetFiles = targetFiles.Concat(Directory.GetFiles(Path.Combine(testDirectory, subDirectory), "*.*", SearchOption.AllDirectories));
        }

        return targetFiles;
    }
}

