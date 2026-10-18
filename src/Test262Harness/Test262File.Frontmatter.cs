namespace Test262Harness;

public sealed partial class Test262File
{
    /// <summary>
    /// Whether this frontmatter text is read without YamlDotNet. For tests.
    /// </summary>
    internal static bool IsReadWithoutYamlParser(string yaml)
    {
        var onlyStrict = false;
        var noStrict = false;
        return TryReadFrontmatter(yaml.AsSpan(), new Test262File("probe.js"), ref onlyStrict, ref noStrict);
    }

    /// <summary>
    /// Reads frontmatter written in the small subset of YAML that test262 actually uses, without a YAML parser.
    /// </summary>
    /// <remarks>
    /// A general YAML parser costs about 11 KB of setup and per-token objects for every file, against a
    /// frontmatter of a few hundred characters; across a suite of ~54k files that is most of what reading
    /// it costs. The subset handled here is a top-level block mapping whose values are single-line plain or
    /// quoted scalars, plain scalars continued on more-indented lines, <c>|</c> and <c>&gt;</c> block scalars
    /// with clip or strip chomping, flow sequences of plain scalars, block sequences of plain scalars, and
    /// one level of nested mapping of such scalars (<c>negative:</c>).
    /// <para>
    /// Anything outside that subset - comments, tabs, carriage returns, escapes, anchors, tags, more-indented
    /// lines inside a folded scalar, unusual characters - makes this return false before anything is
    /// assigned, and the caller parses the file with YamlDotNet instead, so the YAML dialect accepted and
    /// the errors reported for malformed frontmatter are unchanged.
    /// </para>
    /// </remarks>
    private static bool TryReadFrontmatter(ReadOnlySpan<char> yaml, Test262File test, ref bool onlyStrict, ref bool noStrict)
    {
        if (!FrontmatterReader.IsInSubsetAlphabet(yaml))
        {
            return false;
        }

        // Values are collected first and applied only once the whole frontmatter has been read,
        // so a fallback to YamlDotNet never starts from a half-populated instance.
        string? ecmaScriptId = null, description = null, info = null, author = null;
        string[]? features = null, includes = null, locale = null, flags = null;
        NegativeTestCase? negative = null;

        var reader = new FrontmatterReader(yaml);
        var entries = 0;
        while (true)
        {
            var status = reader.ReadEntry(out var key, out var value);
            if (status == FrontmatterReader.Status.End)
            {
                if (entries == 0)
                {
                    // not a mapping at all: let YamlDotNet report it
                    return false;
                }
                break;
            }

            if (status == FrontmatterReader.Status.Unsupported)
            {
                return false;
            }

            entries++;
            switch (key)
            {
                case FrontmatterReader.Esid:
                    if (!value.TryGetScalar(out ecmaScriptId))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Description:
                    if (!value.TryGetScalar(out description))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Info:
                    if (!value.TryGetScalar(out info))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Author:
                    if (!value.TryGetScalar(out author))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Features:
                    if (!value.TryGetArray(out features))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Includes:
                    if (!value.TryGetArray(out includes))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Locale:
                    if (!value.TryGetArray(out locale))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Flags:
                    if (!value.TryGetArray(out flags))
                    {
                        return false;
                    }
                    break;
                case FrontmatterReader.Negative:
                    if (value.Pairs is null)
                    {
                        return false;
                    }

                    var phase = default(TestingPhase);
                    var expectedErrorType = default(ExpectedErrorType);
                    foreach (var (pairKey, pairValue) in value.Pairs)
                    {
                        switch (pairKey)
                        {
                            case "phase":
                                Enum.TryParse(pairValue, ignoreCase: true, out phase);
                                break;
                            case "type":
                                Enum.TryParse(pairValue, ignoreCase: true, out expectedErrorType);
                                break;
                        }
                    }

                    negative = new NegativeTestCase(phase, expectedErrorType);
                    break;
            }
        }

        if (ecmaScriptId is not null)
        {
            test.EcmaScriptId = ecmaScriptId;
        }

        if (description is not null)
        {
            test.Description = description;
        }

        if (info is not null)
        {
            test.Info = info;
        }

        if (author is not null)
        {
            test.Author = author;
        }

        if (features is not null)
        {
            test._features = features;
        }

        if (includes is not null)
        {
            test._includes = includes;
        }

        if (locale is not null)
        {
            test._locale = locale;
        }

        if (negative is not null)
        {
            test.NegativeTestCase = negative;
        }

        if (flags is not null)
        {
            foreach (var flag in flags)
            {
                switch (flag)
                {
                    case "module":
                        test.Type = ProgramType.Module;
                        break;
                    case "onlyStrict":
                        onlyStrict = true;
                        break;
                    case "noStrict":
                    case "raw":
                        noStrict = true;
                        break;
                }
            }

            test._flags = flags;
        }

        return true;
    }

    /// <summary>
    /// A line-oriented reader over test262 frontmatter. See <see cref="TryReadFrontmatter"/> for the subset.
    /// </summary>
    private ref struct FrontmatterReader
    {
        // Recognised keys come back as these instances, so the caller's switch compares constants and
        // the reader never allocates a key string. "es5id" and "es6id" map to Esid, as they do for YamlDotNet.
        internal const string Esid = "esid";
        internal const string Description = "description";
        internal const string Info = "info";
        internal const string Author = "author";
        internal const string Features = "features";
        internal const string Includes = "includes";
        internal const string Locale = "locale";
        internal const string Negative = "negative";
        internal const string Flags = "flags";

        internal enum Status
        {
            Entry,
            End,
            Unsupported
        }

        internal readonly struct Value
        {
            private Value(string? scalar, string[]? items, List<(string Key, string Value)>? pairs)
            {
                Scalar = scalar;
                Items = items;
                Pairs = pairs;
            }

            public string? Scalar { get; }
            public string[]? Items { get; }
            public List<(string Key, string Value)>? Pairs { get; }

            public static Value FromScalar(string scalar) => new(scalar, null, null);
            public static Value FromItems(string[] items) => new(null, items, null);
            public static Value FromPairs(List<(string Key, string Value)> pairs) => new(null, null, pairs);

            public bool TryGetScalar(out string? scalar)
            {
                scalar = Scalar;
                return scalar is not null;
            }

            public bool TryGetArray(out string[]? items)
            {
                // A single scalar where a sequence is expected is accepted, as the YamlDotNet path does.
                items = Items ?? (Scalar is not null ? [Scalar] : null);
                return items is not null;
            }
        }

#if NET8_0_OR_GREATER
        private static readonly System.Buffers.SearchValues<char> _printableAsciiAndLineFeed = System.Buffers.SearchValues.Create(PrintableAsciiAndLineFeed());

        private static string PrintableAsciiAndLineFeed()
        {
            var chars = new char['~' - ' ' + 2];
            chars[0] = '\n';
            for (var c = ' '; c <= '~'; c++)
            {
                chars[c - ' ' + 1] = c;
            }

            return new string(chars);
        }
#endif

        private readonly ReadOnlySpan<char> _text;
        private int _position;

        internal FrontmatterReader(ReadOnlySpan<char> text)
        {
            _text = text;
            _position = 0;
        }

        /// <summary>
        /// True if every character is one the subset can take at face value: printable for YAML, and not a
        /// tab, a carriage return, a byte order mark or one of the Unicode characters YAML treats as a break.
        /// </summary>
        internal static bool IsInSubsetAlphabet(ReadOnlySpan<char> text)
        {
#if NET8_0_OR_GREATER
            // Nearly all frontmatter is printable ASCII and line feeds: one vectorized scan settles it, and the
            // per-character check below only runs from the first character outside that set, if there is one.
            var first = text.IndexOfAnyExcept(_printableAsciiAndLineFeed);
            if (first < 0)
            {
                return true;
            }

            text = text.Slice(first);
#endif
            foreach (var c in text)
            {
                if (c is '\n' or (>= ' ' and <= '~'))
                {
                    continue;
                }

                if (c is (>= '\u00A0' and <= '\uD7FF') or (>= '\uE000' and <= '\uFFFD') && c is not '\u2028' and not '\u2029' and not '\uFEFF')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        internal Status ReadEntry(out string? key, out Value value)
        {
            key = null;
            value = default;

            SkipBlankLines();
            if (_position >= _text.Length)
            {
                return Status.End;
            }

            var line = ReadLine(out _);
            var colon = KeyLength(line);
            if (colon <= 0)
            {
                return Status.Unsupported;
            }

            key = KnownKey(line.Slice(0, colon));
            var rest = line.Slice(colon + 1).TrimStart(' ');
            return ReadValue(rest, out value) ? Status.Entry : Status.Unsupported;
        }

        private bool ReadValue(ReadOnlySpan<char> rest, out Value value)
        {
            value = default;

            if (rest.IsEmpty)
            {
                return ReadIndentedValue(out value);
            }

            switch (rest[0])
            {
                case '|' or '>':
                    return ReadBlockScalar(rest, out value);
                case '[':
                    return ReadFlowSequence(rest, out value) && NextContentIsAtColumnZero();
                case '\'':
                    return ReadSingleQuoted(rest, out var single) && Assign(single, out value) && NextContentIsAtColumnZero();
                case '"':
                    return ReadDoubleQuoted(rest, out var quoted) && Assign(quoted, out value) && NextContentIsAtColumnZero();
            }

            if (!IsPlainStart(rest) || !IsPlainLine(rest))
            {
                return false;
            }

            return ReadPlainContinuation(rest.TrimEnd(' '), out value);
        }

        private static bool Assign(string scalar, out Value value)
        {
            value = Value.FromScalar(scalar);
            return true;
        }

        /// <summary>
        /// A plain scalar that started on the key's line, continued by any more-indented lines that follow it.
        /// </summary>
        private bool ReadPlainContinuation(ReadOnlySpan<char> first, out Value value)
        {
            value = default;
            if (NextContentIsAtColumnZero())
            {
                value = Value.FromScalar(first.ToString());
                return true;
            }

            // A blank line inside a multi-line plain scalar folds to a line feed; leave that to YamlDotNet.
            if (NextLineIsBlank())
            {
                return false;
            }

            var parts = new List<string> { first.ToString() };
            return ReadPlainLines(parts, out value);
        }

        /// <summary>
        /// The value of a key with nothing after its colon: null, or a block sequence, a nested mapping or a
        /// plain scalar on the more-indented lines below.
        /// </summary>
        private bool ReadIndentedValue(out Value value)
        {
            value = default;

            if (NextContentIsAtColumnZero())
            {
                // A sequence written at the key's own indentation is still a sequence.
                var save = _position;
                SkipBlankLines();
                var atEnd = _position >= _text.Length;
                var next = atEnd ? default : PeekLine();
                _position = save;
                if (!atEnd && next.Length > 0 && next[0] == '-')
                {
                    return false;
                }

                value = Value.FromScalar("");
                return true;
            }

            if (NextLineIsBlank())
            {
                return false;
            }

            var line = PeekLine();
            var indent = Indent(line);
            var content = line.Slice(indent);

            if (content[0] == '-' && (content.Length == 1 || content[1] == ' '))
            {
                return ReadBlockSequence(indent, out value);
            }

            var colon = KeyLength(content);
            if (colon > 0)
            {
                return ReadNestedMapping(indent, out value);
            }

            if (!IsPlainStart(content))
            {
                return false;
            }

            return ReadPlainLines(new List<string>(), out value);
        }

        /// <summary>
        /// Folds the more-indented lines that follow into the plain scalar collected so far.
        /// </summary>
        private bool ReadPlainLines(List<string> parts, out Value value)
        {
            value = default;
            while (!NextContentIsAtColumnZero())
            {
                if (NextLineIsBlank())
                {
                    return false;
                }

                var line = ReadLine(out _).Trim(' ');
                if (!IsPlainStart(line) || !IsPlainLine(line))
                {
                    return false;
                }

                parts.Add(line.ToString());
            }

            value = Value.FromScalar(string.Join(" ", parts));
            return true;
        }

        private bool ReadBlockSequence(int indent, out Value value)
        {
            value = default;
            var items = new List<string>();
            while (true)
            {
                var line = ReadLine(out _);
                if (Indent(line) != indent)
                {
                    return false;
                }

                var item = line.Slice(indent);
                if (item.Length < 3 || item[0] != '-' || item[1] != ' ')
                {
                    return false;
                }

                item = item.Slice(2).Trim(' ');
                if (item.IsEmpty || !IsPlainStart(item) || !IsPlainLine(item))
                {
                    return false;
                }

                items.Add(item.ToString());

                if (NextContentIsAtColumnZero())
                {
                    break;
                }

                SkipBlankLines();
                if (Indent(PeekLine()) != indent)
                {
                    return false;
                }
            }

            value = Value.FromItems(items.ToArray());
            return true;
        }

        private bool ReadNestedMapping(int indent, out Value value)
        {
            value = default;
            var pairs = new List<(string Key, string Value)>();
            while (true)
            {
                var line = ReadLine(out _);
                if (Indent(line) != indent)
                {
                    return false;
                }

                line = line.Slice(indent);
                var colon = KeyLength(line);
                if (colon <= 0)
                {
                    return false;
                }

                var rest = line.Slice(colon + 1).TrimStart(' ');
                string scalar;
                if (rest.IsEmpty)
                {
                    return false;
                }
                else if (rest[0] == '\'')
                {
                    if (!ReadSingleQuoted(rest, out scalar))
                    {
                        return false;
                    }
                }
                else if (rest[0] == '"')
                {
                    if (!ReadDoubleQuoted(rest, out scalar))
                    {
                        return false;
                    }
                }
                else if (IsPlainStart(rest) && rest[0] is not '|' and not '>' and not '[' && IsPlainLine(rest))
                {
                    scalar = rest.TrimEnd(' ').ToString();
                }
                else
                {
                    return false;
                }

                pairs.Add((line.Slice(0, colon).ToString(), scalar));

                if (NextContentIsAtColumnZero())
                {
                    break;
                }

                SkipBlankLines();
                if (Indent(PeekLine()) != indent)
                {
                    return false;
                }
            }

            value = Value.FromPairs(pairs);
            return true;
        }

        private bool ReadBlockScalar(ReadOnlySpan<char> header, out Value value)
        {
            value = default;

            var literal = header[0] == '|';
            var strip = header.Length > 1 && header[1] == '-';
            if (!IsBlank(header.Slice(strip ? 2 : 1)))
            {
                // keep chomping, explicit indentation, or a comment
                return false;
            }

            // Leading blank lines and an empty body have rules of their own; leave those to YamlDotNet.
            if (_position >= _text.Length || NextLineIsBlank())
            {
                return false;
            }

            var indent = Indent(PeekLine());
            if (indent == 0)
            {
                return false;
            }

            var builder = new System.Text.StringBuilder();
            var lines = 0;
            var pendingBlank = 0;
            var lastHadBreak = false;
            while (_position < _text.Length)
            {
                var line = PeekLine();
                if (IsBlank(line))
                {
                    if (line.Length > indent)
                    {
                        // white space beyond the indentation is content in a literal scalar
                        return false;
                    }

                    ReadLine(out _);
                    pendingBlank++;
                    continue;
                }

                var lineIndent = Indent(line);
                if (lineIndent < indent)
                {
                    if (lineIndent > 0)
                    {
                        return false;
                    }
                    break;
                }

                var text = line.Slice(indent);
                if (!literal && text[0] == ' ')
                {
                    // more-indented lines are not folded
                    return false;
                }

                if (lines > 0)
                {
                    if (literal)
                    {
                        builder.Append('\n', pendingBlank + 1);
                    }
                    else if (pendingBlank > 0)
                    {
                        builder.Append('\n', pendingBlank);
                    }
                    else
                    {
                        builder.Append(' ');
                    }
                }
                else if (pendingBlank > 0)
                {
                    return false;
                }

                Append(builder, text);
                ReadLine(out lastHadBreak);
                lines++;
                pendingBlank = 0;
            }

            if (!strip && lastHadBreak)
            {
                builder.Append('\n');
            }

            value = Value.FromScalar(builder.ToString());
            return true;
        }

        private bool ReadFlowSequence(ReadOnlySpan<char> rest, out Value value)
        {
            value = default;

            var close = rest.IndexOf(']');
            string inner;
            if (close >= 0)
            {
                if (!IsBlank(rest.Slice(close + 1)))
                {
                    return false;
                }
                inner = rest.Slice(1, close - 1).ToString();
            }
            else
            {
                // The sequence continues on more-indented lines; the line breaks between them fold to spaces.
                var builder = new System.Text.StringBuilder();
                Append(builder, rest.Slice(1).Trim(' '));
                while (true)
                {
                    if (_position >= _text.Length || NextLineIsBlank())
                    {
                        return false;
                    }

                    var line = ReadLine(out _);
                    if (Indent(line) == 0)
                    {
                        return false;
                    }

                    line = line.Trim(' ');
                    close = line.IndexOf(']');
                    builder.Append(' ');
                    if (close >= 0)
                    {
                        if (!IsBlank(line.Slice(close + 1)))
                        {
                            return false;
                        }
                        Append(builder, line.Slice(0, close));
                        break;
                    }

                    Append(builder, line);
                }

                inner = builder.ToString();
            }

            var span = inner.AsSpan();
            if (span.IndexOfAny("[]{}'\"#:&*!|>%@`".AsSpan()) >= 0)
            {
                return false;
            }

            if (IsBlank(span))
            {
                value = Value.FromItems([]);
                return true;
            }

            var count = 1;
            foreach (var c in span)
            {
                if (c == ',')
                {
                    count++;
                }
            }

            var items = new string[count];
            for (var i = 0; i < count; i++)
            {
                var comma = span.IndexOf(',');
                var item = (comma >= 0 ? span.Slice(0, comma) : span).Trim(' ');
                if (item.IsEmpty || !IsPlainStart(item))
                {
                    return false;
                }

                items[i] = item.ToString();
                span = comma >= 0 ? span.Slice(comma + 1) : default;
            }

            value = Value.FromItems(items);
            return true;
        }

        /// <summary>
        /// A single-quoted scalar that closes on this line with nothing but spaces after it.
        /// </summary>
        private static bool ReadSingleQuoted(ReadOnlySpan<char> rest, out string value)
        {
            value = "";
            System.Text.StringBuilder? builder = null;
            var start = 1;
            for (var i = 1; i < rest.Length; i++)
            {
                if (rest[i] != '\'')
                {
                    continue;
                }

                if (i + 1 < rest.Length && rest[i + 1] == '\'')
                {
                    // '' is an escaped quote
                    builder ??= new System.Text.StringBuilder();
                    Append(builder, rest.Slice(start, i + 1 - start));
                    start = i + 2;
                    i++;
                    continue;
                }

                if (!IsBlank(rest.Slice(i + 1)))
                {
                    return false;
                }

                var tail = rest.Slice(start, i - start);
                if (builder is null)
                {
                    value = tail.ToString();
                }
                else
                {
                    Append(builder, tail);
                    value = builder.ToString();
                }

                return true;
            }

            // a single-quoted scalar continued on the next line
            return false;
        }

        /// <summary>
        /// A double-quoted scalar without escapes that closes on this line with nothing but spaces after it.
        /// </summary>
        private static bool ReadDoubleQuoted(ReadOnlySpan<char> rest, out string value)
        {
            value = "";
            for (var i = 1; i < rest.Length; i++)
            {
                switch (rest[i])
                {
                    case '\\':
                        // escapes are YamlDotNet's job
                        return false;
                    case '"':
                        if (!IsBlank(rest.Slice(i + 1)))
                        {
                            return false;
                        }

                        value = rest.Slice(1, i - 1).ToString();
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Length of the key at the start of the line when it is a simple <c>key:</c> followed by a space or the
        /// end of the line, otherwise -1.
        /// </summary>
        private static int KeyLength(ReadOnlySpan<char> line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
                {
                    continue;
                }

                if (i > 0 && c is '-' or '.')
                {
                    continue;
                }

                if (c == ':' && i > 0 && (i + 1 == line.Length || line[i + 1] == ' '))
                {
                    return i;
                }

                return -1;
            }

            return -1;
        }

        private static string? KnownKey(ReadOnlySpan<char> key)
        {
            return key.Length switch
            {
                4 when key.SequenceEqual(Esid.AsSpan()) => Esid,
                4 when key.SequenceEqual(Info.AsSpan()) => Info,
                5 when key.SequenceEqual("es5id".AsSpan()) || key.SequenceEqual("es6id".AsSpan()) => Esid,
                5 when key.SequenceEqual(Flags.AsSpan()) => Flags,
                6 when key.SequenceEqual(Author.AsSpan()) => Author,
                6 when key.SequenceEqual(Locale.AsSpan()) => Locale,
                8 when key.SequenceEqual(Features.AsSpan()) => Features,
                8 when key.SequenceEqual(Includes.AsSpan()) => Includes,
                8 when key.SequenceEqual(Negative.AsSpan()) => Negative,
                11 when key.SequenceEqual(Description.AsSpan()) => Description,
                _ => null
            };
        }

        /// <summary>
        /// True when the scalar may start a plain scalar: no indicator character a YAML parser would treat
        /// specially in first position.
        /// </summary>
        private static bool IsPlainStart(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty)
            {
                return false;
            }

            switch (text[0])
            {
                case '[' or ']' or '{' or '}' or ',' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`':
                    return false;
                case '-' or '?' or ':':
                    return text.Length > 1 && text[1] != ' ';
                default:
                    return !IsDocumentMarker(text);
            }
        }

        private static bool IsDocumentMarker(ReadOnlySpan<char> text)
        {
            return text.StartsWith("---".AsSpan()) || text.StartsWith("...".AsSpan());
        }

        /// <summary>
        /// True when the line can be (part of) a plain scalar in block context: nothing that would start a
        /// comment or a mapping value.
        /// </summary>
        private static bool IsPlainLine(ReadOnlySpan<char> text)
        {
            text = text.TrimEnd(' ');
            return text.Length > 0
                   && text[text.Length - 1] != ':'
                   && text.IndexOf(": ".AsSpan()) < 0
                   && text.IndexOf(" #".AsSpan()) < 0;
        }

        private static int Indent(ReadOnlySpan<char> line)
        {
            var i = 0;
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }
            return i;
        }

        private static bool IsBlank(ReadOnlySpan<char> text)
        {
            return text.TrimStart(' ').IsEmpty;
        }

        private ReadOnlySpan<char> PeekLine()
        {
            var remaining = _text.Slice(_position);
            var end = remaining.IndexOf('\n');
            return end >= 0 ? remaining.Slice(0, end) : remaining;
        }

        private ReadOnlySpan<char> ReadLine(out bool hadBreak)
        {
            var remaining = _text.Slice(_position);
            var end = remaining.IndexOf('\n');
            hadBreak = end >= 0;
            if (end < 0)
            {
                _position = _text.Length;
                return remaining;
            }

            _position += end + 1;
            return remaining.Slice(0, end);
        }

        private bool NextLineIsBlank()
        {
            return _position < _text.Length && IsBlank(PeekLine());
        }

        private void SkipBlankLines()
        {
            while (_position < _text.Length && IsBlank(PeekLine()))
            {
                ReadLine(out _);
            }
        }

        /// <summary>
        /// True when the next non-blank line starts at column 0, or there is none: the current value is over.
        /// Does not consume anything.
        /// </summary>
        private bool NextContentIsAtColumnZero()
        {
            var save = _position;
            SkipBlankLines();
            var result = _position >= _text.Length || Indent(PeekLine()) == 0;
            _position = save;
            return result;
        }

        private static void Append(System.Text.StringBuilder builder, ReadOnlySpan<char> text)
        {
#if NET6_0_OR_GREATER
            builder.Append(text);
#else
            foreach (var c in text)
            {
                builder.Append(c);
            }
#endif
        }
    }
}
