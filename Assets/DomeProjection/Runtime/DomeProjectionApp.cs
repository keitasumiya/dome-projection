using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

namespace DomeProjection
{
    public sealed class DomeProjectionApp : MonoBehaviour
    {
        private const float PanelWidth = 440f;
        private const float ReferenceUiHeight = 900f;
        private const float VideoPathWidth = 288f;
        private readonly string[] gridModeNames = { "なし", "素材に重ねる", "校正パターン" };
        private readonly string[] panelTabNames = { "配置", "校正", "レンズ", "投影" };
        private readonly string[] sourceModeNames = { "動画・画像", "外部ウィンドウ" };
        private readonly int[] bridgeRates = { 30, 60, 120 };

        private DomeProjectionSettings settings;
        private IVideoSource video;
        private IWindowCaptureSource windowCapture;
        private DomeProjectionRenderer outputRenderer;
        private Camera outputCamera;
        private DomeProjectionRenderer previewRenderer;
        private Camera previewCamera;
        private string status = "校正パターンを表示中";
        private string pathField = string.Empty;
        private string windowWidthField = "1600";
        private string windowHeightField = "900";
        private bool showControls = true;
        private GUIStyle titleStyle;
        private GUIStyle smallStyle;
        private GUIStyle panelStyle;
        private GUIStyle sliderStyle;
        private GUIStyle sliderThumbStyle;
        private Texture2D panelTexture;
        private Texture2D sliderTexture;
        private Texture2D sliderThumbTexture;
        private Texture2D sliderThumbActiveTexture;
        private int activeDisplay = -1;
        private int activePanelTab;
        private int selectedWindowIndex = -1;
        private bool focusCapturedWindowNextFrame;
        private bool showWindowDropdown;
        private float windowDropdownScroll;
        private Rect windowDropdownAnchor;
        private bool consumeWindowDropdownMouseUp;
        private bool mediaSeekDragging;
        private double mediaSeekPreviewTime;
        private bool resumeMediaAfterSeek;
        private Rect panelAreaRect;
        private FileStream instanceLock;
        private bool instanceFileRegionLocked;
        private bool projectorMode;
        private bool permissionSetupMode;
        private bool bridgeWantsMediaPlayback;
        private float nextBridgeUpdate;
        private float nextHeartbeatUpdate;
        private long bridgeSequence;
        private long lastAppliedBridgeSequence = -1;
        private long projectorStartedUtcTicks;
        private float outputLaunchPendingUntil;
        private string bridgedMediaPath = string.Empty;
        private long bridgedWindowId;
        private string bridgedWindowOwner = string.Empty;
        private string bridgedWindowTitle = string.Empty;

        private string SettingsPath => ProjectionProcessBridge.SettingsPath;

#if DOME_PROJECTION_OUTPUT
        private const bool IsOutputBuild = true;
#else
        private const bool IsOutputBuild = false;
#endif

        private void Awake()
        {
            permissionSetupMode = HasCommandLineFlag("--permission-setup");
            projectorMode = IsOutputBuild || HasCommandLineFlag("--projector") || permissionSetupMode;
            projectorStartedUtcTicks = DateTime.UtcNow.Ticks;
            if (!AcquireSingleInstanceLock()) return;
            if (projectorMode) ProjectionProcessBridge.InitializeOutput();
            else ProjectionProcessBridge.InitializeControl();

            Application.runInBackground = true;
            LoadSettings();
            QualitySettings.vSyncCount = projectorMode ? 1 : 0;
            Application.targetFrameRate = projectorMode ? -1 : settings.bridgeUpdateHz;
            windowWidthField = settings.windowWidth.ToString();
            windowHeightField = settings.windowHeight.ToString();
#if !UNITY_EDITOR
            if (!projectorMode) ApplyWindowSettings(CommandLineRequestsWindowed());
#endif
            CreateVideoSource();
            CreateWindowCaptureSource();
            CreateOutputCamera();
            SetOutputDisplay(0);
        }

        private void Start()
        {
            if (permissionSetupMode)
            {
                showControls = false;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                StartCoroutine(RequestOutputPermissionAndQuit());
#else
                Application.Quit();
#endif
                return;
            }

            if (projectorMode)
            {
                showControls = false;
#if !UNITY_EDITOR
                ConfigureProjectorDisplay();
#endif
                status = "Controlアプリからの入力を待っています";
                return;
            }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            if (!settings.initialMacPermissionsRequested)
            {
                settings.initialMacPermissionsRequested = true;
                SaveSettings();
                StartCoroutine(RequestInitialMacPermissions());
            }
#endif

            var commandLineWindow = GetCommandLineWindowQuery();
            if (!string.IsNullOrWhiteSpace(commandLineWindow))
            {
                settings.sourceMode = 1;
                RefreshWindowList();
                for (var index = 0; index < windowCapture.Windows.Count; index++)
                {
                    var candidate = windowCapture.Windows[index];
                    if (candidate.DisplayName.IndexOf(commandLineWindow, StringComparison.CurrentCultureIgnoreCase) < 0)
                        continue;
                    selectedWindowIndex = index;
                    BeginWindowCapture(candidate, false);
                    return;
                }
                status = "指定されたウィンドウが見つかりません: " + commandLineWindow;
                return;
            }

            var commandLineMedia = GetCommandLineMediaPath();
            if (!string.IsNullOrWhiteSpace(commandLineMedia))
            {
                pathField = commandLineMedia;
                OpenMedia();
                return;
            }

            if (settings.sourceMode == 1)
            {
                RefreshWindowList();
                RestoreCapturedWindow();
            }
            else if (!string.IsNullOrWhiteSpace(settings.videoPath))
            {
                pathField = settings.videoPath;
                OpenMedia();
            }
        }

        private static string GetCommandLineMediaPath()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if (string.Equals(arguments[i], "--video", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arguments[i], "--image", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arguments[i], "--media", StringComparison.OrdinalIgnoreCase))
                    return arguments[i + 1].Trim().Trim('"');
            }

            return string.Empty;
        }

        private static string GetCommandLineWindowQuery()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], "--window", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arguments[index], "--capture-window", StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1].Trim().Trim('"');
            }
            return string.Empty;
        }

        private static bool CommandLineRequestsWindowed()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], "--windowed", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(arguments[i], "-screen-fullscreen", StringComparison.OrdinalIgnoreCase)
                    && i + 1 < arguments.Length && arguments[i + 1] == "0") return true;
            }

            return false;
        }

        private static bool HasCommandLineFlag(string flag)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length; i++)
                if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void OnApplicationQuit()
        {
            if (!projectorMode)
            {
                if (settings != null) SaveSettings();
                ProjectionProcessBridge.RequestOutputStop();
            }
        }

        private void OnDestroy()
        {
            if (panelTexture != null) Destroy(panelTexture);
            if (sliderTexture != null) Destroy(sliderTexture);
            if (sliderThumbTexture != null) Destroy(sliderThumbTexture);
            if (sliderThumbActiveTexture != null) Destroy(sliderThumbActiveTexture);
            if (instanceFileRegionLocked && instanceLock != null)
            {
                try { instanceLock.Unlock(0, 1); }
                catch { }
            }
            instanceFileRegionLocked = false;
            instanceLock?.Dispose();
            instanceLock = null;
            ProjectionProcessBridge.Shutdown();
        }

        private bool AcquireSingleInstanceLock()
        {
#if UNITY_EDITOR
            return true;
#else
            try
            {
                var lockPath = projectorMode
                    ? ProjectionProcessBridge.OutputLockPath
                    : ProjectionProcessBridge.ControlLockPath;
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? Application.persistentDataPath);
                instanceLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                if (instanceLock.Length < 1) instanceLock.SetLength(1);
                instanceLock.Lock(0, 1);
                instanceFileRegionLocked = true;
                return true;
            }
            catch (Exception)
            {
                instanceLock?.Dispose();
                instanceLock = null;
                Application.Quit();
                enabled = false;
                return false;
            }
#endif
        }

        private void CreateVideoSource()
        {
            var sourceObject = new GameObject("Unity Video Source");
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<UnityVideoSource>();
            source.Prepared += OnMediaPrepared;
            source.Failed += OnMediaFailed;
            video = source;
            video.AudioMuted = settings.audioMuted;
        }

        private void CreateWindowCaptureSource()
        {
            var sourceObject = new GameObject("Native Window Capture Source");
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<NativeWindowCaptureSource>();
            source.Prepared += OnWindowPrepared;
            source.Failed += OnWindowFailed;
            source.CaptureFps = settings.windowCaptureFps;
            source.RequestPermissionAutomatically = projectorMode;
            windowCapture = source;
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
            outputRenderer.WindowSource = windowCapture;

            var previewObject = new GameObject("Operator Preview Camera");
            previewObject.transform.SetParent(transform, false);
            previewCamera = previewObject.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = Color.black;
            previewCamera.cullingMask = 0;
            previewCamera.depth = -100f;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = false;
            previewCamera.targetDisplay = 0;
            previewCamera.enabled = false;

            previewRenderer = previewObject.AddComponent<DomeProjectionRenderer>();
            previewRenderer.Settings = settings;
            previewRenderer.VideoSource = video;
            previewRenderer.WindowSource = windowCapture;
        }

        private void OnMediaPrepared()
        {
            settings.videoPath = video.SourcePath;
            if (!video.IsStillImage) video.IsLooping = settings.loop;
            status = $"{(video.IsStillImage ? "画像" : "動画")}読込完了: {video.Width} × {video.Height}";

            if (video.Height > 0)
            {
                var aspect = (double)video.Width / video.Height;
                if (!settings.fileIsFlat && Math.Abs(aspect - 2d) > 0.03d)
                    status += "  ⚠ 2:1のEquirectangular素材ではありません";
            }

            if (settings.gridMode == 2) settings.gridMode = 0;
            if (!video.IsStillImage)
            {
                if (!projectorMode || bridgeWantsMediaPlayback) video.Play();
                else video.Pause();
            }
            SaveSettings();
        }

        private void OnMediaFailed(string message)
        {
            status = "読込エラー: " + message;
        }

        private void OnWindowPrepared()
        {
            var selected = windowCapture.SelectedWindow;
            status = selected == null
                ? $"ウィンドウ取込完了: {windowCapture.Width} × {windowCapture.Height}"
                : $"取込中: {selected.DisplayName} ({windowCapture.Width} × {windowCapture.Height})";
            if (settings.gridMode == 2) settings.gridMode = 0;
            SaveSettings();
        }

        private void OnWindowFailed(string message)
        {
            status = "ウィンドウ取込エラー: " + message;
        }

        private void OpenMedia()
        {
            var path = pathField.Trim();
            if (string.IsNullOrWhiteSpace(path)) return;
            mediaSeekDragging = false;
            resumeMediaAfterSeek = false;
            SetSourceMode(0);
            status = "素材を準備中…";
            video.Open(path);
        }

        private void BrowseMedia()
        {
            var selected = string.Empty;

#if UNITY_EDITOR
            selected = EditorUtility.OpenFilePanelWithFilters("360°動画・画像を選択", string.Empty,
                new[] { "動画・画像", "mp4,mov,m4v,jpg,jpeg,png", "すべてのファイル", "*" });
#elif UNITY_STANDALONE_OSX
            selected = OpenMacMediaFilePanel(out var macDialogError);
            if (!string.IsNullOrWhiteSpace(macDialogError)) status = macDialogError;
#elif UNITY_STANDALONE_WIN
            selected = OpenWindowsMediaFileDialog(out var dialogError);
            if (!string.IsNullOrWhiteSpace(dialogError))
                status = dialogError;
#else
            status = "このOSではファイル選択を利用できません。パスを直接入力してください。";
#endif

            if (!string.IsNullOrWhiteSpace(selected))
            {
                pathField = selected;
                OpenMedia();
            }
        }

#if UNITY_STANDALONE_OSX
        private static string OpenMacMediaFilePanel(out string error)
        {
            error = string.Empty;
            try
            {
                var panelClass = objc_getClass("NSOpenPanel");
                var panel = objc_msgSend_IntPtr(panelClass, sel_registerName("openPanel"));
                if (panel == IntPtr.Zero) throw new InvalidOperationException("NSOpenPanelを作成できませんでした。");

                objc_msgSend_Byte(panel, sel_registerName("setCanChooseFiles:"), 1);
                objc_msgSend_Byte(panel, sel_registerName("setCanChooseDirectories:"), 0);
                objc_msgSend_Byte(panel, sel_registerName("setAllowsMultipleSelection:"), 0);
                objc_msgSend_Byte(panel, sel_registerName("setResolvesAliases:"), 1);

                // NSModalResponseOK is 1. AppKitを直接呼ぶことでosascriptの起動待ちをなくす。
                if (objc_msgSend_Long(panel, sel_registerName("runModal")) != 1) return string.Empty;
                var url = objc_msgSend_IntPtr(panel, sel_registerName("URL"));
                var path = url == IntPtr.Zero
                    ? IntPtr.Zero
                    : objc_msgSend_IntPtr(url, sel_registerName("path"));
                var utf8 = path == IntPtr.Zero
                    ? IntPtr.Zero
                    : objc_msgSend_IntPtr(path, sel_registerName("UTF8String"));
                return utf8 == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
            }
            catch (Exception exception)
            {
                error = "ファイル選択を開けませんでした: " + exception.Message;
                return string.Empty;
            }
        }

        private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjectiveCLibrary)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjectiveCLibrary)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Byte(IntPtr receiver, IntPtr selector, byte value);
        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern long objc_msgSend_Long(IntPtr receiver, IntPtr selector);
#endif

        private void SetSourceMode(int mode)
        {
            mode = Mathf.Clamp(mode, 0, 1);
            if (settings.sourceMode == mode) return;
            showWindowDropdown = false;
            settings.sourceMode = mode;
            if (mode == 0)
            {
                windowCapture.StopCapture();
                status = video.IsPrepared ? "動画・画像入力へ切り替えました" : "動画・画像を選択してください";
            }
            else
            {
                if (windowCapture.Windows.Count == 0) RefreshWindowList();
                status = "取り込むウィンドウを選択してください";
            }
        }

        private void RefreshWindowList()
        {
            showWindowDropdown = false;
            var previouslyHighlightedId = selectedWindowIndex >= 0 && selectedWindowIndex < windowCapture.Windows.Count
                ? windowCapture.Windows[selectedWindowIndex].Id
                : windowCapture.SelectedWindow?.Id ?? 0L;
            windowCapture.RefreshWindows();
            selectedWindowIndex = -1;

            // The native ID is authoritative while Control and Output are
            // running. Do not let the previously selected Output window win
            // merely because it sorts before a newly selected slide show.
            if (settings.capturedWindowId > 0)
            {
                for (var index = 0; index < windowCapture.Windows.Count; index++)
                {
                    if (windowCapture.Windows[index].Id != settings.capturedWindowId) continue;
                    selectedWindowIndex = index;
                    break;
                }
            }

            if (selectedWindowIndex < 0 && previouslyHighlightedId > 0
                && (!projectorMode || settings.capturedWindowId <= 0))
            {
                for (var index = 0; index < windowCapture.Windows.Count; index++)
                {
                    if (windowCapture.Windows[index].Id != previouslyHighlightedId) continue;
                    selectedWindowIndex = index;
                    break;
                }
            }

            if (selectedWindowIndex < 0 && !string.IsNullOrEmpty(settings.capturedWindowOwner)
                && (!projectorMode || settings.capturedWindowId <= 0))
            {
                for (var index = 0; index < windowCapture.Windows.Count; index++)
                {
                    var item = windowCapture.Windows[index];
                    if (item.OwnerName != settings.capturedWindowOwner
                        || item.Title != settings.capturedWindowTitle) continue;
                    selectedWindowIndex = index;
                    break;
                }
            }

            if (selectedWindowIndex < 0 && windowCapture.Windows.Count > 0) selectedWindowIndex = 0;
            status = windowCapture.Windows.Count > 0
                ? $"ウィンドウを{windowCapture.Windows.Count}件検出しました"
                : "取り込めるウィンドウがありません。対象アプリを開いてから更新してください。";
        }

        private void RestoreCapturedWindow()
        {
            if (selectedWindowIndex < 0 || selectedWindowIndex >= windowCapture.Windows.Count) return;
            var item = windowCapture.Windows[selectedWindowIndex];
            var idMatches = settings.capturedWindowId > 0 && item.Id == settings.capturedWindowId;
            var nameMatches = item.OwnerName == settings.capturedWindowOwner
                && item.Title == settings.capturedWindowTitle;
            if (!idMatches && !nameMatches)
            {
                if (projectorMode) windowCapture.StopCapture();
                return;
            }
            BeginWindowCapture(item, false);
        }

        private void BeginHighlightedWindowCapture()
        {
            if (selectedWindowIndex < 0 || selectedWindowIndex >= windowCapture.Windows.Count)
            {
                status = "取り込むウィンドウを選択してください";
                return;
            }
            BeginWindowCapture(windowCapture.Windows[selectedWindowIndex], false);
        }

        private void BeginWindowCapture(CapturableWindow item, bool requestFocus)
        {
            windowCapture.CaptureFps = settings.windowCaptureFps;
            if (!windowCapture.SelectWindow(item.Id)) return;
            settings.sourceMode = 1;
            settings.capturedWindowId = item.Id;
            settings.capturedWindowOwner = item.OwnerName ?? string.Empty;
            settings.capturedWindowTitle = item.Title ?? string.Empty;
            status = "ウィンドウの取込を開始しています…";
            focusCapturedWindowNextFrame = requestFocus;
            if (!projectorMode) SaveSettings();
        }

        private void FocusCapturedWindow()
        {
            if (!windowCapture.ActivateSelectedWindow())
                status = "対象ウィンドウを前面にできませんでした。閉じていないか確認してください。";
        }

        private void SendSlideNavigation(bool forward)
        {
            var sent = forward ? windowCapture.SendNext() : windowCapture.SendPrevious();
            if (!sent) status = "スライド操作を送信できませんでした。取込対象を確認してください。";
        }

#if UNITY_STANDALONE_WIN
        private string OpenWindowsMediaFileDialog(out string error)
        {
            error = string.Empty;
            const int fileBufferCharacters = 32768;
            var fileBuffer = Marshal.AllocHGlobal(fileBufferCharacters * sizeof(char));
            try
            {
                // OPENFILENAMEW requires a writable, zero-terminated UTF-16 buffer.
                // Allocating it explicitly avoids IL2CPP/Mono StringBuilder marshalling
                // falling back to the current Windows ANSI code page.
                for (var offset = 0; offset < fileBufferCharacters * sizeof(char); offset += sizeof(char))
                    Marshal.WriteInt16(fileBuffer, offset, 0);

                var existingPath = GetExistingMediaPath();
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var characterCount = Math.Min(existingPath.Length, fileBufferCharacters - 1);
                    for (var index = 0; index < characterCount; index++)
                        Marshal.WriteInt16(fileBuffer, index * sizeof(char), existingPath[index]);
                }

                var dialog = new WindowsOpenFileName
                {
                    structSize = (uint)Marshal.SizeOf<WindowsOpenFileName>(),
                    owner = GetActiveWindow(),
                    filter = "360°動画・画像 (*.mp4;*.mov;*.m4v;*.jpg;*.jpeg;*.png)\0*.mp4;*.mov;*.m4v;*.jpg;*.jpeg;*.png\0すべてのファイル (*.*)\0*.*\0\0",
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = fileBufferCharacters,
                    initialDirectory = GetExistingMediaDirectory(),
                    title = "360°動画・画像を選択",
                    flags = WindowsDialogExplorer | WindowsDialogFileMustExist
                        | WindowsDialogPathMustExist | WindowsDialogNoChangeDirectory,
                    defaultExtension = "mp4"
                };

                if (GetOpenFileNameW(ref dialog))
                    return Marshal.PtrToStringUni(fileBuffer) ?? string.Empty;

                var errorCode = CommDlgExtendedError();
                if (errorCode != 0)
                    error = $"ファイル選択を開けませんでした（Windowsエラー 0x{errorCode:X}）";
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }

        private string GetExistingMediaDirectory()
        {
            try
            {
                var savedPath = GetExistingMediaPath();
                return string.IsNullOrEmpty(savedPath) ? null : Path.GetDirectoryName(savedPath);
            }
            catch
            {
                return null;
            }
        }

        private string GetExistingMediaPath()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(pathField) && File.Exists(pathField)
                    ? Path.GetFullPath(pathField)
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private const uint WindowsDialogNoChangeDirectory = 0x00000008;
        private const uint WindowsDialogPathMustExist = 0x00000800;
        private const uint WindowsDialogFileMustExist = 0x00001000;
        private const uint WindowsDialogExplorer = 0x00080000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowsOpenFileName
        {
            public uint structSize;
            public IntPtr owner;
            public IntPtr instance;
            [MarshalAs(UnmanagedType.LPWStr)] public string filter;
            [MarshalAs(UnmanagedType.LPWStr)] public string customFilter;
            public uint maxCustomFilter;
            public uint filterIndex;
            public IntPtr file;
            public uint maxFile;
            public IntPtr fileTitle;
            public uint maxFileTitle;
            [MarshalAs(UnmanagedType.LPWStr)] public string initialDirectory;
            [MarshalAs(UnmanagedType.LPWStr)] public string title;
            public uint flags;
            public ushort fileOffset;
            public ushort fileExtension;
            [MarshalAs(UnmanagedType.LPWStr)] public string defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            [MarshalAs(UnmanagedType.LPWStr)] public string templateName;
            public IntPtr reserved;
            public uint reservedFlags;
            public uint flagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", ExactSpelling = true,
            CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileNameW(ref WindowsOpenFileName dialog);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
#endif

        private void SetOutputDisplay(int requestedDisplay)
        {
            if (activeDisplay == 0) return;
            outputCamera.targetDisplay = 0;
            previewCamera.enabled = false;
            previewCamera.targetDisplay = 0;
            settings.outputDisplay = 0;
            settings.useExtendedDisplayOutput = false;
            activeDisplay = 0;
        }

        private void Update()
        {
            video.IsLooping = settings.loop;
            windowCapture.CaptureFps = settings.windowCaptureFps;
            if (projectorMode) UpdateProjectorProcess();
            else UpdateControlProcess();
            if (focusCapturedWindowNextFrame)
            {
                focusCapturedWindowNextFrame = false;
                FocusCapturedWindow();
            }
        }

        private void UpdateControlProcess()
        {
            if (Application.targetFrameRate != settings.bridgeUpdateHz)
                Application.targetFrameRate = settings.bridgeUpdateHz;
            if (Time.unscaledTime < nextBridgeUpdate) return;
            nextBridgeUpdate = Time.unscaledTime + 1f / Mathf.Max(15, settings.bridgeUpdateHz);
            video.AudioMuted = settings.audioMuted || ProjectionProcessBridge.IsOutputAlive();
            PublishBridgeState();
        }

        private void PublishBridgeState()
        {
            settings.videoPath = pathField;
            ProjectionProcessBridge.WriteState(new ProjectionSharedState
            {
                sequence = ++bridgeSequence,
                settings = settings,
                mediaIsPlaying = video != null && video.IsPlaying,
                mediaTime = video != null ? video.Time : 0d
            });
        }

        private void UpdateProjectorProcess()
        {
            if (Time.unscaledTime < nextBridgeUpdate) return;
            nextBridgeUpdate = Time.unscaledTime + 1f / Mathf.Max(15, settings.bridgeUpdateHz);
            if (Time.unscaledTime >= nextHeartbeatUpdate)
            {
                nextHeartbeatUpdate = Time.unscaledTime + 0.5f;
                ProjectionProcessBridge.WriteHeartbeat(windowCapture.HasCapturePermission);
            }

            if (ProjectionProcessBridge.StopWasRequestedAfter(projectorStartedUtcTicks))
            {
                Application.Quit();
                return;
            }

            if (!ProjectionProcessBridge.TryReadState(out var shared)
                || shared.sequence == lastAppliedBridgeSequence) return;

            lastAppliedBridgeSequence = shared.sequence;
            bridgeWantsMediaPlayback = shared.mediaIsPlaying;
            ApplySharedSettings(shared.settings);

            if (settings.sourceMode == 0)
            {
                if (!string.IsNullOrWhiteSpace(settings.videoPath)
                    && settings.videoPath != bridgedMediaPath)
                {
                    bridgedMediaPath = settings.videoPath;
                    video.Open(bridgedMediaPath);
                }

                if (video.IsPrepared && !video.IsStillImage)
                {
                    if (Math.Abs(video.Time - shared.mediaTime) > 0.75d)
                        video.Seek(shared.mediaTime, shared.mediaIsPlaying);
                    if (shared.mediaIsPlaying && !video.IsPlaying) video.Play();
                    else if (!shared.mediaIsPlaying && video.IsPlaying) video.Pause();
                }
            }
            else
            {
                var windowChanged = settings.capturedWindowId != bridgedWindowId
                    || settings.capturedWindowOwner != bridgedWindowOwner
                    || settings.capturedWindowTitle != bridgedWindowTitle;
                if (windowChanged || windowCapture.SelectedWindow == null)
                {
                    bridgedWindowId = settings.capturedWindowId;
                    bridgedWindowOwner = settings.capturedWindowOwner;
                    bridgedWindowTitle = settings.capturedWindowTitle;
                    RefreshWindowList();
                    RestoreCapturedWindow();
                }
            }
        }

        private void ApplySharedSettings(DomeProjectionSettings sharedSettings)
        {
            if (sharedSettings == null) return;
            settings = sharedSettings;
            settings.Migrate();
            settings.Clamp();
            settings.useExtendedDisplayOutput = false;
            settings.outputDisplay = 0;
            outputRenderer.Settings = settings;
            previewRenderer.Settings = settings;
            windowCapture.CaptureFps = settings.windowCaptureFps;
            video.IsLooping = settings.loop;
            video.AudioMuted = settings.audioMuted;
        }

        private void LaunchProjectionCompanion()
        {
            try
            {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                var startInfo = new ProcessStartInfo { UseShellExecute = true };
                var outputApp = FindSiblingMacOutputApp();
                if (!Directory.Exists(outputApp))
                    throw new FileNotFoundException("DomeProjectionOutput.appがControlアプリの隣にありません。", outputApp);
                startInfo.FileName = "/usr/bin/open";
                startInfo.UseShellExecute = false;
                startInfo.ArgumentList.Add("-n");
                startInfo.ArgumentList.Add("-g");
                startInfo.ArgumentList.Add(outputApp);
                startInfo.ArgumentList.Add("--args");
                startInfo.ArgumentList.Add("--projector");
                startInfo.ArgumentList.Add("-monitor");
                startInfo.ArgumentList.Add("2");
                startInfo.ArgumentList.Add("-screen-fullscreen");
                startInfo.ArgumentList.Add("1");
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
                var startInfo = new ProcessStartInfo { UseShellExecute = true };
                var controlDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                var outputExe = Path.GetFullPath(Path.Combine(controlDirectory, "..", "Output", "DomeProjectionOutput.exe"));
                if (!File.Exists(outputExe))
                    throw new FileNotFoundException("DomeProjectionOutput.exeが見つかりません。", outputExe);
                startInfo.FileName = outputExe;
                startInfo.Arguments = "--projector -monitor 2 -screen-fullscreen 1";
#else
                status = "EditorではOutputアプリの自動起動を行いません";
                return;
#endif
#if (UNITY_STANDALONE_OSX || UNITY_STANDALONE_WIN) && !UNITY_EDITOR
                Process.Start(startInfo);
                outputLaunchPendingUntil = Time.unscaledTime + 4f;
                status = "投影を開始しています…";
#endif
            }
            catch (Exception exception)
            {
                status = "投影を開始できません: " + exception.Message;
            }
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private void LaunchOutputPermissionSetup()
        {
            try
            {
                var outputApp = FindSiblingMacOutputApp();
                if (!Directory.Exists(outputApp)) return;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("-n");
                startInfo.ArgumentList.Add("-j");
                startInfo.ArgumentList.Add(outputApp);
                startInfo.ArgumentList.Add("--args");
                startInfo.ArgumentList.Add("--permission-setup");
                Process.Start(startInfo)?.Dispose();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Output permission setup could not start: " + exception.Message);
            }
        }
#endif

        private static string FindSiblingMacOutputApp()
        {
            var candidates = new[]
            {
                FindSiblingAppFromPath(Application.dataPath),
                FindSiblingAppFromPath(Process.GetCurrentProcess().MainModule?.FileName)
            };
            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate)) return candidate;
            return candidates[0] ?? candidates[1] ?? string.Empty;
        }

        private static string FindSiblingAppFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var current = new DirectoryInfo(path);
            if (!current.Exists && current.Parent != null) current = current.Parent;
            while (current != null && !current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                current = current.Parent;
            return current?.Parent == null
                ? null
                : Path.Combine(current.Parent.FullName, "DomeProjectionOutput.app");
        }

        private void OnGUI()
        {
            if (projectorMode) return;
            HandleHotkeys(Event.current);
            if (!showControls) return;

            BuildStyles();
            var previousMatrix = GUI.matrix;
            // Keep controls physically readable when the operator enlarges or
            // maximizes the window. 1600x900 remains 1:1; larger windows grow
            // the complete UI up to 160% instead of leaving tiny text behind.
            var uiScale = Mathf.Clamp(Screen.height / ReferenceUiHeight, 0.75f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            var logicalHeight = Screen.height / uiScale;
            var margin = 16f / uiScale;
            panelAreaRect = new Rect(margin, margin, PanelWidth, logicalHeight - margin * 2f);
            HandleWindowDropdownInput();
            GUILayout.BeginArea(panelAreaRect, panelStyle);
            GUILayout.Label("Dome Projection Control", titleStyle);
            GUILayout.Label(status, smallStyle);
            DrawOutputModeBanner();
            GUILayout.Space(8f);

            DrawSourceControls();
            GUILayout.Space(8f);
            DrawPanelTabs();
            GUILayout.Space(6f);

            switch (activePanelTab)
            {
                case 0: DrawBasicPanel(); break;
                case 1: DrawCalibrationPanel(); break;
                case 2: DrawLensPanel(); break;
                default: DrawOutputPanel(); break;
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("設定UIを隠す（Hで再表示）")) showControls = false;
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("設定を保存")) { SaveSettings(); status = "設定を保存しました"; }
            if (GUILayout.Button("設定を再読込")) { LoadSettings(); status = "設定を再読込しました"; }
            if (GUILayout.Button("アプリを終了", GUILayout.Width(90f))) QuitApplication();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label(settings.sourceMode == 1
                ? "←/→・PgUp/PgDn: スライド移動   T: 操作先へ切替   H: UI   G: グリッド   F: 全画面   Esc: 全画面解除"
                : "1/2/3/4: タブ   Ctrl+O: 素材選択   Space: 再生/停止   G: グリッド   H: UI   F: 全画面   Esc: 全画面解除", smallStyle);
            GUILayout.Label($"出力 {Screen.width}×{Screen.height} / Display {settings.outputDisplay}", smallStyle);
            GUILayout.EndArea();
            if (showWindowDropdown && settings.sourceMode == 1) DrawWindowDropdown();
            GUI.matrix = previousMatrix;
        }

        private void DrawOutputModeBanner()
        {
            GUILayout.Space(4f);
            var outputAlive = ProjectionProcessBridge.IsOutputAlive();
            if (outputAlive)
            {
                GUILayout.Label("● 投影中（この画面は操作専用）", smallStyle);
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                if (settings.sourceMode == 1 && !ProjectionProcessBridge.OutputHasCapturePermission)
                    GUILayout.Label("⚠ Outputの画面収録が未許可です。システム設定でDomeProjectionOutputも許可してから投影を再起動してください。", smallStyle);
#endif
                if (GUILayout.Button("投影を終了"))
                {
                    ProjectionProcessBridge.RequestOutputStop();
                    status = "投影終了をOutputアプリへ送りました";
                }
                return;
            }

            GUILayout.Label("● この画面で素材選択と調整を行います", smallStyle);
            GUI.enabled = Time.unscaledTime >= outputLaunchPendingUntil;
            if (GUILayout.Button("投影を開始"))
            {
                PublishBridgeState();
                LaunchProjectionCompanion();
            }
            GUI.enabled = true;
            GUILayout.Label("プロジェクターはOSで「拡張」に設定してください。Outputは第2画面で全画面表示します。", smallStyle);
        }

        private void DrawSourceControls()
        {
            GUILayout.Label("入力ソース");
            GUILayout.BeginHorizontal();
            for (var index = 0; index < sourceModeNames.Length; index++)
            {
                if (GUILayout.Toggle(settings.sourceMode == index, sourceModeNames[index], GUI.skin.button))
                    SetSourceMode(index);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4f);

            if (settings.sourceMode == 1) DrawWindowCaptureControls();
            else DrawMediaControls();
        }

        private void DrawMediaControls()
        {
            GUILayout.Label("360°動画・画像（2:1 Equirectangular）");
            GUILayout.BeginHorizontal();
            pathField = GUILayout.TextField(pathField, GUILayout.Width(VideoPathWidth));
            if (GUILayout.Button("選択", GUILayout.Width(54f))) BrowseMedia();
            if (GUILayout.Button("読込", GUILayout.Width(54f))) OpenMedia();
            GUILayout.EndHorizontal();
            settings.fileIsFlat = GUILayout.Toggle(settings.fileIsFlat,
                "平面資料として表示（通常の画像・スライド動画）");

            if (video.IsStillImage)
            {
                GUILayout.Label("静止画を表示中（JPEG / PNG）", smallStyle);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = video.IsPrepared;
                if (GUILayout.Button(video.IsPlaying ? "一時停止" : "再生"))
                {
                    if (video.IsPlaying) video.Pause(); else video.Play();
                }
                if (GUILayout.Button("停止")) video.Stop();
                GUI.enabled = true;
                settings.loop = GUILayout.Toggle(settings.loop, "ループ", GUILayout.Width(75f));
                settings.audioMuted = GUILayout.Toggle(settings.audioMuted, "ミュート", GUILayout.Width(82f));
                GUILayout.EndHorizontal();
            }

            if (video.IsPrepared && video.Duration > 0d)
                DrawMediaSeekBar();
        }

        private void DrawMediaSeekBar()
        {
            var displayedTime = mediaSeekDragging ? mediaSeekPreviewTime : video.Time;
            var rawEventType = Event.current.rawType;
            var mousePosition = Event.current.mousePosition;

            GUILayout.BeginHorizontal();
            GUILayout.Label("再生位置", GUILayout.Width(82f));
            var sliderRect = GUILayoutUtility.GetRect(40f, 18f, GUILayout.ExpandWidth(true));
            if (!mediaSeekDragging && rawEventType == EventType.MouseDown
                && Event.current.button == 0 && sliderRect.Contains(mousePosition))
            {
                mediaSeekDragging = true;
                mediaSeekPreviewTime = video.Time;
                resumeMediaAfterSeek = video.IsPlaying;
                video.Pause();
            }

            displayedTime = GUI.HorizontalSlider(sliderRect, (float)displayedTime, 0f,
                (float)video.Duration, sliderStyle, sliderThumbStyle);
            if (mediaSeekDragging) mediaSeekPreviewTime = displayedTime;
            GUILayout.Label(FormatTime(displayedTime) + " / " + FormatTime(video.Duration),
                GUILayout.Width(92f));
            GUILayout.EndHorizontal();

            // Keep the thumb entirely independent from decoder seek latency while
            // dragging, then perform exactly one seek when the pointer is released.
            if (!mediaSeekDragging || rawEventType != EventType.MouseUp) return;
            var target = mediaSeekPreviewTime;
            var shouldResume = resumeMediaAfterSeek;
            mediaSeekDragging = false;
            resumeMediaAfterSeek = false;
            video.Seek(target, shouldResume);
            status = "再生位置を " + FormatTime(target) + " に移動しました";
            Event.current.Use();
        }

        private void DrawWindowCaptureControls()
        {
            var hasCapturePermission = windowCapture.HasCapturePermission;
            var hasAccessibilityPermission = windowCapture.HasAccessibilityPermission;
            if (!hasCapturePermission || !hasAccessibilityPermission)
            {
                GUILayout.Label("macOS権限: 画面収録 " + (hasCapturePermission ? "✓" : "未許可")
                    + " / アクセシビリティ " + (hasAccessibilityPermission ? "✓" : "未許可"), smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("必要な権限をまとめて設定"))
                {
                    var captureGranted = windowCapture.RequestCapturePermission();
                    var accessibilityGranted = windowCapture.RequestAccessibilityPermission();
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
                    LaunchOutputPermissionSetup();
#endif
                    RefreshWindowList();
                    status = captureGranted && accessibilityGranted
                        ? "必要な権限を確認しました"
                        : "システム設定でControlを許可してから、アプリを再起動してください";
                }
                if (GUILayout.Button("一覧を再読込", GUILayout.Width(100f))) RefreshWindowList();
                GUILayout.EndHorizontal();
                GUILayout.Space(4f);
            }

            GUILayout.BeginHorizontal();
            var selected = selectedWindowIndex >= 0 && selectedWindowIndex < windowCapture.Windows.Count
                ? windowCapture.Windows[selectedWindowIndex]
                : null;
            var dropdownLabel = selected == null
                ? "（ウィンドウ未検出） ▼"
                : TruncateLabel(selected.DisplayName, 44) + " ▼";
            var dropdownClicked = GUILayout.Button(dropdownLabel, GUILayout.Height(28f));
            if (Event.current.type == EventType.Repaint)
                windowDropdownAnchor = GUILayoutUtility.GetLastRect();
            if (dropdownClicked)
            {
                showWindowDropdown = !showWindowDropdown;
                if (showWindowDropdown && selectedWindowIndex >= 0)
                    windowDropdownScroll = Mathf.Max(0f, selectedWindowIndex * 27f - 108f);
            }
            if (GUILayout.Button("更新", GUILayout.Width(48f), GUILayout.Height(28f))) RefreshWindowList();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("取込開始")) BeginHighlightedWindowCapture();
            GUI.enabled = windowCapture.SelectedWindow != null;
            if (GUILayout.Button("◀ 前へ")) SendSlideNavigation(false);
            if (GUILayout.Button("次へ ▶")) SendSlideNavigation(true);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUI.enabled = windowCapture.SelectedWindow != null;
            if (GUILayout.Button("操作先ウィンドウへ切替（マウス・キーボード操作）"))
                focusCapturedWindowNextFrame = true;
            GUI.enabled = true;

            GUILayout.BeginHorizontal();
            GUILayout.Label("取込速度", GUILayout.Width(82f));
            if (GUILayout.Button(settings.windowCaptureFps + " fps", GUILayout.Width(70f)))
                settings.windowCaptureFps = settings.windowCaptureFps < 20 ? 30 : settings.windowCaptureFps < 45 ? 60 : 15;
            GUILayout.EndHorizontal();
            GUILayout.Label("取込開始では操作先へ移動しません。必要なときだけ上の「操作先ウィンドウへ切替」またはTキーを使います。", smallStyle);
            GUILayout.Label("対象を最小化すると取込が止まります。PowerPointはウィンドウ表示のスライドショーを選ぶと操作しやすくなります。", smallStyle);
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private IEnumerator RequestInitialMacPermissions()
        {
            // Let the Control window appear before macOS presents its dialogs.
            yield return null;
            var captureGranted = windowCapture.RequestCapturePermission();
            yield return new WaitForSecondsRealtime(0.25f);
            var accessibilityGranted = windowCapture.RequestAccessibilityPermission();
            // Screen Recording permission is per executable. Start Output once
            // in a hidden setup mode so macOS can register its permission too.
            LaunchOutputPermissionSetup();
            status = captureGranted && accessibilityGranted
                ? "画面収録とアクセシビリティの権限を確認しました"
                : "画面収録とアクセシビリティを許可し、Controlを再起動してください";
        }

        private IEnumerator RequestOutputPermissionAndQuit()
        {
            yield return null;
            windowCapture.RequestCapturePermission();
            // Keep the helper alive while macOS attaches and presents its TCC
            // prompt, but do not leave another app running after setup.
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }
#endif

        private void DrawWindowDropdown()
        {
            var count = windowCapture.Windows.Count;
            const float rowHeight = 27f;
            var contentHeight = count * rowHeight;
            var visibleHeight = count == 0 ? 32f : Mathf.Min(270f, contentHeight);
            var height = visibleHeight + 16f;
            var dropdownRect = new Rect(panelAreaRect.x + windowDropdownAnchor.x,
                panelAreaRect.y + windowDropdownAnchor.yMax + 2f,
                Mathf.Min(windowDropdownAnchor.width, PanelWidth - 24f), height);
            var maxScroll = Mathf.Max(0f, contentHeight - visibleHeight);
            windowDropdownScroll = Mathf.Clamp(windowDropdownScroll, 0f, maxScroll);

            GUI.BeginGroup(dropdownRect, GUIContent.none, panelStyle);
            if (count == 0)
            {
                GUI.Label(new Rect(10f, 8f, dropdownRect.width - 20f, 26f),
                    "対象アプリを開いて「更新」を押してください", smallStyle);
            }
            else
            {
                var first = Mathf.Max(0, Mathf.FloorToInt(windowDropdownScroll / rowHeight));
                var last = Mathf.Min(count - 1,
                    Mathf.CeilToInt((windowDropdownScroll + visibleHeight) / rowHeight));
                for (var index = first; index <= last; index++)
                {
                    var item = windowCapture.Windows[index];
                    var prefix = index == selectedWindowIndex ? "✓ " : "  ";
                    var rowY = 8f + index * rowHeight - windowDropdownScroll;
                    GUI.Box(new Rect(8f, rowY, dropdownRect.width - 26f, 25f),
                        prefix + TruncateLabel(item.DisplayName, 50), GUI.skin.button);
                }

                if (maxScroll > 0f)
                {
                    var track = new Rect(dropdownRect.width - 12f, 8f, 5f, visibleHeight);
                    GUI.DrawTexture(track, sliderTexture);
                    var thumbHeight = Mathf.Max(24f, visibleHeight * visibleHeight / contentHeight);
                    var thumbY = 8f + windowDropdownScroll / maxScroll * (visibleHeight - thumbHeight);
                    GUI.DrawTexture(new Rect(track.x, thumbY, track.width, thumbHeight), sliderThumbTexture);
                }
            }
            GUI.EndGroup();
        }

        private void HandleWindowDropdownInput()
        {
            var rawEventType = Event.current.rawType;
            if (consumeWindowDropdownMouseUp && rawEventType == EventType.MouseUp)
            {
                consumeWindowDropdownMouseUp = false;
                Event.current.Use();
                return;
            }
            if (!showWindowDropdown) return;

            const float rowHeight = 27f;
            var count = windowCapture.Windows.Count;
            var contentHeight = count * rowHeight;
            var visibleHeight = count == 0 ? 32f : Mathf.Min(270f, contentHeight);
            var dropdownRect = new Rect(panelAreaRect.x + windowDropdownAnchor.x,
                panelAreaRect.y + windowDropdownAnchor.yMax + 2f,
                Mathf.Min(windowDropdownAnchor.width, PanelWidth - 24f), visibleHeight + 16f);

            if (rawEventType == EventType.ScrollWheel && dropdownRect.Contains(Event.current.mousePosition))
            {
                var maxScroll = Mathf.Max(0f, contentHeight - visibleHeight);
                windowDropdownScroll = Mathf.Clamp(windowDropdownScroll + Event.current.delta.y * rowHeight,
                    0f, maxScroll);
                Event.current.Use();
                return;
            }
            if (rawEventType != EventType.MouseDown || Event.current.button != 0) return;

            if (dropdownRect.Contains(Event.current.mousePosition) && count > 0
                && Event.current.mousePosition.x < dropdownRect.xMax - 16f)
            {
                var localY = Event.current.mousePosition.y - dropdownRect.y - 8f + windowDropdownScroll;
                var index = Mathf.FloorToInt(localY / rowHeight);
                if (index >= 0 && index < count)
                {
                    selectedWindowIndex = index;
                    status = "選択: " + windowCapture.Windows[index].DisplayName;
                    showWindowDropdown = false;
                }
            }
            else
            {
                showWindowDropdown = false;
            }

            // The list is rendered after the ordinary panel so it appears on top.
            // Consume both halves of the click here before controls behind it see it.
            consumeWindowDropdownMouseUp = true;
            Event.current.Use();
        }

        private static string TruncateLabel(string value, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumCharacters) return value ?? string.Empty;
            return value.Substring(0, Mathf.Max(1, maximumCharacters - 1)) + "…";
        }

        private void DrawPanelTabs()
        {
            GUILayout.BeginHorizontal();
            for (var i = 0; i < panelTabNames.Length; i++)
                if (GUILayout.Toggle(activePanelTab == i, panelTabNames[i], GUI.skin.button)) activePanelTab = i;
            GUILayout.EndHorizontal();
        }

        private void DrawBasicPanel()
        {
            if (IsFlatContent()) DrawFlatPlacementControls();
            else
            {
                GUILayout.Label("表示方向");
                settings.yaw = Slider("Yaw", settings.yaw, -180f, 180f, settings.yaw.ToString("0.0°"));
                settings.pitch = Slider("Pitch", settings.pitch, -180f, 180f, settings.pitch.ToString("0.0°"));
                settings.roll = Slider("Roll", settings.roll, -180f, 180f, settings.roll.ToString("0.0°"));
                settings.domeFov = FineSlider("ドーム画角", settings.domeFov, 90f, 220f, 1f, settings.domeFov.ToString("0°"));
            }

        }

        private void DrawCalibrationPanel()
        {
            GUILayout.Label("校正表示");
            GUILayout.BeginHorizontal();
            GUILayout.Label("校正表示", GUILayout.Width(82f));
            if (GUILayout.Button(gridModeNames[settings.gridMode]))
                settings.gridMode = (settings.gridMode + 1) % 3;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("グリッド間隔", GUILayout.Width(82f));
            if (GUILayout.Button(settings.gridStepDegrees.ToString("0°"), GUILayout.Width(54f)))
                settings.gridStepDegrees = NextGridStep(settings.gridStepDegrees);
            settings.gridOpacity = GUILayout.HorizontalSlider(settings.gridOpacity, 0.15f, 1f, sliderStyle, sliderThumbStyle, GUILayout.Width(120f));
            GUILayout.Label("濃さ " + settings.gridOpacity.ToString("0.00"), GUILayout.Width(76f));
            GUILayout.EndHorizontal();
            settings.gridLineWidth = FineSlider("線の太さ", settings.gridLineWidth, 0.25f, 1.5f, 0.05f, settings.gridLineWidth.ToString("0.00"));
            settings.showOrientationMarkers = GUILayout.Toggle(settings.showOrientationMarkers, "方位色マーカー（上:赤 右:緑 下:青 左:黄）");

            GUILayout.Label("出力円");
            settings.centerX = FineSlider("中心 X", settings.centerX, 0.25f, 0.75f, 0.001f, settings.centerX.ToString("0.000"));
            settings.centerY = FineSlider("中心 Y", settings.centerY, 0.25f, 0.75f, 0.001f, settings.centerY.ToString("0.000"));
            settings.radius = FineSlider("円半径", settings.radius, 0.2f, 0.7f, 0.001f, settings.radius.ToString("0.000"));
            settings.ellipseX = FineSlider("横倍率", settings.ellipseX, 0.75f, 1.25f, 0.001f, settings.ellipseX.ToString("0.000"));
            settings.ellipseY = FineSlider("縦倍率", settings.ellipseY, 0.75f, 1.25f, 0.001f, settings.ellipseY.ToString("0.000"));
            settings.outputRotation = FineSlider("出力回転", settings.outputRotation, -180f, 180f, 0.1f, settings.outputRotation.ToString("0.0°"));
            if (GUILayout.Button("出力円をリセット")) settings.ResetOutputCircle();
        }

        private bool IsFlatContent()
        {
            return settings.sourceMode == 1 || settings.fileIsFlat;
        }

        private void DrawFlatPlacementControls()
        {
            GUILayout.Label("平面資料の配置");
            settings.flatAzimuth = FineSlider("左右方向", settings.flatAzimuth, -180f, 180f, 1f,
                settings.flatAzimuth.ToString("0°"));
            settings.flatPolarAngle = FineSlider("中心→外周", settings.flatPolarAngle, 0f, 89f, 1f,
                settings.flatPolarAngle.ToString("0°"));
            settings.flatRoll = FineSlider("資料の回転", settings.flatRoll, -180f, 180f, 0.5f,
                settings.flatRoll.ToString("0.0°"));
            settings.flatHorizontalFov = FineSlider("横の画角", settings.flatHorizontalFov, 10f, 160f, 1f,
                settings.flatHorizontalFov.ToString("0°"));
            settings.flatHeightScale = FineSlider("縦倍率", settings.flatHeightScale, 0.5f, 2f, 0.01f,
                settings.flatHeightScale.ToString("0.00"));

            GUILayout.Label("端の切り取り", smallStyle);
            settings.flatCropLeft = FineSlider("左", settings.flatCropLeft, 0f, 0.3f, 0.002f,
                settings.flatCropLeft.ToString("0.000"));
            settings.flatCropRight = FineSlider("右", settings.flatCropRight, 0f, 0.3f, 0.002f,
                settings.flatCropRight.ToString("0.000"));
            settings.flatCropTop = FineSlider("上", settings.flatCropTop, 0f, 0.3f, 0.002f,
                settings.flatCropTop.ToString("0.000"));
            settings.flatCropBottom = FineSlider("下", settings.flatCropBottom, 0f, 0.3f, 0.002f,
                settings.flatCropBottom.ToString("0.000"));
            if (GUILayout.Button("平面資料の配置をリセット")) settings.ResetFlatPlacement();
        }

        private void DrawLensPanel()
        {
            GUILayout.Label("レンズ歪み補正");
            GUILayout.Label("K1–K3は円周方向の間隔、P1/P2は偏心・斜め方向の非対称を補正します。", smallStyle);
            settings.radialK1 = FineSlider("放射 K1", settings.radialK1, -0.3f, 0.3f, 0.001f, settings.radialK1.ToString("0.000"));
            settings.radialK2 = FineSlider("放射 K2", settings.radialK2, -0.3f, 0.3f, 0.001f, settings.radialK2.ToString("0.000"));
            settings.radialK3 = FineSlider("放射 K3", settings.radialK3, -0.3f, 0.3f, 0.001f, settings.radialK3.ToString("0.000"));
            settings.tangentialP1 = FineSlider("偏心 P1", settings.tangentialP1, -0.15f, 0.15f, 0.001f, settings.tangentialP1.ToString("0.000"));
            settings.tangentialP2 = FineSlider("偏心 P2", settings.tangentialP2, -0.15f, 0.15f, 0.001f, settings.tangentialP2.ToString("0.000"));

            GUILayout.Space(8f);
            GUILayout.Label("方向別補正（レンズ2枚重ね・偏心配置向け）");
            GUILayout.Label("各方向だけリング間隔がずれる場合に調整します。正値でその方向の画角を広げます。", smallStyle);
            settings.warpTop = FineSlider("上方向", settings.warpTop, -0.3f, 0.3f, 0.001f, settings.warpTop.ToString("0.000"));
            settings.warpBottom = FineSlider("下方向", settings.warpBottom, -0.3f, 0.3f, 0.001f, settings.warpBottom.ToString("0.000"));
            settings.warpLeft = FineSlider("左方向", settings.warpLeft, -0.3f, 0.3f, 0.001f, settings.warpLeft.ToString("0.000"));
            settings.warpRight = FineSlider("右方向", settings.warpRight, -0.3f, 0.3f, 0.001f, settings.warpRight.ToString("0.000"));
            if (GUILayout.Button("レンズ補正をリセット")) settings.ResetLensCalibration();
        }

        private void DrawOutputPanel()
        {
            GUILayout.Label("画質");
            settings.brightness = Slider("明るさ", settings.brightness, 0f, 2f, settings.brightness.ToString("0.00"));
            settings.gamma = Slider("ガンマ", settings.gamma, 0.4f, 2.5f, settings.gamma.ToString("0.00"));
            settings.edgeFade = Slider("円周フェード", settings.edgeFade, 0f, 0.2f, settings.edgeFade.ToString("0.000"));

            GUILayout.BeginHorizontal();
            settings.flipInputX = GUILayout.Toggle(settings.flipInputX, "入力を左右反転");
            settings.flipInputY = GUILayout.Toggle(settings.flipInputY, "入力を上下反転");
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("ウィンドウ");
            settings.startMaximized = GUILayout.Toggle(settings.startMaximized, "最大化ウィンドウで表示（OSフルスクリーンではありません）");
            GUILayout.BeginHorizontal();
            GUILayout.Label("サイズ", GUILayout.Width(82f));
            windowWidthField = GUILayout.TextField(windowWidthField, GUILayout.Width(72f));
            GUILayout.Label("×", GUILayout.Width(16f));
            windowHeightField = GUILayout.TextField(windowHeightField, GUILayout.Width(72f));
            if (GUILayout.Button("適用")) ApplyWindowSettings(false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("プリセット", GUILayout.Width(82f));
            if (GUILayout.Button("1280×720")) SetWindowedSize(1280, 720);
            if (GUILayout.Button("1600×900")) SetWindowedSize(1600, 900);
            if (GUILayout.Button("1920×1080")) SetWindowedSize(1920, 1080);
            GUILayout.EndHorizontal();
            GUILayout.Label("最大化をOFFにすると、ウィンドウ端のドラッグでもサイズを変更できます。", smallStyle);

            GUILayout.Space(8f);
            GUILayout.Label("プロジェクター投影");
            if (ProjectionProcessBridge.IsOutputAlive())
            {
                GUILayout.Label("● 投影中", smallStyle);
                if (GUILayout.Button("投影を終了"))
                    ProjectionProcessBridge.RequestOutputStop();
            }
            else
            {
                GUILayout.Label("停止中", smallStyle);
                if (GUILayout.Button("投影を開始"))
                {
                    PublishBridgeState();
                    LaunchProjectionCompanion();
                }
            }
            GUILayout.Label("Controlはこの画面から移動しません。Outputだけがプロジェクター側で全画面表示されます。", smallStyle);

            GUILayout.Space(8f);
            GUILayout.Label("Control → Output 同期");
            GUILayout.BeginHorizontal();
            GUILayout.Label("更新頻度", GUILayout.Width(82f));
            foreach (var rate in bridgeRates)
            {
                if (GUILayout.Toggle(settings.bridgeUpdateHz == rate, rate + " Hz", GUI.skin.button))
                    settings.bridgeUpdateHz = rate;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("30 Hzは省負荷、60 Hzは標準、120 Hzは調整操作の遅延を最小化します。投影の実フレーム数はディスプレイ性能が上限です。", smallStyle);
        }

        private void SetWindowedSize(int width, int height)
        {
            settings.startMaximized = false;
            windowWidthField = width.ToString();
            windowHeightField = height.ToString();
            ApplyWindowSettings(false);
        }

        private void ApplyWindowSettings(bool forceWindowed)
        {
            if (!int.TryParse(windowWidthField, out var width)) width = settings.windowWidth;
            if (!int.TryParse(windowHeightField, out var height)) height = settings.windowHeight;
            settings.windowWidth = Mathf.Clamp(width, 800, 7680);
            settings.windowHeight = Mathf.Clamp(height, 450, 4320);
            windowWidthField = settings.windowWidth.ToString();
            windowHeightField = settings.windowHeight.ToString();

            var maximize = settings.startMaximized && !forceWindowed;
            var mode = maximize ? FullScreenMode.MaximizedWindow : FullScreenMode.Windowed;
            Screen.SetResolution(settings.windowWidth, settings.windowHeight, mode);
            if (!maximize)
                status = $"ウィンドウサイズ: {settings.windowWidth} × {settings.windowHeight}";
        }

        private static void ConfigureProjectorDisplay()
        {
            // Never reuse a window size saved by a permission/setup launch.
            // Output always starts at the selected monitor's physical size.
            var width = Display.main != null ? Display.main.systemWidth : 0;
            var height = Display.main != null ? Display.main.systemHeight : 0;
            if (width < 640 || height < 360)
            {
                var current = Screen.currentResolution;
                width = Mathf.Max(640, current.width);
                height = Mathf.Max(360, current.height);
            }

            Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            UnityEngine.Debug.Log($"Projector output configured at {width} x {height}");
        }

        private float Slider(string label, float value, float min, float max, string display)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(82f));
            value = GUILayout.HorizontalSlider(value, min, max, sliderStyle, sliderThumbStyle, GUILayout.ExpandWidth(true));
            GUILayout.Label(display, GUILayout.Width(72f));
            GUILayout.EndHorizontal();
            return value;
        }

        private float FineSlider(string label, float value, float min, float max, float step, string display)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(82f));
            if (GUILayout.Button("−", GUILayout.Width(24f))) value -= step;
            value = GUILayout.HorizontalSlider(value, min, max, sliderStyle, sliderThumbStyle, GUILayout.Width(170f));
            if (GUILayout.Button("+", GUILayout.Width(24f))) value += step;
            GUILayout.Label(display, GUILayout.Width(56f));
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private static float NextGridStep(float current)
        {
            var steps = new[] { 1f, 2f, 5f, 10f, 15f };
            for (var i = 0; i < steps.Length; i++)
                if (current < steps[i] + 0.01f) return steps[(i + 1) % steps.Length];
            return steps[0];
        }

        private void HandleHotkeys(Event current)
        {
            if (current.type == EventType.MouseDown && settings.sourceMode == 1 && !showControls)
            {
                if (current.button == 0 || current.button == 1)
                {
                    SendSlideNavigation(current.button == 0);
                    current.Use();
                    return;
                }
            }
            if (current.type != EventType.KeyDown) return;
            switch (current.keyCode)
            {
                case KeyCode.Space:
                    if (settings.sourceMode == 1) SendSlideNavigation(true);
                    else if (video.IsPlaying) video.Pause(); else video.Play();
                    current.Use();
                    break;
                case KeyCode.LeftArrow:
                case KeyCode.UpArrow:
                case KeyCode.PageUp:
                    if (settings.sourceMode != 1) break;
                    SendSlideNavigation(false);
                    current.Use();
                    break;
                case KeyCode.RightArrow:
                case KeyCode.DownArrow:
                case KeyCode.PageDown:
                    if (settings.sourceMode != 1) break;
                    SendSlideNavigation(true);
                    current.Use();
                    break;
                case KeyCode.G:
                    settings.gridMode = (settings.gridMode + 1) % 3;
                    current.Use();
                    break;
                case KeyCode.T:
                    if (settings.sourceMode != 1) break;
                    focusCapturedWindowNextFrame = true;
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
                case KeyCode.O when current.control || current.command:
                    BrowseMedia();
                    current.Use();
                    break;
                case KeyCode.Escape:
                    if (Screen.fullScreenMode != FullScreenMode.Windowed)
                    {
                        settings.startMaximized = false;
                        Screen.SetResolution(settings.windowWidth, settings.windowHeight, FullScreenMode.Windowed);
                        status = "全画面表示を解除しました";
                    }
                    current.Use();
                    break;
                case KeyCode.Alpha1:
                    activePanelTab = 0;
                    current.Use();
                    break;
                case KeyCode.Alpha2:
                    activePanelTab = 1;
                    current.Use();
                    break;
                case KeyCode.Alpha3:
                    activePanelTab = 2;
                    current.Use();
                    break;
                case KeyCode.Alpha4:
                    activePanelTab = 3;
                    current.Use();
                    break;
            }
        }

        private void QuitApplication()
        {
            SaveSettings();
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildStyles()
        {
            if (titleStyle != null) return;

            panelTexture = CreateSolidTexture(new Color(0.105f, 0.115f, 0.13f, 1f));
            sliderTexture = CreateSolidTexture(new Color(0.38f, 0.41f, 0.45f, 1f));
            sliderThumbTexture = CreateSolidTexture(new Color(0.25f, 0.67f, 0.95f, 1f));
            sliderThumbActiveTexture = CreateSolidTexture(new Color(0.55f, 0.84f, 1f, 1f));

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };
            panelStyle.normal.background = panelTexture;

            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 8f
            };
            sliderStyle.normal.background = sliderTexture;

            sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 12f,
                fixedHeight = 18f
            };
            sliderThumbStyle.normal.background = sliderThumbTexture;
            sliderThumbStyle.hover.background = sliderThumbActiveTexture;
            sliderThumbStyle.active.background = sliderThumbActiveTexture;

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        }

        private static Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void LoadSettings()
        {
            try
            {
                var loadPath = SettingsPath;
                if (!File.Exists(loadPath))
                {
                    var sharedDirectory = Directory.GetParent(SettingsPath)?.FullName ?? string.Empty;
                    var companyDirectory = Directory.GetParent(sharedDirectory)?.FullName ?? string.Empty;
                    var legacyPath = Path.Combine(companyDirectory, "dome-projection", "dome-projection-settings.json");
                    if (File.Exists(legacyPath)) loadPath = legacyPath;
                }

                settings = File.Exists(loadPath)
                    ? JsonUtility.FromJson<DomeProjectionSettings>(File.ReadAllText(loadPath))
                    : new DomeProjectionSettings();
                settings ??= new DomeProjectionSettings();
                settings.Migrate();
                settings.Clamp();
                pathField = settings.videoPath ?? string.Empty;
                if (!string.IsNullOrEmpty(pathField) && !pathField.Contains("://")
                    && !File.Exists(pathField) && pathField.Contains("??"))
                {
                    // Older macOS builds could persist non-ASCII characters as '?'.
                    // That information cannot be reconstructed, so discard only the
                    // clearly corrupted missing path and let the user select it again.
                    pathField = string.Empty;
                    settings.videoPath = string.Empty;
                    status = "旧版で文字化けした素材パスを消去しました。素材を選び直してください。";
                }
            }
            catch (Exception exception)
            {
                settings = new DomeProjectionSettings();
                status = "設定読込エラー: " + exception.Message;
            }

            if (outputRenderer != null) outputRenderer.Settings = settings;
            if (previewRenderer != null) previewRenderer.Settings = settings;
        }

        private void SaveSettings()
        {
            if (projectorMode) return;
            try
            {
                settings.videoPath = pathField;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? Application.persistentDataPath);
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

