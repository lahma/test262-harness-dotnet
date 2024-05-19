using Zio;

namespace Test262Harness;

public delegate void Logger(string message, params object[] args);

public sealed class Test262StreamOptions
{
    public Test262StreamOptions(IFileSystem fileSystem)
    {
        FileSystem = fileSystem;
    }

    public IFileSystem FileSystem { get; set; }

    /// <summary>
    /// Sub-directories to search. Defaults to: "annexB", "built-ins", "intl402", "language"
    /// </summary>
    public string[] SubDirectories { get; set; } = { "annexB", "built-ins", "intl402", "language" };

    /// <summary>
    /// Possibility to filter files before they are going to be parsed.
    /// </summary>
    public Func<FileSystemItem, bool> FileFilter { get; set; } = _ => true;

    /// <summary>
    /// Possibility to filter matched and parsed test cases.
    /// </summary>
    public Func<Test262File, bool> TestCaseFilter { get; set; } = _ => true;

    /// <summary>
    /// If false Test262Stream will only return test case file as-is. Otherwise when testing requires also a non-strict
    /// or a strict test case will be returned by appending "use strict;" at the beginning of extra text file. Defaults to: true.
    /// </summary>
    public bool GenerateInverseStrictTestCase { get; set; } = true;

    /// <summary>
    /// Logger to call when informing about progress.
    /// </summary>
    public Logger LogInfo { get; set; } = Console.WriteLine;

    /// <summary>
    /// Logger to call when informing about error.
    /// </summary>
    public Logger LogError { get; set; } = Console.Error.WriteLine;

    internal static void Validate()
    {
    }
}
