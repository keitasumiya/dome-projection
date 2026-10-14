#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DomeProjection.Editor
{
    [InitializeOnLoad]
    internal static class DomeProjectionProjectSetup
    {
        private const string MainScenePath = "Assets/Scenes/Main.unity";
        private const string ReleaseVersion = "2026.10.15.7";

        static DomeProjectionProjectSetup()
        {
            EditorApplication.delayCall += EnsureProjectSettings;
        }

        private static void EnsureProjectSettings()
        {
            // Keep this stable: Unity uses company/product when deriving the
            // persistentDataPath that contains saved calibration settings.
            PlayerSettings.productName = "dome-projection";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.DefaultCompany.dome-projection");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            // Control must start as an ordinary resizable window. Runtime code
            // maximizes it with the native OS window command so macOS does not
            // turn Unity's MaximizedWindow mode into a separate fullscreen Space.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.forceSingleInstance = true;

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(scene => scene.path != MainScenePath))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log("Added Dome Projection Main scene to Build Settings.");
            }
        }

        [MenuItem("Dome Projection/Build Windows x86_64")]
        public static void BuildWindowsPlayer()
        {
            BuildPlayer("Builds/Windows/Control/DomeProjectionControl.exe", BuildTarget.StandaloneWindows64,
                "Dome Projection Control", "com.DefaultCompany.dome-projection.control", null,
                "Assets/DomeProjection/Icons/control.png");
            BuildPlayer("Builds/Windows/Output/DomeProjectionOutput.exe", BuildTarget.StandaloneWindows64,
                "Dome Projection Output", "com.DefaultCompany.dome-projection.output", "DOME_PROJECTION_OUTPUT",
                "Assets/DomeProjection/Icons/output.png");
        }

        [MenuItem("Dome Projection/Build macOS")]
        public static void BuildMacPlayer()
        {
            BuildPlayer("Builds/DomeProjectionControl.app", BuildTarget.StandaloneOSX,
                "Dome Projection Control", "com.DefaultCompany.dome-projection.control", null,
                "Assets/DomeProjection/Icons/control.png");
            BuildPlayer("Builds/DomeProjectionOutput.app", BuildTarget.StandaloneOSX,
                "Dome Projection Output", "com.DefaultCompany.dome-projection.output", "DOME_PROJECTION_OUTPUT",
                "Assets/DomeProjection/Icons/output.png");
        }

        private static void BuildPlayer(string relativeOutputPath, BuildTarget target, string productName,
            string applicationIdentifier, string scriptingDefine, string iconPath)
        {
            EnsureProjectSettings();

            PlayerSettings.productName = productName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, applicationIdentifier);
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.bundleVersion = ReleaseVersion;
            if (target == BuildTarget.StandaloneOSX)
                PlayerSettings.macOS.buildNumber = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            if (icon == null) throw new BuildFailedException("App icon was not found: " + iconPath);
            // Standalone builds require one texture for every generated icon size.
            // Reusing the 1024 px source lets Unity downsample it for macOS and Windows.
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone,
                Enumerable.Repeat(icon, 8).ToArray(), IconKind.Application);

            var outputPath = Path.GetFullPath(relativeOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None,
                extraScriptingDefines = string.IsNullOrEmpty(scriptingDefine)
                    ? new string[0]
                    : new[] { scriptingDefine }
            };
            var report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
#if UNITY_EDITOR_OSX
                if (target == BuildTarget.StandaloneOSX)
                {
                    SignMacDevelopmentBuild(outputPath, applicationIdentifier);
                    ValidateMacIcon(outputPath);
                    Directory.SetLastWriteTimeUtc(outputPath, DateTime.UtcNow);
                }
#endif
                Debug.Log($"{target} player built: {outputPath}");
            }
            else
                Debug.LogError($"{target} player build failed: {report.summary.result}");

            EnsureProjectSettings();
        }

#if UNITY_EDITOR_OSX
        private static void ValidateMacIcon(string appPath)
        {
            var iconPath = Path.Combine(appPath, "Contents", "Resources", "PlayerIcon.icns");
            if (!File.Exists(iconPath) || new FileInfo(iconPath).Length < 1024)
                throw new BuildFailedException("macOS app icon was not embedded: " + iconPath);
        }

        private static void SignMacDevelopmentBuild(string appPath, string applicationIdentifier)
        {
            // Unity's default ad-hoc designated requirement is a changing CDHash,
            // which makes macOS forget Screen Recording permission after every
            // rebuild. Embed a stable development-only designated requirement.
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/codesign",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--force");
            startInfo.ArgumentList.Add("--deep");
            startInfo.ArgumentList.Add("--sign");
            startInfo.ArgumentList.Add("-");
            startInfo.ArgumentList.Add("--identifier");
            startInfo.ArgumentList.Add(applicationIdentifier);
            startInfo.ArgumentList.Add("--requirements");
            startInfo.ArgumentList.Add($"=designated => identifier \"{applicationIdentifier}\"");
            startInfo.ArgumentList.Add(appPath);

            using var process = System.Diagnostics.Process.Start(startInfo);
            var output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            var error = process?.StandardError.ReadToEnd() ?? string.Empty;
            process?.WaitForExit();
            if (process == null || process.ExitCode != 0)
                throw new BuildFailedException("Could not sign macOS development build: " + error);
            if (!string.IsNullOrWhiteSpace(output)) Debug.Log(output.Trim());
            if (!string.IsNullOrWhiteSpace(error)) Debug.Log(error.Trim());
        }
#endif
    }
}
#endif

