using System.Runtime.CompilerServices;
using Test262Harness.TestSuite.Generator;
using Zio;
using Zio.FileSystems;

namespace Test262Harness.Tests;

public class TestSuiteGeneratorTests
{
    private static MemoryFileSystem CreateFixture()
    {
        var fs = new MemoryFileSystem();

        fs.CreateDirectory("/harness");
        fs.WriteAllText("/harness/assert.js", "// assert harness stub\n");
        fs.WriteAllText("/harness/sta.js", "// sta harness stub\n");

        fs.CreateDirectory("/test/built-ins/Atomics/waitAsync");
        fs.WriteAllText("/test/built-ins/Atomics/waitAsync/descriptor.js",
            "/*---\ndescription: Atomics.waitAsync descriptor\nfeatures: [Atomics.waitAsync, Atomics]\n---*/\n// body\n");
        fs.WriteAllText("/test/built-ins/Atomics/waitAsync/length.js",
            "/*---\ndescription: Atomics.waitAsync length\nfeatures: [Atomics.waitAsync, Atomics]\n---*/\n// body\n");

        fs.CreateDirectory("/test/built-ins/Foo");
        fs.WriteAllText("/test/built-ins/Foo/bar.js",
            "/*---\ndescription: Foo bar\nfeatures: [Foo]\n---*/\n// body\n");

        fs.CreateDirectory("/test/built-ins/Async");
        fs.WriteAllText("/test/built-ins/Async/baz.js",
            "/*---\ndescription: Async baz\nflags: [async]\n---*/\n// body\n");

        return fs;
    }

    private static async Task<string> GenerateBuiltInsOutput(TestSuiteGeneratorOptions options)
    {
        var fs = CreateFixture();
        var stream = Test262Stream.FromFileSystem(fs, opts =>
        {
            opts.SubDirectories = ["built-ins"];
            opts.GenerateInverseStrictTestCase = false;
            opts.LogInfo = (_, _) => { };
            opts.LogError = (_, _) => { };
        });

        var targetPath = Path.Combine(Path.GetTempPath(), "Test262HarnessGeneratorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetPath);

        try
        {
            options.TargetPath = targetPath;
            options.Namespace = "Generated.Tests";
            options.SuiteGitSha = "stable-sha-for-snapshot";

            var generator = new TestSuiteGenerator(options, usedSettingsFilePath: "stable-settings-path");
            await generator.Generate(stream);

            var content = await File.ReadAllTextAsync(Path.Combine(targetPath, "Tests262Harness.Tests.built-ins.generated.cs"));
            return StripGeneratedHeader(content);
        }
        finally
        {
            Directory.Delete(targetPath, recursive: true);
        }
    }

    private static string StripGeneratedHeader(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n').ToList();
        var firstDivider = lines.FindIndex(l => l.StartsWith("//----", StringComparison.Ordinal));
        var secondDivider = firstDivider >= 0
            ? lines.FindIndex(firstDivider + 1, l => l.StartsWith("//----", StringComparison.Ordinal))
            : -1;
        if (secondDivider > 0)
        {
            lines.RemoveRange(firstDivider, secondDivider - firstDivider + 1);
        }
        return string.Join("\n", lines).TrimStart('\n');
    }

    private static SettingsTask VerifyOutput(string output, [CallerMemberName] string? testName = null) =>
        Verifier.Verify(output, "cs")
            .UseDirectory("Snapshots")
            .UseMethodName(testName!);

    [Test]
    public async Task DefaultSettingsEmitsNoNonParallelizable()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions());
        await VerifyOutput(output);
    }

    [Test]
    public async Task NonParallelFeatureEmitsAttributeOnMatchedGroup()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            NonParallelFeatures = ["Atomics.waitAsync"],
        });
        await VerifyOutput(output);
    }

    [Test]
    public async Task NonParallelFlagEmitsAttributeOnMatchedGroup()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            NonParallelFlags = ["async"],
        });
        await VerifyOutput(output);
    }

    [Test]
    public async Task NonParallelFileGlobEmitsAttribute()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            NonParallelFiles = ["built-ins/Atomics/waitAsync/*.js"],
        });
        await VerifyOutput(output);
    }

    [Test]
    public async Task NonParallelFileExactEmitsAttribute()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            NonParallelFiles = ["built-ins/Atomics/waitAsync/descriptor.js"],
        });
        await VerifyOutput(output);
    }

    [Test]
    public async Task IgnoredGroupDoesNotGetNonParallelizable()
    {
        var output = await GenerateBuiltInsOutput(new TestSuiteGeneratorOptions
        {
            ExcludedFeatures = ["Atomics.waitAsync"],
            NonParallelFeatures = ["Atomics.waitAsync"],
        });
        await VerifyOutput(output);
    }
}
