using Test262Harness.TestSuite.Generator;

namespace Test262Harness.Tests;

public partial class TestSuiteGeneratorTests
{
    [Test]
    public async Task TemplateHashIsStableAcrossRuns()
    {
        static async Task<string> TemplateShaLine()
        {
            var stream = Test262Stream.FromFileSystem(CreateFixture(), opts =>
            {
                opts.SubDirectories = ["built-ins"];
                opts.LogInfo = (_, _) => { };
            });

            var targetPath = Path.Combine(Path.GetTempPath(), "Test262HarnessGeneratorTests", Guid.NewGuid().ToString("N"));
            try
            {
                await new TestSuiteGenerator(new TestSuiteGeneratorOptions { TargetPath = targetPath }, usedSettingsFilePath: null).Generate(stream);
                var lines = await File.ReadAllLinesAsync(Path.Combine(targetPath, "Tests262Harness.Tests.built-ins.generated.cs"));
                return lines.Single(l => l.Contains("Template SHA:", StringComparison.Ordinal));
            }
            finally
            {
                Directory.Delete(targetPath, recursive: true);
            }
        }

        var first = await TemplateShaLine();
        var second = await TemplateShaLine();

        second.Should().Be(first);
        first.Should().MatchRegex("Template SHA: [0-9A-F]{64}$");
    }
}
