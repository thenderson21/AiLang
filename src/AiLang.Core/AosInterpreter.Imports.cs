using AiVM.Core;

namespace AiLang.Core;

public sealed partial class AosInterpreter
{
    private AosValue EvalImport(AosNode node, AosRuntime runtime, Dictionary<string, AosValue> env)
    {
        if (!node.Attrs.TryGetValue("path", out var pathAttr) || pathAttr.Kind != AosAttrKind.String)
        {
            return CreateRuntimeErr("RUN020", "Import requires string path attribute.", node.Id, node.Span);
        }

        if (node.Children.Count != 0)
        {
            return CreateRuntimeErr("RUN021", "Import must not have children.", node.Id, node.Span);
        }

        var relativePath = pathAttr.AsString();
        if (HostFileSystem.IsPathRooted(relativePath))
        {
            return CreateRuntimeErr("RUN022", "Import path must be relative.", node.Id, node.Span);
        }

        var absolutePath = HostFileSystem.GetFullPath(HostFileSystem.Combine(runtime.ModuleBaseDir, relativePath));
        if (runtime.ModuleExports.TryGetValue(absolutePath, out var cachedExports))
        {
            foreach (var exportEntry in cachedExports)
            {
                env[exportEntry.Key] = exportEntry.Value;
            }
            return AosValue.Void;
        }

        if (runtime.ModuleLoading.Contains(absolutePath))
        {
            return CreateRuntimeErr("RUN023", "Circular import detected.", node.Id, node.Span);
        }

        if (!HostFileSystem.FileExists(absolutePath))
        {
            return CreateRuntimeErr("RUN024", $"Import file not found: {relativePath}", node.Id, node.Span);
        }

        AosParseResult parse;
        try
        {
            parse = AosParsing.ParseFile(absolutePath);
        }
        catch (Exception ex)
        {
            return CreateRuntimeErr("RUN025", $"Failed to read import: {ex.Message}", node.Id, node.Span);
        }

        if (parse.Root is null || parse.Diagnostics.Count > 0)
        {
            var diag = parse.Diagnostics.FirstOrDefault();
            return CreateRuntimeErr(diag?.Code ?? "PAR000", diag?.Message ?? "Parse failed.", node.Id, node.Span);
        }

        if (parse.Root.Kind != "Program")
        {
            return CreateRuntimeErr("RUN026", "Imported root must be Program.", node.Id, node.Span);
        }

        var priorBaseDir = runtime.ModuleBaseDir;
        runtime.ModuleBaseDir = HostFileSystem.GetDirectoryName(absolutePath) ?? priorBaseDir;
        runtime.ModuleLoading.Add(absolutePath);
        runtime.ExportScopes.Push(new Dictionary<string, AosValue>(StringComparer.Ordinal));
        try
        {
            var moduleEnv = new Dictionary<string, AosValue>(StringComparer.Ordinal);
            var result = Evaluate(parse.Root, runtime, moduleEnv);
            if (IsErrValue(result))
            {
                return result;
            }

            var exports = new Dictionary<string, AosValue>(StringComparer.Ordinal);
            foreach (var exportName in CollectExportNames(parse.Root))
            {
                if (!moduleEnv.TryGetValue(exportName, out var exportValue))
                {
                    return CreateRuntimeErr("RUN029", $"Export name not found: {exportName}", node.Id, node.Span);
                }

                exports[exportName] = exportValue;
            }

            runtime.ModuleExports[absolutePath] = new Dictionary<string, AosValue>(exports, StringComparer.Ordinal);
            foreach (var exportEntry in exports)
            {
                env[exportEntry.Key] = exportEntry.Value;
            }

            return AosValue.Void;
        }
        finally
        {
            runtime.ExportScopes.Pop();
            runtime.ModuleLoading.Remove(absolutePath);
            runtime.ModuleBaseDir = priorBaseDir;
        }
    }

    private static List<string> CollectExportNames(AosNode program)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in program.Children)
        {
            if (child.Kind != "Export" ||
                !child.Attrs.TryGetValue("name", out var nameAttr) ||
                nameAttr.Kind != AosAttrKind.Identifier)
            {
                continue;
            }

            var name = nameAttr.AsString();
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static AosNode ResolveImportsForBytecode(AosNode program, string moduleBaseDir, HashSet<string> loading)
    {
        if (program.Kind != "Program")
        {
            throw new VmRuntimeException("VM001", "compiler.emitBytecode expects Program node.", program.Id);
        }

        var outputChildren = new List<AosNode>();
        var seenLets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in program.Children)
        {
            if (child.Kind != "Import")
            {
                outputChildren.Add(child);
                if (child.Kind == "Let" &&
                    child.Attrs.TryGetValue("name", out var letNameAttr) &&
                    letNameAttr.Kind == AosAttrKind.Identifier)
                {
                    seenLets.Add(letNameAttr.AsString());
                }
                continue;
            }

            if (!child.Attrs.TryGetValue("path", out var pathAttr) || pathAttr.Kind != AosAttrKind.String)
            {
                throw new VmRuntimeException("VM001", "Import requires string path attribute.", child.Id);
            }

            var relativePath = pathAttr.AsString();
            if (HostFileSystem.IsPathRooted(relativePath))
            {
                throw new VmRuntimeException("VM001", "Import path must be relative.", child.Id);
            }

            var fullPath = HostFileSystem.GetFullPath(HostFileSystem.Combine(moduleBaseDir, relativePath));
            if (!loading.Add(fullPath))
            {
                throw new VmRuntimeException("VM001", "Circular import detected.", child.Id);
            }
            if (!HostFileSystem.FileExists(fullPath))
            {
                loading.Remove(fullPath);
                throw new VmRuntimeException("VM001", $"Import file not found: {relativePath}", child.Id);
            }

            AosParseResult parse;
            try
            {
                parse = AosParsing.ParseFile(fullPath);
            }
            catch (Exception ex)
            {
                loading.Remove(fullPath);
                throw new VmRuntimeException("VM001", $"Failed to read import: {ex.Message}", child.Id);
            }

            if (parse.Root is null || parse.Diagnostics.Count > 0 || parse.Root.Kind != "Program")
            {
                loading.Remove(fullPath);
                var diag = parse.Diagnostics.FirstOrDefault();
                throw new VmRuntimeException("VM001", diag?.Message ?? "Imported root must be Program.", child.Id);
            }

            var importedDir = HostFileSystem.GetDirectoryName(fullPath) ?? moduleBaseDir;
            var flattenedImport = ResolveImportsForBytecode(parse.Root, importedDir, loading);
            loading.Remove(fullPath);
            foreach (var letNode in ExtractExportedLets(flattenedImport))
            {
                if (letNode.Attrs.TryGetValue("name", out var nameAttr) &&
                    nameAttr.Kind == AosAttrKind.Identifier &&
                    seenLets.Add(nameAttr.AsString()))
                {
                    outputChildren.Add(letNode);
                }
            }
        }

        return new AosNode(
            "Program",
            program.Id,
            new Dictionary<string, AosAttrValue>(program.Attrs, StringComparer.Ordinal),
            outputChildren,
            program.Span);
    }

    private static List<AosNode> ExtractExportedLets(AosNode program)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in program.Children)
        {
            if (child.Kind == "Export" &&
                child.Attrs.TryGetValue("name", out var exportName) &&
                exportName.Kind == AosAttrKind.Identifier)
            {
                names.Add(exportName.AsString());
            }
        }

        var lets = new List<AosNode>();
        foreach (var child in program.Children)
        {
            if (child.Kind == "Let" &&
                child.Attrs.TryGetValue("name", out var letName) &&
                letName.Kind == AosAttrKind.Identifier &&
                names.Contains(letName.AsString()))
            {
                lets.Add(child);
            }
        }
        return lets;
    }
}
