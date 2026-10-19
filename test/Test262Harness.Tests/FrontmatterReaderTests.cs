using System.Text;
using Zio;

namespace Test262Harness.Tests;

/// <summary>
/// The frontmatter fast path must agree with YamlDotNet on everything it accepts, and hand everything else back.
/// </summary>
public class FrontmatterReaderTests
{
    private static IEnumerable<TestCaseData> SubsetCases()
    {
        yield return Case("plain scalars", "esid: sec-array.from\ndescription: Array.from with a plain scalar\nauthor: someone\n");
        yield return Case("es5id and es6id map to esid", "es5id: 15.4.4.17_A1\nes6id: 22.1.3.24\ndescription: x\n");
        yield return Case("trailing spaces", "description: trailing spaces   \n");
        yield return Case("hash without space", "description: Array#map and C# are not comments\n");
        yield return Case("apostrophe in plain", "description: it's a plain scalar\n");
        yield return Case("plain continued on indented lines", "description: >-\n  folded\ninfo: one\n  two\n   three\n");
        yield return Case("plain starting on next line", "description:\n  starts below\n  and continues\n");
        yield return Case("single quoted", "description: 'quoted: with a colon # and hash'\n");
        yield return Case("single quoted with escaped quote", "description: 'it''s escaped'\n");
        yield return Case("double quoted", "description: \"double: quoted\"\n");
        yield return Case("literal block", "info: |\n    line one\n    line two\n\n    after blank\n      more indented\ndescription: x\n");
        yield return Case("literal block strip", "info: |-\n  line one\n  line two\n\nflags: [raw]\n");
        yield return Case("folded block", "info: >\n  folded one\n  folded two\n\n  new paragraph\ndescription: x\n");
        yield return Case("folded block strip", "description: >-\n  folded\n  strip\nflags: [onlyStrict]\n");
        yield return Case("block at end of frontmatter", "description: x\ninfo: |\n  last\n  ");
        yield return Case("block without final break", "description: x\ninfo: |\n  last");
        yield return Case("flow sequence", "features: [Symbol.iterator, class-fields-public, regexp-v-flag]\nflags: [module, async]\n");
        yield return Case("empty flow sequence", "features: []\ndescription: x\n");
        yield return Case("flow sequence over lines", "features: [a, b,\n  c, d]\ndescription: x\n");
        yield return Case("block sequence", "includes:\n  - compareArray.js\n  - propertyHelper.js\ndescription: x\n");
        yield return Case("scalar where sequence expected", "flags: noStrict\ndescription: x\n");
        yield return Case("negative", "description: x\nnegative:\n  phase: parse\n  type: SyntaxError\nflags: [onlyStrict]\n");
        yield return Case("negative quoted", "negative:\n  phase: 'runtime'\n  type: \"TypeError\"\n");
        yield return Case("unknown keys skipped", "description: x\ntimeout: 10\ndefines: [a, b]\nextra:\n  nested: value\n");
        yield return Case("empty value", "description:\nflags: [raw]\n");
        yield return Case("leading and trailing blank lines", "\n\ndescription: x\n\n\n");
        yield return Case("locale", "locale: [en-US, de]\ndescription: x\n");
        yield return Case("non-ascii", "description: \u00dcn\u00efc\u00f6d\u00e9 \u00e9 \u4e2d\u6587\n");
        yield return Case("duplicate key, last wins", "description: first\ndescription: second\n");
    }

    private static IEnumerable<TestCaseData> FallbackCases()
    {
        yield return Case("comment line", "# comment\ndescription: x\n");
        yield return Case("trailing comment", "description: x # comment\n");
        yield return Case("tab", "description:\tx\n");
        yield return Case("carriage return", "description: x\r\nflags: [raw]\r\n");
        yield return Case("repeated flags", "flags: [module]\nflags: [raw]\n");
        yield return Case("repeated flags with strictness", "flags: [onlyStrict]\nflags: [noStrict]\n");
        yield return Case("escape in double quotes", "description: \"a\\tb\"\n");
        yield return Case("multi-line single quoted", "description: 'one\n  two'\n");
        yield return Case("keep chomping", "info: |+\n  kept\n\ndescription: x\n");
        yield return Case("explicit indentation", "info: |2\n    two\n");
        yield return Case("leading blank line in block", "info: |\n\n  text\n");
        yield return Case("more indented folded line", "info: >\n  one\n    indented\n  two\n");
        yield return Case("anchor", "description: &a x\n");
        yield return Case("sequence at key indentation", "includes:\n- a.js\n- b.js\n");
        yield return Case("blank line inside plain", "description: one\n\n  two\n");
        yield return Case("trailing comma", "features: [a, b,]\n");
        yield return Case("quoted flow item", "features: ['a', b]\n");
        yield return Case("not a mapping", "just some text\n");
        yield return Case("only blank lines", "\n  \n");
        yield return Case("mapping where scalar expected", "description:\n  nested: value\n");
        yield return Case("indented key", "  description: x\n");
        yield return Case("line separator", "description: a\u2028b\n");
    }

    private static TestCaseData Case(string name, string yaml) => new TestCaseData(yaml).SetArgDisplayNames(name);

    private static string FileWith(string yaml) => $"/*---{yaml}---*/\nvar x = 1;\n";

    [TestCaseSource(nameof(SubsetCases))]
    public void ReadsTheSubsetWithoutYamlDotNet(string yaml)
    {
        Test262File.IsReadWithoutYamlParser("\n" + yaml).Should().BeTrue();
        AssertParity(FileWith("\n" + yaml), "case.js");
    }

    [TestCaseSource(nameof(FallbackCases))]
    public void HandsAnythingElseToYamlDotNet(string yaml)
    {
        Test262File.IsReadWithoutYamlParser("\n" + yaml).Should().BeFalse();
        AssertParity(FileWith("\n" + yaml), "case.js");
    }

    [Test]
    public void ReadsNonSeekableStreams()
    {
        var content = "/*---\ndescription: piped\nflags: [onlyStrict]\n---*/\nvar x;\n";
        using var stream = new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(content)));

        var files = Test262File.FromStream(stream, "test/piped.js").ToList();

        files.Should().ContainSingle();
        files[0].Strict.Should().BeTrue();
        files[0].Description.Should().Be("piped");
        files[0].Program.Should().EndWith(content);
    }

    /// <summary>
    /// The program is decoded on first use, so it must not be decoded from a buffer its owner can still change.
    /// </summary>
    [Test]
    public void CopiesTheContentOfAWritableMemoryStream()
    {
        var content = "/*---\ndescription: first\n---*/\nvar first;\n";
        using var stream = new MemoryStream();
        stream.Write(Encoding.UTF8.GetBytes(content));
        stream.Position = 0;

        var files = Test262File.FromStream(stream, "test/first.js").ToList();

        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes("/*---\ndescription: second\n---*/\nvar overwritten;\n"));

        files.Should().HaveCount(2);
        foreach (var file in files)
        {
            file.Program.Should().EndWith(content);
        }
    }

    /// <summary>
    /// The whole published suite, both paths, field by field. Also reports how much of it the fast path takes,
    /// which is what decides whether it is worth having.
    /// </summary>
    [Test]
    public async Task AgreesWithYamlDotNetOnTheWholeSuite()
    {
        var zip = Environment.GetEnvironmentVariable("TEST262_PARITY_ZIP");
        var stream = string.IsNullOrEmpty(zip)
            ? await Test262StreamExtensions.FromGitHub("28b31c0bf1960878abb36ab8597a0cae224a684d")
            : Test262Stream.FromZipArchive(zip, Path.GetFileNameWithoutExtension(zip));

        var fileSystem = stream.Options.FileSystem;
        var total = 0;
        var fast = 0;
        var failures = new List<string>();
        var fallbacks = new List<string>();
        foreach (var path in fileSystem.EnumerateFiles(UPath.Root, "*.js", SearchOption.AllDirectories))
        {
            string contents;
            using (var reader = new StreamReader(fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                contents = reader.ReadToEnd();
            }

            total++;
            var start = contents.IndexOf("/*---", StringComparison.Ordinal);
            var end = start < 0 ? -1 : contents.IndexOf("---*/", start, StringComparison.Ordinal);
            if (end > start + 5)
            {
                var yaml = contents.Substring(start + 5, end - start - 5);
                if (Test262File.IsReadWithoutYamlParser(yaml))
                {
                    fast++;
                }
                else
                {
                    fallbacks.Add($"===== {path}{yaml}");
                }
            }

            var fileName = path.FullName.TrimStart('/');
            try
            {
                AssertParity(contents, fileName);
                AssertSameAsReadFromBytes(fileSystem.ReadAllBytes(path), contents, fileName);
            }
            catch (Exception ex)
            {
                failures.Add($"{path}: {ex.Message}");
            }
        }

        TestContext.Out.WriteLine($"{fast} of {total} files ({100.0 * fast / total:0.0}%) read without YamlDotNet");
        var dump = Environment.GetEnvironmentVariable("TEST262_FALLBACK_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            File.WriteAllLines(dump, fallbacks);
        }
        failures.Should().BeEmpty();
        total.Should().BeGreaterThan(40_000);
    }

    private static void AssertParity(string contents, string fileName)
    {
        var expected = Parse(contents, fileName, allowFastPath: false);
        var actual = Parse(contents, fileName, allowFastPath: true);

        if (expected.Error is not null)
        {
            actual.Error.Should().NotBeNull("YamlDotNet rejects this frontmatter: {0}", expected.Error.Message);
            actual.Error!.Message.Should().Be(expected.Error.Message);
            return;
        }

        actual.Error.Should().BeNull();
        var e = expected.File!;
        var a = actual.File!;
        a.EcmaScriptId.Should().Be(e.EcmaScriptId, "esid");
        a.Description.Should().Be(e.Description, "description");
        a.Info.Should().Be(e.Info, "info");
        a.Author.Should().Be(e.Author, "author");
        a.Features.ToArray().Should().Equal(e.Features.ToArray(), "features");
        a.Flags.ToArray().Should().Equal(e.Flags.ToArray(), "flags");
        a.Includes.ToArray().Should().Equal(e.Includes.ToArray(), "includes");
        a.Locale.ToArray().Should().Equal(e.Locale.ToArray(), "locale");
        a.Type.Should().Be(e.Type, "type");
        a.Program.Should().Be(e.Program);
        (a.NegativeTestCase?.Phase).Should().Be(e.NegativeTestCase?.Phase, "negative phase");
        (a.NegativeTestCase?.Type).Should().Be(e.NegativeTestCase?.Type, "negative type");
        actual.OnlyStrict.Should().Be(expected.OnlyStrict, "onlyStrict");
        actual.NoStrict.Should().Be(expected.NoStrict, "noStrict");
    }

    /// <summary>
    /// A stream read from its bytes (decoding only the frontmatter, the program on first use) must produce the
    /// same test cases as the text it decodes to.
    /// </summary>
    private static void AssertSameAsReadFromBytes(byte[] bytes, string contents, string fileName)
    {
        var expected = Parse(contents, fileName, allowFastPath: true);
        IReadOnlyList<Test262File>? actual = null;
        Exception? error = null;
        try
        {
            actual = Test262File.FromStream(new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true), fileName).ToList();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (expected.Error is not null)
        {
            error.Should().NotBeNull();
            error!.Message.Should().Be(expected.Error.Message);
            return;
        }

        error.Should().BeNull();

        // the variants FromStream yields: non-strict unless strict-only or a module, strict unless ruled out
        var expectedStrict = new List<bool>();
        if (expected.File!.Type == ProgramType.Script && !expected.OnlyStrict)
        {
            expectedStrict.Add(false);
        }

        if (!expected.NoStrict)
        {
            expectedStrict.Add(true);
        }

        actual!.Select(x => x.Strict).Should().Equal(expectedStrict, "variants read from bytes");
        foreach (var file in actual!)
        {
            var reference = file.Strict ? expected.File!.AsStrict() : expected.File!;
            file.Program.Should().Be(reference.Program, "program read from bytes");
            file.Description.Should().Be(reference.Description);
            file.Info.Should().Be(reference.Info);
            file.Features.ToArray().Should().Equal(reference.Features.ToArray());
            file.Flags.ToArray().Should().Equal(reference.Flags.ToArray());
            file.Includes.ToArray().Should().Equal(reference.Includes.ToArray());
            file.Type.Should().Be(reference.Type);
        }
    }

    private static (Test262File? File, bool OnlyStrict, bool NoStrict, Exception? Error) Parse(string contents, string fileName, bool allowFastPath)
    {
        try
        {
            var file = Test262File.Parse(contents, fileName, allowFastPath, out var onlyStrict, out var noStrict);
            return (file, onlyStrict, noStrict, null);
        }
        catch (Exception ex)
        {
            return (null, false, false, ex);
        }
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
