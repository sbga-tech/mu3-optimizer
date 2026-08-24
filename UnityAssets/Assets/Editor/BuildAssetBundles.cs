using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Headless entry point for ./build.sh build-assets. Builds every labelled
/// AssetBundle and records the complete flat output set for the shell build
/// to collect after Unity releases its file handles.
/// </summary>
public static class BuildAssetBundles
{
    [MenuItem("MU3/Build AssetBundles")]
    public static void BuildAll()
    {
        string output = Environment.GetEnvironmentVariable("MU3_ASSET_BUNDLE_OUTPUT_DIR");
        if (string.IsNullOrEmpty(output))
        {
            output = Path.Combine(Directory.GetCurrentDirectory(), "Build");
        }
        Directory.CreateDirectory(output);

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            output, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows);
        if (manifest == null)
        {
            throw new Exception("BuildAssetBundles returned no manifest");
        }

        string[] bundles = manifest.GetAllAssetBundles();
        if (bundles.Length == 0)
        {
            throw new Exception("no AssetBundles were built");
        }
        Array.Sort(bundles, StringComparer.Ordinal);
        File.WriteAllLines(Path.Combine(output, "bundles.list"), bundles);
    }
}
