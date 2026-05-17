using System.Buffers;
using System.Runtime.CompilerServices;
using Cysharp.Text;

namespace Test262Harness.TestSuite.Generator
{
    public static class ConversionUtilities
    {
        private static readonly SearchValues<char> _underscoreSeparators = SearchValues.Create(" /.");

        public static string ConvertToUpperCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            return ConvertDashesToCamelCase(ReplaceSeparatorsWithUnderscore(Capitalize(input)));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string Capitalize(string input)
        {
            if (char.IsUpper(input[0]))
            {
                return input;
            }

            if (input.Length == 1)
            {
                return char.ToUpperInvariant(input[0]).ToString();
            }

            return string.Concat([char.ToUpperInvariant(input[0])], input.AsSpan(1));
        }

        private static string ReplaceSeparatorsWithUnderscore(string input)
        {
            var span = input.AsSpan();
            if (!span.ContainsAny(_underscoreSeparators))
            {
                return input;
            }

            return string.Create(span.Length, input, static (dest, src) =>
            {
                var s = src.AsSpan();
                for (var i = 0; i < s.Length; i++)
                {
                    var c = s[i];
                    dest[i] = c is ' ' or '/' or '.' ? '_' : c;
                }
            });
        }

        private static string ConvertDashesToCamelCase(string input)
        {
            if (!input.Contains('-'))
            {
                // no conversion necessary
                return input;
            }

            // we are removing at least one character
            using var sb = ZString.CreateStringBuilder();
            var caseFlag = false;
            foreach (var c in input)
            {
                if (c == '-')
                {
                    caseFlag = true;
                }
                else if (caseFlag)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    caseFlag = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
