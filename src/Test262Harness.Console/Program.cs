using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public sealed class Settings : CommandSettings
    {
        [Description("GitHub SHA for test262 repository commit")]
        [CommandOption("--test262Sha")]
        public string? SuiteGitSha { get; set; }

        [Description("Directory for test262 repository files")]
        [CommandOption("--test262Directory")]
        public string? SuiteDirectory { get; set; }

        [Description("Path to write files to")]
        [CommandOption("-t|--targetPath")]
        public string? TargetPath { get; set; }

        [CommandOption("--testFramework")]
        public TestFramework? TestFramework { get; set; }

        [CommandOption("-n|--namespace")]
        public string? Namespace { get; set; }

        [CommandOption("--excluded-files-source")]
        public string? ExcludedFilesSource { get; set; }

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
            await using var stream = File.OpenRead(settingsFilePath);
            options = await JsonSerializer.DeserializeAsync<TestSuiteGeneratorOptions>(stream, _serializerOptions);
            usedSettingsFilePath = settingsFilePath;

            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "Read settings from [yellow]{0}[/]", settingsFilePath);
        }
        else
        {
            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "Settings file [yellow]{0}[/] not found, using command line options", settingsFilePath);
        }

        options ??= new TestSuiteGeneratorOptions();

        await FinalizeOptions(settings, options);

        Action<Test262StreamOptions> configureOptions = options =>
        {
            options.LogInfo = (s, objects) =>
            {
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, s, objects);
            };
            options.LogError = (s, objects) =>
            {
                s = $"[red]{s}[/]";
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, s, objects);
            };
        };

        Test262Stream test262Stream;
        if (!string.IsNullOrWhiteSpace(options.SuiteGitSha))
        {
            AnsiConsole.MarkupLine($"Downloading from GitHub repository using commit [yellow]{options.SuiteGitSha}[/]");
            test262Stream = await Test262StreamExtensions.FromGitHub(options.SuiteGitSha, configure: configureOptions);
        }
        else if (!string.IsNullOrWhiteSpace(options.SuiteDirectory))
        {
            test262Stream = Test262Stream.FromDirectory(options.SuiteDirectory, configureOptions);
            AnsiConsole.MarkupLine($"Loading from file repository at [yellow]{options.SuiteDirectory}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Need to provide either tests262 directory or commit SHA[/]");
            return 1;
        }

        var generator = new TestSuiteGenerator(options, usedSettingsFilePath);
        var (totalCount, ignoreCount) = await generator.Generate(test262Stream);

        //await using var fileStream = File.Create(Path.Combine(options.TargetPath, "Test262Settings.settings.sample.json"));
        //await JsonSerializer.SerializeAsync(fileStream, options, new JsonSerializerOptions { WriteIndented = true });

        AnsiConsole.MarkupLine($"Generated [green]{totalCount}[/] test cases");
        AnsiConsole.MarkupLine($"Total of [yellow]{ignoreCount}[/] test cases were ignored (some might have been grouped into single folder ignore)");

        return 0;
    }

    private static async Task FinalizeOptions(Settings settings, TestSuiteGeneratorOptions options)
    {
        if (!string.IsNullOrWhiteSpace(settings.SuiteGitSha))
        {
            options.SuiteGitSha = settings.SuiteGitSha;
        }

        if (!string.IsNullOrWhiteSpace(settings.SuiteDirectory))
        {
            options.SuiteDirectory = settings.SuiteDirectory;
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

        if (!string.IsNullOrWhiteSpace(settings.ExcludedFilesSource))
        {
            options.ExcludedFilesSource = settings.ExcludedFilesSource;
        }

        if (!string.IsNullOrWhiteSpace(options.ExcludedFilesSource))
        {
            var lines = await File.ReadAllLinesAsync(options.ExcludedFilesSource);

            var valid = lines
                .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'))
                // support esprima format
                .Select(x => x.StartsWith("test/", StringComparison.OrdinalIgnoreCase) ? x.Substring("test/".Length) : x);

            options.ExcludedFiles = options.ExcludedFiles.Concat(valid).ToArray();
        }
    }
}
