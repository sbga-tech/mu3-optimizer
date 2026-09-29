using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MU3.Mod.Native;

/// <summary>
/// Process-lifetime handle for an embedded native module.
/// </summary>
public sealed class ModuleHandle
{
    private readonly InMemoryModuleLoader _module;
    private readonly object _sync = new object();
    private readonly Dictionary<string, Dictionary<Type, Delegate>> _functions =
        new Dictionary<string, Dictionary<Type, Delegate>>(StringComparer.Ordinal);

    internal ModuleHandle(string name, InMemoryModuleLoader module)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Module name is required.", nameof(name));
        if (module == null)
            throw new ArgumentNullException(nameof(module));
        Name = name;
        _module = module;
    }

    public string Name { get; private set; }

    public IntPtr GetExport(string exportName)
    {
        return _module.GetExport(exportName);
    }

    public TDelegate GetFunction<TDelegate>(string exportName)
        where TDelegate : class
    {
        if (string.IsNullOrEmpty(exportName))
            throw new ArgumentException("Export name is required.", nameof(exportName));
        var delegateType = typeof(TDelegate);
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new ArgumentException(
                "Native function type must be a delegate: " + delegateType.FullName,
                nameof(TDelegate));

        lock (_sync)
        {
            Dictionary<Type, Delegate> overloads;
            if (!_functions.TryGetValue(exportName, out overloads))
            {
                overloads = new Dictionary<Type, Delegate>();
                _functions.Add(exportName, overloads);
            }

            Delegate function;
            if (!overloads.TryGetValue(delegateType, out function))
            {
                function = Marshal.GetDelegateForFunctionPointer(
                    GetExport(exportName), delegateType);
                overloads.Add(delegateType, function);
            }
            return (TDelegate)(object)function;
        }
    }
}
