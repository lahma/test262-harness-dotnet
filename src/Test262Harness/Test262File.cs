using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Test262Harness;

public sealed class Test262File
{
    private const string YamlSectionStart = "/*---";
    private const string YamlSectionEnd = "---*/";


    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .Build();

    public string FileName { get; set; } = "";

    [YamlMember(Alias = "es5id")]
    public string EcmaScript5Id { get; set; } = "";

    [YamlMember(Alias = "es6id")]
    public string EcmaScript6Id { get; set; } = "";

    [YamlMember(Alias = "esid")]
    public string EcmaScriptId { get; set; } = "";

    public string Author { get; set; } = "";

    public string Description { get; set; } = "";

    public string Info { get; set; } = "";

    public string[] Locale { get; set; } = Array.Empty<string>();

    public string[] Features { get; set; } = Array.Empty<string>();

    public string[] Flags { get; set; } = Array.Empty<string>();

    public string[] Includes { get; set; } = Array.Empty<string>();

    [YamlMember(Alias = "negative")]
    public NegativeTestCase? NegativeTestCase { get; set; }

    public string Contents { get; private set; } = "";

    public bool Strict { get; set; }

    public bool Negative => Array.IndexOf(Flags, "negative") != -1 || NegativeTestCase is not null;

    public string FormatFileLine()
    {
        var mode = Strict ? "strict mode" : "default";
        return $"{FileName}({mode})";
    }

    public static IEnumerable<Test262File> FromFile(string filePath)
    {
        var content = File.ReadAllText(filePath);

        var start = content.IndexOf(YamlSectionStart, StringComparison.OrdinalIgnoreCase) + YamlSectionStart.Length;
        var end = content.IndexOf(YamlSectionEnd, StringComparison.OrdinalIgnoreCase);
        var yaml = content.Substring(start, end - start);

        var test = _yamlDeserializer.Deserialize<Test262File>(yaml);

        var isScript = Array.IndexOf(test.Flags, "module") < 0;

        if (isScript && Array.IndexOf(test.Flags, "onlyStrict") < 0)
        {
            test.Contents = content;
            test.Strict = false;
            yield return test;
        }

        if (Array.IndexOf(test.Flags, "noStrict") < 0)
        {
            // TODO efficient clone
            var strictVersion = _yamlDeserializer.Deserialize<Test262File>(yaml);
            strictVersion.Strict = true;
            yield return strictVersion;
        }
    }
}
