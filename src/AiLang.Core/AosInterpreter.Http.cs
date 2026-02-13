using System.Text;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static AosNode ParseHttpRequestNode(string raw, AosSpan span)
    {
        var split = SplitHttpHeadBody(raw);
        var head = split.Head.Replace("\r\n", "\n", StringComparison.Ordinal);
        var body = split.Body;

        var lines = head.Split('\n');
        var requestLine = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var method = requestParts.Length > 0 ? requestParts[0] : string.Empty;
        var rawPath = requestParts.Length > 1 ? requestParts[1] : string.Empty;

        var query = string.Empty;
        var path = rawPath;
        var queryStart = rawPath.IndexOf('?', StringComparison.Ordinal);
        if (queryStart >= 0)
        {
            path = rawPath[..queryStart];
            query = queryStart + 1 < rawPath.Length ? rawPath[(queryStart + 1)..] : string.Empty;
        }

        var headerMapChildren = new List<AosNode>();
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headerMapChildren.Add(new AosNode(
                "Field",
                $"http_header_{i}",
                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                {
                    ["key"] = new AosAttrValue(AosAttrKind.String, key)
                },
                new List<AosNode>
                {
                    new AosNode(
                        "Lit",
                        $"http_header_val_{i}",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                        {
                            ["value"] = new AosAttrValue(AosAttrKind.String, value)
                        },
                        new List<AosNode>(),
                        span)
                },
                span));
        }

        var queryMapChildren = new List<AosNode>();
        if (!string.IsNullOrEmpty(query))
        {
            var queryParts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < queryParts.Length; i++)
            {
                var part = queryParts[i];
                var eq = part.IndexOf('=');
                var key = UrlDecodeComponent(eq >= 0 ? part[..eq] : part);
                var value = UrlDecodeComponent(eq >= 0 && eq + 1 < part.Length ? part[(eq + 1)..] : string.Empty);
                queryMapChildren.Add(new AosNode(
                    "Field",
                    $"http_query_{i}",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["key"] = new AosAttrValue(AosAttrKind.String, key)
                    },
                    new List<AosNode>
                    {
                        new AosNode(
                            "Lit",
                            $"http_query_val_{i}",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["value"] = new AosAttrValue(AosAttrKind.String, value)
                            },
                            new List<AosNode>(),
                            span)
                    },
                    span));
            }
        }

        return new AosNode(
            "HttpRequest",
            "auto",
            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
            {
                ["method"] = new AosAttrValue(AosAttrKind.String, method),
                ["path"] = new AosAttrValue(AosAttrKind.String, path)
            },
            new List<AosNode>
            {
                new AosNode("Map", "http_headers", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), headerMapChildren, span),
                new AosNode("Map", "http_query", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), queryMapChildren, span),
                new AosNode(
                    "Lit",
                    "http_body",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.String, body)
                    },
                    new List<AosNode>(),
                    span)
            },
            span);
    }

    private static (string Head, string Body) SplitHttpHeadBody(string raw)
    {
        var separator = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator >= 0)
        {
            var bodyStart = separator + 4;
            var body = bodyStart < raw.Length ? raw[bodyStart..] : string.Empty;
            return (raw[..separator], body);
        }

        separator = raw.IndexOf("\n\n", StringComparison.Ordinal);
        if (separator >= 0)
        {
            var bodyStart = separator + 2;
            var body = bodyStart < raw.Length ? raw[bodyStart..] : string.Empty;
            return (raw[..separator], body);
        }

        return (raw, string.Empty);
    }

    private static AosNode ParseFormBodyNode(string body, AosSpan span)
    {
        var fields = new List<AosNode>();
        if (!string.IsNullOrEmpty(body))
        {
            var parts = body.Split('&', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var eq = part.IndexOf('=');
                var key = UrlDecodeComponent(eq >= 0 ? part[..eq] : part);
                var value = UrlDecodeComponent(eq >= 0 && eq + 1 < part.Length ? part[(eq + 1)..] : string.Empty);
                fields.Add(new AosNode(
                    "Field",
                    $"http_form_{i}",
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["key"] = new AosAttrValue(AosAttrKind.String, key)
                    },
                    new List<AosNode>
                    {
                        new AosNode(
                            "Lit",
                            $"http_form_val_{i}",
                            new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                            {
                                ["value"] = new AosAttrValue(AosAttrKind.String, value)
                            },
                            new List<AosNode>(),
                            span)
                    },
                    span));
            }
        }

        return new AosNode("Map", "http_form_map", new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), fields, span);
    }

    private static bool TryParseJsonBodyNode(string body, AosSpan span, out AosNode node, out string message)
    {
        var parser = new JsonBodyParser(body, span);
        return parser.TryParse(out node, out message);
    }

    private static string UrlDecodeComponent(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '+')
            {
                bytes.Add((byte)' ');
                continue;
            }

            if (ch == '%' && i + 2 < value.Length && TryParseHexByte(value[i + 1], value[i + 2], out var decoded))
            {
                bytes.Add(decoded);
                i += 2;
                continue;
            }

            var scalarLength = 1;
            if (char.IsHighSurrogate(ch) &&
                i + 1 < value.Length &&
                char.IsLowSurrogate(value[i + 1]))
            {
                scalarLength = 2;
            }

            var charByteCount = Encoding.UTF8.GetByteCount(value.AsSpan(i, scalarLength));
            if (charByteCount == 1)
            {
                bytes.Add((byte)ch);
                i += scalarLength - 1;
                continue;
            }

            var encoded = new byte[charByteCount];
            Encoding.UTF8.GetBytes(value.AsSpan(i, scalarLength), encoded);
            bytes.AddRange(encoded);
            i += scalarLength - 1;
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool TryParseHexByte(char hi, char lo, out byte value)
    {
        value = 0;
        if (!TryParseHexNibble(hi, out var hiNibble) || !TryParseHexNibble(lo, out var loNibble))
        {
            return false;
        }

        value = (byte)((hiNibble << 4) | loNibble);
        return true;
    }

    private static bool TryParseHexNibble(char ch, out int value)
    {
        if (ch is >= '0' and <= '9')
        {
            value = ch - '0';
            return true;
        }

        if (ch is >= 'A' and <= 'F')
        {
            value = ch - 'A' + 10;
            return true;
        }

        if (ch is >= 'a' and <= 'f')
        {
            value = ch - 'a' + 10;
            return true;
        }

        value = 0;
        return false;
    }

    private sealed class JsonBodyParser
    {
        private readonly string _text;
        private readonly AosSpan _span;
        private int _index;
        private int _nextId;

        public JsonBodyParser(string text, AosSpan span)
        {
            _text = text;
            _span = span;
            _nextId = 1;
        }

        public bool TryParse(out AosNode node, out string message)
        {
            node = default!;
            message = string.Empty;

            SkipWhitespace();
            if (_index >= _text.Length || _text[_index] != '{')
            {
                message = "JSON body must be an object.";
                return false;
            }

            if (!TryParseObject(out node, out message))
            {
                return false;
            }

            SkipWhitespace();
            if (_index != _text.Length)
            {
                message = "Unexpected trailing characters after JSON body.";
                return false;
            }

            return true;
        }

        private bool TryParseObject(out AosNode node, out string message)
        {
            node = default!;
            message = string.Empty;
            if (!Consume('{'))
            {
                message = "Expected '{'.";
                return false;
            }

            SkipWhitespace();
            var fields = new List<AosNode>();
            if (Consume('}'))
            {
                node = new AosNode("Map", NextId("http_json_obj"), new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), fields, _span);
                return true;
            }

            while (true)
            {
                if (!TryParseString(out var key, out message))
                {
                    return false;
                }

                SkipWhitespace();
                if (!Consume(':'))
                {
                    message = "Expected ':' after JSON key.";
                    return false;
                }

                SkipWhitespace();
                if (!TryParseValue(out var valueNode, out message))
                {
                    return false;
                }

                fields.Add(new AosNode(
                    "Field",
                    NextId("http_json_field"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["key"] = new AosAttrValue(AosAttrKind.String, key)
                    },
                    new List<AosNode> { valueNode },
                    _span));

                SkipWhitespace();
                if (Consume('}'))
                {
                    node = new AosNode("Map", NextId("http_json_obj"), new Dictionary<string, AosAttrValue>(StringComparer.Ordinal), fields, _span);
                    return true;
                }

                if (!Consume(','))
                {
                    message = "Expected ',' or '}' in JSON object.";
                    return false;
                }

                SkipWhitespace();
            }
        }

        private bool TryParseValue(out AosNode node, out string message)
        {
            node = default!;
            message = string.Empty;

            if (_index >= _text.Length)
            {
                message = "Unexpected end of JSON input.";
                return false;
            }

            var ch = _text[_index];
            if (ch == '"')
            {
                if (!TryParseString(out var value, out message))
                {
                    return false;
                }

                node = new AosNode(
                    "Lit",
                    NextId("http_json_lit"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.String, value)
                    },
                    new List<AosNode>(),
                    _span);
                return true;
            }

            if (ch == '{')
            {
                return TryParseObject(out node, out message);
            }

            if (ch == '-' || (ch >= '0' && ch <= '9'))
            {
                if (!TryParseInt(out var intValue, out message))
                {
                    return false;
                }

                node = new AosNode(
                    "Lit",
                    NextId("http_json_lit"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Int, intValue)
                    },
                    new List<AosNode>(),
                    _span);
                return true;
            }

            if (TryConsumeWord("true"))
            {
                node = new AosNode(
                    "Lit",
                    NextId("http_json_lit"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Bool, true)
                    },
                    new List<AosNode>(),
                    _span);
                return true;
            }

            if (TryConsumeWord("false"))
            {
                node = new AosNode(
                    "Lit",
                    NextId("http_json_lit"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                    {
                        ["value"] = new AosAttrValue(AosAttrKind.Bool, false)
                    },
                    new List<AosNode>(),
                    _span);
                return true;
            }

            if (TryConsumeWord("null"))
            {
                node = new AosNode(
                    "Null",
                    NextId("http_json_null"),
                    new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                    new List<AosNode>(),
                    _span);
                return true;
            }

            message = "Unsupported JSON value.";
            return false;
        }

        private bool TryParseString(out string value, out string message)
        {
            value = string.Empty;
            message = string.Empty;
            if (!Consume('"'))
            {
                message = "Expected JSON string.";
                return false;
            }

            var builder = new StringBuilder();
            while (_index < _text.Length)
            {
                var ch = _text[_index++];
                if (ch == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (_index >= _text.Length)
                {
                    message = "Invalid JSON escape sequence.";
                    return false;
                }

                var escape = _text[_index++];
                switch (escape)
                {
                    case '"':
                    case '\\':
                    case '/':
                        builder.Append(escape);
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        if (_index + 4 > _text.Length)
                        {
                            message = "Invalid JSON unicode escape sequence.";
                            return false;
                        }
                        var hex = _text.AsSpan(_index, 4);
                        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
                        {
                            message = "Invalid JSON unicode escape sequence.";
                            return false;
                        }
                        builder.Append((char)codePoint);
                        _index += 4;
                        break;
                    default:
                        message = "Invalid JSON escape sequence.";
                        return false;
                }
            }

            message = "Unterminated JSON string.";
            return false;
        }

        private bool TryParseInt(out int value, out string message)
        {
            value = 0;
            message = string.Empty;
            var start = _index;
            if (_text[_index] == '-')
            {
                _index++;
            }

            var digitStart = _index;
            while (_index < _text.Length && _text[_index] >= '0' && _text[_index] <= '9')
            {
                _index++;
            }

            if (_index == digitStart)
            {
                message = "Invalid JSON number.";
                return false;
            }

            if (!int.TryParse(_text.AsSpan(start, _index - start), out value))
            {
                message = "JSON number is out of range for int.";
                return false;
            }

            return true;
        }

        private bool Consume(char ch)
        {
            if (_index < _text.Length && _text[_index] == ch)
            {
                _index++;
                return true;
            }

            return false;
        }

        private bool TryConsumeWord(string word)
        {
            if (_index + word.Length > _text.Length)
            {
                return false;
            }

            if (!_text.AsSpan(_index, word.Length).SequenceEqual(word.AsSpan()))
            {
                return false;
            }

            _index += word.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length)
            {
                var ch = _text[_index];
                if (ch is ' ' or '\t' or '\r' or '\n')
                {
                    _index++;
                    continue;
                }
                break;
            }
        }

        private string NextId(string prefix)
        {
            var id = $"{prefix}_{_nextId}";
            _nextId++;
            return id;
        }
    }
}
