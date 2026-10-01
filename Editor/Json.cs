using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aeternum.Packages
{
    /// <summary>
    /// A small JSON reader: objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double. JsonUtility can't read dictionaries (the manifest's dependencies) or
    /// top-level arrays (most GitHub API answers).
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            var parser = new Parser(text ?? throw new ArgumentNullException(nameof(text)));
            parser.SkipWhitespace();
            var value = parser.ReadValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("unexpected text after the JSON value");
            return value;
        }

        public static Dictionary<string, object> ParseObject(string text) =>
            Parse(text) as Dictionary<string, object> ?? throw new FormatException("Expected a JSON object.");

        public static List<object> ParseArray(string text) =>
            Parse(text) as List<object> ?? throw new FormatException("Expected a JSON array.");

        public static string GetString(this Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) ? value as string : null;

        public static Dictionary<string, object> GetObject(this Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) ? value as Dictionary<string, object> : null;

        public static List<object> GetArray(this Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) ? value as List<object> : null;

        sealed class Parser
        {
            readonly string text;
            int position;

            public Parser(string text) => this.text = text;

            public bool AtEnd => position >= text.Length;

            public FormatException Error(string message) =>
                new FormatException($"Invalid JSON at character {position}: {message}.");

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(text[position]))
                    position++;
            }

            public object ReadValue()
            {
                if (AtEnd)
                    throw Error("unexpected end");
                switch (text[position])
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default: return ReadNumber();
                }
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                position++;
                SkipWhitespace();
                if (Peek('}'))
                {
                    position++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    if (!Peek('"'))
                        throw Error("expected a property name");
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        position++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                position++;
                SkipWhitespace();
                if (Peek(']'))
                {
                    position++;
                    return result;
                }
                while (true)
                {
                    SkipWhitespace();
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        position++;
                        continue;
                    }
                    Expect(']');
                    return result;
                }
            }

            string ReadString()
            {
                position++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("unterminated string");
                    var c = text[position++];
                    if (c == '"')
                        return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }
                    if (AtEnd)
                        throw Error("unterminated escape");
                    var escape = text[position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length ||
                                !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                                throw Error("bad \\u escape");
                            builder.Append((char)code);
                            position += 4;
                            break;
                        default:
                            throw Error($"bad escape \\{escape}");
                    }
                }
            }

            double ReadNumber()
            {
                var start = position;
                while (!AtEnd && "+-0123456789.eE".IndexOf(text[position]) >= 0)
                    position++;
                if (start == position ||
                    !double.TryParse(text.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    position = start;
                    throw Error("expected a value");
                }
                return number;
            }

            void ReadLiteral(string literal)
            {
                if (string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                    throw Error("expected a value");
                position += literal.Length;
            }

            bool Peek(char c) => !AtEnd && text[position] == c;

            void Expect(char c)
            {
                if (!Peek(c))
                    throw Error($"expected '{c}'");
                position++;
            }
        }
    }
}
