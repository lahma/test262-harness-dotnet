using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Fluid;
using Fluid.Values;
using GlobExpressions;

namespace Test262Harness.TestSuite.Generator;

public class TestSuiteGenerator
{
    private const string TestPrefix = "test/";
    private const string DefaultSuffix = "(default)";
    private const string StrictSuffix = "(strict mode)";

    private static readonly SearchValues<char> _globChars = SearchValues.Create("*[{!?");

    private readonly FluidParser _parser = new();
    private readonly TestSuiteGeneratorOptions _options;
    private readonly string? _usedSettingsFilePath;
    private readonly SearchValues<string> _excludedFilesBothModes;
    private readonly SearchValues<string> _excludedFilesDefaultOnly;
    private readonly SearchValues<string> _excludedFilesStrictOnly;
    private readonly SearchValues<string> _excludedFeatures;
    private readonly SearchValues<string> _excludedFlags;
    private readonly Glob[] _excludedFilesGlobPatterns;
    private readonly SearchValues<string> _nonParallelFiles;
    private readonly SearchValues<string> _nonParallelFeatures;
    private readonly SearchValues<string> _nonParallelFlags;
    private readonly Glob[] _nonParallelFilesGlobPatterns;
    private readonly bool _anyNonParallelConfigured;

    public TestSuiteGenerator(TestSuiteGeneratorOptions options, string? usedSettingsFilePath)
    {
        _options = options;
        _usedSettingsFilePath = usedSettingsFilePath;

        var bothModes = new List<string>();
        var defaultOnly = new List<string>();
        var strictOnly = new List<string>();
        foreach (var raw in _options.ExcludedFiles.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (raw.EndsWith(DefaultSuffix, StringComparison.Ordinal))
            {
                defaultOnly.Add(raw[..^DefaultSuffix.Length]);
            }
            else if (raw.EndsWith(StrictSuffix, StringComparison.Ordinal))
            {
                strictOnly.Add(raw[..^StrictSuffix.Length]);
            }
            else
            {
                bothModes.Add(raw);
            }
        }

        _excludedFilesBothModes = SearchValues.Create([.. bothModes], StringComparison.OrdinalIgnoreCase);
        _excludedFilesDefaultOnly = SearchValues.Create([.. defaultOnly], StringComparison.OrdinalIgnoreCase);
        _excludedFilesStrictOnly = SearchValues.Create([.. strictOnly], StringComparison.OrdinalIgnoreCase);

        _excludedFeatures = SearchValues.Create([.. _options.ExcludedFeatures.Distinct(StringComparer.OrdinalIgnoreCase)], StringComparison.OrdinalIgnoreCase);
        _excludedFlags = SearchValues.Create([.. _options.ExcludedFlags.Distinct(StringComparer.OrdinalIgnoreCase)], StringComparison.OrdinalIgnoreCase);

        _excludedFilesGlobPatterns = _options.ExcludedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => x.AsSpan().ContainsAny(_globChars))
            .Select(x => new Glob(x, GlobOptions.Compiled | GlobOptions.CaseInsensitive))
            .ToArray();

        _nonParallelFiles = SearchValues.Create(
            [.. _options.NonParallelFiles
                .Where(x => !x.AsSpan().ContainsAny(_globChars))
                .Select(x => x.Trim())],
            StringComparison.OrdinalIgnoreCase);

        _nonParallelFeatures = SearchValues.Create([.. _options.NonParallelFeatures.Distinct(StringComparer.OrdinalIgnoreCase)], StringComparison.OrdinalIgnoreCase);
        _nonParallelFlags = SearchValues.Create([.. _options.NonParallelFlags.Distinct(StringComparer.OrdinalIgnoreCase)], StringComparison.OrdinalIgnoreCase);

        _nonParallelFilesGlobPatterns = _options.NonParallelFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => x.AsSpan().ContainsAny(_globChars))
            .Select(x => new Glob(x, GlobOptions.Compiled | GlobOptions.CaseInsensitive))
            .ToArray();

        _anyNonParallelConfigured = _options.NonParallelFiles.Length > 0
                                    || _options.NonParallelFeatures.Length > 0
                                    || _options.NonParallelFlags.Length > 0;
    }

    public async Task<(int TotalTestCaseCount, int IgnoreCount)> Generate(Test262Stream stream)
    {
        var templateOptions = new TemplateOptions
        {
            MemberAccessStrategy = UnsafeMemberAccessStrategy.Instance,
        };

        templateOptions.Filters.AddFilter("methodName", (input, _, _) =>
        {
            var testFile = (Test262File) input.ToObjectValue();
            var name = testFile.FileName.Replace(Path.GetExtension(testFile.FileName), "");
            var cleaned = ConversionUtilities.ConvertToUpperCamelCase(name);
            return new ValueTask<FluidValue>(new StringValue(cleaned));
        });

        var (bootstrapTemplate, bootstrapHash) = await GetTemplate("Test262Test");

        var context = new TemplateContext(templateOptions);
        SetCommonInfo(context, bootstrapHash);

        WriteFile("Tests262Harness.Test262Test.generated.cs", await bootstrapTemplate.RenderAsync(context));

        var totalCount = 0;
        var ignoreCount = 0;
        var (testTemplate, testHash) = await GetTemplate("Tests");
        foreach (var item in stream.Options.SubDirectories)
        {
            var tests = stream.GetTestFiles([item]).ToList();

            if (tests.Count == 0)
            {
                continue;
            }

            string? directoryExcludeReason = null;
            if (_options.ExcludedDirectories.Contains(item))
            {
                directoryExcludeReason = $"Directory {item} was excluded";
            }

            string TestMethodName(string s) => ConversionUtilities.ConvertToUpperCamelCase(s.Replace(item, ""));

            var model = new GeneratorModel
            {
                TestClassName = $"{ConversionUtilities.ConvertToUpperCamelCase(item)}Tests",
                Namespace = _options.Namespace,
                TestCaseGroupings = tests
                    .GroupBy(x =>
                    {
                        var final = x.FileName.AsSpan();
                        if (final.StartsWith("test/"))
                        {
                            final = final.Slice("test/".Length);
                        }

                        if (final.StartsWith(item))
                        {
                            final = final.Slice(item.Length);
                        }

                        final = final.TrimStart('/');

                        // remove file name
                        var slashIndex = final.LastIndexOf('/');
                        if (slashIndex != -1)
                        {
                            final = final.Slice(0, slashIndex);
                        }
                        else
                        {
                            // just remove the extension
                            final = final.Slice(0, final.LastIndexOf('.'));
                        }

                        return final.ToString();
                    })
                    .Select(x =>
                    {
                        var testCases = x
                            .Select(file =>
                            {
                                var excludeReason = directoryExcludeReason ?? GetExcludeReason(file);
                                var nonParallel = excludeReason is null && IsNonParallelizable(file);
                                return new TestCase(file, excludeReason, nonParallel);
                            })
                            .OrderBy(x => x.FileName)
                            .ToList();

                        return new TestCaseGroup(TestMethodName(x.Key), testCases);
                    })
                    .OrderBy(x => x.Name)
                    .ToList()
            };

            context = new TemplateContext(model, templateOptions);
            SetCommonInfo(context, testHash);

            WriteFile($"Tests262Harness.Tests.{item}.generated.cs", await testTemplate.RenderAsync(context));

            totalCount += model.TestCaseGroupings.Sum(x => Math.Max(1, x.TestCases.Count));
            ignoreCount += model.TestCaseGroupings.Sum(x => x.IgnoreCount);
        }

        return (totalCount, ignoreCount);
    }

    private void SetCommonInfo(TemplateContext context, string templateHash)
    {
        context.SetValue("Namespace", _options.Namespace);
        context.SetValue("Parallel", _options.Parallel);
        context.SetValue("CommandLine", Environment.CommandLine);
        context.SetValue("Version", typeof(TestSuiteGenerator).Assembly.GetName().Version);
        context.SetValue("SettingsFile", _usedSettingsFilePath ?? "<none>");
        context.SetValue("SuiteGitSha", _options.SuiteGitSha);
        context.SetValue("SuiteDirectory", _options.SuiteDirectory);
        context.SetValue("TemplateSha", templateHash);
    }

    private static string StripTestPrefix(string fileName)
    {
        return fileName.StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase)
            ? fileName[TestPrefix.Length..]
            : fileName;
    }

    private string? GetExcludeReason(Test262File file)
    {
        var fileName = StripTestPrefix(file.FileName);

        if (_excludedFilesBothModes.Contains(fileName)
            || (file.Strict
                ? _excludedFilesStrictOnly.Contains(fileName)
                : _excludedFilesDefaultOnly.Contains(fileName)))
        {
            return $"File {fileName.ToLowerInvariant()} excluded ({(file.Strict ? "strict mode" : "default")})";
        }

        foreach (var feature in file.Features)
        {
            if (_excludedFeatures.Contains(feature))
            {
                return $"Feature {feature} excluded";
            }
        }

        foreach (var flag in file.Flags)
        {
            if (_excludedFlags.Contains(flag))
            {
                return $"Flag {flag} excluded";
            }
        }

        foreach (var pattern in _excludedFilesGlobPatterns)
        {
            if (pattern.IsMatch(fileName))
            {
                return $"File {fileName.ToLowerInvariant()} excluded (glob pattern)";
            }
        }

        return null;
    }

    private bool IsNonParallelizable(Test262File file)
    {
        if (!_anyNonParallelConfigured)
        {
            return false;
        }

        var fileName = StripTestPrefix(file.FileName);

        if (_nonParallelFiles.Contains(fileName))
        {
            return true;
        }

        foreach (var feature in file.Features)
        {
            if (_nonParallelFeatures.Contains(feature))
            {
                return true;
            }
        }

        foreach (var flag in file.Flags)
        {
            if (_nonParallelFlags.Contains(flag))
            {
                return true;
            }
        }

        foreach (var pattern in _nonParallelFilesGlobPatterns)
        {
            if (pattern.IsMatch(fileName))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<(IFluidTemplate Template, string Hash)> GetTemplate(string name)
    {
        var type = typeof(TestSuiteGenerator);
        var resourceName = $"{type.Namespace}.Templates.{_options.TestFramework}.{name}.liquid";
        var manifestResourceStream = type.Assembly.GetManifestResourceStream(resourceName);
        if (manifestResourceStream is null)
        {
            throw new ArgumentException($"Could not find template {resourceName}");
        }

        string template;
        using (var stream = new StreamReader(manifestResourceStream))
        {
            template = await stream.ReadToEndAsync();
        }

        using var sha = new HMACSHA256();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(template)));

        return (_parser.Parse(template), hash);
    }

    private void WriteFile(string fileName, string contents)
    {
        if (!Directory.Exists(_options.TargetPath))
        {
            Directory.CreateDirectory(_options.TargetPath);
        }

        File.WriteAllText(Path.Combine(_options.TargetPath, fileName), contents);
    }
}
