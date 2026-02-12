using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private static bool TryLoadProjectNode(string manifestPath, out AosNode? projectNode, out AosNode? errNode)
    {
        projectNode = null;
        errNode = null;

        if (!HostFileSystem.FileExists(manifestPath))
        {
            return false;
        }

        AosParseResult parse;
        try
        {
            parse = AosParsing.ParseFile(manifestPath);
        }
        catch (Exception ex)
        {
            errNode = CreateErrNode(
                "publish_err",
                "PUB009",
                $"Failed to read project manifest: {ex.Message}",
                "project",
                new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
            return false;
        }

        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var first = parse.Diagnostics.FirstOrDefault();
            errNode = CreateErrNode(
                "publish_err",
                first?.Code ?? "PAR000",
                first?.Message ?? "Failed to parse project manifest.",
                first?.NodeId ?? "project",
                parse.Root?.Span ?? new AosSpan(new AosPosition(0, 0, 0), new AosPosition(0, 0, 0)));
            return false;
        }

        if (parse.Root.Kind != "Program" || parse.Root.Children.Count != 1 || parse.Root.Children[0].Kind != "Project")
        {
            errNode = CreateErrNode(
                "publish_err",
                "PUB010",
                "project.aiproj must contain Program with one Project child.",
                parse.Root.Id,
                parse.Root.Span);
            return false;
        }

        projectNode = parse.Root.Children[0];
        return true;
    }

    private static bool TryGetStringProjectAttr(AosNode node, string key, out string value)
    {
        value = string.Empty;
        if (!node.Attrs.TryGetValue(key, out var attr) || attr.Kind != AosAttrKind.String)
        {
            return false;
        }

        value = attr.AsString();
        return true;
    }

    private static bool ValidateProjectIncludesForPublish(string publishDir, AosNode projectNode, out AosNode? errNode)
    {
        errNode = null;
        foreach (var includeNode in projectNode.Children)
        {
            if (includeNode.Kind != "Include")
            {
                continue;
            }

            if (!TryGetStringProjectAttr(includeNode, "name", out var includeName) ||
                !TryGetStringProjectAttr(includeNode, "path", out var includePath) ||
                !TryGetStringProjectAttr(includeNode, "version", out var includeVersion))
            {
                errNode = CreateErrNode("publish_err", "PUB011", "Include requires name, path, and version.", includeNode.Id, includeNode.Span);
                return false;
            }

            if (HostFileSystem.IsPathRooted(includePath))
            {
                errNode = CreateErrNode("publish_err", "PUB012", "Include path must be relative.", includeNode.Id, includeNode.Span);
                return false;
            }

            var includeDir = HostFileSystem.GetFullPath(HostFileSystem.Combine(publishDir, includePath));
            var includeManifestPath = HostFileSystem.Combine(includeDir, $"{includeName}.ailib");
            if (!HostFileSystem.FileExists(includeManifestPath))
            {
                errNode = CreateErrNode("publish_err", "PUB015", $"Included library not found: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryLoadProjectNode(includeManifestPath, out var includeProjectNode, out var includeParseErr))
            {
                errNode = includeParseErr ?? CreateErrNode("publish_err", "PUB016", $"Invalid included library manifest: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }
            if (includeProjectNode is null)
            {
                errNode = CreateErrNode("publish_err", "PUB016", $"Invalid included library manifest: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryGetStringProjectAttr(includeProjectNode, "name", out var actualName) ||
                !string.Equals(actualName, includeName, StringComparison.Ordinal))
            {
                errNode = CreateErrNode("publish_err", "PUB017", $"Included library name mismatch for {includeName}.", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!TryGetStringProjectAttr(includeProjectNode, "version", out var actualVersion))
            {
                errNode = CreateErrNode("publish_err", "PUB018", $"Included library missing version: {includeName}", includeNode.Id, includeNode.Span);
                return false;
            }

            if (!string.Equals(actualVersion, includeVersion, StringComparison.Ordinal))
            {
                errNode = CreateErrNode(
                    "publish_err",
                    "PUB019",
                    $"Included library version mismatch for {includeName}: expected {includeVersion}, got {actualVersion}.",
                    includeNode.Id,
                    includeNode.Span);
                return false;
            }
        }

        return true;
    }
}
