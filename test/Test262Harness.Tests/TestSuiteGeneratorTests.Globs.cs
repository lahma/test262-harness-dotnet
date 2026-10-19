using Test262Harness.TestSuite.Generator;

namespace Test262Harness.Tests;

public partial class TestSuiteGeneratorTests
{
    [Test]
    public async Task ExcludedFileGlobsMatchPathsAndFileNames()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            // a pattern with a directory part matches the whole path, one without matches the file name
            ExcludedFiles = ["built-ins/Atomics/**/desc*.js", "b?r.js"],
        });

        output.Should().Contain("\"built-ins/Atomics/waitAsync/descriptor.js\", false, Category = \"Atomics.waitAsync,Atomics\", IgnoreReason = \"File built-ins/atomics/waitasync/descriptor.js excluded (glob pattern)\"");
        // the only file of its group, so the whole method is ignored
        output.Should().Contain("[Ignore(\"File built-ins/foo/bar.js excluded (glob pattern)\")]");
        output.Should().Contain("[TestCase(\"built-ins/Atomics/waitAsync/length.js\", false, Category = \"Atomics.waitAsync,Atomics\")]");
        output.Should().NotContain("built-ins/async/baz.js excluded");
    }
}
