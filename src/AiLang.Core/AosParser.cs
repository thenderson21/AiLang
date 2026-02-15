namespace AiLang.Core;

public sealed class AosParseResult
{
    public AosParseResult(AosNode? root, List<AosDiagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }

    public AosNode? Root { get; }
    public List<AosDiagnostic> Diagnostics { get; }
}

public sealed class AosParser
{
    private readonly List<AosToken> _tokens;
    private int _index;
    private readonly List<AosDiagnostic> _diagnostics = new();

    public AosParser(List<AosToken> tokens)
    {
        _tokens = tokens;
    }

    public IReadOnlyList<AosDiagnostic> Diagnostics => _diagnostics;

    public AosParseResult ParseSingle()
    {
        var node = ParseNode();
        if (node is not null && !Check(AosTokenKind.End))
        {
            var token = Peek();
            _diagnostics.Add(new AosDiagnostic("PAR004", "Unexpected tokens after node.", null, token.Span));
        }
        return new AosParseResult(node, _diagnostics);
    }

    private AosNode? ParseNode()
    {
        var kindToken = Consume(AosTokenKind.Identifier, "Expected node kind.");
        if (kindToken is null)
        {
            return null;
        }

        if (!IsKindName(kindToken.Text))
        {
            _diagnostics.Add(new AosDiagnostic("PAR001", $"Invalid node kind '{kindToken.Text}'.", null, kindToken.Span));
        }

        var idText = string.Empty;
        if (Match(AosTokenKind.Hash))
        {
            var idToken = Peek();
            if (idToken.Kind != AosTokenKind.Identifier && idToken.Kind != AosTokenKind.Int && idToken.Kind != AosTokenKind.Bool)
            {
                _diagnostics.Add(new AosDiagnostic("PAR002", "Expected node id after '#'.", null, idToken.Span));
                return null;
            }
            Advance();
            idText = idToken.Text;
            if (!IsId(idText))
            {
                _diagnostics.Add(new AosDiagnostic("PAR003", $"Invalid node id '{idText}'.", idText, idToken.Span));
            }
        }

        var attrs = new Dictionary<string, AosAttrValue>(StringComparer.Ordinal);
        var nodeId = string.IsNullOrEmpty(idText) ? null : idText;
        if (Match(AosTokenKind.LParen))
        {
            while (!Check(AosTokenKind.RParen) && !Check(AosTokenKind.End))
            {
                var keyToken = Consume(AosTokenKind.Identifier, "Expected attribute name.");
                if (keyToken is null)
                {
                    break;
                }

                if (!IsKindName(keyToken.Text))
                {
                    _diagnostics.Add(new AosDiagnostic("PAR005", $"Invalid attribute name '{keyToken.Text}'.", nodeId, keyToken.Span));
                }

                Consume(AosTokenKind.Equals, "Expected '=' after attribute name.");

                var value = ParseAttrValue(nodeId);
                if (value is not null)
                {
                    attrs[keyToken.Text] = value;
                }

                if (Check(AosTokenKind.RParen))
                {
                    break;
                }
            }

            Consume(AosTokenKind.RParen, "Expected ')' after attributes.");
        }

        var children = new List<AosNode>();
        if (Match(AosTokenKind.LBrace))
        {
            while (!Check(AosTokenKind.RBrace) && !Check(AosTokenKind.End))
            {
                var child = ParseNode();
                if (child is not null)
                {
                    children.Add(child);
                }
                else
                {
                    break;
                }
            }

            Consume(AosTokenKind.RBrace, "Expected '}' after children.");
        }

        var span = new AosSpan(kindToken.Span.Start, Previous().Span.End);
        return new AosNode(kindToken.Text, idText, attrs, children, span);
    }

    private AosAttrValue? ParseAttrValue(string? nodeId)
    {
        var token = Peek();
        switch (token.Kind)
        {
            case AosTokenKind.String:
                Advance();
                return new AosAttrValue(AosAttrKind.String, token.Text);
            case AosTokenKind.Int:
                Advance();
                if (!int.TryParse(token.Text, out var intValue))
                {
                    _diagnostics.Add(new AosDiagnostic("PAR006", $"Invalid int literal '{token.Text}'.", nodeId, token.Span));
                    return new AosAttrValue(AosAttrKind.Int, 0);
                }
                return new AosAttrValue(AosAttrKind.Int, intValue);
            case AosTokenKind.Bool:
                Advance();
                return new AosAttrValue(AosAttrKind.Bool, token.Text == "true");
            case AosTokenKind.Identifier:
                return ParseIdentifierValue(nodeId);
            default:
                _diagnostics.Add(new AosDiagnostic("PAR007", "Expected attribute value.", nodeId, token.Span));
                return null;
        }
    }

    private AosAttrValue? ParseIdentifierValue(string? nodeId)
    {
        var first = Consume(AosTokenKind.Identifier, "Expected identifier value.");
        if (first is null)
        {
            return null;
        }

        var value = ParseDottedIdentifier(first, nodeId);
        if (value is null)
        {
            return null;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(value);

        while (Match(AosTokenKind.Comma))
        {
            var next = Consume(AosTokenKind.Identifier, "Expected identifier after ','.");
            if (next is null)
            {
                break;
            }

            var dotted = ParseDottedIdentifier(next, nodeId);
            if (dotted is null)
            {
                break;
            }

            sb.Append(',');
            sb.Append(dotted);
        }

        return new AosAttrValue(AosAttrKind.Identifier, sb.ToString());
    }

    private string? ParseDottedIdentifier(AosToken first, string? nodeId)
    {
        if (!IsBareIdentifier(first.Text))
        {
            _diagnostics.Add(new AosDiagnostic("PAR008", $"Invalid identifier '{first.Text}'.", nodeId, first.Span));
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(first.Text);
        while (Match(AosTokenKind.Dot))
        {
            var part = Consume(AosTokenKind.Identifier, "Expected identifier after '.'.");
            if (part is null)
            {
                return null;
            }
            if (!IsBareIdentifier(part.Text))
            {
                _diagnostics.Add(new AosDiagnostic("PAR009", $"Invalid identifier '{part.Text}'.", nodeId, part.Span));
            }
            sb.Append('.');
            sb.Append(part.Text);
        }

        return sb.ToString();
    }

    private AosToken? Consume(AosTokenKind kind, string message)
    {
        if (Check(kind))
        {
            return Advance();
        }

        var token = Peek();
        _diagnostics.Add(new AosDiagnostic("PAR000", message, null, token.Span));
        return null;
    }

    private bool Match(AosTokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }
        return false;
    }

    private bool Check(AosTokenKind kind) => Peek().Kind == kind;

    private AosToken Advance()
    {
        if (!IsAtEnd())
        {
            _index++;
        }
        return Previous();
    }

    private bool IsAtEnd() => Peek().Kind == AosTokenKind.End;

    private AosToken Peek() => _tokens[_index];

    private AosToken Previous() => _tokens[_index - 1];

    private static bool IsKindName(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        if (!(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }
        for (var i = 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsBareIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        if (!(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }
        for (var i = 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsId(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-'))
            {
                return false;
            }
        }
        return true;
    }
}
