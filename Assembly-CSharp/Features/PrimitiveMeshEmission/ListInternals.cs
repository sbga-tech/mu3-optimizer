using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace MU3.Mod.PrimitiveMeshEmission;

/// <summary>
/// Compiled accessors for List&lt;T&gt;'s private backing store (`_items`,
/// `_size`). The fast mesh-emission path writes quad data straight into the
/// backing arrays and fixes the size up once per frame before upload,
/// removing the per-element List.Add overhead. Binding fails soft: when the
/// runtime's List layout differs, callers keep the original List.Add path.
/// </summary>
internal static class ListInternals<T>
{
    private static bool _probed;
    private static Func<List<T>, T[]> _getItems;
    private static Action<List<T>, int> _setSize;

    internal static Func<List<T>, T[]> GetItems
    {
        get
        {
            Probe();
            return _getItems;
        }
    }

    internal static Action<List<T>, int> SetSize
    {
        get
        {
            Probe();
            return _setSize;
        }
    }

    private static void Probe()
    {
        if (_probed)
            return;

        _probed = true;
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var itemsField = typeof(List<T>).GetField("_items", flags);
            var sizeField = typeof(List<T>).GetField("_size", flags);
            if (itemsField == null || itemsField.FieldType != typeof(T[])
                || sizeField == null || sizeField.FieldType != typeof(int))
                return;

            var get = new DynamicMethod(
                "GetListItems", typeof(T[]), new[] { typeof(List<T>) },
                typeof(ListInternals<T>).Module, true);
            var il = get.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, itemsField);
            il.Emit(OpCodes.Ret);

            var set = new DynamicMethod(
                "SetListSize", typeof(void), new[] { typeof(List<T>), typeof(int) },
                typeof(ListInternals<T>).Module, true);
            il = set.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, sizeField);
            il.Emit(OpCodes.Ret);

            // Assign both only after both compiled, so callers see all-or-nothing.
            _getItems = (Func<List<T>, T[]>)get.CreateDelegate(typeof(Func<List<T>, T[]>));
            _setSize = (Action<List<T>, int>)set.CreateDelegate(typeof(Action<List<T>, int>));
        }
        catch (Exception)
        {
            _getItems = null;
            _setSize = null;
        }
    }
}
