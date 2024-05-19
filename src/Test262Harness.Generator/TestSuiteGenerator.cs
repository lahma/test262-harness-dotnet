using System.Security.Cryptography;
using System.Text;
using Fluid;
using Fluid.Values;
using GlobExpressions;

namespace Test262Harness.TestSuite.Generator;

public class TestSuiteGenerator
{
    private readonly FluidParser _parser = new();
    private readonly TestSuiteGeneratorOptions _options;
    private readonly string? _usedSettingsFilePath;
    private readonly Dictionary<(string Name, bool Strict), string> _excludedFiles;
    private readonly Dictionary<string,string> _excludedFeatures;
    private readonly Dictionary<string,string> _excludedFlags;
    private readonly Glob[] _excludedFilesGlobPatterns;

    public TestSuiteGenerator(TestSuiteGeneratorOptions options, string? usedSettingsFilePath)
    {
        _options = options;
        _usedSettingsFilePath = usedSettingsFilePath;

        _excludedFiles =_options.ExcludedFiles
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            // translate esprima format
            .SelectMany(x =>
            {
                if (x.EndsWith("(default)", StringComparison.Ordinal))
                {
                    return new (string Name, bool Strict)[] { (x.Substring(0, x.Length - "(default)".Length), false) };
                }

                if (x.EndsWith("(strict mode)", StringComparison.Ordinal))
                {
                    return new (string Name, bool Strict)[] { (x.Substring(0, x.Length - "(strict mode)".Length), true) };
                }
                else
                {
                    // as-is and both
                    return new (string Name, bool Strict)[]
                    {
                        (x, false),
                        (x, true)
                    };
                }
            })
            .ToDictionary(x => x, x => $"File {x.Name} excluded ({(string?) (x.Strict ? "strict mode" : "default")})");

        _excludedFeatures = _options.ExcludedFeatures.Distinct().ToDictionary(x => x, x => $"Feature {x} excluded", StringComparer.OrdinalIgnoreCase);
        _excludedFlags = _options.ExcludedFlags.Distinct().ToDictionary(x => x, x => $"Flag {x} excluded", StringComparer.OrdinalIgnoreCase);

        var globChars = new[] { '*', '[', '{', '!', '?' };
        _excludedFilesGlobPatterns = _options.ExcludedFiles
            .Distinct()
            .Where(x => x.IndexOfAny(globChars) != -1)
            .Select(x => new Glob(x.ToLowerInvariant(), GlobOptions.Compiled | GlobOptions.CaseInsensitive))
            .ToArray();
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
            var tests = stream.GetTestFiles(new [] { item }).ToList();

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
                            .Select(x =>
                            {
                                var excludeReason = directoryExcludeReason ?? GetExcludeReason(x);
                                return new TestCase(x, excludeReason);
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

    private string? GetExcludeReason(Test262File file)
    {
        const string TestPrefix = "test/";
        var fileName = file.FileName.StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase) ? file.FileName.Substring(TestPrefix.Length) : file.FileName;
        fileName = fileName.ToLowerInvariant();

        _excludedFiles.TryGetValue((fileName, file.Strict), out var excludeReason);

        if (excludeReason is null)
        {
            foreach (var feature in file.Features)
            {
                if (_excludedFeatures.TryGetValue(feature, out excludeReason))
                {
                    break;
                }
            }
        }

        if (excludeReason is null)
        {
            foreach (var flag in file.Flags)
            {
                if (_excludedFlags.TryGetValue(flag, out excludeReason))
                {
                    break;
                }
            }
        }

        if (excludeReason is null)
        {
            foreach (var pattern in _excludedFilesGlobPatterns)
            {
                if (pattern.IsMatch(fileName))
                {
                    return $"File {fileName} excluded (glob pattern)";
                }
            }
        }

        return excludeReason;
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
        var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(template))).Replace("-", "").ToLowerInvariant();

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
