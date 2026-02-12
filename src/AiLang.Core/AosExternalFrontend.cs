using System.Text;
using AiVM.Core;

namespace AiLang.Core;

public static class AosExternalFrontend
{
    public static AosParseResult Parse(string source)
    {
        var frontend = ResolveFrontendPath();
        if (frontend is null)
        {
            return FallbackParse(source);
        }

        try
        {
            var process = HostProcessRunner.RunWithStdIn(frontend, "--stdin", source);
            if (process is null)
            {
                return new AosParseResult(null, new List<AosDiagnostic>
                {
                    new("PAR900", "Failed to launch frontend parser.", null, null)
                });
            }

            if (process.ExitCode != 0)
            {
                return new AosParseResult(null, new List<AosDiagnostic> { ParseFrontendError(process.Stderr) });
            }

            return Decode(process.Stdout);
        }
        catch (Exception ex)
        {
            return new AosParseResult(null, new List<AosDiagnostic>
            {
                new("PAR900", $"Frontend parser failure: {ex.Message}", null, null)
            });
        }
    }

    private static AosParseResult Decode(byte[] data)
    {
        try
        {
            var reader = new WireReader(data);
            reader.ExpectLiteral("AOSAST1\n");
            var root = reader.ReadNode();
            return new AosParseResult(root, new List<AosDiagnostic>());
        }
        catch (Exception ex)
        {
            return new AosParseResult(null, new List<AosDiagnostic>
            {
                new("PAR901", $"Frontend decode error: {ex.Message}", null, null)
            });
        }
    }

    private static AosDiagnostic ParseFrontendError(string stderr)
    {
        // Expected: ERR <CODE> <LINE> <COL> <MESSAGE>
        var text = stderr.Trim();
        if (!text.StartsWith("ERR ", StringComparison.Ordinal))
        {
            return new AosDiagnostic("PAR900", string.IsNullOrWhiteSpace(text) ? "Frontend parse failed." : text, null, null);
        }

        var parts = text.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 5)
        {
            var message = parts[4];
            return new AosDiagnostic(parts[1], message, null, null);
        }

        return new AosDiagnostic("PAR900", text, null, null);
    }

    private static string? ResolveFrontendPath()
    {
        var env = HostEnvironment.GetEnvironmentVariable("AOS_FRONTEND");
        if (!string.IsNullOrWhiteSpace(env) && HostFileSystem.FileExists(env))
        {
            return env;
        }

        var candidates = new[]
        {
            HostFileSystem.Combine(HostEnvironment.BaseDirectory, "aos_frontend"),
            HostFileSystem.Combine(HostFileSystem.GetCurrentDirectory(), "tools", "aos_frontend"),
            HostFileSystem.Combine(HostFileSystem.GetCurrentDirectory(), "aos_frontend")
        };

        foreach (var candidate in candidates)
        {
            if (HostFileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static AosParseResult FallbackParse(string source)
    {
        var tokenizer = new AosTokenizer(source);
        var tokens = tokenizer.Tokenize();
        var parser = new AosParser(tokens);
        var parse = parser.ParseSingle();
        parse.Diagnostics.AddRange(tokenizer.Diagnostics);
        return parse;
    }

    private sealed class WireReader
    {
        private readonly byte[] _data;
        private int _index;

        public WireReader(byte[] data)
        {
            _data = data;
        }

        public void ExpectLiteral(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (ReadByte() != bytes[i])
                {
                    throw new InvalidOperationException("Missing wire header.");
                }
            }
        }

        public AosNode ReadNode()
        {
            ExpectByte((byte)'N');
            ExpectByte((byte)' ');
            var kind = ReadLenString();
            ExpectByte((byte)' ');
            var id = ReadLenString();
            ExpectByte((byte)' ');
            var attrCount = ReadIntToken();
            ExpectByte((byte)' ');
            var childCount = ReadIntToken();
            ExpectByte((byte)'\n');

            var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal);
            for (var i = 0; i < attrCount; i++)
            {
                ExpectByte((byte)'A');
                ExpectByte((byte)' ');
                var key = ReadLenString();
                ExpectByte((byte)' ');
                var type = (char)ReadByte();
                ExpectByte((byte)' ');
                var value = ReadLenString();
                ExpectByte((byte)'\n');

                attrs[key] = type switch
                {
                    'S' => new AosAttrValue(AosAttrKind.String, value),
                    'I' => new AosAttrValue(AosAttrKind.Int, int.Parse(value)),
                    'B' => new AosAttrValue(AosAttrKind.Bool, value == "true"),
                    _ => new AosAttrValue(AosAttrKind.Identifier, value)
                };
            }

            var children = new List<AosNode>(childCount);
            for (var i = 0; i < childCount; i++)
            {
                children.Add(ReadNode());
            }

            ExpectByte((byte)'E');
            ExpectByte((byte)'\n');

            return new AosNode(
                kind,
                id,
                attrs,
                children,
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
        }

        private string ReadLenString()
        {
            var len = ReadIntUntilColon();
            if (len < 0 || _index + len > _data.Length)
            {
                throw new InvalidOperationException("Invalid wire string length.");
            }

            var value = Encoding.UTF8.GetString(_data, _index, len);
            _index += len;
            return value;
        }

        private int ReadIntUntilColon()
        {
            var start = _index;
            while (_index < _data.Length && _data[_index] != (byte)':')
            {
                if (_data[_index] < (byte)'0' || _data[_index] > (byte)'9')
                {
                    throw new InvalidOperationException("Invalid wire integer.");
                }
                _index++;
            }

            if (_index >= _data.Length || _data[_index] != (byte)':')
            {
                throw new InvalidOperationException("Missing ':' in wire string.");
            }

            var intText = Encoding.ASCII.GetString(_data, start, _index - start);
            _index++; // :
            return int.Parse(intText);
        }

        private int ReadIntToken()
        {
            var start = _index;
            while (_index < _data.Length && _data[_index] >= (byte)'0' && _data[_index] <= (byte)'9')
            {
                _index++;
            }

            if (start == _index)
            {
                throw new InvalidOperationException("Expected integer token.");
            }

            var intText = Encoding.ASCII.GetString(_data, start, _index - start);
            return int.Parse(intText);
        }

        private byte ReadByte()
        {
            if (_index >= _data.Length)
            {
                throw new InvalidOperationException("Unexpected end of wire data.");
            }
            return _data[_index++];
        }

        private void ExpectByte(byte value)
        {
            var actual = ReadByte();
            if (actual != value)
            {
                throw new InvalidOperationException($"Expected byte {(char)value}.");
            }
        }
    }
}
