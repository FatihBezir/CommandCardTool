#if NETFRAMEWORK

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace LauncherWinUI.Compat
{

/// <summary>
/// net48 has no PublishSingleFile, so the support assemblies (System.Text.Json and
/// friends) are embedded as resources by the EmbedDependencies target and loaded
/// from memory on first use. Keeps the tool a single portable .exe.
/// </summary>
internal static class EmbeddedAssemblyLoader
{
    private const string Prefix = "Embedded.";

    [ModuleInitializer]
    internal static void Install()
        => AppDomain.CurrentDomain.AssemblyResolve += Resolve;

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        string fileName = new AssemblyName(args.Name).Name + ".dll";
        var self = typeof(EmbeddedAssemblyLoader).Assembly;

        using var stream = self.GetManifestResourceStream(Prefix + fileName);
        if (stream == null) return null;

        var bytes = new byte[stream.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int n = stream.Read(bytes, read, bytes.Length - read);
            if (n == 0) break;
            read += n;
        }
        return Assembly.Load(bytes);
    }
}

}

#endif
