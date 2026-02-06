using System.Reflection;
using System.Runtime.Loader;

namespace PrintO.Services.Plugins;

/// <summary>
/// AssemblyLoadContext for loading plugins in isolation.
/// Supports unloading (collectible) to allow hot-reload of plugins.
/// </summary>
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _pluginDirectory = Path.GetDirectoryName(pluginPath) ?? "";
    }

    /// <summary>
    /// Load assembly from file into memory (doesn't lock the file).
    /// </summary>
    public Assembly LoadFromFileWithoutLock(string assemblyPath)
    {
        using var fs = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Check if PDB exists for debugging
        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            using var pdbFs = new FileStream(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return LoadFromStream(fs, pdbFs);
        }

        return LoadFromStream(fs);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // First try to resolve from plugin dependencies
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            // Load from memory to avoid file locks
            return LoadFromFileWithoutLock(assemblyPath);
        }

        // Fall back to default context (for SDK types)
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
