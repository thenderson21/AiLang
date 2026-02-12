namespace AiLang.Core;

public static class AosParsing
{
    public static AosParseResult Parse(string source)
    {
        return AosExternalFrontend.Parse(source);
    }

    public static AosParseResult ParseFile(string path)
    {
        return Parse(File.ReadAllText(path));
    }
}
