using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CedarPolicyTool;

internal static class CedarPolicyGeneration
{
    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the cedar-agent policies file from a Cedar source file; the output is untouched unless the source is valid.</summary>
    public static void Generate(string cedarPath, string outputPath)
    {
        var result = CedarPolicySplitter.Split(File.ReadAllText(cedarPath));
        if (result.Errors.Count > 0)
        {
            var lines = result.Errors.Select(e => $"  line {e.Line}:{e.Column} {e.Message}");
            throw new InvalidOperationException($"{cedarPath} is not valid Cedar policy source:\n{string.Join('\n', lines)}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);

        var temp = outputPath + ".tmp";
        File.WriteAllText(temp, AgentPolicyJson.Serialize(result.Policies), _utf8NoBom);
        File.Move(temp, outputPath, overwrite: true);
    }
}
