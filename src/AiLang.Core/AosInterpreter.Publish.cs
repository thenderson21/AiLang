using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalCompilerPublish(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!runtime.Permissions.Contains("compiler"))
        {
            return AosValue.Unknown;
        }
        if (node.Children.Count != 2)
        {
            return AosValue.Unknown;
        }

        var projectNameValue = EvalNode(node.Children[1], runtime, env);
        if (projectNameValue.Kind != AosValueKind.String)
        {
            return AosValue.Unknown;
        }

        if (!runtime.Env.TryGetValue("argv", out var argvValue) || argvValue.Kind != AosValueKind.Node)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB001", "publish directory argument not found.", node.Id, node.Span));
        }

        var argvNode = argvValue.AsNode();
        if (argvNode.Children.Count < 2)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB001", "publish directory argument not found.", node.Id, node.Span));
        }

        var dirNode = argvNode.Children[1];
        if (!dirNode.Attrs.TryGetValue("value", out var dirAttr) || dirAttr.Kind != AosAttrKind.String)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB002", "invalid publish directory argument.", node.Id, node.Span));
        }

        var publishDir = dirAttr.AsString();
        var projectName = projectNameValue.AsString();
        var bundlePath = HostFileSystem.Combine(publishDir, $"{projectName}.aibundle");
        var outputBinaryPath = HostFileSystem.Combine(publishDir, projectName);
        var libraryPath = HostFileSystem.Combine(publishDir, $"{projectName}.ailib");

        var manifestPath = HostFileSystem.Combine(publishDir, "project.aiproj");
        if (TryLoadProjectNode(manifestPath, out var projectNode, out var manifestErr) && projectNode is not null)
        {
            if (ValidateProjectIncludesForPublish(publishDir, projectNode, out var includeErr))
            {
                if (TryGetStringProjectAttr(projectNode, "entryExport", out var entryExportValue) &&
                    string.Equals(entryExportValue, "library", StringComparison.Ordinal))
                {
                    if (!TryGetStringProjectAttr(projectNode, "version", out var libraryVersion) || string.IsNullOrWhiteSpace(libraryVersion))
                    {
                        return AosValue.FromNode(CreateErrNode("publish_err", "PUB014", "Library project requires non-empty version.", projectNode.Id, node.Span));
                    }

                    var canonicalProgram = new AosNode(
                        "Program",
                        "libp1",
                        new Dictionary<string, AosAttrValue>(StringComparer.Ordinal),
                        new List<AosNode>
                        {
                            new AosNode(
                                "Project",
                                "proj1",
                                new Dictionary<string, AosAttrValue>(StringComparer.Ordinal)
                                {
                                    ["name"] = new AosAttrValue(AosAttrKind.String, projectName),
                                    ["entryFile"] = new AosAttrValue(AosAttrKind.String, TryGetStringProjectAttr(projectNode, "entryFile", out var ef) ? ef : string.Empty),
                                    ["entryExport"] = new AosAttrValue(AosAttrKind.String, "library"),
                                    ["version"] = new AosAttrValue(AosAttrKind.String, libraryVersion)
                                },
                                new List<AosNode>(),
                                node.Span)
                        },
                        node.Span);
                    if (VmPublishArtifacts.TryWriteLibrary(publishDir, libraryPath, AosFormatter.Format(canonicalProgram), out var libraryWriteError))
                    {
                        return AosValue.FromInt(0);
                    }

                    return AosValue.FromNode(CreateErrNode("publish_err", "PUB003", libraryWriteError, node.Id, node.Span));
                }
            }
            else
            {
                return AosValue.FromNode(includeErr!);
            }
        }
        else if (manifestErr is not null)
        {
            return AosValue.FromNode(manifestErr);
        }

        var rawBundleNode = node.Children.Count > 0 ? node.Children[0] : null;
        if (rawBundleNode is null || rawBundleNode.Kind != "Bundle")
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB005", "publish requires Bundle node.", node.Id, node.Span));
        }

        if (!rawBundleNode.Attrs.TryGetValue("entryFile", out var entryFileAttr) || entryFileAttr.Kind != AosAttrKind.String)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB006", "Bundle missing entryFile.", node.Id, node.Span));
        }

        var entryFile = entryFileAttr.AsString();
        var entryPath = HostFileSystem.GetFullPath(HostFileSystem.Combine(publishDir, entryFile));
        if (!HostFileSystem.FileExists(entryPath))
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB007", $"Entry file not found: {entryFile}", node.Id, node.Span));
        }

        AosNode? entryProgram;
        try
        {
            var parsed = AosParsing.ParseFile(entryPath);
            if (parsed.Root is null || parsed.Diagnostics.Count > 0 || parsed.Root.Kind != "Program")
            {
                var diagnostic = parsed.Diagnostics.FirstOrDefault();
                return AosValue.FromNode(CreateErrNode(
                    "publish_err",
                    diagnostic?.Code ?? "PAR000",
                    diagnostic?.Message ?? "Failed to parse entry file.",
                    diagnostic?.NodeId ?? node.Id,
                    node.Span));
            }
            entryProgram = parsed.Root;
        }
        catch (Exception ex)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", "PUB008", ex.Message, node.Id, node.Span));
        }

        AosNode bytecodeNode;
        try
        {
            bytecodeNode = BytecodeCompiler.Compile(entryProgram!, allowImportNodes: true);
        }
        catch (VmRuntimeException vmEx)
        {
            return AosValue.FromNode(CreateErrNode("publish_err", vmEx.Code, vmEx.Message, vmEx.NodeId, node.Span));
        }

        var bytecodeText = AosFormatter.Format(bytecodeNode);

        if (VmPublishArtifacts.TryWriteBundleExecutable(
                publishDir,
                bundlePath,
                outputBinaryPath,
                bytecodeText,
                out var publishErrorCode,
                out var publishErrorMessage))
        {
            return AosValue.FromInt(0);
        }

        return AosValue.FromNode(CreateErrNode("publish_err", publishErrorCode, publishErrorMessage, node.Id, node.Span));
    }
}
