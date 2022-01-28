using Fluid;
using Fluid.Values;

namespace Test262Harness.TestSuite.Generator;

public class TestSuiteGenerator
{
    private readonly FluidParser _parser = new();
    private readonly TestSuiteGeneratorOptions _options;
    private readonly Dictionary<string,string> _excludedFiles;
    private readonly Dictionary<string,string> _excludedFeatures;
    private readonly Dictionary<string,string> _excludedFlags;

    public TestSuiteGenerator(TestSuiteGeneratorOptions options)
    {
        _options = options;

        // TODO expand *
        _excludedFiles =_options.ExcludedFiles.Distinct().ToDictionary(x => x, x => $"File {x} excluded", StringComparer.OrdinalIgnoreCase);
        _excludedFeatures = _options.ExcludedFeatures.Distinct().ToDictionary(x => x, x => $"Feature {x} excluded", StringComparer.OrdinalIgnoreCase);
        _excludedFlags = _options.ExcludedFlags.Distinct().ToDictionary(x => x, x => $"Flag {x} excluded", StringComparer.OrdinalIgnoreCase);
    }

    public async Task Generate(Test262Stream stream)
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

        var context = new TemplateContext(templateOptions);
        context.SetValue("Namespace", _options.Namespace);
        context.SetValue("Parallel", _options.Parallel);
        var bootstrapTemplate = await GetTemplate("Test262Test");
        WriteFile("Tests262Harness.Test262Test.generated.cs", await bootstrapTemplate.RenderAsync(context));

        var testTemplate = await GetTemplate("Tests");
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
                GitSha = _options.GitSha,
                TestClassName = ConversionUtilities.ConvertToUpperCamelCase(item) + "Tests",
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
                        return new TestCaseGroup
                        {
                            Name = TestMethodName(x.Key!),
                            TestCases = x
                                .Select(x =>
                                {
                                    var excludeReason = directoryExcludeReason ?? GetExcludeReason(x);
                                    return new TestCase(x, excludeReason);
                                })
                                .OrderBy(x => x.FileName)
                                .ToList()
                        };
                    })
                    .OrderBy(x => x.Name)
                    .ToList()
            };

            context = new TemplateContext(model, templateOptions);

            WriteFile($"Tests262Harness.Tests.{item}.generated.cs", await testTemplate.RenderAsync(context));
        }
    }

    private string? GetExcludeReason(Test262File file)
    {
        var name = file.FileName.StartsWith("test/") ? file.FileName.Substring("test/".Length) : file.FileName;
        _excludedFiles.TryGetValue(name, out var excludeReason);

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

        return excludeReason;
    }

    private async Task<IFluidTemplate> GetTemplate(string name)
    {
        var type = typeof(TestSuiteGenerator);
        var resourceName = $"{type.Namespace}.Templates.{_options.TestFramework}.{name}.liquid";
        var manifestResourceStream = type.Assembly.GetManifestResourceStream(resourceName);
        if (manifestResourceStream is null)
        {
            throw new Exception("Could not find template " + resourceName);
        }

        string template;
        using (var stream = new StreamReader(manifestResourceStream))
        {
            template = await stream.ReadToEndAsync();
        }

        return _parser.Parse(template);
    }

    private void WriteFile(string fileName, string contents)
    {
        File.WriteAllText(Path.Combine(_options.TargetPath, fileName), contents);
    }
}
