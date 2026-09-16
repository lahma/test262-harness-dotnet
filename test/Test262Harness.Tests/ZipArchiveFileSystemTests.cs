using System.IO.Compression;
using System.Text;

namespace Test262Harness.Tests;

/// <summary>
/// Covers reading a suite out of a zip archive without going through the network.
/// </summary>
public class ZipArchiveFileSystemTests
{
    private const string RootName = "test262-abc123";

    private string _archivePath = null!;

    [OneTimeSetUp]
    public void CreateArchive()
    {
        _archivePath = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}.zip");

        using var file = File.Create(_archivePath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);

        AddEntry(archive, $"{RootName}/harness/assert.js", """
            /*---
            description: harness helper
            ---*/
            function assert() {}
            """);

        for (var i = 0; i < 25; i++)
        {
            AddEntry(archive, $"{RootName}/test/language/sample-{i}.js", TestFileContent(i));
        }

        // a file large enough that a single Read() is not guaranteed to return everything
        AddEntry(archive, $"{RootName}/test/language/large.js", TestFileContent(0, padding: 512 * 1024));
    }

    [OneTimeTearDown]
    public void DeleteArchive()
    {
        File.Delete(_archivePath);
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        using var stream = archive.CreateEntry(path).Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static string TestFileContent(int i, int padding = 0)
    {
        return $"""
            /*---
            description: sample {i}
            flags: [noStrict]
            ---*/
            var marker = {i};{new string('/', padding)}
            """;
    }

    private Test262Stream CreateStream() => Test262Stream.FromZipArchive(_archivePath, RootName);

    [Test]
    public void ReadsFileContentsRatherThanEmptyPlaceholders()
    {
        var file = CreateStream().GetTestFile("language/sample-7.js");

        file.FileName.Should().Be("language/sample-7.js");
        file.Description.Should().Be("sample 7");
        file.Program.Should().Contain("var marker = 7;");
    }

    [Test]
    public void ReadsFilesLargerThanASingleBufferInFull()
    {
        var file = CreateStream().GetTestFile("language/large.js");

        file.Program.Should().HaveLength(TestFileContent(0, padding: 512 * 1024).Length);
        file.Program.Should().EndWith("/");
    }

    [Test]
    public void EnumeratesTestAndHarnessFiles()
    {
        var stream = CreateStream();

        stream.GetTestFiles(["language"]).Should().HaveCount(26);
        stream.GetHarnessFiles().Select(x => x.FileName).Should().ContainSingle(x => x.EndsWith("assert.js"));
    }

    [Test]
    public void ThrowsForUnknownFile()
    {
        var stream = CreateStream();

        var act = () => stream.GetTestFile("language/does-not-exist.js");

        act.Should().Throw<FileNotFoundException>();
    }

    [Test]
    public void BuildsTheStrictVariant()
    {
        var file = CreateStream().GetTestFile("language/sample-4.js");

        var strict = file.AsStrict();

        strict.Strict.Should().BeTrue();
        strict.Program.Should().StartWith("\"use strict\";");
        strict.Program.Should().EndWith(file.Program);
        strict.FileName.Should().Be(file.FileName);

        // an already-strict instance is its own strict variant
        strict.AsStrict().Should().BeSameAs(strict);
    }

    [Test]
    public void StrictVariantDoesNotAffectTheSloppyOne()
    {
        var file = CreateStream().GetTestFile("language/sample-5.js");
        var sloppyProgram = file.Program;

        file.AsStrict();

        file.Strict.Should().BeFalse();
        file.Program.Should().Be(sloppyProgram);
    }

    /// <summary>
    /// The reason the archive is materialized into memory: SharpZipLib serves every read from one
    /// shared base stream under a single lock, which serializes parallel test execution.
    /// </summary>
    [Test]
    public void SupportsConcurrentReads()
    {
        var stream = CreateStream();

        var results = new string[25];
        Parallel.For(0, 25, i =>
        {
            for (var repeat = 0; repeat < 20; repeat++)
            {
                results[i] = stream.GetTestFile($"language/sample-{i}.js").Program;
            }
        });

        for (var i = 0; i < results.Length; i++)
        {
            results[i].Should().Contain($"var marker = {i};");
        }
    }
}
