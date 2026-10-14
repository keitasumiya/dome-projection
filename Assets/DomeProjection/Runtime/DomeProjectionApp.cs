using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DomeProjection
{
    public sealed class DomeProjectionApp : MonoBehaviour
    {
        private const float PanelWidth = 440f;
        private readonly string[] gridModeNames = { "なし", "動画に重ねる", "校正パターン" };

        private DomeProjectionSettings settings;
        private IVideoSource video;
        private DomeProjectionRenderer outputRenderer;
        private Camera outputCamera;
        private string status = "校正パターンを表示中";
        private string pathField = string.Empty;
        private bool showControls = true;
        private bool showAdvanced;
        private GUIStyle titleStyle;
        private GUIStyle smallStyle;
        private int activeDisplay = -1;

        private string SettingsPath => Path.Combine(Application.persistentDataPath, "dome-projection-settings.json");

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 1;
            LoadSettings();
            CreateVideoSource();
            CreateOutputCamera();
            SetOutputDisplay(settings.outputDisplay);
        }

        private void Start()
        {
            var commandLineVideo = GetCommandLineVideoPath();
            if (!string.IsNullOrWhiteSpace(commandLineVideo))
            {
                pathField = commandLineVideo;
                OpenVideo();
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.videoPath))
                pathField = settings.videoPath;
        }

        private static string GetCommandLineVideoPath()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], "--video", StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1].Trim().Trim('"');
            }

            return string.Empty;
        }

        private void OnApplicationQuit()
        {
            SaveSettings();
        }

        private void CreateVideoSource()
        {
            var sourceObject = new GameObject("Unity Video Source");
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<UnityVideoSource>();
            source.Prepared += OnVideoPrepared;
            source.Failed += OnVideoFailed;
            video = source;
        }

        private void CreateOutputCamera()
        {
            var cameraObject = new GameObject("Dome Output Camera");
            cameraObject.transform.SetParent(transform, false);
            outputCamera = cameraObject.AddComponent<Camera>();
            outputCamera.clearFlags = CameraClearFlags.SolidColor;
            outputCamera.backgroundColor = Color.black;
            outputCamera.cullingMask = 0;
            outputCamera.depth = -100f;
            outputCamera.allowHDR = false;
            outputCamera.allowMSAA = false;

            outputRenderer = cameraObject.AddComponent<DomeProjectionRenderer>();
            outputRenderer.Settings = settings;
            outputRenderer.VideoSource = video;
        }

        private void OnVideoPrepared()
        {
            settings.videoPath = video.SourcePath;
            settings.loop = true;
            video.IsLooping = settings.loop;
            status = $"読込完了: {video.Width} × {video.Height}";

            if (video.Height > 0)
            {
                var aspect = (double)video.Width / video.Height;
                if (Math.Abs(aspect - 2d) > 0.03d)
                    status += "  ⚠ 2:1のEquirectangular映像ではありません";
            }

            if (settings.gridMode == 2) settings.gridMode = 0;
            video.Play();
            SaveSettings();
        }

        private void OnVideoFailed(string message)
        {
            status = "動画エラー: " + message;
        }

        private void OpenVideo()
        {
            var path = pathField.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            status = "動画を準備中…";
            video.Open(path);
        }

        private void BrowseVideo()
        {
            var selected = string.Empty;

#if UNITY_EDITOR
            selected = EditorUtility.OpenFilePanel("360°動画を選択", string.Empty, "mp4,mov,m4v");
#elif UNITY_STANDALONE_OSX
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    Arguments = "-e \"POSIX path of (choose file with prompt \\\"360°動画を選択\\\")\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(startInfo);
                selected = process?.StandardOutput.ReadToEnd().Trim() ?? string.Empty;
                process?.WaitForExit();
            }
            catch (Exception exception)
            {
                status = "ファイル選択を開けませんでした: " + exception.Message;
            }
#endif

            if (!string.IsNullOrWhiteSpace(selected))
            {
                pathField = selected;
                OpenVideo();
            }
        }

        private void SetOutputDisplay(int requestedDisplay)
        {
            var maxDisplay = Mathf.Max(0, Display.displays.Length - 1);
            var display = Mathf.Clamp(requestedDisplay, 0, maxDisplay);
            if (display == activeDisplay) return;

            if (display > 0) Display.displays[display].Activate();
            outputCamera.targetDisplay = display;
            settings.outputDisplay = display;
            activeDisplay = display;
        }

        private void Update()
        {
            video.IsLooping = settings.loop;
            SetOutputDisplay(settings.outputDisplay);
        }

        private void OnGUI()
        {
            HandleHotkeys(Event.current);
            if (!showControls) return;

            BuildStyles();
            GUILayout.BeginArea(new Rect(16f, 16f, PanelWidth, Screen.height - 32f), GUI.skin.window);
            GUILayout.Label("Dome Projection Player", titleStyle);
            GUILayout.Label(status, smallStyle);
            GUILayout.Space(8f);

            GUILayout.Label("360°動画（2:1 Equirectangular MP4）");
            GUILayout.BeginHorizontal();
            pathField = GUILayout.TextField(pathField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("選択", GUILayout.Width(54f))) BrowseVideo();
            if (GUILayout.Button("読込", GUILayout.Width(54f))) OpenVideo();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = video.IsPrepared;
            if (GUILayout.Button(video.IsPlaying ? "一時停止" : "再生"))
            {
                if (video.IsPlaying) video.Pause(); else video.Play();
            }
            if (GUILayout.Button("停止")) video.Stop();
            GUI.enabled = true;
            settings.loop = GUILayout.Toggle(settings.loop, "ループ", GUILayout.Width(75f));
            GUILayout.EndHorizontal();

            if (video.IsPrepared && video.Duration > 0d)
            {
                var nextTime = Slider("再生位置", (float)video.Time, 0f, (float)video.Duration, FormatTime(video.Time) + " / " + FormatTime(video.Duration));
                if (Math.Abs(nextTime - video.Time) > 0.25d) video.Time = nextTime;
            }

            GUILayout.Space(8f);
            GUILayout.Label("表示方向");
            settings.yaw = Slider("Yaw", settings.yaw, -180f, 180f, settings.yaw.ToString("0.0°"));
            settings.pitch = Slider("Pitch", settings.pitch, -90f, 90f, settings.pitch.ToString("0.0°"));
            settings.roll = Slider("Roll", settings.roll, -180f, 180f, settings.roll.ToString("0.0°"));

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("校正表示", GUILayout.Width(82f));
            if (GUILayout.Button(gridModeNames[settings.gridMode]))
                settings.gridMode = (settings.gridMode + 1) % 3;
            if (GUILayout.Button("幾何をリセット", GUILayout.Width(110f))) settings.ResetGeometry();
            GUILayout.EndHorizontal();

            settings.centerX = Slider("中心 X", settings.centerX, 0.25f, 0.75f, settings.centerX.ToString("0.000"));
            settings.centerY = Slider("中心 Y", settings.centerY, 0.25f, 0.75f, settings.centerY.ToString("0.000"));
            settings.radius = Slider("円半径", settings.radius, 0.2f, 0.7f, settings.radius.ToString("0.000"));

            showAdvanced = GUILayout.Toggle(showAdvanced, "詳細なレンズ補正を表示");
            if (showAdvanced)
            {
                settings.ellipseX = Slider("横倍率", settings.ellipseX, 0.75f, 1.25f, settings.ellipseX.ToString("0.000"));
                settings.ellipseY = Slider("縦倍率", settings.ellipseY, 0.75f, 1.25f, settings.ellipseY.ToString("0.000"));
                settings.outputRotation = Slider("出力回転", settings.outputRotation, -180f, 180f, settings.outputRotation.ToString("0.0°"));
                settings.radialK1 = Slider("放射補正 K1", settings.radialK1, -0.3f, 0.3f, settings.radialK1.ToString("0.000"));
                settings.radialK2 = Slider("放射補正 K2", settings.radialK2, -0.3f, 0.3f, settings.radialK2.ToString("0.000"));
                settings.brightness = Slider("明るさ", settings.brightness, 0f, 2f, settings.brightness.ToString("0.00"));
                settings.gamma = Slider("ガンマ", settings.gamma, 0.4f, 2.5f, settings.gamma.ToString("0.00"));
                settings.edgeFade = Slider("円周フェード", settings.edgeFade, 0f, 0.2f, settings.edgeFade.ToString("0.000"));

                GUILayout.BeginHorizontal();
                settings.flipInputX = GUILayout.Toggle(settings.flipInputX, "入力を左右反転");
                settings.flipInputY = GUILayout.Toggle(settings.flipInputY, "入力を上下反転");
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("設定を保存")) { SaveSettings(); status = "設定を保存しました"; }
            if (GUILayout.Button("設定を再読込")) { LoadSettings(); status = "設定を再読込しました"; }
            GUILayout.EndHorizontal();

            if (Display.displays.Length > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("出力画面", GUILayout.Width(82f));
                for (var i = 0; i < Display.displays.Length; i++)
                    if (GUILayout.Toggle(settings.outputDisplay == i, i.ToString(), GUI.skin.button)) settings.outputDisplay = i;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Space: 再生/停止   G: グリッド   H: UI表示   F: 全画面", smallStyle);
            GUILayout.Label($"出力 {Screen.width}×{Screen.height} / Display {settings.outputDisplay}", smallStyle);
            GUILayout.EndArea();
        }

        private static float Slider(string label, float value, float min, float max, string display)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(82f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(display, GUILayout.Width(72f));
            GUILayout.EndHorizontal();
            return value;
        }

        private void HandleHotkeys(Event current)
        {
            if (current.type != EventType.KeyDown) return;
            switch (current.keyCode)
            {
                case KeyCode.Space:
                    if (video.IsPlaying) video.Pause(); else video.Play();
                    current.Use();
                    break;
                case KeyCode.G:
                    settings.gridMode = (settings.gridMode + 1) % 3;
                    current.Use();
                    break;
                case KeyCode.H:
                    showControls = !showControls;
                    current.Use();
                    break;
                case KeyCode.F:
                    Screen.fullScreen = !Screen.fullScreen;
                    current.Use();
                    break;
                case KeyCode.R:
                    settings.ResetGeometry();
                    current.Use();
                    break;
            }
        }

        private void BuildStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        }

        private void LoadSettings()
        {
            try
            {
                settings = File.Exists(SettingsPath)
                    ? JsonUtility.FromJson<DomeProjectionSettings>(File.ReadAllText(SettingsPath))
                    : new DomeProjectionSettings();
                settings ??= new DomeProjectionSettings();
                settings.Clamp();
                pathField = settings.videoPath ?? string.Empty;
            }
            catch (Exception exception)
            {
                settings = new DomeProjectionSettings();
                status = "設定読込エラー: " + exception.Message;
            }

            if (outputRenderer != null) outputRenderer.Settings = settings;
        }

        private void SaveSettings()
        {
            try
            {
                settings.videoPath = pathField;
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(settings, true));
            }
            catch (Exception exception)
            {
                status = "設定保存エラー: " + exception.Message;
            }
        }

        private static string FormatTime(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0d) seconds = 0d;
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1d ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }
    }
}

