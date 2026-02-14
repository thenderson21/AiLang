using AiVM.Core;

namespace AiLang.Core;

public static class AosParsing
{
    public static AosParseResult Parse(string source)
    {
        var parse = AosExternalFrontend.Parse(source);
        if (parse.Root is null)
        {
            return parse;
        }

        var withIds = AosNodeIdCanonicalizer.AssignMissingIds(parse.Root);
        return new AosParseResult(withIds, parse.Diagnostics);
    }

    public static AosParseResult ParseFile(string path)
    {
        return Parse(HostFileSystem.ReadAllText(path));
    }
}
