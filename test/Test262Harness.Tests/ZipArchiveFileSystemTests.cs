using Zio;
using Zio.FileSystems;
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
    /// A consumer reading through Zio's IFileSystem.OpenFile(path, mode, access) overload gets
    /// FileShare.None, while Test262Stream.GetTestFile asks for FileShare.Read. Both reach for the
    /// same file, so neither may take a lock the other is refused.
    /// </summary>
    [Test]
    public void AllowsConcurrentReadsAskingForDifferentShareModes()
    {
        var stream = CreateStream();
        var fileSystem = stream.Options.FileSystem;

        using var held = fileSystem.OpenFile("/test/language/sample-2.js", FileMode.Open, FileAccess.Read);

        Assert.DoesNotThrow(() => stream.GetTestFile("language/sample-2.js"));
        Assert.DoesNotThrow(() => fileSystem.OpenFile("/test/language/sample-2.js", FileMode.Open, FileAccess.Read).Dispose());
    }

    /// <summary>
    /// The reason the archive is materialized into memory: a zip archive serves every read from one
    /// shared base stream, which would serialize parallel test execution.
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

    [Test]
    public void ReportsExistenceAndLengthFromTheDecompressedContent()
    {
        var fileSystem = CreateStream().Options.FileSystem;
        var expectedLength = Encoding.UTF8.GetByteCount(TestFileContent(3));

        fileSystem.FileExists("/test/language/sample-3.js").Should().BeTrue();
        fileSystem.FileExists("/test/language/does-not-exist.js").Should().BeFalse();
        fileSystem.DirectoryExists("/test/language").Should().BeTrue();
        fileSystem.GetFileLength("/test/language/sample-3.js").Should().Be(expectedLength);

        var item = fileSystem
            .EnumerateItems("/test/language", SearchOption.AllDirectories)
            .Single(x => x.FullName == "/test/language/sample-3.js");
        item.Length.Should().Be(expectedLength);
    }

    /// <summary>
    /// Entries are inflated by several workers into one shared buffer; every file has to come back
    /// byte for byte whichever worker's range it fell into.
    /// </summary>
    [Test]
    public void DecompressesEveryEntryWhicheverWorkerReadsIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}.zip");
        try
        {
            var expected = new Dictionary<string, string>();
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 200; i++)
                {
                    var name = $"built-ins/many/file-{i}.js";
                    var content = TestFileContent(i, padding: 1 + i * 97 % 5000);
                    expected[name] = content;
                    AddEntry(archive, $"{RootName}/test/{name}", content);
                }
            }

            var stream = Test262Stream.FromZipArchive(path, RootName);

            foreach (var pair in expected)
            {
                stream.GetTestFile(pair.Key).Program.Should().Be(pair.Value);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadsStoredEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}.zip");
        try
        {
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                using var stream = archive.CreateEntry($"{RootName}/test/language/stored.js", CompressionLevel.NoCompression).Open();
                var bytes = Encoding.UTF8.GetBytes(TestFileContent(42));
                stream.Write(bytes, 0, bytes.Length);
            }

            Test262Stream.FromZipArchive(path, RootName).GetTestFile("language/stored.js").Program.Should().Be(TestFileContent(42));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// test262 is close to the 65,535 entries a classic zip can hold; past that, archives are Zip64.
    /// </summary>
    [Test]
    public void ReadsZip64Archives()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}.zip");
        try
        {
            const int Count = ushort.MaxValue + 10;
            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                for (var i = 0; i < Count; i++)
                {
                    using var stream = archive.CreateEntry($"{RootName}/test/language/f{i}.js", CompressionLevel.NoCompression).Open();
                    stream.WriteByte((byte) ('a' + i % 26));
                }
            }

            var fileSystem = Test262Stream.FromZipArchive(path, RootName).Options.FileSystem;

            fileSystem.EnumerateFiles("/test/language").Should().HaveCount(Count);
            fileSystem.ReadAllText($"/test/language/f{Count - 1}.js").Should().Be(((char) ('a' + (Count - 1) % 26)).ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void RejectsATruncatedArchiveAsInvalidData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test262harness-tests-{Guid.NewGuid():N}.zip");
        try
        {
            var bytes = File.ReadAllBytes(_archivePath);
            File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length / 2).ToArray());

            var act = () => Test262Stream.FromZipArchive(path, RootName);

            act.Should().Throw<InvalidDataException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Every file of a real suite archive, byte for byte against <see cref="ZipArchive"/>.
    /// </summary>
    [Test]
    public async Task MatchesZipArchiveOnAWholeSuite()
    {
        var zip = Environment.GetEnvironmentVariable("TEST262_PARITY_ZIP");
        string root;
        Test262Stream stream;
        if (string.IsNullOrEmpty(zip))
        {
            const string Sha = "28b31c0bf1960878abb36ab8597a0cae224a684d";
            stream = await Test262StreamExtensions.FromGitHub(Sha);
            zip = Path.Combine(Path.GetTempPath(), $"test262-{Sha}.zip");
            root = $"test262-{Sha}";
        }
        else
        {
            root = Path.GetFileNameWithoutExtension(zip);
            stream = Test262Stream.FromZipArchive(zip, root);
        }

        var fileSystem = stream.Options.FileSystem;

        // The tree a MemoryFileSystem builds from the same entries in the same order: enumeration must visit
        // paths in exactly its order, since that is what consumers saw before.
        var reference = new MemoryFileSystem();
        var compared = 0;
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (!name.StartsWith($"{root}/test", StringComparison.Ordinal) && !name.StartsWith($"{root}/harness", StringComparison.Ordinal))
                {
                    continue;
                }

                var path = (UPath) name.Substring(root.Length);
                if (name.EndsWith('/'))
                {
                    reference.CreateDirectory(path);
                    continue;
                }

                reference.CreateDirectory(path.GetDirectory());
                reference.WriteAllBytes(path, []);

                using var expected = new MemoryStream();
                using (var source = entry.Open())
                {
                    source.CopyTo(expected);
                }

                fileSystem.ReadAllBytes(path).AsSpan().SequenceEqual(expected.ToArray()).Should().BeTrue(path.FullName);
                compared++;
            }
        }

        compared.Should().BeGreaterThan(40_000);

        foreach (var directory in new UPath[] { "/test", "/test/built-ins", "/harness" })
        {
            fileSystem.EnumerateItems(directory, SearchOption.AllDirectories).Select(x => x.FullName)
                .Should().Equal(reference.EnumerateItems(directory, SearchOption.AllDirectories).Select(x => x.FullName), directory.FullName);
            fileSystem.EnumerateFiles(directory, "*.js", SearchOption.AllDirectories).Select(x => x.FullName)
                .Should().Equal(reference.EnumerateFiles(directory, "*.js", SearchOption.AllDirectories).Select(x => x.FullName), directory.FullName);
            fileSystem.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Select(x => x.FullName)
                .Should().Equal(reference.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly).Select(x => x.FullName), directory.FullName);
        }
    }

    [Test]
    public void IsReadOnly()
    {
        var fileSystem = CreateStream().Options.FileSystem;

        fileSystem.Invoking(x => x.WriteAllText("/test/language/new.js", "x")).Should().Throw<UnauthorizedAccessException>();
        fileSystem.Invoking(x => x.OpenFile("/test/language/sample-1.js", FileMode.Open, FileAccess.ReadWrite)).Should().Throw<UnauthorizedAccessException>();
        fileSystem.Invoking(x => x.DeleteFile("/test/language/sample-1.js")).Should().Throw<UnauthorizedAccessException>();
        fileSystem.Invoking(x => x.CreateDirectory("/test/other")).Should().Throw<UnauthorizedAccessException>();

        fileSystem.FileExists("/test/language/sample-1.js").Should().BeTrue();
    }
}
