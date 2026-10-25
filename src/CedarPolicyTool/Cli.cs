using System;
using System.Collections.Generic;
using System.IO;

namespace CedarPolicyTool;

/// <summary>
/// <c>generate &lt;authz.cedar&gt; &lt;policies.json&gt;</c>: writes the cedar-agent policies file. Exit codes: 0 written,
/// 1 invalid or missing source (nothing written), 2 wrong arguments.
/// </summary>
internal static class Cli
{
    private const string Usage = "usage: CedarPolicyTool generate <authz.cedar> <policies.json>";

    public static int Run(IReadOnlyList<string> args, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count != 3 || args[0] != "generate")
        {
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            CedarPolicyGeneration.Generate(args[1], args[2]);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }
}
