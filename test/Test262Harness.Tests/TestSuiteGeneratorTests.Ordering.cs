using System.Globalization;
using Test262Harness.TestSuite.Generator;
using Zio;
using Zio.FileSystems;

namespace Test262Harness.Tests;

public partial class TestSuiteGeneratorTests
{
    /// <summary>
    /// The generated files must come out the same on every machine. Culture-aware comparison - whichever
    /// culture - orders case and punctuation differently from the ordinal comparison that a process in
    /// invariant globalization mode (the usual setting in Linux containers) falls back to, so ordering
    /// has to be ordinal everywhere.
    /// </summary>
    [Test]
    public async Task OrdersTestsOrdinallyWhateverTheCulture()
    {
        var fs = new MemoryFileSystem();
        fs.CreateDirectory("/harness");
        fs.CreateDirectory("/test/built-ins/Foo/bar");
        fs.CreateDirectory("/test/built-ins/FooBar");
        foreach (var path in new[] { "/test/built-ins/Foo/b.js", "/test/built-ins/Foo/Zed.js", "/test/built-ins/Foo/bar/x.js", "/test/built-ins/FooBar/x.js" })
        {
            fs.WriteAllText(path, "/*---\ndescription: ordering\n---*/\n");
        }

        var stream = Test262Stream.FromFileSystem(fs, opts =>
        {
            opts.SubDirectories = ["built-ins"];
            opts.GenerateInverseStrictTestCase = false;
            opts.LogInfo = (_, _) => { };
        });

        var targetPath = Path.Combine(Path.GetTempPath(), "Test262HarnessGeneratorTests", Guid.NewGuid().ToString("N"));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var generator = new TestSuiteGenerator(new TestSuiteGeneratorOptions { TargetPath = targetPath }, usedSettingsFilePath: null);
            await generator.Generate(stream);

            var content = await File.ReadAllTextAsync(Path.Combine(targetPath, "Tests262Harness.Tests.built-ins.generated.cs"));
            content.IndexOf("Foo/Zed.js", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("Foo/b.js", StringComparison.Ordinal));
            content.IndexOf("void FooBar(", StringComparison.Ordinal).Should().BeLessThan(content.IndexOf("void Foo_bar(", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            Directory.Delete(targetPath, recursive: true);
        }
    }
}
