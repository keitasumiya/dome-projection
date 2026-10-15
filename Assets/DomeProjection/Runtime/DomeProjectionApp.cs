using System;
using System.Collections;
using System.Collections.Generic;
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
        private const float PanelWidth = 500f;
        private const float ReferenceUiHeight = 900f;
        private const float VideoPathWidth = 288f;
        // AVPro's native decoder accepts approximate preview seeks continuously;
        // match a typical display refresh rate while the pointer is dragged.
        private const float SeekPreviewInterval = 1f / 60f;
        private const string MediaPathInputName = "text-input-media-path";
        private const string LensProfileInputName = "text-input-lens-profile";
        private const string MaterialNameInputName = "text-input-material-name";
        private const string PlaylistNameInputName = "text-input-playlist-name";
        private const string WindowWidthInputName = "text-input-window-width";
        private const string WindowHeightInputName = "text-input-window-height";
        private readonly string[] gridModeNames = { "なし", "素材に重ねる", "校正パターン" };
        private readonly string[] panelTabNames = { "配置", "校正", "レンズ", "出力", "素材一覧" };
        private readonly string[] sourceModeNames = { "動画・画像", "外部ウィンドウ" };
        private readonly int[] bridgeRates = { 30, 60, 120 };

        private DomeProjectionSettings settings;
        private IVideoSource video;
        private IVideoSource preloadVideo;
        private IVideoSource idleImage;
        private IVideoSource windowBackgroundImage;
        private IWindowCaptureSource windowCapture;
        private DomeProjectionRenderer outputRenderer;
        private Camera outputCamera;
        private DomeProjectionRenderer previewRenderer;
        private Camera previewCamera;
        private string status = "校正パターンを表示中";
        private string pathField = string.Empty;
        private string idleImagePathField = string.Empty;
        private string windowBackgroundImagePathField = string.Empty;
        private string lensProfileName = string.Empty;
        private string[] lensProfilePaths = Array.Empty<string>();
        private string[] lensProfileNames = Array.Empty<string>();
        private int selectedLensProfileIndex = -1;
        private string pendingLensProfileDeletePath = string.Empty;
        private string pendingLensProfileDeleteName = string.Empty;
        private DomeConfigurationStore configurationStore;
        private DomeMaterialCatalog materialCatalog = new DomeMaterialCatalog();
        private DomePlaylistCatalog playlistCatalog = new DomePlaylistCatalog();
        private string materialNameField = string.Empty;
        private string playlistNameField = string.Empty;
        private int selectedMaterialIndex = -1;
        private int selectedPlaylistIndex = -1;
        private int selectedPlaylistEntryIndex = -1;
        private int activePlaylistIndex = -1;
        private int activePlaylistEntryIndex = -1;
        private bool activePlaylistCompleted;
        private string pendingContentDeleteKind = string.Empty;
        private string pendingContentDeleteId = string.Empty;
        private string windowWidthField = "1600";
        private string windowHeightField = "900";
        private bool showControls = true;
        private bool textInputFocused;
        private GUIStyle titleStyle;
        private GUIStyle smallStyle;
        private GUIStyle rowLabelStyle;
        private GUIStyle panelStyle;
        private GUIStyle sectionStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle chapterLabelStyle;
        private GUIStyle tabStyle;
        private GUIStyle activeTabStyle;
        private GUIStyle selectedListButtonStyle;
        private GUIStyle playingListButtonStyle;
        private GUIStyle tabContentStyle;
        private GUIStyle dropdownButtonStyle;
        private GUIStyle dropdownRowStyle;
        private GUIStyle dropdownSelectedRowStyle;
        private GUIStyle dropdownLabelStyle;
        private GUIStyle sliderStyle;
        private GUIStyle sliderThumbStyle;
        private Texture2D panelTexture;
        private Texture2D buttonTexture;
        private Texture2D buttonHoverTexture;
        private Texture2D buttonPressedTexture;
        private Texture2D inputTexture;
        private Texture2D inputFocusedTexture;
        private Texture2D tabTexture;
        private Texture2D activeTabTexture;
        private Texture2D tabContentTexture;
        private Texture2D dropdownRowTexture;
        private Texture2D dropdownSelectedRowTexture;
        private Texture2D playingRowTexture;
        private Texture2D scrollbarTexture;
        private Texture2D scrollbarThumbTexture;
        private Texture2D sliderTexture;
        private Texture2D sliderThumbTexture;
        private Texture2D sliderThumbActiveTexture;
        private Texture2D sectionTexture;
        private Texture2D chapterPendingTexture;
        private Texture2D chapterCompleteTexture;
        private Texture2D chapterCurrentTexture;
        private int activeDisplay = -1;
        private int activePanelTab;
        private int selectedWindowIndex = -1;
        private bool focusCapturedWindowNextFrame;
        private bool showWindowDropdown;
        private float windowDropdownScroll;
        private Rect windowDropdownAnchor;
        private bool consumeWindowDropdownMouseUp;
        private int hoveredWindowIndex = -1;
        private float hoveredWindowSince;
        private Vector2 panelScrollPosition;
        private bool mediaSeekDragging;
        private double mediaSeekPreviewTime;
        private bool resumeMediaAfterSeek;
        private float nextSeekPreviewTime;
        private double lastSeekPreviewTarget = -1d;
        private bool mediaSelectedThisSession;
        private bool mediaShouldPlayWhenVisible;
        private bool mediaPreparingForDisplay;
        private bool showLensCalibrationHelp;
        private bool drawLargeTransportOverlay;
        private float pendingPlaylistSeekFraction = -1f;
        private string pendingPlaylistSeekMaterialId = string.Empty;
        private bool pendingPlaylistSeekShouldPlay = true;
        private bool playlistSeekDragging;
        private int playlistSeekDraggingPlaylistIndex = -1;
        private int playlistSeekPreviewEntryIndex = -1;
        private float playlistSeekPreviewFraction;
        private bool resumePlaylistAfterSeek;
        private float nextPlaylistSeekPreviewTime;
        private int lastPlaylistSeekPreviewEntryIndex = -1;
        private float lastPlaylistSeekPreviewFraction = -1f;
        private Rect panelAreaRect;
        private FileStream instanceLock;
        private bool instanceFileRegionLocked;
        private bool projectorMode;
        private bool permissionSetupMode;
        private bool bridgeWantsMediaPlayback;
        private bool bridgeWantsMediaLooping;
        private bool bridgeWasScrubbing;
        private float nextBridgeUpdate;
        private float nextHeartbeatUpdate;
        private float nextMediaResyncAllowed;
        private long bridgeSequence;
        private long lastAppliedBridgeSequence = -1;
        private long projectorStartedUtcTicks;
        private float outputLaunchPendingUntil;
        private string bridgedMediaPath = string.Empty;
        private string bridgedPreloadMediaPath = string.Empty;
        private string bridgedIdleImagePath = string.Empty;
        private string bridgedWindowBackgroundImagePath = string.Empty;
        private long bridgedWindowId;
        private string bridgedWindowOwner = string.Empty;
        private string bridgedWindowTitle = string.Empty;
        private int nativeWindowStateGeneration;
        private string preloadedPlaylistMaterialId = string.Empty;
        private int playlistTransitionPhase;
        private float playlistTransitionOpacity = 1f;
        private float playlistFadeInStarted;

        private string SettingsPath => ProjectionProcessBridge.SettingsPath;
        private string LegacyLensProfileDirectory => Path.Combine(
            Path.GetDirectoryName(SettingsPath) ?? Application.persistentDataPath, "LensProfiles");
        private string LensProfileDirectory => configurationStore?.LensDirectoryPath ?? LegacyLensProfileDirectory;

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
            if (!projectorMode)
            {
                InitializeConfigurationStore();
                ReloadContentConfiguration(false);
            }
            // Network synchronization frequency must not cap UI/video rendering.
            // Both windows render at the display refresh rate independently.
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
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

            if (!string.IsNullOrWhiteSpace(settings.idleImagePath))
                OpenIdleImage(settings.idleImagePath, false);
            if (!string.IsNullOrWhiteSpace(settings.windowBackgroundImagePath))
                OpenWindowBackgroundImage(settings.windowBackgroundImagePath);

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
                if (!RestoreCapturedWindow())
                {
                    ClearCapturedWindowReference();
                    settings.showIdleScreen = true;
                    status = "取り込むウィンドウを選択してください";
                    SaveSettings();
                }
            }
            else
            {
                // Media files are deliberately session-scoped. Start on the
                // configured standby screen until the operator selects one.
                pathField = string.Empty;
                settings.videoPath = string.Empty;
                settings.showIdleScreen = true;
                status = "動画・画像を選択してください";
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
            if (playingRowTexture != null) Destroy(playingRowTexture);
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
            var sourceObject = new GameObject("AVPro Video Source");
            sourceObject.transform.SetParent(transform, false);
            var source = sourceObject.AddComponent<AVProVideoSource>();
            video = source;
            BindPrimaryVideoEvents(video);
            video.AudioMuted = settings.audioMuted;

            var preloadObject = new GameObject("AVPro Playlist Preload Source");
            preloadObject.transform.SetParent(transform, false);
            var preloadSource = preloadObject.AddComponent<AVProVideoSource>();
            preloadVideo = preloadSource;
            BindPreloadVideoEvents(preloadVideo);
            preloadVideo.AudioMuted = true;
            preloadVideo.IsLooping = false;

            var idleObject = new GameObject("Idle Image Source");
            idleObject.transform.SetParent(transform, false);
            var idleSource = idleObject.AddComponent<UnityVideoSource>();
            idleSource.Prepared += OnIdleImagePrepared;
            idleSource.Failed += OnIdleImageFailed;
            idleImage = idleSource;

            var windowBackgroundObject = new GameObject("Window Background Image Source");
            windowBackgroundObject.transform.SetParent(transform, false);
            var windowBackgroundSource = windowBackgroundObject.AddComponent<UnityVideoSource>();
            windowBackgroundSource.Prepared += OnWindowBackgroundImagePrepared;
            windowBackgroundSource.Failed += OnWindowBackgroundImageFailed;
            windowBackgroundImage = windowBackgroundSource;
        }

        private void BindPrimaryVideoEvents(IVideoSource source)
        {
            if (source == null) return;
            source.Prepared += OnMediaPrepared;
            source.Failed += OnMediaFailed;
            source.Ended += OnMediaEnded;
        }

        private void UnbindPrimaryVideoEvents(IVideoSource source)
        {
            if (source == null) return;
            source.Prepared -= OnMediaPrepared;
            source.Failed -= OnMediaFailed;
            source.Ended -= OnMediaEnded;
        }

        private void BindPreloadVideoEvents(IVideoSource source)
        {
            if (source == null) return;
            source.Prepared += OnPreloadMediaPrepared;
            source.Failed += OnPreloadMediaFailed;
        }

        private void UnbindPreloadVideoEvents(IVideoSource source)
        {
            if (source == null) return;
            source.Prepared -= OnPreloadMediaPrepared;
            source.Failed -= OnPreloadMediaFailed;
        }

        private void SwapVideoSourceRoles()
        {
            UnbindPrimaryVideoEvents(video);
            UnbindPreloadVideoEvents(preloadVideo);
            (video, preloadVideo) = (preloadVideo, video);
            BindPrimaryVideoEvents(video);
            BindPreloadVideoEvents(preloadVideo);
            outputRenderer.VideoSource = video;
            previewRenderer.VideoSource = video;
            preloadVideo.AudioMuted = true;
            preloadVideo.IsLooping = false;
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

            // UnityVideoSource routes movie audio through an AudioSource so that
            // seeking can safely pause and resume the native decoder. Each app is
            // a separate process and therefore needs exactly one AudioListener.
            // The operator preview camera intentionally has no listener.
            cameraObject.AddComponent<AudioListener>();

            outputRenderer = cameraObject.AddComponent<DomeProjectionRenderer>();
            outputRenderer.Settings = settings;
            outputRenderer.VideoSource = video;
            outputRenderer.IdleImageSource = idleImage;
            outputRenderer.WindowBackgroundImageSource = windowBackgroundImage;
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
            previewRenderer.IdleImageSource = idleImage;
            previewRenderer.WindowBackgroundImageSource = windowBackgroundImage;
            previewRenderer.WindowSource = windowCapture;
        }

        private void OnMediaPrepared()
        {
            // Output must keep the standby state received from Control while its
            // decoder catches up. Only Control decides when content is ready.
            if (!projectorMode && mediaPreparingForDisplay && settings.sourceMode == 0)
                settings.showIdleScreen = false;
            mediaPreparingForDisplay = false;
            settings.videoPath = video.SourcePath;
            // A playlist item must reach Ended so Control can advance it. The
            // playlist's own loop flag handles wrapping from the last item.
            if (!video.IsStillImage)
                video.IsLooping = projectorMode
                    ? bridgeWantsMediaLooping
                    : activePlaylistIndex < 0 && settings.loop;
            status = $"{(video.IsStillImage ? "画像" : "動画")}読込完了: {video.Width} × {video.Height}";

            if (video.Height > 0)
            {
                var aspect = (double)video.Width / video.Height;
                if (settings.fileProjectionMode == 0 && Math.Abs(aspect - 2d) > 0.03d)
                    status += "  ⚠ 2:1ではありません。全天円形または平面を選択してください";
            }

            if (settings.gridMode == 2) settings.gridMode = 0;
            if (!projectorMode) ApplyPendingPlaylistSeek();
            if (!video.IsStillImage)
            {
                if (projectorMode)
                {
                    if (settings.sourceMode == 0 && !settings.showIdleScreen && bridgeWantsMediaPlayback)
                        video.Play();
                    else
                        video.Pause();
                }
                else
                {
                    ApplyControlMediaPlaybackState();
                }
            }
            if (!projectorMode) UpdateMaterialDurationForCurrentPath();
            SaveSettings();
        }

        private void OnPreloadMediaPrepared()
        {
            preloadVideo.IsLooping = false;
            preloadVideo.AudioMuted = true;
            preloadVideo.AudioVolume = 0f;
            preloadVideo.Pause();
        }

        private void OnPreloadMediaFailed(string message)
        {
            preloadedPlaylistMaterialId = string.Empty;
            UnityEngine.Debug.LogWarning("Playlist preload failed: " + message);
        }

        private void ApplyPendingPlaylistSeek()
        {
            if (pendingPlaylistSeekFraction < 0f || activePlaylistIndex < 0
                || activePlaylistIndex >= playlistCatalog.playlists.Count) return;
            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (activePlaylistEntryIndex < 0 || activePlaylistEntryIndex >= playlist.materialIds.Count) return;
            var materialId = playlist.materialIds[activePlaylistEntryIndex];
            if (!string.Equals(materialId, pendingPlaylistSeekMaterialId, StringComparison.Ordinal)) return;

            var fraction = Mathf.Clamp01(pendingPlaylistSeekFraction);
            pendingPlaylistSeekFraction = -1f;
            pendingPlaylistSeekMaterialId = string.Empty;
            if (video.IsStillImage || video.Duration <= 0d) return;
            if (playlistSeekDragging)
            {
                mediaShouldPlayWhenVisible = false;
                video.SeekPreview(video.Duration * fraction);
            }
            else
            {
                mediaShouldPlayWhenVisible = pendingPlaylistSeekShouldPlay;
                video.Seek(video.Duration * fraction, pendingPlaylistSeekShouldPlay);
            }
        }

        private void UpdateMaterialDurationForCurrentPath()
        {
            if (video == null || !video.IsPrepared || video.IsStillImage || video.Duration <= 0d
                || configurationStore == null) return;
            foreach (var item in materialCatalog.materials)
            {
                var resolvedPath = configurationStore.ResolveMediaPath(item.path);
                if (!MediaPathsEqual(resolvedPath, video.SourcePath)) continue;
                var duration = (float)video.Duration;
                if (Mathf.Abs(item.durationSeconds - duration) < 0.25f) return;
                item.durationSeconds = duration;
                SaveMaterialCatalog();
                return;
            }
        }

        private void OnMediaFailed(string message)
        {
            status = "読込エラー: " + message;
        }

        private void OnMediaEnded()
        {
            // Some native backends can report FinishedPlaying while pausing at
            // the final frame. A user pause must never destroy playlist state.
            if (projectorMode || !mediaShouldPlayWhenVisible) return;
            var playlistAdvanceAttempted = false;
            if (playlistTransitionPhase == 1)
            {
                playlistTransitionOpacity = 0f;
                playlistAdvanceAttempted = true;
                if (AdvanceActivePlaylist(1))
                {
                    playlistTransitionPhase = 2;
                    playlistFadeInStarted = Time.unscaledTime;
                    return;
                }
                ResetPlaylistTransition();
            }
            if (!playlistAdvanceAttempted && !projectorMode && activePlaylistIndex >= 0
                && activePlaylistIndex < playlistCatalog.playlists.Count
                && playlistCatalog.playlists[activePlaylistIndex].autoAdvance
                && AdvanceActivePlaylist(1)) return;
            mediaShouldPlayWhenVisible = false;
            SetIdleScreenVisible(true);
            status = activePlaylistCompleted
                ? "プレイリストの再生が完了しました。中央の再生ボタンで先頭から再生できます"
                : "動画が終了したため待機画面へ切り替えました";
            SaveSettings();
        }

        private void OnIdleImagePrepared()
        {
            settings.idleImagePath = idleImage.SourcePath;
            idleImagePathField = settings.idleImagePath;
            status = $"既定画像を読込みました: {idleImage.Width} × {idleImage.Height}";
            SaveSettings();
        }

        private void OnIdleImageFailed(string message)
        {
            status = "既定画像の読込エラー: " + message;
        }

        private void OnWindowBackgroundImagePrepared()
        {
            settings.windowBackgroundImagePath = windowBackgroundImage.SourcePath;
            windowBackgroundImagePathField = settings.windowBackgroundImagePath;
            status = $"外部ウィンドウ背景を読込みました: {windowBackgroundImage.Width} × {windowBackgroundImage.Height}";
            SaveSettings();
        }

        private void OnWindowBackgroundImageFailed(string message)
        {
            status = "外部ウィンドウ背景の読込エラー: " + message;
        }

        private void OnWindowPrepared()
        {
            if (!projectorMode) settings.showIdleScreen = false;
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

        private void OpenMedia(bool preserveMaterialName = false)
        {
            var path = pathField.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!preserveMaterialName && !MediaPathsEqual(path, video?.SourcePath))
                materialNameField = Path.GetFileNameWithoutExtension(path);
            mediaSeekDragging = false;
            resumeMediaAfterSeek = false;
            lastSeekPreviewTarget = -1d;
            // Keep the standby screen visible until the replacement texture is
            // prepared. This prevents a frame from the previous file flashing.
            SetSourceMode(0);
            mediaSelectedThisSession = true;
            mediaShouldPlayWhenVisible = true;
            mediaPreparingForDisplay = true;
            settings.showIdleScreen = true;
            pathField = path;
            settings.videoPath = path;
            status = "素材を準備中…";
            video.Open(path);
        }

        private static bool MediaPathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            if (first.Contains("://") || second.Contains("://"))
                return string.Equals(first, second, StringComparison.Ordinal);
            try
            {
                return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void OpenIdleImage(string path, bool switchToIdleImage)
        {
            path = path?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                status = "既定画像にはJPEGまたはPNGを選択してください";
                return;
            }

            settings.idleImagePath = path;
            idleImagePathField = path;
            if (switchToIdleImage)
            {
                settings.idleDisplayMode = 1;
                SetIdleScreenVisible(true);
            }
            idleImage.Open(path);
        }

        private void BrowseIdleImage()
        {
            var selected = string.Empty;
#if UNITY_EDITOR
            selected = EditorUtility.OpenFilePanelWithFilters("待機画面の既定画像を選択", string.Empty,
                new[] { "画像", "jpg,jpeg,png" });
#elif UNITY_STANDALONE_OSX
            selected = OpenMacMediaFilePanel(out var macDialogError);
            if (!string.IsNullOrWhiteSpace(macDialogError)) status = macDialogError;
#elif UNITY_STANDALONE_WIN
            selected = OpenWindowsMediaFileDialog(out var dialogError);
            if (!string.IsNullOrWhiteSpace(dialogError)) status = dialogError;
#endif
            if (!string.IsNullOrWhiteSpace(selected)) ImportIdleImage(selected);
        }

        private void ImportIdleImage(string sourcePath)
        {
            sourcePath = sourcePath?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                status = "既定画像にはJPEGまたはPNGを選択してください";
                return;
            }

            try
            {
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("画像が見つかりません。", sourcePath);
                var storageDirectory = Path.GetDirectoryName(SettingsPath) ?? Application.persistentDataPath;
                Directory.CreateDirectory(storageDirectory);
                var storedPath = Path.Combine(storageDirectory, "default-idle-image" + extension);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(storedPath),
                        StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, storedPath, true);

                OpenIdleImage(storedPath, true);
                status = "既定画像をアプリ内へ保存しました: " + Path.GetFileName(sourcePath);
            }
            catch (Exception exception)
            {
                status = "既定画像を保存できませんでした: " + exception.Message;
            }
        }

        private void OpenWindowBackgroundImage(string path)
        {
            path = path?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                status = "外部ウィンドウ背景にはJPEGまたはPNGを選択してください";
                return;
            }

            settings.windowBackgroundImagePath = path;
            windowBackgroundImagePathField = path;
            windowBackgroundImage.Open(path);
        }

        private void BrowseWindowBackgroundImage()
        {
            var selected = string.Empty;
#if UNITY_EDITOR
            selected = EditorUtility.OpenFilePanelWithFilters("外部ウィンドウの背景半球画像を選択", string.Empty,
                new[] { "画像", "jpg,jpeg,png" });
#elif UNITY_STANDALONE_OSX
            selected = OpenMacMediaFilePanel(out var macDialogError);
            if (!string.IsNullOrWhiteSpace(macDialogError)) status = macDialogError;
#elif UNITY_STANDALONE_WIN
            selected = OpenWindowsMediaFileDialog(out var dialogError);
            if (!string.IsNullOrWhiteSpace(dialogError)) status = dialogError;
#endif
            if (!string.IsNullOrWhiteSpace(selected)) ImportWindowBackgroundImage(selected);
        }

        private void ImportWindowBackgroundImage(string sourcePath)
        {
            sourcePath = sourcePath?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                status = "外部ウィンドウ背景にはJPEGまたはPNGを選択してください";
                return;
            }

            try
            {
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("画像が見つかりません。", sourcePath);
                var storageDirectory = Path.GetDirectoryName(SettingsPath) ?? Application.persistentDataPath;
                Directory.CreateDirectory(storageDirectory);
                var storedPath = Path.Combine(storageDirectory, "window-background-image" + extension);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(storedPath),
                        StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, storedPath, true);

                OpenWindowBackgroundImage(storedPath);
                status = "外部ウィンドウ背景をアプリ内へ保存しました: " + Path.GetFileName(sourcePath);
            }
            catch (Exception exception)
            {
                status = "外部ウィンドウ背景を保存できませんでした: " + exception.Message;
            }
        }

        private void ClearWindowBackgroundImage()
        {
            settings.windowBackgroundImagePath = string.Empty;
            windowBackgroundImagePathField = string.Empty;
            windowBackgroundImage.Clear();
            status = "外部ウィンドウ背景を「なし」にしました";
            SaveSettings();
        }

        private void BrowseMedia()
        {
            var selected = string.Empty;

#if UNITY_EDITOR
            selected = EditorUtility.OpenFilePanelWithFilters("投影する動画・画像を選択", string.Empty,
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
        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_ByteResult(IntPtr receiver, IntPtr selector);
        [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);
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
                if (mediaSelectedThisSession && video.IsPrepared)
                {
                    pathField = video.SourcePath;
                    settings.videoPath = video.SourcePath;
                    settings.showIdleScreen = false;
                    ApplyControlMediaPlaybackState();
                    status = video.IsStillImage
                        ? "選択済みの画像へ戻りました"
                        : "選択済みの動画へ戻りました";
                }
                else
                {
                    video.Clear();
                    mediaShouldPlayWhenVisible = false;
                    mediaPreparingForDisplay = false;
                    pathField = string.Empty;
                    settings.videoPath = string.Empty;
                    settings.showIdleScreen = true;
                    bridgedMediaPath = string.Empty;
                    status = "動画・画像を選択してください";
                }
            }
            else
            {
                ResetPlaylistTransition();
                ClearPlaylistPreload();
                if (video.IsPlaying) video.Pause();
                if (windowCapture.Windows.Count == 0) RefreshWindowList();
                if (!RestoreCapturedWindow())
                {
                    ClearCapturedWindowReference();
                    settings.showIdleScreen = true;
                    status = "取り込むウィンドウを選択してください";
                }
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

            if (selectedWindowIndex < 0 && previouslyHighlightedId > 0)
            {
                for (var index = 0; index < windowCapture.Windows.Count; index++)
                {
                    if (windowCapture.Windows[index].Id != previouslyHighlightedId) continue;
                    selectedWindowIndex = index;
                    break;
                }
            }

            if (selectedWindowIndex < 0 && !string.IsNullOrEmpty(settings.capturedWindowOwner))
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

        private bool RestoreCapturedWindow()
        {
            if (selectedWindowIndex < 0 || selectedWindowIndex >= windowCapture.Windows.Count) return false;
            var item = windowCapture.Windows[selectedWindowIndex];
            var idMatches = settings.capturedWindowId > 0 && item.Id == settings.capturedWindowId;
            var nameMatches = item.OwnerName == settings.capturedWindowOwner
                && item.Title == settings.capturedWindowTitle;
            if (!idMatches && !nameMatches)
            {
                windowCapture.StopCapture();
                return false;
            }
            return BeginWindowCapture(item, false);
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

        private bool BeginWindowCapture(CapturableWindow item, bool requestFocus)
        {
            if (video.IsPlaying) video.Pause();
            windowCapture.CaptureFps = settings.windowCaptureFps;
            if (!windowCapture.SelectWindow(item.Id))
            {
                if (!projectorMode)
                {
                    ClearCapturedWindowReference();
                    settings.showIdleScreen = true;
                    SaveSettings();
                }
                return false;
            }
            settings.sourceMode = 1;
            settings.capturedWindowId = item.Id;
            settings.capturedWindowOwner = item.OwnerName ?? string.Empty;
            settings.capturedWindowTitle = item.Title ?? string.Empty;
            status = "ウィンドウの取込を開始しています…";
            focusCapturedWindowNextFrame = requestFocus;
            if (!projectorMode) SaveSettings();
            return true;
        }

        private void ClearCapturedWindowReference()
        {
            windowCapture.StopCapture();
            settings.capturedWindowId = 0L;
            settings.capturedWindowOwner = string.Empty;
            settings.capturedWindowTitle = string.Empty;
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
            else status = forward ? "スライドを次へ進めました" : "スライドを前へ戻しました";
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
                    filter = "投影する動画・画像 (*.mp4;*.mov;*.m4v;*.jpg;*.jpeg;*.png)\0*.mp4;*.mov;*.m4v;*.jpg;*.jpeg;*.png\0すべてのファイル (*.*)\0*.*\0\0",
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = fileBufferCharacters,
                    initialDirectory = GetExistingMediaDirectory(),
                    title = "投影する動画・画像を選択",
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);
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
            // Playlist videos must be allowed to finish so OnMediaEnded can
            // advance to the next entry. Do not overwrite this every frame with
            // the individual material's loop preference.
            if (!projectorMode) video.IsLooping = activePlaylistIndex < 0 && settings.loop;
            preloadVideo.IsLooping = false;
            preloadVideo.AudioMuted = true;
            windowCapture.CaptureFps = settings.windowCaptureFps;
            if (projectorMode)
            {
                UpdateProjectorProcess();
            }
            else
            {
                UpdatePlaylistTransition();
                UpdateControlProcess();
            }
            outputRenderer.ContentOpacity = playlistTransitionOpacity;
            previewRenderer.ContentOpacity = playlistTransitionOpacity;
            video.AudioVolume = playlistTransitionOpacity;
            if (focusCapturedWindowNextFrame)
            {
                focusCapturedWindowNextFrame = false;
                FocusCapturedWindow();
            }
        }

        private void UpdateControlProcess()
        {
            ApplyControlMediaPlaybackState();
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
                // Publish operator intent rather than VideoPlayer.isPlaying.
                // isPlaying is temporarily false during every seek, which used
                // to make Output pause and resume a second time.
                mediaIsPlaying = settings.sourceMode == 0 && !settings.showIdleScreen
                    && mediaShouldPlayWhenVisible && !mediaSeekDragging && !playlistSeekDragging,
                mediaIsLooping = activePlaylistIndex < 0 && settings.loop,
                mediaIsScrubbing = mediaSeekDragging || playlistSeekDragging,
                mediaTime = mediaSeekDragging
                    ? mediaSeekPreviewTime
                    : PlaylistSeekPreviewTime(),
                preloadMediaPath = preloadVideo?.SourcePath ?? string.Empty,
                transitionOpacity = playlistTransitionOpacity
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
            bridgeWantsMediaLooping = shared.mediaIsLooping;
            ApplySharedSettings(shared.settings);
            video.IsLooping = shared.mediaIsLooping;
            playlistTransitionOpacity = Mathf.Clamp01(shared.transitionOpacity);
            var requestedPreloadPath = shared.preloadMediaPath ?? string.Empty;

            // Output captures the selected window independently from Control.
            // Changing sourceMode previously hid that texture but left
            // PrintWindow running in the background, competing with AVPro's
            // decoder and causing Windows-only video stalls/black flashes.
            if (settings.sourceMode == 0 && windowCapture.SelectedWindow != null)
                windowCapture.StopCapture();

            if (settings.sourceMode == 0 && string.IsNullOrWhiteSpace(settings.videoPath)
                && (video.IsPrepared || !string.IsNullOrEmpty(video.SourcePath)))
            {
                video.Clear();
                bridgedMediaPath = string.Empty;
            }

            // Captured windows have no audio in this app. Pause the hidden
            // media decoder as soon as Control switches away from it so a
            // looping video's sound cannot continue behind the presentation.
            if ((settings.sourceMode != 0 || settings.showIdleScreen) && video.IsPlaying)
                video.Pause();

            if (settings.idleImagePath != bridgedIdleImagePath)
            {
                bridgedIdleImagePath = settings.idleImagePath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(bridgedIdleImagePath))
                    OpenIdleImage(bridgedIdleImagePath, false);
                else
                    idleImage.Clear();
            }

            if (settings.windowBackgroundImagePath != bridgedWindowBackgroundImagePath)
            {
                bridgedWindowBackgroundImagePath = settings.windowBackgroundImagePath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(bridgedWindowBackgroundImagePath))
                    OpenWindowBackgroundImage(bridgedWindowBackgroundImagePath);
                else
                    windowBackgroundImage.Clear();
            }

            if (settings.sourceMode == 0)
            {
                if (!string.IsNullOrWhiteSpace(settings.videoPath)
                    && settings.videoPath != bridgedMediaPath)
                {
                    bridgedMediaPath = settings.videoPath;
                    if (preloadVideo.IsPrepared && MediaPathsEqual(bridgedMediaPath, preloadVideo.SourcePath))
                    {
                        SwapVideoSourceRoles();
                        // The former primary decoder is now the preload slot.
                        // Release it immediately so a long previous movie does
                        // not keep decoding invisibly until the next preload.
                        preloadVideo.Clear();
                        video.AudioMuted = settings.audioMuted;
                        video.IsLooping = shared.mediaIsLooping;
                        bridgedPreloadMediaPath = string.Empty;
                    }
                    else
                        video.Open(bridgedMediaPath);
                }

                var preloadChanged = string.IsNullOrWhiteSpace(requestedPreloadPath)
                    ? !string.IsNullOrWhiteSpace(bridgedPreloadMediaPath)
                    : !MediaPathsEqual(requestedPreloadPath, bridgedPreloadMediaPath);
                if (preloadChanged)
                {
                    bridgedPreloadMediaPath = requestedPreloadPath;
                    if (string.IsNullOrWhiteSpace(bridgedPreloadMediaPath)) preloadVideo.Clear();
                    else if (!MediaPathsEqual(bridgedPreloadMediaPath, video.SourcePath))
                        preloadVideo.Open(bridgedPreloadMediaPath);
                }

                if (video.IsPrepared && !video.IsStillImage)
                {
                    var timeError = Math.Abs(video.Time - shared.mediaTime);
                    if (shared.mediaIsScrubbing)
                    {
                        if (timeError > 0.02d) video.SeekPreview(shared.mediaTime);
                    }
                    else if (bridgeWasScrubbing)
                    {
                        // One exact seek on release corrects the approximate
                        // keyframe/tolerance seeks used during dragging.
                        nextMediaResyncAllowed = Time.unscaledTime + 2f;
                        video.Seek(shared.mediaTime, shared.mediaIsPlaying);
                    }
                    else if (timeError > 0.75d && Time.unscaledTime >= nextMediaResyncAllowed)
                    {
                        nextMediaResyncAllowed = Time.unscaledTime + 2f;
                        video.Seek(shared.mediaTime, shared.mediaIsPlaying);
                    }
                    if (shared.mediaIsPlaying && !video.IsPlaying) video.Play();
                    else if (!shared.mediaIsPlaying && video.IsPlaying) video.Pause();
                }
                bridgeWasScrubbing = shared.mediaIsScrubbing;
            }
            else
            {
                bridgeWasScrubbing = false;
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
            BuildStyles();
            var previousMatrix = GUI.matrix;
            // Keep controls physically readable when the operator enlarges or
            // maximizes the window. 1600x900 remains 1:1; larger windows grow
            // the complete UI up to 160% instead of leaving tiny text behind.
            var uiScale = Mathf.Clamp(Screen.height / ReferenceUiHeight, 0.75f, 1.6f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));
            var logicalWidth = Screen.width / uiScale;
            var logicalHeight = Screen.height / uiScale;
            var margin = 16f / uiScale;
            drawLargeTransportOverlay = showControls && settings.sourceMode == 0
                && logicalWidth >= PanelWidth * 2f + 560f
                && ((video != null && video.IsPrepared && !video.IsStillImage)
                    || activePlaylistIndex >= 0);
            var panelX = settings.controlPanelOnRight
                ? logicalWidth - PanelWidth - margin
                : margin;
            panelAreaRect = new Rect(panelX, margin, PanelWidth, logicalHeight - margin * 2f);
            HandleWindowDropdownInput();
            HandlePanelScrollWheel();

            if (showControls)
            {
                GUILayout.BeginArea(panelAreaRect, panelStyle);
                panelScrollPosition = GUILayout.BeginScrollView(panelScrollPosition, false, false,
                    GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));
                // Reserve explicit room for the scrollbar. Unity's IMGUI scroll
                // view can otherwise draw it over the right edge of its content.
                // Account for panel padding, the scrollbar and IMGUI's scroll
                // view gutter. This produces matching 12 px visual margins on
                // the left and immediately before the scrollbar on the right.
                const float panelContentReservedWidth = 50f;
                GUILayout.BeginVertical(GUILayout.Width(PanelWidth - panelContentReservedWidth));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Dome Projection Control", titleStyle, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(settings.controlPanelOnRight ? "◀ 左へ" : "右へ ▶",
                        GUILayout.Width(72f), GUILayout.Height(28f)))
                {
                    settings.controlPanelOnRight = !settings.controlPanelOnRight;
                    showWindowDropdown = false;
                    SaveSettings();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Version " + Application.version, smallStyle);
                GUILayout.Label(status, smallStyle);
                DrawOutputModeBanner();
                GUILayout.Space(8f);

                DrawSourceControls();
                GUILayout.Space(8f);
                DrawPanelTabs();
                GUILayout.BeginVertical(tabContentStyle);
                switch (activePanelTab)
                {
                    case 0: DrawBasicPanel(); break;
                    case 1: DrawCalibrationPanel(); break;
                    case 2: DrawLensPanel(); break;
                    case 3: DrawOutputPanel(); break;
                    default: DrawContentLibraryPanel(); break;
                }
                GUILayout.EndVertical();

                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("設定を保存")) { SaveSettings(); status = "設定を保存しました"; }
                if (GUILayout.Button("設定を再読込"))
                {
                    LoadSettings();
                    if (!string.IsNullOrWhiteSpace(settings.idleImagePath))
                        OpenIdleImage(settings.idleImagePath, false);
                    else
                        idleImage.Clear();
                    if (!string.IsNullOrWhiteSpace(settings.windowBackgroundImagePath))
                        OpenWindowBackgroundImage(settings.windowBackgroundImagePath);
                    else
                        windowBackgroundImage.Clear();
                    status = "設定を再読込しました";
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8f);
                GUILayout.Label("アプリ操作", sectionTitleStyle);
                if (GUILayout.Button("Dome Projection Controlを終了", GUILayout.Height(30f)))
                    QuitApplication();

                GUILayout.Space(6f);
                GUILayout.Label(settings.sourceMode == 1
                    ? "←/→・PgUp/PgDn: スライド移動   T: 操作先へ切替   B: 待機画面   G: ガイド   H: UI   F: 全画面   Esc: 全画面解除"
                    : "1/2/3/4/5: タブ   F5/F6: 素材/画面取込   Ctrl+O: 素材選択   Space: 再生/停止   B: 待機画面   G: ガイド   H: UI", smallStyle);
                GUILayout.Label($"出力 {Screen.width}×{Screen.height} / Display {settings.outputDisplay}", smallStyle);
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            DrawPanelVisibilityToggle(logicalWidth, margin);
            if (drawLargeTransportOverlay) DrawTransportOverlay(logicalWidth, logicalHeight, margin);
            if (showWindowDropdown && settings.sourceMode == 1) DrawWindowDropdown();
            // Cache after controls have registered their IMGUI names so plain
            // character shortcuts never consume text typed into a field.
            textInputFocused = showControls && IsTextInputFocused();
            HandleHotkeys(Event.current);
            GUI.matrix = previousMatrix;
        }

        private void DrawTransportOverlay(float logicalWidth, float logicalHeight, float margin)
        {
            // Keep playback controls at the actual horizontal center of the
            // application window. Their position must not jump when the setup
            // panel is moved between the left and right sides.
            var availableWidth = logicalWidth - margin * 2f;
            if (availableWidth < 500f) return;

            var hasActivePlaylist = activePlaylistIndex >= 0
                                    && activePlaylistIndex < playlistCatalog.playlists.Count;
            var width = Mathf.Min(760f, availableWidth - 32f);
            var height = hasActivePlaylist ? 150f : 96f;
            var area = new Rect((logicalWidth - width) * 0.5f,
                logicalHeight - height - margin, width, height);
            GUILayout.BeginArea(area, sectionStyle);

            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            if (hasActivePlaylist)
            {
                var playlist = playlistCatalog.playlists[activePlaylistIndex];
                GUILayout.Label("プレイリスト: " + playlist.name, sectionTitleStyle,
                    GUILayout.ExpandWidth(true));
                var autoAdvance = GUILayout.Toggle(playlist.autoAdvance,
                    new GUIContent(playlist.autoAdvance ? "自動切替 ON" : "自動切替 OFF",
                        "動画終了時に次の素材へ自動切替"),
                    GUILayout.Width(108f));
                if (autoAdvance != playlist.autoAdvance)
                {
                    playlist.autoAdvance = autoAdvance;
                    ResetPlaylistTransition();
                    if (!autoAdvance) ClearPlaylistPreload();
                    SavePlaylistCatalog();
                }
                if (GUILayout.Button(new GUIContent("|◀ 前へ", "前の素材"), GUILayout.Width(82f)))
                    AdvanceActivePlaylist(-1);
                var effectivelyPlaying = mediaShouldPlayWhenVisible && !settings.showIdleScreen
                                         && !activePlaylistCompleted;
                if (GUILayout.Button(effectivelyPlaying ? "Ⅱ 一時停止" : "▶ 再生",
                        GUILayout.Width(92f)))
                    ToggleActivePlaylistPlayback();
                if (GUILayout.Button(new GUIContent("次へ ▶|", "次の素材"), GUILayout.Width(82f)))
                    AdvanceActivePlaylist(1);
                var loopStyle = playlist.loop ? activeTabStyle : tabStyle;
                if (GUILayout.Button(new GUIContent("↻", "末尾から先頭へループ"), loopStyle,
                        GUILayout.Width(40f), GUILayout.Height(28f)))
                {
                    playlist.loop = !playlist.loop;
                    SavePlaylistCatalog();
                }
            }
            else
            {
                var name = video == null || string.IsNullOrWhiteSpace(video.SourcePath)
                    ? "動画"
                    : Path.GetFileNameWithoutExtension(video.SourcePath);
                GUILayout.Label(TruncateLabel(name, 54), sectionTitleStyle, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(mediaShouldPlayWhenVisible ? "一時停止" : "再生", GUILayout.Width(82f)))
                {
                    mediaShouldPlayWhenVisible = !mediaShouldPlayWhenVisible;
                    ApplyControlMediaPlaybackState();
                }
            }
            GUILayout.EndHorizontal();

            if (hasActivePlaylist)
                DrawPlaylistSeekBar(playlistCatalog.playlists[activePlaylistIndex], activePlaylistIndex, true);
            else if (video != null && video.IsPrepared && !video.IsStillImage && video.Duration > 0d)
                DrawMediaSeekBar();
            GUILayout.EndArea();
        }

        private void DrawPanelVisibilityToggle(float logicalWidth, float margin)
        {
            const float width = 148f;
            const float height = 34f;
            float x;
            string label;
            if (showControls)
            {
                x = settings.controlPanelOnRight
                    ? panelAreaRect.x - width - 5f
                    : panelAreaRect.xMax + 5f;
                label = settings.controlPanelOnRight
                    ? "設定パネルを隠す ▶"
                    : "◀ 設定パネルを隠す";
            }
            else
            {
                x = settings.controlPanelOnRight
                    ? logicalWidth - margin - width
                    : margin;
                label = "☰ 設定パネルを表示";
            }

            if (!GUI.Button(new Rect(x, margin, width, height), label)) return;
            showControls = !showControls;
            if (!showControls) showWindowDropdown = false;
        }

        private void HandlePanelScrollWheel()
        {
            if (!showControls || Event.current.type != EventType.ScrollWheel) return;
            panelScrollPosition.y = Mathf.Max(0f,
                panelScrollPosition.y + Event.current.delta.y * 30f);
            Event.current.Use();
        }

        private static Vector2 GetLogicalMousePosition()
        {
            var point = GUI.matrix.inverse.MultiplyPoint3x4(Event.current.mousePosition);
            return new Vector2(point.x, point.y);
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
            BeginSection("入力ソースを選択");
            GUILayout.BeginHorizontal();
            for (var index = 0; index < sourceModeNames.Length; index++)
            {
                if (GUILayout.Toggle(settings.sourceMode == index, sourceModeNames[index], GUI.skin.button))
                    SetSourceMode(index);
            }
            GUILayout.EndHorizontal();
            EndSection();

            BeginSection("表示オプション");
            if (GUILayout.Button(settings.showIdleScreen ? "待機画面：表示中" : "待機画面：非表示",
                    settings.showIdleScreen ? activeTabStyle : tabStyle, GUILayout.Height(30f)))
                SetIdleScreenVisible(!settings.showIdleScreen);
            var guideVisible = settings.gridMode == 1;
            if (GUILayout.Button(guideVisible ? "半球ガイド：表示中" : "半球ガイド：非表示",
                    guideVisible ? activeTabStyle : tabStyle, GUILayout.Height(30f)))
                ToggleDomeGuide();
            EndSection();

            if (settings.sourceMode == 1) DrawWindowCaptureControls();
            else DrawMediaControls();
        }

        private void DrawMediaControls()
        {
            BeginSection("動画・画像");
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(MediaPathInputName);
            pathField = GUILayout.TextField(pathField, GUILayout.Width(VideoPathWidth));
            if (GUILayout.Button("選択", GUILayout.Width(54f))) BrowseMedia();
            if (GUILayout.Button("読込", GUILayout.Width(54f))) OpenMedia();
            GUILayout.EndHorizontal();
            GUILayout.Label("素材の形式（どれか1つ）", smallStyle);
            GUILayout.BeginHorizontal();
            DrawFileProjectionModeButton(0, "360° 2:1");
            DrawFileProjectionModeButton(2, "全天円形");
            DrawFileProjectionModeButton(1, "平面");
            GUILayout.EndHorizontal();
            if (settings.fileProjectionMode == 2)
                GUILayout.Label("POMS全天画像・魚眼・ドームマスターを円形のまま投影します。", smallStyle);

            if (video.IsStillImage)
            {
                GUILayout.Label("静止画を表示中（JPEG / PNG）", smallStyle);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = video.IsPrepared;
                if (GUILayout.Button(mediaShouldPlayWhenVisible ? "一時停止" : "再生"))
                {
                    mediaShouldPlayWhenVisible = !mediaShouldPlayWhenVisible;
                    ApplyControlMediaPlaybackState();
                }
                if (GUILayout.Button("停止"))
                {
                    mediaShouldPlayWhenVisible = false;
                    video.Stop();
                    SetIdleScreenVisible(true);
                    status = "停止して待機画面へ切り替えました";
                }
                GUI.enabled = true;
                settings.loop = GUILayout.Toggle(settings.loop, "ループ", GUILayout.Width(75f));
                settings.audioMuted = GUILayout.Toggle(settings.audioMuted, "ミュート", GUILayout.Width(82f));
                GUILayout.EndHorizontal();
            }

            if (video.IsPrepared && video.Duration > 0d && !drawLargeTransportOverlay)
                DrawMediaSeekBar();
            EndSection();
        }

        private void DrawFileProjectionModeButton(int mode, string label)
        {
            var selected = settings.fileProjectionMode == mode;
            if (!GUILayout.Button(label,
                    selected ? activeTabStyle : tabStyle, GUILayout.ExpandWidth(true))) return;
            settings.fileProjectionMode = mode;
            settings.fileIsFlat = mode == 1;
        }

        private void ToggleDomeGuide()
        {
            // This is an overlay over the current source or standby screen;
            // it must never replace the selected standby image.
            settings.gridMode = settings.gridMode == 1 ? 0 : 1;
        }

        private bool IsPrimarySourcePrepared()
        {
            return settings.sourceMode == 1
                ? windowCapture?.IsPrepared == true
                : video?.IsPrepared == true;
        }

        private void SetIdleScreenVisible(bool visible)
        {
            settings.showIdleScreen = visible;
            if (!projectorMode) ApplyControlMediaPlaybackState();
        }

        private void ApplyControlMediaPlaybackState()
        {
            if (projectorMode || video == null || video.IsStillImage || !video.IsPrepared) return;
            var shouldPlay = settings.sourceMode == 0
                && !settings.showIdleScreen
                && mediaShouldPlayWhenVisible;
            if (shouldPlay)
            {
                if (!video.IsPlaying) video.Play();
            }
            else if (video.IsPlaying)
            {
                video.Pause();
            }
        }

        private void DrawMediaSeekBar()
        {
            var displayedTime = mediaSeekDragging ? mediaSeekPreviewTime : video.Time;
            var rawEventType = Event.current.rawType;
            var mousePosition = Event.current.mousePosition;

            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label("再生位置", rowLabelStyle, GUILayout.Width(82f));
            var sliderRect = ReserveCenteredSliderRect();
            if (!mediaSeekDragging && rawEventType == EventType.MouseDown
                && Event.current.button == 0 && sliderRect.Contains(mousePosition))
            {
                mediaSeekDragging = true;
                mediaSeekPreviewTime = video.Time;
                resumeMediaAfterSeek = video.IsPlaying;
                nextSeekPreviewTime = 0f;
                lastSeekPreviewTarget = -1d;
                video.Pause();
            }

            displayedTime = DrawCenteredSlider(sliderRect, (float)displayedTime, 0f,
                (float)video.Duration);
            if (mediaSeekDragging)
            {
                mediaSeekPreviewTime = displayedTime;
                if (Time.unscaledTime >= nextSeekPreviewTime
                    && Math.Abs(mediaSeekPreviewTime - lastSeekPreviewTarget) > 0.02d)
                {
                    nextSeekPreviewTime = Time.unscaledTime + SeekPreviewInterval;
                    lastSeekPreviewTarget = mediaSeekPreviewTime;
                    // Approximate native seek for immediate visual feedback. An
                    // exact seek is issued once when the pointer is released.
                    video.SeekPreview(mediaSeekPreviewTime);
                }
            }
            GUILayout.Label(FormatTime(displayedTime) + " / " + FormatTime(video.Duration), rowLabelStyle,
                GUILayout.Width(92f));
            GUILayout.EndHorizontal();

            // Keep the thumb entirely independent from decoder seek latency while
            // dragging, then perform exactly one seek when the pointer is released.
            if (!mediaSeekDragging || rawEventType != EventType.MouseUp) return;
            var target = mediaSeekPreviewTime;
            var shouldResume = resumeMediaAfterSeek;
            mediaSeekDragging = false;
            resumeMediaAfterSeek = false;
            lastSeekPreviewTarget = -1d;
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
            var dropdownClicked = GUILayout.Button(dropdownLabel, dropdownButtonStyle, GUILayout.Height(28f));
            if (Event.current.type == EventType.Repaint)
            {
                windowDropdownAnchor = GUILayoutUtility.GetLastRect();
                windowDropdownAnchor.y -= panelScrollPosition.y;
            }
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

            GUILayout.Space(8f);
            GUILayout.Label("ウィンドウ外の背景（360°半球画像）");
            GUILayout.Label("取り込んだ平面ウィンドウの周囲に表示します。画像はアプリ内へコピーして保持します。", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("背景画像を選択…")) BrowseWindowBackgroundImage();
            GUI.enabled = !string.IsNullOrWhiteSpace(windowBackgroundImagePathField);
            if (GUILayout.Button("なし", GUILayout.Width(64f))) ClearWindowBackgroundImage();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrWhiteSpace(windowBackgroundImagePathField)
                ? "背景画像: なし（黒）"
                : "背景画像: " + Path.GetFileName(windowBackgroundImagePathField), smallStyle);
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
            var mouseInDropdown = GetLogicalMousePosition() - dropdownRect.position;
            var hoveredIndex = -1;
            if (count > 0 && mouseInDropdown.x >= 8f && mouseInDropdown.x < dropdownRect.width - 18f
                && mouseInDropdown.y >= 8f && mouseInDropdown.y < 8f + visibleHeight)
            {
                var candidate = Mathf.FloorToInt((mouseInDropdown.y - 8f + windowDropdownScroll) / rowHeight);
                if (candidate >= 0 && candidate < count) hoveredIndex = candidate;
            }
            if (hoveredWindowIndex != hoveredIndex)
            {
                hoveredWindowIndex = hoveredIndex;
                hoveredWindowSince = Time.unscaledTime;
            }

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
                    var rowRect = new Rect(8f, rowY, dropdownRect.width - 26f, 25f);
                    GUI.Box(rowRect, GUIContent.none,
                        index == selectedWindowIndex ? dropdownSelectedRowStyle : dropdownRowStyle);

                    var text = prefix + item.DisplayName;
                    var clipRect = new Rect(rowRect.x + 8f, rowRect.y, rowRect.width - 16f, rowRect.height);
                    var textWidth = dropdownLabelStyle.CalcSize(new GUIContent(text)).x;
                    var overflow = Mathf.Max(0f, textWidth - clipRect.width);
                    var offset = 0f;
                    if (index == hoveredWindowIndex && overflow > 0f)
                    {
                        var elapsed = Mathf.Max(0f, Time.unscaledTime - hoveredWindowSince - 0.55f);
                        offset = Mathf.PingPong(elapsed * 34f, overflow);
                    }
                    GUI.BeginGroup(clipRect);
                    GUI.Label(new Rect(-offset, 0f, Mathf.Max(textWidth, clipRect.width), clipRect.height),
                        text, dropdownLabelStyle);
                    GUI.EndGroup();
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
            var mousePosition = GetLogicalMousePosition();

            if (rawEventType == EventType.ScrollWheel && dropdownRect.Contains(mousePosition))
            {
                var maxScroll = Mathf.Max(0f, contentHeight - visibleHeight);
                windowDropdownScroll = Mathf.Clamp(windowDropdownScroll + Event.current.delta.y * rowHeight,
                    0f, maxScroll);
                Event.current.Use();
                return;
            }
            if (rawEventType != EventType.MouseDown || Event.current.button != 0) return;

            if (dropdownRect.Contains(mousePosition) && count > 0
                && mousePosition.x < dropdownRect.xMax - 16f)
            {
                var localY = mousePosition.y - dropdownRect.y - 8f + windowDropdownScroll;
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
            GUILayout.BeginHorizontal(GUILayout.Height(34f));
            for (var i = 0; i < panelTabNames.Length; i++)
            {
                var label = (i + 1) + "  " + panelTabNames[i];
                var style = activePanelTab == i ? activeTabStyle : tabStyle;
                if (!GUILayout.Button(label, style, GUILayout.ExpandWidth(true), GUILayout.Height(34f))) continue;
                if (activePanelTab != i) panelScrollPosition = Vector2.zero;
                activePanelTab = i;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawBasicPanel()
        {
            if (IsFlatContent()) DrawFlatPlacementControls();
            else if (IsAllSkyCircularContent()) DrawAllSkyPlacementControls();
            else
            {
                BeginSection("表示方向");
                settings.yaw = Slider("Yaw", settings.yaw, -180f, 180f, settings.yaw.ToString("0.0°"), 0f);
                settings.pitch = Slider("Pitch", settings.pitch, -180f, 180f, settings.pitch.ToString("0.0°"), 180f);
                settings.roll = Slider("Roll", settings.roll, -180f, 180f, settings.roll.ToString("0.0°"), 0f);
                settings.domeFov = FineSlider("ドーム画角", settings.domeFov, 90f, 220f, 1f, settings.domeFov.ToString("0°"), 180f);
                EndSection();
            }

        }

        private void DrawCalibrationPanel()
        {
            BeginSection("校正表示");
            GUILayout.BeginHorizontal();
            GUILayout.Label("校正表示", GUILayout.Width(82f));
            if (GUILayout.Button(gridModeNames[settings.gridMode]))
                settings.gridMode = (settings.gridMode + 1) % 3;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label("グリッド間隔", rowLabelStyle, GUILayout.Width(82f));
            if (GUILayout.Button(settings.gridStepDegrees.ToString("0°"), GUILayout.Width(54f)))
                settings.gridStepDegrees = NextGridStep(settings.gridStepDegrees);
            GUILayout.Label("押すたびに 1° / 2° / 5° / 10° / 15°", smallStyle,
                GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
            settings.gridOpacity = FineSlider("グリッド濃さ", settings.gridOpacity, 0.15f, 1f,
                0.05f, settings.gridOpacity.ToString("0.00"), 1f);
            settings.gridLineWidth = FineSlider("線の太さ", settings.gridLineWidth, 0.25f, 1.5f, 0.05f, settings.gridLineWidth.ToString("0.00"), 0.65f);
            settings.showOrientationMarkers = GUILayout.Toggle(settings.showOrientationMarkers, "方位色マーカー（上:赤 右:緑 下:青 左:黄）");
            EndSection();

            BeginSection("出力円");
            settings.centerX = FineSlider("中心 X", settings.centerX, 0.25f, 0.75f, 0.001f, settings.centerX.ToString("0.000"), 0.5f);
            settings.centerY = FineSlider("中心 Y", settings.centerY, 0.25f, 0.75f, 0.001f, settings.centerY.ToString("0.000"), 0.5f);
            settings.radius = FineSlider("円半径", settings.radius, 0.2f, 0.7f, 0.001f, settings.radius.ToString("0.000"), 0.485f);
            settings.ellipseX = FineSlider("横倍率", settings.ellipseX, 0.75f, 1.25f, 0.001f, settings.ellipseX.ToString("0.000"), 1f);
            settings.ellipseY = FineSlider("縦倍率", settings.ellipseY, 0.75f, 1.25f, 0.001f, settings.ellipseY.ToString("0.000"), 1f);
            settings.outputRotation = FineSlider("出力回転", settings.outputRotation, -180f, 180f, 0.1f, settings.outputRotation.ToString("0.0°"), 0f);
            if (GUILayout.Button("出力円をリセット")) settings.ResetOutputCircle();
            EndSection();
        }

        private bool IsFlatContent()
        {
            return settings.sourceMode == 1 || settings.fileProjectionMode == 1;
        }

        private bool IsAllSkyCircularContent()
        {
            return settings.sourceMode == 0 && settings.fileProjectionMode == 2;
        }

        private void DrawAllSkyPlacementControls()
        {
            BeginSection("全天円形素材の位置合わせ");
            GUILayout.Label("素材内の円だけを調整します。投影円の位置・大きさは「校正」タブの出力円で調整します。", smallStyle);
            settings.allSkySourceCenterX = FineSlider("素材中心 X", settings.allSkySourceCenterX,
                0f, 1f, 0.001f, settings.allSkySourceCenterX.ToString("0.000"), 0.5f);
            settings.allSkySourceCenterY = FineSlider("素材中心 Y", settings.allSkySourceCenterY,
                0f, 1f, 0.001f, settings.allSkySourceCenterY.ToString("0.000"), 0.5f);
            settings.allSkySourceRadius = FineSlider("素材円半径", settings.allSkySourceRadius,
                0.1f, 1.5f, 0.005f, settings.allSkySourceRadius.ToString("0.000"), 1f);
            settings.allSkySourceRotation = FineSlider("素材回転", settings.allSkySourceRotation,
                -180f, 180f, 0.5f, settings.allSkySourceRotation.ToString("0.0°"), 0f);
            GUILayout.Label("入力魚眼の補正（投影レンズのK1–K3とは別です）", smallStyle);
            settings.allSkySourceRadialK1 = FineSlider("入力 K1", settings.allSkySourceRadialK1,
                -0.3f, 0.3f, 0.001f, settings.allSkySourceRadialK1.ToString("0.000"), 0f);
            settings.allSkySourceRadialK2 = FineSlider("入力 K2", settings.allSkySourceRadialK2,
                -0.3f, 0.3f, 0.001f, settings.allSkySourceRadialK2.ToString("0.000"), 0f);
            if (GUILayout.Button("全天円形素材の位置をリセット")) settings.ResetAllSkyPlacement();
            EndSection();
        }

        private void DrawFlatPlacementControls()
        {
            BeginSection("平面資料の配置");
            settings.flatAzimuth = FineSlider("左右方向", settings.flatAzimuth, -180f, 180f, 1f,
                settings.flatAzimuth.ToString("0°"), 0f);
            settings.flatPolarAngle = FineSlider("中心→外周", settings.flatPolarAngle, 0f, 89f, 1f,
                settings.flatPolarAngle.ToString("0°"), 45f);
            settings.flatRoll = FineSlider("資料の回転", settings.flatRoll, -180f, 180f, 0.5f,
                settings.flatRoll.ToString("0.0°"), 180f);
            settings.flatHorizontalFov = FineSlider("横の画角", settings.flatHorizontalFov, 10f, 160f, 1f,
                settings.flatHorizontalFov.ToString("0°"), 100f);
            settings.flatHeightScale = FineSlider("縦倍率", settings.flatHeightScale, 0.5f, 2f, 0.01f,
                settings.flatHeightScale.ToString("0.00"), 1f);

            GUILayout.Label("端の切り取り", smallStyle);
            settings.flatCropLeft = FineSlider("左", settings.flatCropLeft, 0f, 0.3f, 0.002f,
                settings.flatCropLeft.ToString("0.000"), 0f);
            settings.flatCropRight = FineSlider("右", settings.flatCropRight, 0f, 0.3f, 0.002f,
                settings.flatCropRight.ToString("0.000"), 0f);
            settings.flatCropTop = FineSlider("上", settings.flatCropTop, 0f, 0.3f, 0.002f,
                settings.flatCropTop.ToString("0.000"), 0f);
            settings.flatCropBottom = FineSlider("下", settings.flatCropBottom, 0f, 0.3f, 0.002f,
                settings.flatCropBottom.ToString("0.000"), 0f);
            if (GUILayout.Button("平面資料の配置をリセット")) settings.ResetFlatPlacement();
            EndSection();
        }

        private void DrawLensPanel()
        {
            BeginSection("校正ヘルプ");
            if (GUILayout.Button(showLensCalibrationHelp
                    ? "▼  レンズ校正の進め方を閉じる"
                    : "▶  レンズ校正の進め方を表示"))
                showLensCalibrationHelp = !showLensCalibrationHelp;
            if (showLensCalibrationHelp)
            {
                GUILayout.Label("1.「校正」タブで中心・円半径・縦横倍率を実際のドーム外周へ合わせます。\n"
                    + "2. まずK1だけを少しずつ動かし、角度リングの中間～外側のずれを合わせます。\n"
                    + "3. 外周付近だけ残るずれをK2、さらに外周端だけをK3で微調整します。\n"
                    + "4. 上下左右で非対称なずれが残る場合だけP1/P2、方向別補正を使います。",
                    smallStyle);
                GUILayout.Label("一度に1項目だけ、±0.001程度ずつ動かすのがおすすめです。「既定」でその項目だけ元へ戻せます。", smallStyle);
            }
            EndSection();

            BeginSection("円対称の歪み補正");
            GUILayout.Label("K1は全体、K2は主に外側、K3は主に外周端へ強く効きます。", smallStyle);
            GUILayout.Label("＋側では角度リングが中心側へ、－側では外周側へ移動します。", smallStyle);
            settings.radialK1 = FineSlider("放射 K1", settings.radialK1, -0.3f, 0.3f, 0.001f, settings.radialK1.ToString("0.000"), 0f);
            settings.radialK2 = FineSlider("放射 K2", settings.radialK2, -0.3f, 0.3f, 0.001f, settings.radialK2.ToString("0.000"), 0f);
            settings.radialK3 = FineSlider("放射 K3", settings.radialK3, -0.3f, 0.3f, 0.001f, settings.radialK3.ToString("0.000"), 0f);
            EndSection();

            BeginSection("偏心・斜め方向の補正");
            GUILayout.Label("中心と縦横倍率を合わせてもリングが片側へ寄る場合だけ使います。P1は主に上下、P2は主に左右の偏心へ効きます。", smallStyle);
            settings.tangentialP1 = FineSlider("偏心 P1", settings.tangentialP1, -0.15f, 0.15f, 0.001f, settings.tangentialP1.ToString("0.000"), 0f);
            settings.tangentialP2 = FineSlider("偏心 P2", settings.tangentialP2, -0.15f, 0.15f, 0.001f, settings.tangentialP2.ToString("0.000"), 0f);
            EndSection();

            BeginSection("方向別補正（レンズ2枚重ね・偏心配置向け）");
            GUILayout.Label("各方向だけリング間隔がずれる場合に調整します。正値でその方向の画角を広げます。", smallStyle);
            settings.warpTop = FineSlider("上方向", settings.warpTop, -0.3f, 0.3f, 0.001f, settings.warpTop.ToString("0.000"), 0f);
            settings.warpBottom = FineSlider("下方向", settings.warpBottom, -0.3f, 0.3f, 0.001f, settings.warpBottom.ToString("0.000"), 0f);
            settings.warpLeft = FineSlider("左方向", settings.warpLeft, -0.3f, 0.3f, 0.001f, settings.warpLeft.ToString("0.000"), 0f);
            settings.warpRight = FineSlider("右方向", settings.warpRight, -0.3f, 0.3f, 0.001f, settings.warpRight.ToString("0.000"), 0f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("歪み補正をすべて0へ")) settings.ResetLensCalibration();
            if (GUILayout.Button("出力円も含め全て初期値へ"))
            {
                settings.domeFov = 180f;
                settings.ResetOutputCircle();
                settings.ResetLensCalibration();
            }
            GUILayout.EndHorizontal();
            EndSection();

            DrawLensProfileControls();
        }

        private void DrawLensProfileControls()
        {
            BeginSection("レンズ設定の保存／読込");
            GUILayout.Label("出力円・ドーム画角・K1～K3・P1/P2・方向別補正をまとめて保存します。素材の向きや再生設定は変わりません。", smallStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(58f));
            GUI.SetNextControlName(LensProfileInputName);
            lensProfileName = GUILayout.TextField(lensProfileName ?? string.Empty, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("現在値を保存", GUILayout.Width(100f))) SaveLensProfile();
            GUILayout.EndHorizontal();
            GUILayout.Label("同じ名前で保存すると、そのプロファイルを更新します。日本語名も使用できます。", smallStyle);

            if (lensProfileNames.Length == 0)
            {
                GUILayout.Label("保存済みプロファイル: なし", smallStyle);
                EndSection();
                return;
            }

            selectedLensProfileIndex = Mathf.Clamp(selectedLensProfileIndex, 0, lensProfileNames.Length - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(34f))) SelectLensProfile(selectedLensProfileIndex - 1);
            GUILayout.Box(TruncateLabel(lensProfileNames[selectedLensProfileIndex], 28), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(34f))) SelectLensProfile(selectedLensProfileIndex + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("選択した設定を読込")) LoadSelectedLensProfile();
            if (GUILayout.Button("一覧を更新", GUILayout.Width(88f))) RefreshLensProfiles();
            GUILayout.EndHorizontal();

            var selectedPath = lensProfilePaths[selectedLensProfileIndex];
            if (string.Equals(pendingLensProfileDeletePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Label("「" + TruncateLabel(pendingLensProfileDeleteName, 24)
                    + "」を削除しますか？ 現在の画面上の調整値は変わりません。", smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("削除を確定")) DeletePendingLensProfile();
                if (GUILayout.Button("キャンセル")) CancelLensProfileDelete();
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("選択した設定を削除"))
            {
                pendingLensProfileDeletePath = selectedPath;
                pendingLensProfileDeleteName = lensProfileNames[selectedLensProfileIndex];
            }
            EndSection();
        }

        private void SelectLensProfile(int index)
        {
            if (lensProfileNames.Length == 0)
            {
                selectedLensProfileIndex = -1;
                return;
            }

            selectedLensProfileIndex = (index % lensProfileNames.Length + lensProfileNames.Length)
                                       % lensProfileNames.Length;
            lensProfileName = lensProfileNames[selectedLensProfileIndex];
            CancelLensProfileDelete();
        }

        private void RefreshLensProfiles(string selectPath = null)
        {
            var previousPath = selectPath;
            if (string.IsNullOrWhiteSpace(previousPath)
                && selectedLensProfileIndex >= 0
                && selectedLensProfileIndex < lensProfilePaths.Length)
                previousPath = lensProfilePaths[selectedLensProfileIndex];

            var paths = new List<string>();
            var names = new List<string>();
            try
            {
                if (Directory.Exists(LensProfileDirectory))
                {
                    var files = Directory.GetFiles(LensProfileDirectory, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var path in files)
                    {
                        try
                        {
                            var profile = JsonUtility.FromJson<LensCalibrationProfile>(File.ReadAllText(path));
                            if (profile == null) continue;
                            paths.Add(path);
                            names.Add(string.IsNullOrWhiteSpace(profile.displayName)
                                ? Path.GetFileNameWithoutExtension(path)
                                : profile.displayName.Trim());
                        }
                        catch (Exception exception)
                        {
                            UnityEngine.Debug.LogWarning("Lens profile could not be read: " + exception.Message);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                status = "レンズ設定一覧の読込エラー: " + exception.Message;
            }

            lensProfilePaths = paths.ToArray();
            lensProfileNames = names.ToArray();
            // Profile files use GUIDs so Japanese and Windows-reserved names
            // are always safe on disk. Present them alphabetically by their
            // human-readable names instead of that internal filename order.
            for (var i = 0; i < lensProfileNames.Length; i++)
            {
                for (var j = i + 1; j < lensProfileNames.Length; j++)
                {
                    if (string.Compare(lensProfileNames[i], lensProfileNames[j],
                            StringComparison.CurrentCultureIgnoreCase) <= 0) continue;
                    (lensProfileNames[i], lensProfileNames[j]) = (lensProfileNames[j], lensProfileNames[i]);
                    (lensProfilePaths[i], lensProfilePaths[j]) = (lensProfilePaths[j], lensProfilePaths[i]);
                }
            }
            selectedLensProfileIndex = lensProfilePaths.Length == 0 ? -1 : 0;
            if (!string.IsNullOrWhiteSpace(previousPath))
            {
                for (var i = 0; i < lensProfilePaths.Length; i++)
                {
                    if (!string.Equals(lensProfilePaths[i], previousPath, StringComparison.OrdinalIgnoreCase)) continue;
                    selectedLensProfileIndex = i;
                    break;
                }
            }

            if (selectedLensProfileIndex >= 0 && string.IsNullOrWhiteSpace(lensProfileName))
                lensProfileName = lensProfileNames[selectedLensProfileIndex];
            if (!string.IsNullOrWhiteSpace(pendingLensProfileDeletePath)
                && Array.IndexOf(lensProfilePaths, pendingLensProfileDeletePath) < 0)
                CancelLensProfileDelete();
        }

        private void SaveLensProfile()
        {
            var requestedName = (lensProfileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(requestedName))
            {
                status = "レンズ設定の保存名を入力してください";
                return;
            }

            if (requestedName.Length > 80) requestedName = requestedName.Substring(0, 80);
            try
            {
                Directory.CreateDirectory(LensProfileDirectory);
                var savePath = string.Empty;
                for (var i = 0; i < lensProfileNames.Length; i++)
                {
                    if (!string.Equals(lensProfileNames[i], requestedName, StringComparison.OrdinalIgnoreCase)) continue;
                    savePath = lensProfilePaths[i];
                    break;
                }

                if (string.IsNullOrWhiteSpace(savePath))
                    savePath = Path.Combine(LensProfileDirectory, Guid.NewGuid().ToString("N") + ".json");

                var profile = LensCalibrationProfile.Capture(requestedName, settings);
                File.WriteAllText(savePath, JsonUtility.ToJson(profile, true));
                lensProfileName = requestedName;
                RefreshLensProfiles(savePath);
                status = "レンズ設定を保存しました: " + requestedName;
            }
            catch (Exception exception)
            {
                status = "レンズ設定の保存エラー: " + exception.Message;
            }
        }

        private void LoadSelectedLensProfile()
        {
            if (selectedLensProfileIndex < 0 || selectedLensProfileIndex >= lensProfilePaths.Length)
            {
                status = "読込むレンズ設定を選択してください";
                return;
            }

            try
            {
                var profile = JsonUtility.FromJson<LensCalibrationProfile>(
                    File.ReadAllText(lensProfilePaths[selectedLensProfileIndex]));
                if (profile == null) throw new InvalidDataException("JSONの内容が空です。");
                profile.ApplyTo(settings);
                lensProfileName = lensProfileNames[selectedLensProfileIndex];
                SaveSettings();
                status = "レンズ設定を読込みました: " + lensProfileName;
            }
            catch (Exception exception)
            {
                status = "レンズ設定の読込エラー: " + exception.Message;
            }
        }

        private void DeletePendingLensProfile()
        {
            var deletePath = pendingLensProfileDeletePath;
            var deleteName = pendingLensProfileDeleteName;
            if (string.IsNullOrWhiteSpace(deletePath)) return;

            try
            {
                if (File.Exists(deletePath)) File.Delete(deletePath);
                CancelLensProfileDelete();
                lensProfileName = string.Empty;
                RefreshLensProfiles();
                status = "レンズ設定を削除しました: " + deleteName;
            }
            catch (Exception exception)
            {
                CancelLensProfileDelete();
                status = "レンズ設定の削除エラー: " + exception.Message;
            }
        }

        private void CancelLensProfileDelete()
        {
            pendingLensProfileDeletePath = string.Empty;
            pendingLensProfileDeleteName = string.Empty;
        }

        private void InitializeConfigurationStore()
        {
            var fallback = Path.Combine(
                Path.GetDirectoryName(SettingsPath) ?? Application.persistentDataPath,
                "DomeProjectionConfig");
            var preferred = GetPreferredConfigurationDirectory();
            try
            {
                configurationStore = new DomeConfigurationStore(preferred);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("External config directory is not writable: " + exception.Message);
                configurationStore = new DomeConfigurationStore(fallback);
            }

            MigrateLegacyLensProfiles();
        }

        private static string GetPreferredConfigurationDirectory()
        {
#if UNITY_EDITOR
            var baseDirectory = Directory.GetParent(Application.dataPath)?.FullName
                                ?? Application.persistentDataPath;
#elif UNITY_STANDALONE_OSX
            // Unity's dataPath is inside App.app/Contents/Resources/Data. Walk
            // out of the bundle and store JSON beside the .app bundles, never
            // inside the signed/read-only application.
            var bundle = new DirectoryInfo(Application.dataPath);
            while (bundle != null && !bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                bundle = bundle.Parent;
            var baseDirectory = bundle?.Parent?.FullName ?? Application.persistentDataPath;
#elif UNITY_STANDALONE_WIN
            var executable = Process.GetCurrentProcess().MainModule?.FileName;
            var executableDirectory = new DirectoryInfo(
                Path.GetDirectoryName(executable) ?? Application.persistentDataPath);
            // Control and Output live in sibling directories in the Windows
            // package, so keep one shared configuration in their parent.
            var baseDirectory = string.Equals(executableDirectory.Name, "Control", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(executableDirectory.Name, "Output", StringComparison.OrdinalIgnoreCase)
                ? executableDirectory.Parent?.FullName ?? executableDirectory.FullName
                : executableDirectory.FullName;
#else
            var baseDirectory = Application.persistentDataPath;
#endif
            return Path.Combine(baseDirectory, "DomeProjectionConfig");
        }

        private void MigrateLegacyLensProfiles()
        {
            if (configurationStore == null
                || string.Equals(LegacyLensProfileDirectory, configurationStore.LensDirectoryPath,
                    StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(LegacyLensProfileDirectory)) return;

            try
            {
                foreach (var sourcePath in Directory.GetFiles(LegacyLensProfileDirectory, "*.json"))
                {
                    var destination = Path.Combine(configurationStore.LensDirectoryPath,
                        Path.GetFileName(sourcePath));
                    if (!File.Exists(destination)) File.Copy(sourcePath, destination, false);
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Legacy lens profiles could not be copied: " + exception.Message);
            }
        }

        private void ReloadContentConfiguration(bool announce)
        {
            if (configurationStore == null) return;
            try
            {
                materialCatalog = configurationStore.LoadMaterials();
                playlistCatalog = configurationStore.LoadPlaylists();
                selectedMaterialIndex = materialCatalog.materials.Count == 0
                    ? -1
                    : Mathf.Clamp(selectedMaterialIndex, 0, materialCatalog.materials.Count - 1);
                selectedPlaylistIndex = playlistCatalog.playlists.Count == 0
                    ? -1
                    : Mathf.Clamp(selectedPlaylistIndex, 0, playlistCatalog.playlists.Count - 1);
                materialNameField = selectedMaterialIndex >= 0
                    ? materialCatalog.materials[selectedMaterialIndex].name
                    : string.Empty;
                playlistNameField = selectedPlaylistIndex >= 0
                    ? playlistCatalog.playlists[selectedPlaylistIndex].name
                    : string.Empty;
                selectedPlaylistEntryIndex = -1;
                activePlaylistIndex = -1;
                activePlaylistEntryIndex = -1;
                activePlaylistCompleted = false;
                pendingContentDeleteKind = string.Empty;
                pendingContentDeleteId = string.Empty;
                RefreshLensProfiles();
                if (announce) status = "JSON設定を再読込しました";
            }
            catch (Exception exception)
            {
                status = "JSON設定の読込エラー: " + exception.Message;
            }
        }

        private void OpenConfigurationDirectory()
        {
            if (configurationStore == null) return;
            try
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + configurationStore.DirectoryPath + "\"",
                    UseShellExecute = true
                })?.Dispose();
#else
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add(configurationStore.DirectoryPath);
                Process.Start(startInfo)?.Dispose();
#endif
                status = "設定フォルダを開きました";
            }
            catch (Exception exception)
            {
                status = "設定フォルダを開けませんでした: " + exception.Message;
            }
        }

        private void DrawContentLibraryPanel()
        {
            GUILayout.Label("JSON設定フォルダ");
            GUILayout.Label(configurationStore?.DirectoryPath ?? "利用できません", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("設定フォルダを開く")) OpenConfigurationDirectory();
            if (GUILayout.Button("JSONを再読込")) ReloadContentConfiguration(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("起動時自動読込・相対パス基準：このフォルダ", smallStyle);

            GUILayout.Space(10f);
            DrawMaterialLibraryControls();
            GUILayout.Space(6f);
            DrawAddMaterialToPlaylistControl();
            GUILayout.Space(6f);
            DrawPlaylistControls();
        }

        private void DrawAddMaterialToPlaylistControl()
        {
            var canAdd = selectedMaterialIndex >= 0
                         && selectedMaterialIndex < materialCatalog.materials.Count
                         && selectedPlaylistIndex >= 0
                         && selectedPlaylistIndex < playlistCatalog.playlists.Count;
            GUILayout.BeginVertical(sectionStyle);
            GUI.enabled = canAdd;
            var destination = canAdd
                ? playlistCatalog.playlists[selectedPlaylistIndex].name
                : "プレイリスト未選択";
            if (GUILayout.Button("↓  選択素材を「" + TruncateLabel(destination, 22) + "」の末尾へ追加",
                    GUILayout.Height(32f)))
                AddSelectedMaterialToPlaylist();
            GUI.enabled = true;
            GUILayout.EndVertical();
        }

        private void DrawMaterialLibraryControls()
        {
            BeginSection("素材リスト（materials.json）");
            GUI.SetNextControlName(MaterialNameInputName);
            materialNameField = GUILayout.TextField(materialNameField ?? string.Empty);
            GUILayout.Label("名前を入力し、現在選択中のファイルと表示設定を登録します。", smallStyle);
            if (GUILayout.Button("現在の素材を新規追加")) AddCurrentMaterial();

            if (materialCatalog.materials.Count == 0)
            {
                GUILayout.Label("登録素材: なし", smallStyle);
                EndSection();
                return;
            }

            selectedMaterialIndex = Mathf.Clamp(selectedMaterialIndex, 0, materialCatalog.materials.Count - 1);
            GUILayout.Label("登録素材を選択", smallStyle);
            var materialClicked = -1;
            for (var i = 0; i < materialCatalog.materials.Count; i++)
            {
                var item = materialCatalog.materials[i];
                var label = (i + 1) + ". " + TruncateLabel(item.name, 38);
                if (GUILayout.Button(label, i == selectedMaterialIndex ? selectedListButtonStyle : dropdownButtonStyle,
                        GUILayout.Height(26f)))
                    materialClicked = i;
            }
            if (materialClicked >= 0) SelectMaterial(materialClicked);

            var selected = materialCatalog.materials[selectedMaterialIndex];
            GUILayout.Label(TruncateLabel(selected.path, 58), smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("選択素材を表示")) LoadMaterial(selected, false);
            if (GUILayout.Button("現在値で更新")) UpdateSelectedMaterial();
            GUILayout.EndHorizontal();

            if (pendingContentDeleteKind == "material" && pendingContentDeleteId == selected.id)
            {
                GUILayout.Label("この素材と、プレイリスト内の参照を削除しますか？", smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("削除を確定")) DeleteSelectedMaterial();
                if (GUILayout.Button("キャンセル")) CancelContentDelete();
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("選択素材をリストから削除"))
            {
                pendingContentDeleteKind = "material";
                pendingContentDeleteId = selected.id;
            }
            EndSection();
        }

        private void DrawPlaylistControls()
        {
            BeginSection("プレイリスト（playlists.json）");
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(PlaylistNameInputName);
            playlistNameField = GUILayout.TextField(playlistNameField ?? string.Empty, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("新規作成", GUILayout.Width(82f))) AddPlaylist();
            GUILayout.EndHorizontal();

            if (playlistCatalog.playlists.Count == 0)
            {
                GUILayout.Label("プレイリスト: なし", smallStyle);
                EndSection();
                return;
            }

            selectedPlaylistIndex = Mathf.Clamp(selectedPlaylistIndex, 0, playlistCatalog.playlists.Count - 1);
            GUILayout.Label("プレイリストを選択", smallStyle);
            var playlistClicked = -1;
            var playlistRenameClicked = -1;
            var playlistDeleteClicked = -1;
            for (var i = 0; i < playlistCatalog.playlists.Count; i++)
            {
                var item = playlistCatalog.playlists[i];
                var label = (i + 1) + ". " + TruncateLabel(item.name, 38);
                GUILayout.BeginHorizontal(GUILayout.Height(26f));
                if (GUILayout.Button(label, i == selectedPlaylistIndex ? selectedListButtonStyle : dropdownButtonStyle,
                        GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                    playlistClicked = i;
                if (GUILayout.Button(new GUIContent("✎", "上の名前欄の内容で更新"),
                        GUILayout.Width(30f), GUILayout.Height(26f)))
                    playlistRenameClicked = i;
                if (GUILayout.Button(new GUIContent("✕", "このプレイリストを削除"),
                        GUILayout.Width(30f), GUILayout.Height(26f)))
                    playlistDeleteClicked = i;
                GUILayout.EndHorizontal();
            }
            if (playlistClicked >= 0) SelectPlaylist(playlistClicked);
            if (playlistRenameClicked >= 0)
            {
                if (playlistRenameClicked != selectedPlaylistIndex)
                {
                    SelectPlaylist(playlistRenameClicked);
                    status = "上の名前欄を編集し、もう一度✎を押してください";
                }
                else
                {
                    RenameSelectedPlaylist();
                }
            }
            if (playlistDeleteClicked >= 0)
            {
                if (playlistDeleteClicked != selectedPlaylistIndex) SelectPlaylist(playlistDeleteClicked);
                pendingContentDeleteKind = "playlist";
                pendingContentDeleteId = playlistCatalog.playlists[selectedPlaylistIndex].id;
            }

            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            GUILayout.Label("素材間のつなぎ", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("カット", playlist.transitionMode == 0 ? activeTabStyle : tabStyle))
            {
                playlist.transitionMode = 0;
                ResetPlaylistTransition();
                SavePlaylistCatalog();
            }
            if (GUILayout.Button("黒フェード", playlist.transitionMode == 1 ? activeTabStyle : tabStyle))
            {
                playlist.transitionMode = 1;
                ResetPlaylistTransition();
                SavePlaylistCatalog();
            }
            GUILayout.EndHorizontal();
            if (playlist.transitionMode == 1)
            {
                var duration = FineSlider("フェード時間", playlist.transitionSeconds,
                    0.2f, 3f, 0.1f, playlist.transitionSeconds.ToString("F1") + "秒", 0.8f);
                if (Mathf.Abs(duration - playlist.transitionSeconds) > 0.001f)
                {
                    playlist.transitionSeconds = duration;
                    SavePlaylistCatalog();
                }
            }

            if (playlist.materialIds.Count == 0)
            {
                GUILayout.Label("再生項目: なし", smallStyle);
            }
            else
            {
                selectedPlaylistEntryIndex = Mathf.Clamp(selectedPlaylistEntryIndex, 0,
                    playlist.materialIds.Count - 1);
                GUILayout.Label("プレイリストの中身", smallStyle);
                GUILayout.Label("緑：再生中　青：選択中", smallStyle);
                var entryClicked = -1;
                var entryMoveIndex = -1;
                var entryMoveDirection = 0;
                var entryRemoveIndex = -1;
                for (var i = 0; i < playlist.materialIds.Count; i++)
                {
                    var entryMaterial = FindMaterial(playlist.materialIds[i]);
                    var isActiveEntry = activePlaylistIndex == selectedPlaylistIndex
                                        && activePlaylistEntryIndex == i;
                    var entryLabel = (isActiveEntry ? "▶ " : string.Empty) + (i + 1) + ". "
                        + TruncateLabel(entryMaterial?.name ?? "⚠ 素材IDが見つかりません", 36);
                    var isSelectedEntry = i == selectedPlaylistEntryIndex;
                    GUILayout.BeginHorizontal(GUILayout.Height(26f));
                    if (GUILayout.Button(entryLabel,
                            isActiveEntry ? playingListButtonStyle
                                : isSelectedEntry ? selectedListButtonStyle : dropdownButtonStyle,
                            GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                        entryClicked = i;
                    GUI.enabled = i > 0;
                    if (GUILayout.Button(new GUIContent("▲", "上へ移動"),
                            GUILayout.Width(28f), GUILayout.Height(26f)))
                    {
                        entryMoveIndex = i;
                        entryMoveDirection = -1;
                    }
                    GUI.enabled = i < playlist.materialIds.Count - 1;
                    if (GUILayout.Button(new GUIContent("▼", "下へ移動"),
                            GUILayout.Width(28f), GUILayout.Height(26f)))
                    {
                        entryMoveIndex = i;
                        entryMoveDirection = 1;
                    }
                    GUI.enabled = true;
                    if (GUILayout.Button(new GUIContent("✕", "プレイリストから外す"),
                            GUILayout.Width(28f), GUILayout.Height(26f)))
                        entryRemoveIndex = i;
                    GUILayout.EndHorizontal();
                }
                if (entryClicked >= 0) selectedPlaylistEntryIndex = entryClicked;
                if (entryMoveIndex >= 0)
                {
                    selectedPlaylistEntryIndex = entryMoveIndex;
                    MovePlaylistEntry(entryMoveDirection);
                }
                if (entryRemoveIndex >= 0)
                {
                    selectedPlaylistEntryIndex = entryRemoveIndex;
                    RemovePlaylistEntry();
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("|◀ 前へ", "前の素材")))
                    AdvanceActivePlaylist(-1);
                if (GUILayout.Button(new GUIContent("▶ 再生", "選択項目から再生")))
                    StartSelectedPlaylist();
                if (GUILayout.Button(new GUIContent("次へ ▶|", "次の素材")))
                    AdvanceActivePlaylist(1);
                GUILayout.EndHorizontal();
            }

            if (activePlaylistIndex >= 0 && activePlaylistIndex < playlistCatalog.playlists.Count)
            {
                var active = playlistCatalog.playlists[activePlaylistIndex];
                GUILayout.Label((activePlaylistCompleted ? "再生完了: " : "再生中: ")
                    + active.name + "  " + (activePlaylistEntryIndex + 1)
                    + " / " + active.materialIds.Count, smallStyle);
            }

            if (pendingContentDeleteKind == "playlist" && pendingContentDeleteId == playlist.id)
            {
                GUILayout.Label("このプレイリストを削除しますか？ 素材自体は残ります。", smallStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("削除を確定")) DeleteSelectedPlaylist();
                if (GUILayout.Button("キャンセル")) CancelContentDelete();
                GUILayout.EndHorizontal();
            }
            EndSection();
        }

        private void DrawPlaylistSeekBar(DomePlaylistDefinition playlist, int playlistIndex, bool large)
        {
            var count = playlist.materialIds.Count;
            if (count == 0) return;

            GUILayout.Label("プレイリスト再生位置（クリック・ドラッグで移動）", smallStyle);
            var outerHeight = large ? 34f : 28f;
            var outer = GUILayoutUtility.GetRect(1f, outerHeight, GUILayout.ExpandWidth(true));
            var barHeight = large ? 16f : 12f;
            var bar = new Rect(outer.x + 2f, outer.center.y - barHeight * 0.5f,
                outer.width - 4f, barHeight);
            GUI.DrawTexture(bar, chapterPendingTexture, ScaleMode.StretchToFill);
            var totalWeight = 0f;
            var weights = new float[count];
            var knownDurationTotal = 0f;
            var knownDurationCount = 0;
            for (var i = 0; i < count; i++)
            {
                var material = FindMaterial(playlist.materialIds[i]);
                if (material == null || material.durationSeconds <= 0f) continue;
                knownDurationTotal += material.durationSeconds;
                knownDurationCount++;
            }
            var fallbackDuration = knownDurationCount > 0
                ? knownDurationTotal / knownDurationCount
                : 1f;
            for (var i = 0; i < count; i++)
            {
                var material = FindMaterial(playlist.materialIds[i]);
                weights[i] = material != null && material.durationSeconds > 0f
                    ? material.durationSeconds
                    : fallbackDuration;
                totalWeight += weights[i];
            }

            var activeThisPlaylist = activePlaylistIndex == playlistIndex;
            var x = bar.x;
            var thumbX = -1f;
            for (var i = 0; i < count; i++)
            {
                var width = i == count - 1
                    ? bar.xMax - x
                    : bar.width * weights[i] / Mathf.Max(totalWeight, 0.001f);
                var segment = new Rect(x, bar.y, Mathf.Max(1f, width), bar.height);
                var isActive = activeThisPlaylist && activePlaylistEntryIndex == i;
                var isComplete = activeThisPlaylist && i < activePlaylistEntryIndex;
                if (isComplete) GUI.DrawTexture(segment, chapterCompleteTexture, ScaleMode.StretchToFill);

                if (isActive)
                {
                    var progress = 1f;
                    if (video != null && video.IsPrepared && !video.IsStillImage && video.Duration > 0d)
                        progress = Mathf.Clamp01((float)(video.Time / video.Duration));
                    GUI.DrawTexture(new Rect(segment.x, segment.y,
                            Mathf.Max(2f, segment.width * progress), segment.height),
                        chapterCurrentTexture, ScaleMode.StretchToFill);
                    thumbX = Mathf.Lerp(segment.x, segment.xMax, progress);
                }

                // A single one-pixel chapter boundary keeps the bar readable
                // without the previous thick dark gutter on every segment.
                if (i > 0)
                    GUI.DrawTexture(new Rect(segment.x - 0.5f, bar.y, 1f, bar.height),
                        panelTexture, ScaleMode.StretchToFill);
                if (segment.width >= 18f) GUI.Label(segment, (i + 1).ToString(), chapterLabelStyle);
                x += width;
            }

            var draggingThisPlaylist = playlistSeekDragging
                                       && playlistSeekDraggingPlaylistIndex == playlistIndex;
            if (draggingThisPlaylist && playlistSeekPreviewEntryIndex >= 0)
                thumbX = PlaylistPositionToX(bar, weights, totalWeight,
                    playlistSeekPreviewEntryIndex, playlistSeekPreviewFraction);

            if (thumbX >= 0f)
            {
                var thumbWidth = large ? 12f : 10f;
                var thumbHeight = large ? 24f : 20f;
                var thumb = new Rect(thumbX - thumbWidth * 0.5f,
                    bar.center.y - thumbHeight * 0.5f + 1.5f, thumbWidth, thumbHeight);
                GUI.Box(thumb, GUIContent.none, sliderThumbStyle);
            }

            HandlePlaylistSeekInput(playlistIndex, bar, weights, totalWeight);

            if (!activeThisPlaylist || activePlaylistEntryIndex < 0 || activePlaylistEntryIndex >= count)
            {
                GUILayout.Label("未再生", smallStyle);
                return;
            }

            var currentMaterial = FindMaterial(playlist.materialIds[activePlaylistEntryIndex]);
            var time = video != null && video.IsPrepared && !video.IsStillImage
                ? "  " + FormatTime(video.Time) + " / " + FormatTime(video.Duration)
                : string.Empty;
            GUILayout.Label((activePlaylistCompleted ? "再生完了 " : "再生中 ")
                + (activePlaylistEntryIndex + 1) + " / " + count + "  "
                + TruncateLabel(currentMaterial?.name ?? "素材不明", large ? 76 : 34) + time,
                smallStyle, GUILayout.Height(20f));
        }

        private void HandlePlaylistSeekInput(int playlistIndex, Rect bar,
            float[] weights, float totalWeight)
        {
            var current = Event.current;
            var controlId = GUIUtility.GetControlID(0x51A7 + playlistIndex, FocusType.Passive, bar);
            var isThisControl = playlistSeekDragging
                                && playlistSeekDraggingPlaylistIndex == playlistIndex
                                && GUIUtility.hotControl == controlId;

            if (!playlistSeekDragging && current.type == EventType.MouseDown && current.button == 0
                && bar.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = controlId;
                playlistSeekDragging = true;
                playlistSeekDraggingPlaylistIndex = playlistIndex;
                resumePlaylistAfterSeek = activePlaylistIndex == playlistIndex
                    ? mediaShouldPlayWhenVisible
                    : true;
                mediaShouldPlayWhenVisible = false;
                if (video != null && video.IsPlaying) video.Pause();
                nextPlaylistSeekPreviewTime = 0f;
                lastPlaylistSeekPreviewEntryIndex = -1;
                lastPlaylistSeekPreviewFraction = -1f;
                ResolvePlaylistPosition(bar, weights, totalWeight, current.mousePosition.x,
                    out var entryIndex, out var fraction);
                UpdatePlaylistSeekTarget(playlistIndex, entryIndex, fraction, false);
                current.Use();
                return;
            }

            if (!isThisControl) return;
            if (current.type == EventType.MouseDrag)
            {
                ResolvePlaylistPosition(bar, weights, totalWeight, current.mousePosition.x,
                    out var entryIndex, out var fraction);
                UpdatePlaylistSeekTarget(playlistIndex, entryIndex, fraction, false);
                current.Use();
                return;
            }

            if (current.rawType != EventType.MouseUp) return;
            ResolvePlaylistPosition(bar, weights, totalWeight, current.mousePosition.x,
                out var finalEntryIndex, out var finalFraction);
            UpdatePlaylistSeekTarget(playlistIndex, finalEntryIndex, finalFraction, true);
            playlistSeekDragging = false;
            playlistSeekDraggingPlaylistIndex = -1;
            playlistSeekPreviewEntryIndex = -1;
            GUIUtility.hotControl = 0;
            status = "プレイリストの再生位置を移動しました";
            current.Use();
        }

        private static void ResolvePlaylistPosition(Rect bar, float[] weights, float totalWeight, float mouseX,
            out int entryIndex, out float fraction)
        {
            var normalizedX = Mathf.Clamp(mouseX, bar.x, bar.xMax);
            var x = bar.x;
            entryIndex = Mathf.Max(0, weights.Length - 1);
            fraction = 1f;
            for (var i = 0; i < weights.Length; i++)
            {
                var width = i == weights.Length - 1
                    ? bar.xMax - x
                    : bar.width * weights[i] / Mathf.Max(totalWeight, 0.001f);
                if (normalizedX <= x + width || i == weights.Length - 1)
                {
                    entryIndex = i;
                    fraction = Mathf.Clamp01(Mathf.InverseLerp(x, x + width, normalizedX));
                    return;
                }
                x += width;
            }
        }

        private static float PlaylistPositionToX(Rect bar, float[] weights, float totalWeight,
            int entryIndex, float fraction)
        {
            var x = bar.x;
            for (var i = 0; i < weights.Length; i++)
            {
                var width = i == weights.Length - 1
                    ? bar.xMax - x
                    : bar.width * weights[i] / Mathf.Max(totalWeight, 0.001f);
                if (i == entryIndex) return Mathf.Lerp(x, x + width, Mathf.Clamp01(fraction));
                x += width;
            }
            return bar.xMax;
        }

        private double PlaylistSeekPreviewTime()
        {
            if (playlistSeekDragging && activePlaylistIndex == playlistSeekDraggingPlaylistIndex
                && activePlaylistEntryIndex == playlistSeekPreviewEntryIndex
                && video != null && video.IsPrepared && !video.IsStillImage && video.Duration > 0d)
                return video.Duration * Mathf.Clamp01(playlistSeekPreviewFraction);
            return video != null ? video.Time : 0d;
        }

        private void UpdatePlaylistSeekTarget(int playlistIndex, int entryIndex, float fraction, bool final)
        {
            if (playlistIndex < 0 || playlistIndex >= playlistCatalog.playlists.Count) return;
            var playlist = playlistCatalog.playlists[playlistIndex];
            if (entryIndex < 0 || entryIndex >= playlist.materialIds.Count) return;

            fraction = Mathf.Clamp01(fraction);
            selectedPlaylistIndex = playlistIndex;
            selectedPlaylistEntryIndex = entryIndex;
            activePlaylistCompleted = false;
            playlistNameField = playlist.name;
            settings.showIdleScreen = false;
            playlistSeekPreviewEntryIndex = entryIndex;
            playlistSeekPreviewFraction = fraction;
            var materialId = playlist.materialIds[entryIndex];

            if (activePlaylistIndex != playlistIndex || activePlaylistEntryIndex != entryIndex)
            {
                activePlaylistIndex = playlistIndex;
                activePlaylistEntryIndex = entryIndex;
                pendingPlaylistSeekFraction = fraction;
                pendingPlaylistSeekMaterialId = materialId;
                pendingPlaylistSeekShouldPlay = final && resumePlaylistAfterSeek;
                LoadActivePlaylistEntry();
                // OpenMedia normally starts a chosen material. During a drag,
                // however, playback stays paused until pointer release.
                mediaShouldPlayWhenVisible = final && resumePlaylistAfterSeek;
                return;
            }

            if (video == null || !video.IsPrepared)
            {
                pendingPlaylistSeekFraction = fraction;
                pendingPlaylistSeekMaterialId = materialId;
                pendingPlaylistSeekShouldPlay = final && resumePlaylistAfterSeek;
                mediaShouldPlayWhenVisible = final && resumePlaylistAfterSeek;
                return;
            }

            pendingPlaylistSeekFraction = -1f;
            pendingPlaylistSeekMaterialId = string.Empty;
            if (video.IsStillImage || video.Duration <= 0d)
            {
                mediaShouldPlayWhenVisible = false;
                return;
            }

            var target = video.Duration * fraction;
            if (final)
            {
                mediaShouldPlayWhenVisible = resumePlaylistAfterSeek;
                video.Seek(target, resumePlaylistAfterSeek);
                ApplyControlMediaPlaybackState();
                return;
            }

            mediaShouldPlayWhenVisible = false;
            if (Time.unscaledTime < nextPlaylistSeekPreviewTime
                && lastPlaylistSeekPreviewEntryIndex == entryIndex
                && Mathf.Abs(lastPlaylistSeekPreviewFraction - fraction) < 0.002f) return;
            nextPlaylistSeekPreviewTime = Time.unscaledTime + SeekPreviewInterval;
            lastPlaylistSeekPreviewEntryIndex = entryIndex;
            lastPlaylistSeekPreviewFraction = fraction;
            video.SeekPreview(target);
        }

        private void AddCurrentMaterial()
        {
            var mediaPath = (pathField ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                status = "登録する動画・画像を先に選択してください";
                return;
            }

            var name = (materialNameField ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(mediaPath);
            var item = DomeMaterialDefinition.Capture(string.Empty, name,
                configurationStore.MakePortableMediaPath(mediaPath), settings, CurrentMediaDuration(mediaPath));
            materialCatalog.materials.Add(item);
            selectedMaterialIndex = materialCatalog.materials.Count - 1;
            materialNameField = item.name;
            SaveMaterialCatalog();
            status = "素材リストへ追加しました: " + item.name;
        }

        private void UpdateSelectedMaterial()
        {
            if (selectedMaterialIndex < 0 || selectedMaterialIndex >= materialCatalog.materials.Count) return;
            var previous = materialCatalog.materials[selectedMaterialIndex];
            var mediaPath = (pathField ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(mediaPath))
            {
                status = "更新する動画・画像を先に選択してください";
                return;
            }

            var name = (materialNameField ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) name = previous.name;
            materialCatalog.materials[selectedMaterialIndex] = DomeMaterialDefinition.Capture(
                previous.id, name, configurationStore.MakePortableMediaPath(mediaPath), settings,
                CurrentMediaDuration(mediaPath));
            materialNameField = name;
            SaveMaterialCatalog();
            status = "素材設定を更新しました: " + name;
        }

        private float CurrentMediaDuration(string path)
        {
            return video != null && video.IsPrepared && !video.IsStillImage
                && MediaPathsEqual(path, video.SourcePath)
                ? Mathf.Max(0f, (float)video.Duration)
                : 0f;
        }

        private void SelectMaterial(int index)
        {
            if (materialCatalog.materials.Count == 0)
            {
                selectedMaterialIndex = -1;
                return;
            }

            selectedMaterialIndex = (index % materialCatalog.materials.Count + materialCatalog.materials.Count)
                                    % materialCatalog.materials.Count;
            materialNameField = materialCatalog.materials[selectedMaterialIndex].name;
            CancelContentDelete();
        }

        private void LoadMaterial(DomeMaterialDefinition item, bool keepActivePlaylist)
        {
            if (item == null) return;
            if (!keepActivePlaylist)
            {
                activePlaylistIndex = -1;
                activePlaylistEntryIndex = -1;
                activePlaylistCompleted = false;
                pendingPlaylistSeekFraction = -1f;
                pendingPlaylistSeekMaterialId = string.Empty;
                ResetPlaylistTransition();
                ClearPlaylistPreload();
            }

            item.ApplyTo(settings);
            pathField = configurationStore.ResolveMediaPath(item.path);
            materialNameField = item.name;
            OpenMedia(true);
            status = "素材リストから読込中: " + item.name;
        }

        private void DeleteSelectedMaterial()
        {
            if (selectedMaterialIndex < 0 || selectedMaterialIndex >= materialCatalog.materials.Count) return;
            var deleted = materialCatalog.materials[selectedMaterialIndex];
            materialCatalog.materials.RemoveAt(selectedMaterialIndex);
            foreach (var playlist in playlistCatalog.playlists)
                playlist.materialIds.RemoveAll(id => string.Equals(id, deleted.id, StringComparison.Ordinal));
            selectedMaterialIndex = materialCatalog.materials.Count == 0
                ? -1
                : Mathf.Clamp(selectedMaterialIndex, 0, materialCatalog.materials.Count - 1);
            materialNameField = selectedMaterialIndex >= 0
                ? materialCatalog.materials[selectedMaterialIndex].name
                : string.Empty;
            activePlaylistIndex = -1;
            activePlaylistEntryIndex = -1;
            activePlaylistCompleted = false;
            CancelContentDelete();
            SaveMaterialCatalog();
            SavePlaylistCatalog();
            status = "素材リストから削除しました: " + deleted.name;
        }

        private void AddPlaylist()
        {
            var name = (playlistNameField ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                status = "プレイリスト名を入力してください";
                return;
            }

            var playlist = new DomePlaylistDefinition
            {
                id = Guid.NewGuid().ToString("N"),
                name = name
            };
            playlistCatalog.playlists.Add(playlist);
            selectedPlaylistIndex = playlistCatalog.playlists.Count - 1;
            selectedPlaylistEntryIndex = -1;
            SavePlaylistCatalog();
            status = "プレイリストを作成しました: " + name;
        }

        private void RenameSelectedPlaylist()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var name = (playlistNameField ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                status = "プレイリスト名を入力してください";
                return;
            }

            playlistCatalog.playlists[selectedPlaylistIndex].name = name;
            SavePlaylistCatalog();
            status = "プレイリスト名を更新しました: " + name;
        }

        private void SelectPlaylist(int index)
        {
            if (playlistCatalog.playlists.Count == 0)
            {
                selectedPlaylistIndex = -1;
                return;
            }

            selectedPlaylistIndex = (index % playlistCatalog.playlists.Count + playlistCatalog.playlists.Count)
                                    % playlistCatalog.playlists.Count;
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            playlistNameField = playlist.name;
            selectedPlaylistEntryIndex = playlist.materialIds.Count == 0 ? -1 : 0;
            CancelContentDelete();
        }

        private void AddSelectedMaterialToPlaylist()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count
                || selectedMaterialIndex < 0 || selectedMaterialIndex >= materialCatalog.materials.Count) return;
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            playlist.materialIds.Add(materialCatalog.materials[selectedMaterialIndex].id);
            selectedPlaylistEntryIndex = playlist.materialIds.Count - 1;
            SavePlaylistCatalog();
            status = "プレイリストへ追加しました: " + materialCatalog.materials[selectedMaterialIndex].name;
        }

        private void MovePlaylistEntry(int direction)
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var entries = playlistCatalog.playlists[selectedPlaylistIndex].materialIds;
            if (selectedPlaylistEntryIndex < 0 || selectedPlaylistEntryIndex >= entries.Count) return;
            var source = selectedPlaylistEntryIndex;
            var destination = Mathf.Clamp(source + direction, 0, entries.Count - 1);
            if (destination == source) return;
            (entries[source], entries[destination]) = (entries[destination], entries[source]);

            // The transport follows the exact slot that is currently playing.
            // Without updating this index, reordering either the active row or
            // the adjacent row makes the central chapter thumb jump to the
            // wrong material even though the decoder itself has not changed.
            if (activePlaylistIndex == selectedPlaylistIndex)
            {
                if (activePlaylistEntryIndex == source) activePlaylistEntryIndex = destination;
                else if (activePlaylistEntryIndex == destination) activePlaylistEntryIndex = source;
                ResetPlaylistTransition();
                ClearPlaylistPreload();
            }
            selectedPlaylistEntryIndex = destination;
            SavePlaylistCatalog();
        }

        private void RemovePlaylistEntry()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var entries = playlistCatalog.playlists[selectedPlaylistIndex].materialIds;
            if (selectedPlaylistEntryIndex < 0 || selectedPlaylistEntryIndex >= entries.Count) return;
            entries.RemoveAt(selectedPlaylistEntryIndex);
            selectedPlaylistEntryIndex = entries.Count == 0
                ? -1
                : Mathf.Clamp(selectedPlaylistEntryIndex, 0, entries.Count - 1);
            activePlaylistIndex = -1;
            activePlaylistEntryIndex = -1;
            activePlaylistCompleted = false;
            SavePlaylistCatalog();
            status = "プレイリストから項目を外しました";
        }

        private void StartSelectedPlaylist()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            if (playlist.materialIds.Count == 0)
            {
                status = "プレイリストに素材がありません";
                return;
            }
            activePlaylistIndex = selectedPlaylistIndex;
            activePlaylistEntryIndex = Mathf.Clamp(selectedPlaylistEntryIndex, 0, playlist.materialIds.Count - 1);
            activePlaylistCompleted = false;
            pendingPlaylistSeekFraction = -1f;
            pendingPlaylistSeekMaterialId = string.Empty;
            ResetPlaylistTransition();
            ClearPlaylistPreload();
            LoadActivePlaylistEntry();
        }

        private void ToggleActivePlaylistPlayback()
        {
            if (activePlaylistIndex < 0 || activePlaylistIndex >= playlistCatalog.playlists.Count) return;
            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (playlist.materialIds.Count == 0) return;

            if (activePlaylistCompleted)
            {
                activePlaylistCompleted = false;
                activePlaylistEntryIndex = 0;
                selectedPlaylistIndex = activePlaylistIndex;
                selectedPlaylistEntryIndex = 0;
                pendingPlaylistSeekFraction = -1f;
                pendingPlaylistSeekMaterialId = string.Empty;
                ResetPlaylistTransition();
                ClearPlaylistPreload();
                settings.showIdleScreen = false;
                mediaShouldPlayWhenVisible = true;
                LoadActivePlaylistEntry();
                return;
            }

            if (video != null && video.IsPrepared && !video.IsStillImage && video.Duration > 0d
                && video.Time >= video.Duration - 0.05d)
            {
                settings.showIdleScreen = false;
                mediaShouldPlayWhenVisible = true;
                video.Seek(0d, true);
                return;
            }

            var effectivelyPlaying = mediaShouldPlayWhenVisible && !settings.showIdleScreen;
            mediaShouldPlayWhenVisible = !effectivelyPlaying;
            if (mediaShouldPlayWhenVisible) settings.showIdleScreen = false;
            ApplyControlMediaPlaybackState();
        }

        private void UpdatePlaylistTransition()
        {
            if (playlistTransitionPhase == 2)
            {
                var playlist = activePlaylistIndex >= 0 && activePlaylistIndex < playlistCatalog.playlists.Count
                    ? playlistCatalog.playlists[activePlaylistIndex]
                    : null;
                var halfDuration = Mathf.Max(0.1f, (playlist?.transitionSeconds ?? 0.8f) * 0.5f);
                playlistTransitionOpacity = Mathf.Clamp01(
                    (Time.unscaledTime - playlistFadeInStarted) / halfDuration);
                if (playlistTransitionOpacity >= 1f) ResetPlaylistTransition();
                return;
            }

            if (playlistTransitionPhase == 1)
            {
                if (activePlaylistIndex < 0 || activePlaylistIndex >= playlistCatalog.playlists.Count
                    || video == null || !video.IsPrepared || video.IsStillImage || video.Duration <= 0d)
                {
                    ResetPlaylistTransition();
                    return;
                }
                var playlist = playlistCatalog.playlists[activePlaylistIndex];
                var halfDuration = Mathf.Max(0.1f, playlist.transitionSeconds * 0.5f);
                var remaining = Mathf.Max(0f, (float)(video.Duration - video.Time));
                playlistTransitionOpacity = Mathf.Clamp01(remaining / halfDuration);
                if (remaining <= 0.02f)
                {
                    playlistTransitionOpacity = 0f;
                    if (AdvanceActivePlaylist(1))
                    {
                        playlistTransitionPhase = 2;
                        playlistFadeInStarted = Time.unscaledTime;
                    }
                    else
                    {
                        ResetPlaylistTransition();
                    }
                }
                return;
            }

            playlistTransitionOpacity = 1f;
            if (!mediaShouldPlayWhenVisible || activePlaylistIndex < 0
                || activePlaylistIndex >= playlistCatalog.playlists.Count
                || video == null || !video.IsPrepared || video.IsStillImage || !video.IsPlaying
                || video.Duration <= 0d) return;
            var activePlaylist = playlistCatalog.playlists[activePlaylistIndex];
            if (!activePlaylist.autoAdvance) return;
            var nextIndex = GetNextPlaylistEntryIndex(activePlaylist, activePlaylistEntryIndex);
            if (nextIndex < 0) return;
            var remainingTime = video.Duration - video.Time;
            var preloadLeadTime = Mathf.Max(3f, activePlaylist.transitionSeconds + 1f);
            if (remainingTime <= preloadLeadTime && !IsPlaylistEntryPreloaded(activePlaylist, nextIndex))
                PrepareNextPlaylistEntry();
            if (activePlaylist.transitionMode != 1
                || !IsPlaylistEntryPreloaded(activePlaylist, nextIndex)) return;
            var fadeOutDuration = Mathf.Max(0.1f, activePlaylist.transitionSeconds * 0.5f);
            if (remainingTime <= fadeOutDuration)
                playlistTransitionPhase = 1;
        }

        private void ResetPlaylistTransition()
        {
            playlistTransitionPhase = 0;
            playlistTransitionOpacity = 1f;
        }

        private int GetNextPlaylistEntryIndex(DomePlaylistDefinition playlist, int currentIndex)
        {
            if (playlist == null || playlist.materialIds.Count == 0) return -1;
            var next = currentIndex + 1;
            if (next < playlist.materialIds.Count) return next;
            return playlist.loop ? 0 : -1;
        }

        private bool IsPlaylistEntryPreloaded(DomePlaylistDefinition playlist, int entryIndex)
        {
            if (playlist == null || entryIndex < 0 || entryIndex >= playlist.materialIds.Count
                || preloadVideo == null || !preloadVideo.IsPrepared) return false;
            return string.Equals(preloadedPlaylistMaterialId, playlist.materialIds[entryIndex],
                StringComparison.Ordinal);
        }

        private void PrepareNextPlaylistEntry()
        {
            if (projectorMode || activePlaylistIndex < 0
                || activePlaylistIndex >= playlistCatalog.playlists.Count)
            {
                ClearPlaylistPreload();
                return;
            }
            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (!playlist.autoAdvance)
            {
                ClearPlaylistPreload();
                return;
            }
            var nextIndex = GetNextPlaylistEntryIndex(playlist, activePlaylistEntryIndex);
            if (nextIndex < 0)
            {
                ClearPlaylistPreload();
                return;
            }
            var material = FindMaterial(playlist.materialIds[nextIndex]);
            if (material == null)
            {
                ClearPlaylistPreload();
                return;
            }
            var path = configurationStore.ResolveMediaPath(material.path);
            if (string.Equals(preloadedPlaylistMaterialId, material.id, StringComparison.Ordinal)
                && MediaPathsEqual(preloadVideo.SourcePath, path)) return;
            preloadedPlaylistMaterialId = material.id;
            preloadVideo.AudioMuted = true;
            preloadVideo.IsLooping = false;
            preloadVideo.Open(path);
        }

        private void ClearPlaylistPreload()
        {
            preloadedPlaylistMaterialId = string.Empty;
            if (preloadVideo != null && (!string.IsNullOrWhiteSpace(preloadVideo.SourcePath)
                                         || preloadVideo.IsPrepared))
                preloadVideo.Clear();
        }

        private bool AdvanceActivePlaylist(int direction)
        {
            if (activePlaylistIndex < 0 || activePlaylistIndex >= playlistCatalog.playlists.Count)
            {
                if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count)
                    return false;
                activePlaylistIndex = selectedPlaylistIndex;
                activePlaylistEntryIndex = selectedPlaylistEntryIndex < 0 ? 0 : selectedPlaylistEntryIndex;
                activePlaylistCompleted = false;
            }
            else
            {
                activePlaylistEntryIndex += direction;
            }

            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (playlist.materialIds.Count == 0) return false;
            if (activePlaylistEntryIndex < 0 || activePlaylistEntryIndex >= playlist.materialIds.Count)
            {
                if (!playlist.loop)
                {
                    // Keep the playlist and its chapters attached to the
                    // transport after reaching the end. Pressing Play can then
                    // restart the complete playlist without selecting it again.
                    if (activePlaylistEntryIndex >= playlist.materialIds.Count)
                    {
                        activePlaylistEntryIndex = playlist.materialIds.Count - 1;
                        selectedPlaylistIndex = activePlaylistIndex;
                        selectedPlaylistEntryIndex = activePlaylistEntryIndex;
                        activePlaylistCompleted = true;
                    }
                    else
                    {
                        activePlaylistEntryIndex = 0;
                        selectedPlaylistIndex = activePlaylistIndex;
                        selectedPlaylistEntryIndex = 0;
                    }
                    return false;
                }
                activePlaylistEntryIndex = activePlaylistEntryIndex < 0
                    ? playlist.materialIds.Count - 1
                    : 0;
            }

            selectedPlaylistIndex = activePlaylistIndex;
            selectedPlaylistEntryIndex = activePlaylistEntryIndex;
            activePlaylistCompleted = false;
            pendingPlaylistSeekFraction = -1f;
            pendingPlaylistSeekMaterialId = string.Empty;
            return LoadActivePlaylistEntry();
        }

        private bool LoadActivePlaylistEntry()
        {
            if (activePlaylistIndex < 0 || activePlaylistIndex >= playlistCatalog.playlists.Count) return false;
            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (activePlaylistEntryIndex < 0 || activePlaylistEntryIndex >= playlist.materialIds.Count) return false;
            var material = FindMaterial(playlist.materialIds[activePlaylistEntryIndex]);
            if (material == null)
            {
                status = "プレイリスト内の素材IDが見つかりません: "
                         + playlist.materialIds[activePlaylistEntryIndex];
                return false;
            }
            selectedMaterialIndex = materialCatalog.materials.IndexOf(material);
            materialNameField = material.name;
            activePlaylistCompleted = false;
            if (IsPlaylistEntryPreloaded(playlist, activePlaylistEntryIndex))
                return ActivatePreloadedPlaylistEntry(material);
            LoadMaterial(material, true);
            return true;
        }

        private bool ActivatePreloadedPlaylistEntry(DomeMaterialDefinition material)
        {
            if (material == null || preloadVideo == null || !preloadVideo.IsPrepared) return false;
            var path = configurationStore.ResolveMediaPath(material.path);
            if (!MediaPathsEqual(path, preloadVideo.SourcePath)) return false;

            SwapVideoSourceRoles();
            preloadVideo.Clear();
            preloadedPlaylistMaterialId = string.Empty;
            material.ApplyTo(settings);
            settings.sourceMode = 0;
            settings.showIdleScreen = false;
            settings.videoPath = path;
            pathField = path;
            mediaSelectedThisSession = true;
            mediaPreparingForDisplay = false;
            mediaShouldPlayWhenVisible = true;
            video.IsLooping = false;
            video.AudioMuted = settings.audioMuted || ProjectionProcessBridge.IsOutputAlive();
            if (!video.IsStillImage) video.Play();
            status = "プレイリスト再生中: " + material.name;
            SaveSettings();
            return true;
        }

        private DomeMaterialDefinition FindMaterial(string id)
        {
            return materialCatalog.materials.Find(item =>
                string.Equals(item.id, id, StringComparison.Ordinal));
        }

        private void DeleteSelectedPlaylist()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var deleted = playlistCatalog.playlists[selectedPlaylistIndex];
            playlistCatalog.playlists.RemoveAt(selectedPlaylistIndex);
            selectedPlaylistIndex = playlistCatalog.playlists.Count == 0
                ? -1
                : Mathf.Clamp(selectedPlaylistIndex, 0, playlistCatalog.playlists.Count - 1);
            selectedPlaylistEntryIndex = -1;
            playlistNameField = selectedPlaylistIndex >= 0
                ? playlistCatalog.playlists[selectedPlaylistIndex].name
                : string.Empty;
            activePlaylistIndex = -1;
            activePlaylistEntryIndex = -1;
            activePlaylistCompleted = false;
            CancelContentDelete();
            SavePlaylistCatalog();
            status = "プレイリストを削除しました: " + deleted.name;
        }

        private void CancelContentDelete()
        {
            pendingContentDeleteKind = string.Empty;
            pendingContentDeleteId = string.Empty;
        }

        private void SaveMaterialCatalog()
        {
            try
            {
                configurationStore.SaveMaterials(materialCatalog);
            }
            catch (Exception exception)
            {
                status = "materials.jsonの保存エラー: " + exception.Message;
            }
        }

        private void SavePlaylistCatalog()
        {
            try
            {
                configurationStore.SavePlaylists(playlistCatalog);
            }
            catch (Exception exception)
            {
                status = "playlists.jsonの保存エラー: " + exception.Message;
            }
        }

        private void DrawOutputPanel()
        {
            GUILayout.Label("待機画面");
            GUILayout.Label("素材未選択・動画終了・画面取込終了時に表示します。", smallStyle);
            GUILayout.Label("待機画面の種類（どちらか1つを選択）", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("黒",
                    settings.idleDisplayMode == 0 ? activeTabStyle : tabStyle))
                settings.idleDisplayMode = 0;
            if (GUILayout.Button("既定画像",
                    settings.idleDisplayMode == 1 ? activeTabStyle : tabStyle))
                settings.idleDisplayMode = 1;
            GUILayout.EndHorizontal();

            GUILayout.Label("選択した画像はアプリの設定フォルダへコピーして保持します。", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("既定画像を変更…"))
                BrowseIdleImage();
            GUI.enabled = !string.IsNullOrWhiteSpace(idleImagePathField);
            if (GUILayout.Button("再読込", GUILayout.Width(72f))) OpenIdleImage(idleImagePathField, true);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrWhiteSpace(idleImagePathField)
                ? "保存済み画像: なし"
                : "保存済み画像: " + Path.GetFileName(idleImagePathField), smallStyle);
            GUILayout.Label("既定画像の表示方法（どちらか1つを選択）", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("360°画像（半球）",
                    settings.idleImageIsFlat ? tabStyle : activeTabStyle))
                settings.idleImageIsFlat = false;
            if (GUILayout.Button("平面画像",
                    settings.idleImageIsFlat ? activeTabStyle : tabStyle))
                settings.idleImageIsFlat = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("360°画像は2:1 Equirectangular形式を半球へ展開します。", smallStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("今すぐ待機画面を表示")) SetIdleScreenVisible(true);
            if (GUILayout.Button("素材表示へ戻す")) SetIdleScreenVisible(false);
            GUILayout.EndHorizontal();
            if (settings.idleDisplayMode == 1 && string.IsNullOrWhiteSpace(idleImagePathField))
                GUILayout.Label("既定画像が未選択の場合は黒を表示します。", smallStyle);

            GUILayout.Space(8f);
            GUILayout.Label("半球ガイド（現在の表示内容に重ねる）");
            var guideVisible = settings.gridMode == 1;
            if (GUILayout.Button(guideVisible ? "半球ガイド：表示中" : "半球ガイド：非表示",
                    guideVisible ? activeTabStyle : tabStyle))
                ToggleDomeGuide();
            GUILayout.Label("待機画像・動画・画像・外部ウィンドウのどの表示にも重ねられます。", smallStyle);

            GUILayout.Space(10f);
            GUILayout.Label("画質");
            settings.brightness = Slider("明るさ", settings.brightness, 0f, 2f, settings.brightness.ToString("0.00"), 1f);
            settings.gamma = Slider("ガンマ", settings.gamma, 0.4f, 2.5f, settings.gamma.ToString("0.00"), 1f);
            settings.edgeFade = Slider("円周フェード", settings.edgeFade, 0f, 0.2f, settings.edgeFade.ToString("0.000"), 0f);

            GUILayout.BeginHorizontal();
            settings.flipInputX = GUILayout.Toggle(settings.flipInputX, "入力を左右反転");
            settings.flipInputY = GUILayout.Toggle(settings.flipInputY, "入力を上下反転");
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("ウィンドウ");
            settings.startMaximized = GUILayout.Toggle(settings.startMaximized, "最大化ウィンドウで表示（OSフルスクリーンではありません）");
            GUILayout.BeginHorizontal();
            GUILayout.Label("サイズ", GUILayout.Width(82f));
            GUI.SetNextControlName(WindowWidthInputName);
            windowWidthField = GUILayout.TextField(windowWidthField, GUILayout.Width(72f));
            GUILayout.Label("×", GUILayout.Width(16f));
            GUI.SetNextControlName(WindowHeightInputName);
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
            // Unity's MaximizedWindow enters an OS fullscreen Space on some
            // macOS versions. Keep Control in Windowed mode and ask the native
            // window manager to zoom/maximize the ordinary titled window.
            Screen.SetResolution(settings.windowWidth, settings.windowHeight, FullScreenMode.Windowed);
            var generation = ++nativeWindowStateGeneration;
            StartCoroutine(ApplyNativeWindowState(maximize, generation));
            status = maximize
                ? "通常ウィンドウを最大化しました"
                : $"ウィンドウサイズ: {settings.windowWidth} × {settings.windowHeight}";
        }

        private IEnumerator ApplyNativeWindowState(bool maximize, int generation)
        {
            // Screen.SetResolution and native window creation are asynchronous.
            // A few frames also allow macOS to leave an old fullscreen Space
            // before applying the normal window's zoom state.
            for (var frame = 0; frame < 4; frame++) yield return null;
            if (generation != nativeWindowStateGeneration) yield break;

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            var applicationClass = objc_getClass("NSApplication");
            var application = applicationClass == IntPtr.Zero
                ? IntPtr.Zero
                : objc_msgSend_IntPtr(applicationClass, sel_registerName("sharedApplication"));
            if (application == IntPtr.Zero) yield break;

            var window = objc_msgSend_IntPtr(application, sel_registerName("keyWindow"));
            if (window == IntPtr.Zero)
                window = objc_msgSend_IntPtr(application, sel_registerName("mainWindow"));
            if (window == IntPtr.Zero) yield break;

            var isZoomed = objc_msgSend_ByteResult(window, sel_registerName("isZoomed")) != 0;
            if (isZoomed != maximize)
                objc_msgSend_Void_IntPtr(window, sel_registerName("zoom:"), IntPtr.Zero);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
            var window = GetActiveWindow();
            if (window == IntPtr.Zero)
                window = Process.GetCurrentProcess().MainWindowHandle;
            if (window != IntPtr.Zero)
                ShowWindow(window, maximize ? 3 : 9); // SW_MAXIMIZE / SW_RESTORE
#endif
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

        private float Slider(string label, float value, float min, float max, string display,
            float? defaultValue = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label(label, rowLabelStyle, GUILayout.Width(82f));
            value = DrawCenteredSlider(ReserveCenteredSliderRect(), value, min, max);
            GUILayout.Label(display, rowLabelStyle, GUILayout.Width(defaultValue.HasValue ? 54f : 72f));
            if (defaultValue.HasValue && GUILayout.Button("既定", GUILayout.Width(40f)))
                value = defaultValue.Value;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private float FineSlider(string label, float value, float min, float max, float step, string display,
            float? defaultValue = null)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label(label, rowLabelStyle, GUILayout.Width(82f));
            if (GUILayout.Button("−", GUILayout.Width(24f))) value -= step;
            value = DrawCenteredSlider(ReserveCenteredSliderRect(defaultValue.HasValue ? 124f : 170f),
                value, min, max);
            if (GUILayout.Button("+", GUILayout.Width(24f))) value += step;
            GUILayout.Label(display, rowLabelStyle, GUILayout.Width(56f));
            if (defaultValue.HasValue && GUILayout.Button("既定", GUILayout.Width(40f)))
                value = defaultValue.Value;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        private static Rect ReserveCenteredSliderRect(float width = 0f)
        {
            var lane = width > 0f
                ? GUILayoutUtility.GetRect(width, 28f, GUILayout.Width(width), GUILayout.Height(28f))
                : GUILayoutUtility.GetRect(40f, 28f, GUILayout.ExpandWidth(true), GUILayout.Height(28f));
            return new Rect(lane.x, lane.y + (lane.height - 18f) * 0.5f, lane.width, 18f);
        }

        private float DrawCenteredSlider(Rect rect, float value, float min, float max)
        {
            var controlId = GUIUtility.GetControlID(0x44D05E, FocusType.Passive, rect);
            var current = Event.current;
            if (GUI.enabled)
            {
                if (current.type == EventType.MouseDown && current.button == 0
                    && rect.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    value = SliderValueAtMouse(rect, min, max);
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    value = SliderValueAtMouse(rect, min, max);
                    current.Use();
                }
                else if (current.rawType == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    value = SliderValueAtMouse(rect, min, max);
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }

            value = Mathf.Clamp(value, min, max);
            var track = new Rect(rect.x, rect.center.y - 3f, rect.width, 6f);
            GUI.Box(track, GUIContent.none, sliderStyle);
            var normalized = Mathf.InverseLerp(min, max, value);
            var thumb = new Rect(Mathf.Lerp(rect.x, rect.xMax, normalized) - 6f,
                rect.center.y - 9f + 1.5f, 12f, 18f);
            GUI.Box(thumb, GUIContent.none, sliderThumbStyle);
            return value;
        }

        private static float SliderValueAtMouse(Rect rect, float min, float max)
        {
            return Mathf.Lerp(min, max,
                Mathf.Clamp01(Mathf.InverseLerp(rect.x, rect.xMax, Event.current.mousePosition.x)));
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
            // IMGUI sends the same key event to OnGUI and the focused TextField.
            // Never consume text-editing keys as app shortcuts: without this,
            // typing e.g. "test1" switches to tab 1 before '1' reaches the field.
            if (textInputFocused || IsTextInputFocused()) return;
            switch (current.keyCode)
            {
                case KeyCode.Space:
                    if (settings.sourceMode == 1) SendSlideNavigation(true);
                    else
                    {
                        mediaShouldPlayWhenVisible = !mediaShouldPlayWhenVisible;
                        ApplyControlMediaPlaybackState();
                    }
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
                    ToggleDomeGuide();
                    current.Use();
                    break;
                case KeyCode.B:
                    SetIdleScreenVisible(!settings.showIdleScreen);
                    status = settings.showIdleScreen ? "待機画面を表示しています" : "素材表示へ戻しました";
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
                case KeyCode.F5:
                    SetSourceMode(0);
                    current.Use();
                    break;
                case KeyCode.F6:
                    SetSourceMode(1);
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
                case KeyCode.Alpha5:
                    activePanelTab = 4;
                    current.Use();
                    break;
            }
        }

        private static bool IsTextInputFocused()
        {
            var focused = GUI.GetNameOfFocusedControl();
            return focused == MediaPathInputName
                   || focused == LensProfileInputName
                   || focused == MaterialNameInputName
                   || focused == PlaylistNameInputName
                   || focused == WindowWidthInputName
                   || focused == WindowHeightInputName;
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

        private void BeginSection(string title)
        {
            GUILayout.BeginVertical(sectionStyle);
            if (!string.IsNullOrWhiteSpace(title)) GUILayout.Label(title, sectionTitleStyle);
        }

        private static void EndSection()
        {
            GUILayout.EndVertical();
            GUILayout.Space(6f);
        }

        private void BuildStyles()
        {
            if (titleStyle != null) return;

            panelTexture = CreateSolidTexture(new Color(0.105f, 0.115f, 0.13f, 1f));
            buttonTexture = CreateBorderedTexture(new Color(0.22f, 0.24f, 0.27f, 1f),
                new Color(0.38f, 0.41f, 0.45f, 1f));
            buttonHoverTexture = CreateBorderedTexture(new Color(0.28f, 0.31f, 0.35f, 1f),
                new Color(0.50f, 0.55f, 0.60f, 1f));
            buttonPressedTexture = CreateBorderedTexture(new Color(0.12f, 0.47f, 0.72f, 1f),
                new Color(0.40f, 0.76f, 0.96f, 1f));
            inputTexture = CreateBorderedTexture(new Color(0.075f, 0.085f, 0.10f, 1f),
                new Color(0.34f, 0.37f, 0.42f, 1f));
            inputFocusedTexture = CreateBorderedTexture(new Color(0.10f, 0.13f, 0.16f, 1f),
                new Color(0.28f, 0.67f, 0.92f, 1f));
            tabTexture = CreateBorderedTexture(new Color(0.16f, 0.18f, 0.21f, 1f),
                new Color(0.34f, 0.37f, 0.42f, 1f));
            activeTabTexture = CreateBorderedTexture(new Color(0.12f, 0.47f, 0.72f, 1f),
                new Color(0.42f, 0.78f, 0.98f, 1f));
            tabContentTexture = CreateBorderedTexture(new Color(0.22f, 0.235f, 0.26f, 1f),
                new Color(0.36f, 0.39f, 0.44f, 1f));
            dropdownRowTexture = CreateBorderedTexture(new Color(0.16f, 0.18f, 0.21f, 1f),
                new Color(0.32f, 0.35f, 0.39f, 1f));
            dropdownSelectedRowTexture = CreateBorderedTexture(new Color(0.12f, 0.39f, 0.59f, 1f),
                new Color(0.37f, 0.72f, 0.93f, 1f));
            playingRowTexture = CreateBorderedTexture(new Color(0.10f, 0.43f, 0.36f, 1f),
                new Color(0.40f, 0.90f, 0.72f, 1f));
            scrollbarTexture = CreateSolidTexture(new Color(0.16f, 0.18f, 0.2f, 1f));
            scrollbarThumbTexture = CreateSolidTexture(new Color(0.38f, 0.62f, 0.76f, 1f));
            sliderTexture = CreateBorderedTexture(new Color(0.30f, 0.33f, 0.37f, 1f),
                new Color(0.52f, 0.56f, 0.61f, 1f));
            sliderThumbTexture = CreateBorderedTexture(new Color(0.25f, 0.67f, 0.95f, 1f),
                new Color(0.58f, 0.86f, 1f, 1f));
            sliderThumbActiveTexture = CreateBorderedTexture(new Color(0.55f, 0.84f, 1f, 1f),
                Color.white);
            sectionTexture = CreateBorderedTexture(new Color(0.175f, 0.19f, 0.215f, 1f),
                new Color(0.37f, 0.40f, 0.45f, 1f));
            chapterPendingTexture = CreateSolidTexture(new Color(0.29f, 0.31f, 0.34f, 1f));
            chapterCompleteTexture = CreateSolidTexture(new Color(0.13f, 0.40f, 0.59f, 1f));
            chapterCurrentTexture = CreateSolidTexture(new Color(0.28f, 0.72f, 1f, 1f));

            var button = GUI.skin.button;
            button.normal.background = buttonTexture;
            button.hover.background = buttonHoverTexture;
            button.active.background = buttonPressedTexture;
            button.focused.background = buttonHoverTexture;
            button.onNormal.background = buttonPressedTexture;
            button.onHover.background = activeTabTexture;
            button.onActive.background = activeTabTexture;
            button.onFocused.background = activeTabTexture;
            button.border = new RectOffset(1, 1, 1, 1);
            button.margin = new RectOffset(2, 2, 2, 2);
            button.padding = new RectOffset(8, 8, 5, 5);
            button.fixedHeight = 28f;

            var toggle = GUI.skin.toggle;
            toggle.normal.background = buttonTexture;
            toggle.hover.background = buttonHoverTexture;
            toggle.active.background = buttonPressedTexture;
            toggle.focused.background = buttonHoverTexture;
            toggle.onNormal.background = buttonPressedTexture;
            toggle.onHover.background = activeTabTexture;
            toggle.onActive.background = activeTabTexture;
            toggle.onFocused.background = activeTabTexture;
            toggle.border = new RectOffset(1, 1, 1, 1);
            toggle.margin = new RectOffset(2, 2, 2, 2);
            toggle.padding = new RectOffset(8, 8, 5, 5);
            toggle.alignment = TextAnchor.MiddleCenter;
            toggle.fixedHeight = 28f;

            GUI.skin.box.normal.background = tabTexture;
            GUI.skin.box.border = new RectOffset(1, 1, 1, 1);

            var textField = GUI.skin.textField;
            textField.normal.background = inputTexture;
            textField.hover.background = inputFocusedTexture;
            textField.focused.background = inputFocusedTexture;
            textField.active.background = inputFocusedTexture;
            textField.border = new RectOffset(1, 1, 1, 1);
            textField.padding = new RectOffset(8, 8, 5, 5);
            textField.fixedHeight = 28f;

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 12, 12)
            };
            panelStyle.normal.background = panelTexture;

            sectionStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(9, 9, 7, 9),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(1, 1, 1, 1)
            };
            sectionStyle.normal.background = sectionTexture;
            sectionTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(2, 2, 0, 4)
            };

            tabStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                fixedHeight = 0f,
                padding = new RectOffset(5, 5, 6, 5),
                margin = new RectOffset(1, 1, 0, 0)
            };
            tabStyle.normal.background = tabTexture;
            tabStyle.hover.background = dropdownSelectedRowTexture;

            activeTabStyle = new GUIStyle(tabStyle) { fontStyle = FontStyle.Bold };
            activeTabStyle.normal.background = activeTabTexture;
            activeTabStyle.hover.background = activeTabTexture;
            activeTabStyle.active.background = activeTabTexture;

            selectedListButtonStyle = new GUIStyle(activeTabStyle)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                fixedHeight = 0f,
                padding = new RectOffset(10, 8, 4, 4)
            };
            playingListButtonStyle = new GUIStyle(selectedListButtonStyle);
            playingListButtonStyle.normal.background = playingRowTexture;
            playingListButtonStyle.hover.background = playingRowTexture;
            playingListButtonStyle.active.background = playingRowTexture;

            tabContentStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 10, 12),
                margin = new RectOffset(0, 0, 0, 0)
            };
            tabContentStyle.normal.background = tabContentTexture;

            dropdownButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 0f,
                padding = new RectOffset(10, 8, 4, 4)
            };
            dropdownRowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(0, 0, 0, 0)
            };
            dropdownRowStyle.normal.background = dropdownRowTexture;
            dropdownSelectedRowStyle = new GUIStyle(dropdownRowStyle);
            dropdownSelectedRowStyle.normal.background = dropdownSelectedRowTexture;
            dropdownLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Clip
            };

            const float scrollbarWidth = 12f;
            GUI.skin.verticalScrollbar.fixedWidth = scrollbarWidth;
            GUI.skin.verticalScrollbar.margin = new RectOffset(8, 0, 0, 0);
            GUI.skin.verticalScrollbar.padding = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbar.border = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbar.normal.background = scrollbarTexture;
            GUI.skin.verticalScrollbar.hover.background = scrollbarTexture;
            GUI.skin.verticalScrollbar.active.background = scrollbarTexture;
            GUI.skin.verticalScrollbarThumb.fixedWidth = scrollbarWidth;
            GUI.skin.verticalScrollbarThumb.margin = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbarThumb.padding = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbarThumb.border = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbarThumb.overflow = new RectOffset(0, 0, 0, 0);
            GUI.skin.verticalScrollbarThumb.normal.background = scrollbarThumbTexture;
            GUI.skin.verticalScrollbarThumb.hover.background = sliderThumbActiveTexture;
            GUI.skin.verticalScrollbarThumb.active.background = sliderThumbActiveTexture;
            ConfigureScrollbarButton(GUI.skin.verticalScrollbarUpButton, scrollbarWidth);
            ConfigureScrollbarButton(GUI.skin.verticalScrollbarDownButton, scrollbarWidth);

            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider)
            {
                fixedHeight = 8f,
                border = new RectOffset(1, 1, 1, 1)
            };
            sliderStyle.normal.background = sliderTexture;

            sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb)
            {
                fixedWidth = 12f,
                fixedHeight = 18f,
                border = new RectOffset(1, 1, 1, 1)
            };
            sliderThumbStyle.normal.background = sliderThumbTexture;
            sliderThumbStyle.hover.background = sliderThumbActiveTexture;
            sliderThumbStyle.active.background = sliderThumbActiveTexture;

            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            rowLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 28f
            };
            chapterLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip
            };
        }

        private void ConfigureScrollbarButton(GUIStyle style, float width)
        {
            style.fixedWidth = width;
            style.margin = new RectOffset(0, 0, 0, 0);
            style.padding = new RectOffset(0, 0, 0, 0);
            style.border = new RectOffset(0, 0, 0, 0);
            style.overflow = new RectOffset(0, 0, 0, 0);
            style.normal.background = scrollbarTexture;
            style.hover.background = scrollbarThumbTexture;
            style.active.background = sliderThumbActiveTexture;
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

        private static Texture2D CreateBorderedTexture(Color fill, Color border)
        {
            var texture = new Texture2D(3, 3, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                texture.SetPixel(x, y, x == 0 || x == 2 || y == 0 || y == 2 ? border : fill);
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
                idleImagePathField = settings.idleImagePath ?? string.Empty;
                windowBackgroundImagePathField = settings.windowBackgroundImagePath ?? string.Empty;
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
                idleImagePathField = string.Empty;
                windowBackgroundImagePathField = string.Empty;
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
                settings.idleImagePath = idleImagePathField;
                settings.windowBackgroundImagePath = windowBackgroundImagePathField;
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

