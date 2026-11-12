using System.IO;
using UnityEditor;
using UnityEngine;

namespace Friendslop.EditorTools
{
    public static class Builder
    {
        [MenuItem("Tools/Friendslop/Build All")]
        public static void BuildAll()
        {
            EnsureConfig();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[BUILDALL OK]");
        }

        [MenuItem("Tools/Friendslop/Build Windows")]
        public static void BuildWindows()
        {
            BuildAll();
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            Directory.CreateDirectory("Build");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = "Build/Friendslop.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded) Debug.Log("[BUILD OK]");
            else { Debug.LogError("[BUILD FAIL] " + report.summary.result); EditorApplication.Exit(1); }
        }

        static void EnsureConfig()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GameConfig.AssetPath));
            if (AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfig.AssetPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<GameConfig>(), GameConfig.AssetPath);
        }
    }
}
