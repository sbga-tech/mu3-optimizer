using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace MU3.Mod.Native;

/// <summary>
/// Discovers and maps trusted native DLLs embedded under MU3.Mod.Native.Res.*.
/// Loaded modules remain mapped for the lifetime of the process.
/// </summary>
public static class ModulesRegistry
{
    private const string ResourcePrefix = "MU3.Mod.Native.Res.";
    private static readonly Assembly Assembly = typeof(ModulesRegistry).Assembly;
    private static readonly Dictionary<string, string> Resources = FindResources();
    private static readonly Dictionary<string, ModuleHandle> Modules =
        new Dictionary<string, ModuleHandle>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Loading =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new object();

    public static ModuleHandle LoadLibrary(string libraryName)
    {
        if (string.IsNullOrEmpty(libraryName))
            throw new ArgumentException("Library name is required.", nameof(libraryName));

        lock (Sync)
        {
            ModuleHandle loaded;
            if (Modules.TryGetValue(libraryName, out loaded))
                return loaded;

            string resourceName;
            if (!Resources.TryGetValue(libraryName, out resourceName))
                throw new InvalidOperationException(
                    "Missing embedded native module: " + libraryName
                    + ". Available modules: " + FormatAvailableModules());
            if (!Loading.Add(libraryName))
                throw new InvalidOperationException(
                    "Recursive embedded native module load: " + libraryName);

            try
            {
                var module = InMemoryModuleLoader.Load(ReadResource(resourceName));
                loaded = new ModuleHandle(libraryName, module);
                Modules.Add(libraryName, loaded);
                return loaded;
            }
            finally
            {
                Loading.Remove(libraryName);
            }
        }
    }

    public static TDelegate LoadFunction<TDelegate>(string libraryName, string exportName)
        where TDelegate : class
    {
        return LoadLibrary(libraryName).GetFunction<TDelegate>(exportName);
    }

    private static Dictionary<string, string> FindResources()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = Assembly.GetManifestResourceNames();
        for (var i = 0; i < names.Length; ++i)
        {
            var resourceName = names[i];
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            var libraryName = resourceName.Substring(ResourcePrefix.Length);
            if (libraryName.Length == 0 || resources.ContainsKey(libraryName))
                throw new InvalidOperationException(
                    "Invalid embedded native module resource: " + resourceName);
            resources.Add(libraryName, resourceName);
        }
        return resources;
    }

    private static string FormatAvailableModules()
    {
        var names = new List<string>(Resources.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.Count == 0 ? "(none)" : string.Join(", ", names.ToArray());
    }

    private static byte[] ReadResource(string resourceName)
    {
        using (var stream = Assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                throw new InvalidOperationException(
                    "Missing embedded native module resource: " + resourceName);
            if (stream.Length > int.MaxValue)
                throw new BadImageFormatException(
                    "Embedded native module is too large: " + resourceName);

            var image = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < image.Length)
            {
                var read = stream.Read(image, offset, image.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException(
                        "Unexpected end of embedded native module: " + resourceName);
                offset += read;
            }
            return image;
        }
    }
}
