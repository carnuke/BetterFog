using UnityEditor;
using UnityEngine;
using System.IO;

public static class BuildBundles
{
    [MenuItem("Build/Build BetterFog Bundle")]
    static void Build()
    {
        const string shaderAssetPath = "Assets/Shaders/CylindricalFog.shader";

        var importer = AssetImporter.GetAtPath(shaderAssetPath);
        if (importer == null)
        {
            Debug.LogError($"Asset not found: {shaderAssetPath}");
            return;
        }

        importer.SetAssetBundleNameAndVariant("betterfog", "");
        AssetDatabase.SaveAssets();

        // Output alongside the BetterFog C# project (assumes this Unity project
        // lives at  <repo>/BetterFogShaders/  next to  <repo>/BetterFog/)
        string outputDir = Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "..", "BetterFog", "Assets"));
        Directory.CreateDirectory(outputDir);

        BuildPipeline.BuildAssetBundles(
            outputDir,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log($"BetterFog bundle written to: {outputDir}");
    }
}
