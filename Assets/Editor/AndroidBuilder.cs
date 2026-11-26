using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityBuild
{
    /// <summary>
    /// GameCI (game-ci/unity-builder) 调用的 Android 构建入口。
    /// 工程里没有场景时自动生成一个含相机与平行光的默认场景，保证构建不会因空场景列表失败。
    /// </summary>
    public static class AndroidBuilder
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string FallbackOutputPath = "build/Android/UnityBuild.apk";
        private const string ApplicationIdentifier = "com.defaultcompany.unitybuild";

        public static void Build()
        {
            try
            {
                Dictionary<string, string> args = ParseCommandLine();
                string outputPath = GetValue(args, "customBuildPath", FallbackOutputPath);
                if (!outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) &&
                    !outputPath.EndsWith(".aab", StringComparison.OrdinalIgnoreCase))
                {
                    outputPath += ".apk";
                }

                string scenePath = EnsureMainScene();
                ApplyPlayerSettings(args);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None,
                };

                Debug.Log($"[AndroidBuilder] scene      : {scenePath}");
                Debug.Log($"[AndroidBuilder] output     : {outputPath}");
                Debug.Log($"[AndroidBuilder] backend    : {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, " +
                          $"architectures: {PlayerSettings.Android.targetArchitectures}, " +
                          $"minSdk: {PlayerSettings.Android.minSdkVersion}, targetSdk: {PlayerSettings.Android.targetSdkVersion}");

                BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
                Debug.Log($"[AndroidBuilder] result     : {summary.result}, " +
                          $"errors: {summary.totalErrors}, warnings: {summary.totalWarnings}, " +
                          $"size: {summary.totalSize} bytes, time: {summary.totalTime}");

                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidBuilder] 构建异常: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static string EnsureMainScene()
        {
            if (!File.Exists(MainScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MainScenePath) ?? "Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, MainScenePath);
                AssetDatabase.Refresh();
                Debug.Log($"[AndroidBuilder] 已生成默认场景: {MainScenePath}");
            }

            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == MainScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log($"[AndroidBuilder] 已把场景加入 Build Settings: {MainScenePath}");
            }

            return MainScenePath;
        }

        private static void ApplyPlayerSettings(Dictionary<string, string> args)
        {
            PlayerSettings.companyName = "DefaultCompany";
            PlayerSettings.productName = "UnityBuild";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);

            string buildVersion = GetValue(args, "buildVersion", string.Empty);
            if (!string.IsNullOrEmpty(buildVersion) && !buildVersion.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                PlayerSettings.bundleVersion = buildVersion;
            }

            int versionCode = 0;
            if (int.TryParse(GetValue(args, "androidVersionCode", string.Empty), out int parsed))
            {
                versionCode = parsed;
            }

            if (versionCode < 1)
            {
                versionCode = Math.Max(PlayerSettings.Android.bundleVersionCode, 1);
            }

            PlayerSettings.Android.bundleVersionCode = versionCode;

            // 默认用 Unity 自动生成的 debug keystore 签名，产出可直接安装的 APK。
            // 需要正式签名时把 keystore 放进仓库并在这里设置 useCustomKeystore / keystoreName 等字段。
            PlayerSettings.Android.useCustomKeystore = false;
        }

        private static string GetValue(Dictionary<string, string> args, string key, string fallback)
        {
            return args.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        private static Dictionary<string, string> ParseCommandLine()
        {
            var parsed = new Dictionary<string, string>();
            string[] argv = Environment.GetCommandLineArgs();

            for (int i = 0; i < argv.Length; i++)
            {
                if (!argv[i].StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = argv[i].TrimStart('-');
                string value = i + 1 < argv.Length && !argv[i + 1].StartsWith("-", StringComparison.Ordinal)
                    ? argv[i + 1]
                    : string.Empty;

                parsed[key] = value;
            }

            return parsed;
        }
    }
}
