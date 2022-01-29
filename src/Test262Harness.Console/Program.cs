using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;
using Test262Harness;
using Test262Harness.TestSuite.Generator;

var app = new CommandApp();
app.Configure(configurator =>
{
    configurator.AddCommand<GenerateCommand>("generate");
});
return app.Run(args);

[Description("Generates test suite")]
internal sealed class GenerateCommand : AsyncCommand<GenerateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [Description("GitHub SHA for test262 repository commit")]
        [CommandOption("--test262Sha")]
        public string? GitSha { get; set; }

        [Description("Path to write files to")]
        [CommandOption("-t|--targetPath")]
        public string? TargetPath { get; set; }

        [CommandOption("--testFramework")]
        public TestFramework? TestFramework { get; set; }

        [CommandOption("-n|--namespace")]
        public string? Namespace { get; set; }

        [Description("Allows to use custom settings file")]
        [CommandOption("-s|--settings")]
        [DefaultValue("Test262Harness.settings.json")]
        public string SettingsFile { get; set; } = "";
    }

    public override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] Settings settings)
    {
        TestSuiteGeneratorOptions? options = null;
        var settingsFilePath = Path.Combine(Environment.CurrentDirectory, settings.SettingsFile);
        string? usedSettingsFilePath = null;

        if (File.Exists(settingsFilePath))
        {
            var serializerOptions = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            await using var stream = File.OpenRead(settingsFilePath);
            options = await JsonSerializer.DeserializeAsync<TestSuiteGeneratorOptions>(stream, serializerOptions);
            usedSettingsFilePath = settingsFilePath;

            AnsiConsole.MarkupLine("Read settings from [yellow]{0}[/]", settingsFilePath);
        }
        else
        {
            AnsiConsole.MarkupLine("Settings file [yellow]{0}[/] not found, using command line options", settingsFilePath);
        }

        options ??= new TestSuiteGeneratorOptions();

        if (!string.IsNullOrWhiteSpace(settings.GitSha))
        {
            options.GitSha = settings.GitSha;
        }

        if (settings.TestFramework != null)
        {
            options.TestFramework = settings.TestFramework.Value;
        }

        if (!string.IsNullOrWhiteSpace(settings.TargetPath))
        {
            options.TargetPath = settings.TargetPath;
        }

        if (!string.IsNullOrWhiteSpace(settings.Namespace))
        {
            options.Namespace = settings.Namespace;
        }

        var test262Stream = await Test262StreamExtensions.FromGitHub(options.GitSha, configure: options =>
        {
            options.LogInfo = (s, objects) =>
            {
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(s, objects);
            };
            options.LogError = (s, objects) =>
            {
                s = "[red]" + s + "[/]";
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(s, objects);
            };
        });

        var generator = new TestSuiteGenerator(options, usedSettingsFilePath);
        await generator.Generate(test262Stream);

        //await using var fileStream = File.Create(Path.Combine(options.TargetPath, "Test262Settings.settings.sample.json"));
        //await JsonSerializer.SerializeAsync(fileStream, options, new JsonSerializerOptions { WriteIndented = true });
        //AnsiConsole.MarkupLine($"Total file size for [green]{searchPattern}[/] files in [green]{searchPath}[/]: [blue]{totalFileSize:N0}[/] bytes");

        return 0;
    }
}
