using System.Text;
using Zio;
using Zio.FileSystems;

namespace Test262Harness.Tests;

public class Test262StreamTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task CanLoadFromGitHub()
    {
        var test262Stream = await Test262StreamExtensions.FromGitHub("28b31c0bf1960878abb36ab8597a0cae224a684d");
        test262Stream.GetHarnessFiles().ToList().Should().NotBeEmpty();
        test262Stream.GetTestFiles().ToList().Should().NotBeEmpty();
    }

    [Test]
    public void FileFilterSkipsFilesBeforeTheyAreParsed()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/test/language");
        fs.WriteAllText("/test/language/kept.js", "/*---\ndescription: kept\n---*/\n");
        // no frontmatter: parsing this one would throw
        fs.WriteAllText("/test/language/skipped.js", "var x;\n");

        var stream = Test262Stream.FromFileSystem(fs, options =>
        {
            options.SubDirectories = ["language"];
            options.FileFilter = item => !item.FullName.EndsWith("skipped.js", StringComparison.Ordinal);
        });

        stream.GetTestFiles().Select(x => x.FileName).Distinct().Should().Equal("test/language/kept.js");
    }

    [TestCase("SyntaxError", ExpectedErrorType.SyntaxError)]
    [TestCase("TypeError", ExpectedErrorType.TypeError)]
    [TestCase("ReferenceError", ExpectedErrorType.ReferenceError)]
    [TestCase("RangeError", ExpectedErrorType.RangeError)]
    [TestCase("Test262Error", ExpectedErrorType.Test262Error)]
    [TestCase("EvalError", ExpectedErrorType.EvalError)]
    [TestCase("URIError", ExpectedErrorType.URIError)]
    public void NegativeTestCaseParsesExpectedErrorType(string errorType, ExpectedErrorType expected)
    {
        var content = $"""
            /*---
            description: test
            negative:
              phase: runtime
              type: {errorType}
            ---*/
            throw new {errorType}();
            """;

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var files = Test262File.FromStream(stream, "test.js", generateInverseStrictTestCase: false).ToList();

        files.Should().HaveCount(1);
        files[0].NegativeTestCase.Should().NotBeNull();
        files[0].NegativeTestCase!.Type.Should().Be(expected);
        files[0].NegativeTestCase!.Phase.Should().Be(TestingPhase.Runtime);
    }
}
