using System.IO.Compression;
using System.Text;

namespace Test262Harness.Tests;

/// <summary>
/// Covers FromGitHub against an archive that is already cached, without going through the network.
/// </summary>
public class Test262StreamExtensionsTests
{
    // "test262-<sha>" is the name FromGitHub caches the archive under and the archive's root directory.
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

        for (var i = 0; i < 3; i++)
        {
            AddEntry(archive, $"{RootName}/test/language/sample-{i}.js", TestFileContent(i));
        }

        // outside the sub-directories the test configures
        AddEntry(archive, $"{RootName}/test/built-ins/sample-3.js", TestFileContent(3));
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

    private static string TestFileContent(int i)
    {
        return $"""
            /*---
            description: sample {i}
            flags: [noStrict]
            ---*/
            var marker = {i};
            """;
    }

    /// <summary>
    /// A cached archive is used without a download; extracting honours the configuration and copes with
    /// an earlier run having extracted it already.
    /// </summary>
    [Test]
    public async Task ExtractsACachedArchiveOnceAndHonoursTheConfiguration()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        try
        {
            var sha = RootName.Substring("test262-".Length);
            File.Copy(_archivePath, Path.Combine(tempPath, $"{RootName}.zip"));

            for (var run = 0; run < 2; run++)
            {
                var stream = await Test262StreamExtensions.FromGitHub(sha, tempPath, extract: true, configure: options =>
                {
                    options.SubDirectories = ["language"];
                    options.GenerateInverseStrictTestCase = false;
                    options.LogInfo = (_, _) => { };
                });

                stream.Options.GenerateInverseStrictTestCase.Should().BeFalse();
                stream.GetTestFiles().Should().HaveCount(3);
            }
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }

    /// <summary>
    /// Only a damaged download is downloaded again: a cached archive that reads fine but cannot be used as a
    /// suite is reported as it is, since a fresh copy of the same commit would fail the same way.
    /// </summary>
    [Test]
    public async Task ReportsAnUnusableCachedArchiveWithoutDownloadingItAgain()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        try
        {
            using (var file = File.Create(Path.Combine(tempPath, $"{RootName}.zip")))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                AddEntry(archive, $"{RootName}/test/language/twice.js", TestFileContent(0));
                AddEntry(archive, $"{RootName}/test/language/twice.js", TestFileContent(1));
            }

            var act = () => Test262StreamExtensions.FromGitHub(RootName.Substring("test262-".Length), tempPath, configure: options =>
            {
                options.LogInfo = (_, _) => { };
            });

            await act.Should().ThrowAsync<IOException>().WithMessage("*more than once*");
        }
        finally
        {
            Directory.Delete(tempPath, recursive: true);
        }
    }
}
