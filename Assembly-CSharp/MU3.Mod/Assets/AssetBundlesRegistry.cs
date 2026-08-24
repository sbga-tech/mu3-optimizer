using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace MU3.Mod.Assets;

public static class AssetBundlesRegistry
{
    private const string ResourcePrefix = "MU3.Mod.Assets.Res.";
    private static readonly Assembly Assembly = typeof(AssetBundlesRegistry).Assembly;
    private static readonly Dictionary<string, string> Resources = FindResources();
    private static readonly Dictionary<string, Dictionary<string, UnityEngine.Object>> Assets =
        new Dictionary<string, Dictionary<string, UnityEngine.Object>>(StringComparer.OrdinalIgnoreCase);

    public static T LoadAsset<T>(string bundleName, string assetPath)
        where T : UnityEngine.Object
    {
        Dictionary<string, UnityEngine.Object> bundleAssets;
        if (!Assets.TryGetValue(bundleName, out bundleAssets))
        {
            bundleAssets = LoadAllAssets(bundleName);
            Assets.Add(bundleName, bundleAssets);
        }

        UnityEngine.Object asset;
        if (!bundleAssets.TryGetValue(assetPath, out asset))
            throw new InvalidOperationException(
                "AssetBundle " + bundleName + " is missing asset: " + assetPath);

        T typedAsset = asset as T;
        if (typedAsset == null)
            throw new InvalidOperationException(
                "AssetBundle asset " + assetPath + " is not a " + typeof(T).FullName);
        return typedAsset;
    }

    private static Dictionary<string, UnityEngine.Object> LoadAllAssets(string bundleName)
    {
        AssetBundle bundle = LoadBundle(bundleName);
        bool succeeded = false;
        try
        {
            var assets = new Dictionary<string, UnityEngine.Object>(StringComparer.OrdinalIgnoreCase);
            string[] assetNames = bundle.GetAllAssetNames();
            for (int i = 0; i < assetNames.Length; i++)
            {
                string assetName = assetNames[i];
                UnityEngine.Object asset = bundle.LoadAsset(assetName);
                if (asset == null)
                    throw new InvalidOperationException(
                        "Failed to load AssetBundle asset: " + assetName);
                assets.Add(assetName, asset);
            }
            succeeded = true;
            return assets;
        }
        finally
        {
            bundle.Unload(!succeeded);
        }
    }

    private static Dictionary<string, string> FindResources()
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] names = Assembly.GetManifestResourceNames();
        for (int i = 0; i < names.Length; i++)
        {
            string resourceName = names[i];
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;

            string bundleName = resourceName.Substring(ResourcePrefix.Length);
            if (bundleName.Length == 0 || resources.ContainsKey(bundleName))
                throw new InvalidOperationException("Invalid embedded AssetBundle resource: " + resourceName);
            resources.Add(bundleName, resourceName);
        }
        return resources;
    }

    private static AssetBundle LoadBundle(string bundleName)
    {
        string resourceName;
        if (!Resources.TryGetValue(bundleName, out resourceName))
            throw new InvalidOperationException("Missing embedded AssetBundle: " + bundleName);

        AssetBundle bundle = AssetBundle.LoadFromMemory(ReadResource(resourceName));
        if (bundle == null)
            throw new InvalidOperationException("Failed to load embedded AssetBundle: " + bundleName);
        return bundle;
    }

    private static byte[] ReadResource(string resourceName)
    {
        using (Stream stream = Assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null)
                throw new InvalidOperationException("Missing embedded resource: " + resourceName);
            if (stream.Length > int.MaxValue)
                throw new InvalidOperationException("Embedded AssetBundle is too large: " + resourceName);

            byte[] data = new byte[(int)stream.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                int read = stream.Read(data, offset, data.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("Unexpected end of embedded AssetBundle: " + resourceName);
                offset += read;
            }
            return data;
        }
    }
}
