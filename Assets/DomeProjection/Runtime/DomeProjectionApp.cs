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
        private const int TransportWindowId = 0x44A1;
        private const float VideoPathWidth = 288f;
        private const float MaterialYawKeyHoldDelay = 0.25f;
        // AVPro's native decoder accepts approximate preview seeks continuously;
        // match a typical display refresh rate while the pointer is dragged.
        private const float SeekPreviewInterval = 1f / 60f;
        private const string MediaPathInputName = "text-input-media-path";
        private const string LensProfileInputName = "text-input-lens-profile";
        private const string MaterialNameInputName = "text-input-material-name";
        private const string PlaylistNameInputName = "text-input-playlist-name";
        private const string MaterialRenameInputName = "text-input-material-rename";
        private const string PlaylistRenameInputName = "text-input-playlist-rename";
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
        private string editingMaterialId = string.Empty;
        private string editingMaterialName = string.Empty;
        private string editingPlaylistId = string.Empty;
        private string editingPlaylistName = string.Empty;
        private string pendingNameEditFocus = string.Empty;
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
        private Rect transportWindowRect;
        private bool transportWindowPositionInitialized;
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
        private float activeStillImageElapsed;
        private bool roomWarpEditing;
        // Session-only Control inspection view. It intentionally never changes
        // the projector/output process, and returns to the output view at launch.
        private float controlPreviewScale = 1f;
        private float controlPreviewRotation;
        private float controlPreviewYaw;
        private float controlPreviewPitch;
        private bool roomWarpAutoFitPending;
        private readonly List<RoomCalibrationAnchor> roomCalibrationAnchors = new();
        private bool roomCalibrationViewDragging;
        private Vector2 roomCalibrationLastMouse;
        private bool roomCalibrationShowOverview;
        private LensCalibrationProfile roomCalibrationStartProfile;
        private float roomOverviewYaw = 35f;
        private float roomOverviewPitch = 24f;
        private float roomOverviewZoom = 1f;
        private bool roomOverviewOrbitDragging;
        private Vector2 roomOverviewLastMouse;
        private RoomContentPreviewRenderer roomContentPreviewRenderer;
        private DomeSimulationPreviewRenderer domeSimulationPreviewRenderer;
        private bool roomOverviewShowContent = true;
        private float roomContentPreviewYaw;
        private float roomContentPreviewPitch;
        private float roomContentPreviewFov = 95f;
        private bool roomContentPreviewDragging;
        private Vector2 roomContentPreviewLastMouse;
        private bool roomContentPreviewShowGuide;
        private bool materialYawDragging;
        private Vector2 materialYawLastMouse;
        private bool materialYawQHeld;
        private bool materialYawEHeld;
        private float materialYawQHoldStarted;
        private float materialYawEHoldStarted;
        private bool domeGuiEditing;
        private LensCalibrationProfile domeGuiCalibrationStartProfile;
        private readonly Stack<LensCalibrationProfile> domeGuiUndoHistory = new();
        private bool domeSimulationVisible;
        private float domeSimulationYaw = 35f;
        private float domeSimulationPitch = 22f;
        private float domeSimulationZoom = 1f;
        private bool domeSimulationOrbitDragging;
        private Vector2 domeSimulationLastMouse;
        private bool domeSimulationProjectorDragging;
        private Vector2 domeSimulationProjectorLastMouse;
        private bool domeSimulationAimDragging;
        private Vector2 domeSimulationAimLastMouse;
        private bool domeSimulationShowFullSphere;
        private bool domeSimulationShowMaterial = true;
        private bool domeSimulationShowGuide = true;

        private sealed class RoomCalibrationAnchor
        {
            public string label;
            public Vector3 worldPoint;
            public Vector2 targetPoint;
            public bool adjusted;
        }

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
                StartCoroutine(ConfigureProjectorDisplay());
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

        private void OnApplicationFocus(bool hasFocus)
        {
            // A KeyUp event is not guaranteed when the operator switches to
            // another application while holding Q/E. Never leave continuous
            // Yaw rotation latched after focus is lost.
            if (!hasFocus) materialYawQHeld = materialYawEHeld = false;
        }

        private void OnDestroy()
        {
            roomContentPreviewRenderer?.Dispose();
            roomContentPreviewRenderer = null;
            domeSimulationPreviewRenderer?.Dispose();
            domeSimulationPreviewRenderer = null;
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
            if (!projectorMode)
            {
                roomContentPreviewRenderer = new RoomContentPreviewRenderer(transform);
                domeSimulationPreviewRenderer = new DomeSimulationPreviewRenderer(transform);
            }
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
            if (video.IsStillImage)
            {
                var material = FindMaterial(materialId);
                activeStillImageElapsed = PlaylistEntryDuration(
                    playlist, activePlaylistEntryIndex, material) * fraction;
                mediaShouldPlayWhenVisible = pendingPlaylistSeekShouldPlay;
                return;
            }
            if (video.Duration <= 0d) return;
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
                if (AdvancePlaylistAfterCompletion())
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
                && AdvancePlaylistAfterCompletion()) return;
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
                UpdateMaterialYawKeyHold();
                UpdatePlaylistTransition();
                UpdateControlProcess();
            }
            outputRenderer.ContentOpacity = playlistTransitionOpacity;
            previewRenderer.ContentOpacity = playlistTransitionOpacity;
            var controlScale = projectorMode ? 1f : controlPreviewScale;
            var controlRotation = projectorMode ? 0f : controlPreviewRotation;
            var controlYaw = projectorMode ? 0f : controlPreviewYaw;
            var controlPitch = projectorMode ? 0f : controlPreviewPitch;
            var projectionAspect = projectorMode ? 0f : GetProjectionAspect();
            outputRenderer.ControlPreviewScale = controlScale;
            outputRenderer.ControlPreviewRotation = controlRotation;
            outputRenderer.ControlPreviewYaw = controlYaw;
            outputRenderer.ControlPreviewPitch = controlPitch;
            outputRenderer.ProjectionAspectOverride = projectionAspect;
            outputRenderer.CaptureOperatorPreview = !projectorMode
                && settings.projectionRegionMode < 2
                && (domeSimulationVisible || domeGuiEditing);
            previewRenderer.ControlPreviewScale = controlScale;
            previewRenderer.ControlPreviewRotation = controlRotation;
            previewRenderer.ControlPreviewYaw = controlYaw;
            previewRenderer.ControlPreviewPitch = controlPitch;
            previewRenderer.ProjectionAspectOverride = projectionAspect;
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
                ProjectionProcessBridge.WriteHeartbeat(windowCapture.HasCapturePermission,
                    Screen.height > 0 ? (float)Screen.width / Screen.height : 1f);
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
                    : "1/2/3/4/5: タブ   F5/F6: 素材/画面取込   Ctrl+O: 素材選択   Space: 再生/停止   Q/E: 表示Yaw±1°   Alt＋横ドラッグ: 表示Yaw   B: 待機画面   G: ガイド   H: UI", smallStyle);
                if (settings.projectionRegionMode == 2)
                    GUILayout.Label("W: 前面4隅ワープを開始／完了   V: 投影確認／3D俯瞰   C: 調整キャンセル", smallStyle);
                else
                    GUILayout.Label("V: 投影結果／3Dシミュレーション", smallStyle);
                GUILayout.Label($"出力 {Screen.width}×{Screen.height} / Display {settings.outputDisplay}", smallStyle);
                GUILayout.EndVertical();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            if (!roomWarpEditing && !domeGuiEditing) DrawPanelVisibilityToggle(logicalWidth, margin);
            if (domeGuiEditing)
            {
                // The toolbar must register IMGUI controls before any 3D
                // drag handler. Projected handles can legitimately be far
                // outside the visible sphere; if the diagram sees MouseDown
                // first, one of those handles can otherwise steal the click
                // from Cancel / Reset / Finish.
                DrawDomePhysicalCalibrationToolbar(logicalWidth, logicalHeight, margin);
                if (domeGuiEditing)
                    DrawDomeSimulationEditor(logicalWidth, logicalHeight, margin);
            }
            else if (settings.projectionRegionMode == 2 && roomWarpEditing)
                DrawRoomAdjustmentHandles(logicalWidth, logicalHeight, margin);
            else if (settings.projectionRegionMode == 2 && settings.roomControlViewMode > 0)
                DrawRoomObserverEditor(logicalWidth, logicalHeight, margin);
            else if (settings.projectionRegionMode < 2 && domeSimulationVisible)
                DrawDomeSimulationEditor(logicalWidth, logicalHeight, margin);
            else if (IsControlPreviewTransformed())
                DrawControlPreviewFrame(logicalWidth, logicalHeight, margin, true);
            if (drawLargeTransportOverlay) DrawTransportOverlay(logicalWidth, logicalHeight, margin);
            if (showWindowDropdown && settings.sourceMode == 1) DrawWindowDropdown();
            HandleMaterialYawDragGesture();
            // Cache after controls have registered their IMGUI names so plain
            // character shortcuts never consume text typed into a field.
            textInputFocused = showControls && IsTextInputFocused();
            HandleHotkeys(Event.current);
            GUI.matrix = previousMatrix;
        }

        private void DrawTransportOverlay(float logicalWidth, float logicalHeight, float margin)
        {
            var availableWidth = logicalWidth - margin * 2f;
            if (availableWidth < 500f) return;

            var hasActivePlaylist = activePlaylistIndex >= 0
                                    && activePlaylistIndex < playlistCatalog.playlists.Count;
            var width = Mathf.Min(760f, availableWidth - 32f);
            var height = hasActivePlaylist ? 178f : 124f;
            if (!transportWindowPositionInitialized)
            {
                // The position is intentionally session-only. Every launch
                // starts from the familiar centered lower edge.
                transportWindowRect = new Rect((logicalWidth - width) * 0.5f,
                    logicalHeight - height - margin, width, height);
                transportWindowPositionInitialized = true;
            }
            else
            {
                transportWindowRect.width = width;
                transportWindowRect.height = height;
            }

            ClampTransportWindow(logicalWidth, logicalHeight, margin);
            transportWindowRect = GUI.Window(TransportWindowId, transportWindowRect,
                DrawTransportWindowContents, GUIContent.none, sectionStyle);
            ClampTransportWindow(logicalWidth, logicalHeight, margin);
        }

        private void DrawTransportWindowContents(int windowId)
        {
            GUILayout.Label("再生コントロール（ここをドラッグして移動）", smallStyle,
                GUILayout.Height(22f));

            var hasActivePlaylist = activePlaylistIndex >= 0
                                    && activePlaylistIndex < playlistCatalog.playlists.Count;

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
                var loopLabel = playlist.loopMode switch
                {
                    1 => "↻ 全体",
                    2 => "↻ 1素材",
                    _ => "↻ なし"
                };
                var loopTip = playlist.loopMode switch
                {
                    1 => "プレイリスト末尾から先頭へ戻る",
                    2 => "現在の1素材だけを繰り返す",
                    _ => "ループしない"
                };
                var loopStyle = playlist.loopMode > 0 ? activeTabStyle : tabStyle;
                if (GUILayout.Button(new GUIContent(loopLabel, loopTip), loopStyle,
                        GUILayout.Width(102f), GUILayout.Height(28f)))
                {
                    playlist.loopMode = (playlist.loopMode + 1) % 3;
                    playlist.loop = playlist.loopMode == 1;
                    ResetPlaylistTransition();
                    ClearPlaylistPreload();
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

            // Only the dedicated top strip moves the floating panel, leaving
            // every playback button and seek bar free to receive drag input.
            GUI.DragWindow(new Rect(0f, 0f, transportWindowRect.width, 30f));
        }

        private void ClampTransportWindow(float logicalWidth, float logicalHeight, float margin)
        {
            var maximumX = Mathf.Max(margin, logicalWidth - transportWindowRect.width - margin);
            var maximumY = Mathf.Max(margin, logicalHeight - transportWindowRect.height - margin);
            transportWindowRect.x = Mathf.Clamp(transportWindowRect.x, margin, maximumX);
            transportWindowRect.y = Mathf.Clamp(transportWindowRect.y, margin, maximumY);
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

        private void DrawRoomAdjustmentHandles(float logicalWidth, float logicalHeight, float margin)
        {
            if (roomWarpAutoFitPending)
            {
                roomWarpAutoFitPending = false;
                FitRoomWarpPreview(logicalWidth, logicalHeight);
            }
            var toolbarWidth = Mathf.Min(1060f, logicalWidth - margin * 2f);
            var toolbar = new Rect((logicalWidth - toolbarWidth) * 0.5f, margin,
                toolbarWidth, 138f);
            DrawSolidGuiRect(toolbar, new Color(0.12f, 0.14f, 0.17f, 0.96f));
            GUI.Label(new Rect(toolbar.x + 12f, toolbar.y + 8f, 230f, 26f),
                "前面4隅ワープ：各頂点をドラッグ", sectionTitleStyle);
            GUI.Label(new Rect(toolbar.x + 246f, toolbar.y + 9f, 84f, 24f),
                $"表示 {controlPreviewScale * 100f:0}%", smallStyle);
            if (GUI.Button(new Rect(toolbar.x + 330f, toolbar.y + 6f, 30f, 30f), "－"))
                controlPreviewScale = Mathf.Max(0.01f, controlPreviewScale - 0.05f);
            controlPreviewScale = GUI.HorizontalSlider(
                new Rect(toolbar.x + 368f, toolbar.y + 13f, 100f, 18f),
                controlPreviewScale, 0.01f, 1f);
            if (GUI.Button(new Rect(toolbar.x + 476f, toolbar.y + 6f, 30f, 30f), "+"))
                controlPreviewScale = Mathf.Min(1f, controlPreviewScale + 0.05f);
            if (GUI.Button(new Rect(toolbar.x + 514f, toolbar.y + 6f, 54f, 30f), "全体"))
                FitRoomWarpPreview(logicalWidth, logicalHeight);
            if (GUI.Button(new Rect(toolbar.xMax - 314f, toolbar.y + 6f, 98f, 30f), "調整キャンセル"))
                CancelRoomWarpEditing();
            if (!roomWarpEditing) return;
            if (GUI.Button(new Rect(toolbar.xMax - 210f, toolbar.y + 6f, 98f, 30f), "四隅リセット"))
            {
                settings.ResetRoomWarp();
                roomWarpAutoFitPending = true;
                status = "前面4隅ワープをリセットしました";
            }
            if (GUI.Button(new Rect(toolbar.xMax - 106f, toolbar.y + 6f, 94f, 30f), "調整完了"))
                FinishRoomWarpEditing();

            DrawPreviewAngleControl(toolbar.x + 12f, toolbar.y + 41f,
                "左右", ref controlPreviewYaw, -75f, 75f, false);
            DrawPreviewAngleControl(toolbar.x + 334f, toolbar.y + 41f,
                "上下", ref controlPreviewPitch, -75f, 75f, false);
            if (GUI.Button(new Rect(toolbar.xMax - 210f, toolbar.y + 41f, 98f, 30f),
                    "投影確認", roomCalibrationShowOverview ? tabStyle : activeTabStyle))
                roomCalibrationShowOverview = false;
            if (GUI.Button(new Rect(toolbar.xMax - 106f, toolbar.y + 41f, 94f, 30f),
                    "3D俯瞰", roomCalibrationShowOverview ? activeTabStyle : tabStyle))
                roomCalibrationShowOverview = true;
            DrawPreviewAngleControl(toolbar.x + 12f, toolbar.y + 76f,
                "傾き", ref controlPreviewRotation, -180f, 180f, true);
            if (GUI.Button(new Rect(toolbar.xMax - 210f, toolbar.y + 76f, 198f, 30f),
                    "Outputと同じ表示に戻す")) ResetControlPreview();
            GUI.Label(new Rect(toolbar.x + 334f, toolbar.y + 82f, 390f, 24f),
                "空き領域をドラッグ：確認視点を回転 / ホイール：拡大縮小", smallStyle);

            if (roomCalibrationShowOverview)
            {
                var overviewArea = new Rect(margin, toolbar.yMax + 8f,
                    logicalWidth - margin * 2f,
                    logicalHeight - toolbar.yMax - margin - 8f);
                DrawSolidGuiRect(overviewArea, new Color(0.035f, 0.045f, 0.06f, 1f));
                DrawRoomOverviewEditor(overviewArea);
                return;
            }

            var aspect = GetProjectionAspect();
            DomeProjectionRenderer.CalculateRoomReferenceCorners(settings, aspect,
                out var topLeft, out var topRight, out var bottomLeft, out var bottomRight);
            var idealPoints = new[] { topLeft, topRight, bottomLeft, bottomRight };
            var handleRects = new List<Rect>(4);
            for (var index = 0; index < 4; index++)
            {
                var point = RoomWarpPointToGui(idealPoints[index] + GetRoomWarpPoint(index),
                    logicalWidth, logicalHeight, aspect);
                handleRects.Add(new Rect(point.x - 18f, point.y - 18f, 36f, 36f));
            }
            HandleRoomCalibrationViewDrag(toolbar, handleRects);
            if (IsControlPreviewTransformed())
                DrawControlPreviewFrame(logicalWidth, logicalHeight, margin, false);
            for (var index = 0; index < 4; index++)
                DrawRoomWarpHandle(index,
                    RoomWarpPointToGui(idealPoints[index] + GetRoomWarpPoint(index),
                        logicalWidth, logicalHeight, aspect),
                    idealPoints[index], logicalWidth, logicalHeight);
        }

        private void HandleRoomCalibrationViewDrag(Rect toolbar, List<Rect> handleRects)
        {
            var current = Event.current;
            var mouse = GetLogicalMousePosition();
            if (current.type == EventType.ScrollWheel && !toolbar.Contains(mouse))
            {
                controlPreviewScale = Mathf.Clamp(controlPreviewScale
                                                   * Mathf.Exp(-current.delta.y * 0.08f),
                    0.01f, 1f);
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDown && current.button == 0
                && !toolbar.Contains(mouse))
            {
                foreach (var rect in handleRects)
                    if (rect.Contains(mouse)) return;
                roomCalibrationViewDragging = true;
                roomCalibrationLastMouse = mouse;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && roomCalibrationViewDragging)
            {
                var delta = mouse - roomCalibrationLastMouse;
                roomCalibrationLastMouse = mouse;
                controlPreviewYaw = Mathf.Clamp(controlPreviewYaw - delta.x * 0.18f, -85f, 85f);
                controlPreviewPitch = Mathf.Clamp(controlPreviewPitch + delta.y * 0.18f, -85f, 85f);
                current.Use();
                return;
            }
            if (current.rawType == EventType.MouseUp) roomCalibrationViewDragging = false;
        }

        private void DrawRoomCalibrationHandle(int index, float logicalWidth,
            float logicalHeight, float aspect)
        {
            var anchor = roomCalibrationAnchors[index];
            var screenPoint = RoomWarpPointToGui(anchor.targetPoint, logicalWidth,
                logicalHeight, aspect);
            const float hitSize = 36f;
            if (screenPoint.x < -hitSize || screenPoint.x > logicalWidth + hitSize
                || screenPoint.y < -hitSize || screenPoint.y > logicalHeight + hitSize)
                return;
            var hitRect = new Rect(screenPoint.x - hitSize * 0.5f,
                screenPoint.y - hitSize * 0.5f, hitSize, hitSize);
            var moved = DrawDraggableHandleWindow(62100 + index, hitRect);
            if ((moved.position - hitRect.position).sqrMagnitude > 0.01f)
            {
                var displayPoint = new Vector2(moved.center.x / Mathf.Max(logicalWidth, 1f),
                    1f - moved.center.y / Mathf.Max(logicalHeight, 1f));
                anchor.targetPoint = InverseControlPreviewPoint(displayPoint, aspect);
                anchor.targetPoint.x = Mathf.Clamp(anchor.targetPoint.x, -2f, 3f);
                anchor.targetPoint.y = Mathf.Clamp(anchor.targetPoint.y, -2f, 3f);
                anchor.adjusted = true;
                SolveRoomProjectorFromAnchors(aspect);
            }

            var color = anchor.adjusted
                ? new Color(1f, 0.72f, 0.10f, 1f)
                : new Color(0.10f, 0.82f, 1f, 1f);
            DrawSolidGuiRect(new Rect(screenPoint.x - 13f, screenPoint.y - 2f, 26f, 4f), color);
            DrawSolidGuiRect(new Rect(screenPoint.x - 2f, screenPoint.y - 13f, 4f, 26f), color);
            DrawSolidGuiRect(new Rect(screenPoint.x - 6f, screenPoint.y - 6f, 12f, 12f), Color.white);
            DrawSolidGuiRect(new Rect(screenPoint.x - 3f, screenPoint.y - 3f, 6f, 6f), color);
            GUI.Label(new Rect(screenPoint.x + 14f, screenPoint.y - 12f, 150f, 24f),
                anchor.label, smallStyle);

            if (DomeProjectionRenderer.TryProjectRoomPoint(settings, aspect,
                    anchor.worldPoint, out var projected))
            {
                var projectedGui = RoomWarpPointToGui(projected, logicalWidth,
                    logicalHeight, aspect);
                DrawGuiSegment(projectedGui, screenPoint,
                    new Color(color.r, color.g, color.b, 0.55f), 2f);
            }
        }

        private void BuildRoomCalibrationAnchors()
        {
            roomCalibrationAnchors.Clear();
            var aspect = GetProjectionAspect();
            var halfWidth = settings.roomWidth * 0.5f;
            // The literal rear corners can be behind a projector placed near
            // the rear wall. Keep them when optically reachable, and otherwise
            // use one measured midpoint on each longitudinal seam plus the
            // four opposite-wall corners. Eight well-spread observations are
            // enough to constrain the seven projector parameters without the
            // unreadable forest of handles produced by sampling every 25%.
            var depthFractions = new[] { 0f, 0.5f, 1f };
            foreach (var fraction in depthFractions)
            {
                var z = settings.roomDepth * fraction;
                var depthLabel = fraction <= 0.001f ? "背面"
                    : fraction >= 0.999f ? "前面"
                    : $"奥行{fraction * 100f:0}%";
                AddRoomCalibrationAnchor($"左天井・{depthLabel}",
                    new Vector3(-halfWidth, settings.roomHeight, z), aspect);
                AddRoomCalibrationAnchor($"右天井・{depthLabel}",
                    new Vector3(halfWidth, settings.roomHeight, z), aspect);
                AddRoomCalibrationAnchor($"左床・{depthLabel}",
                    new Vector3(-halfWidth, 0f, z), aspect);
                AddRoomCalibrationAnchor($"右床・{depthLabel}",
                    new Vector3(halfWidth, 0f, z), aspect);
            }

        }

        private void AddRoomCalibrationAnchor(string label, Vector3 worldPoint, float aspect)
        {
            if (!DomeProjectionRenderer.TryProjectRoomPoint(settings, aspect,
                    worldPoint, out var point)) return;
            roomCalibrationAnchors.Add(new RoomCalibrationAnchor
            {
                label = label,
                worldPoint = worldPoint,
                targetPoint = point,
                adjusted = false
            });
        }

        private void ResetRoomCalibrationTargets()
        {
            BuildRoomCalibrationAnchors();
            FitRoomCalibrationPreview();
            status = "多点校正の交点を現在の投影位置へ戻しました";
        }

        private void FitRoomCalibrationPreview()
        {
            if (roomCalibrationAnchors.Count == 0) return;
            var aspect = GetProjectionAspect();
            var maxHorizontal = 0.001f;
            var maxVertical = 0.001f;
            var previousScale = controlPreviewScale;
            controlPreviewScale = 1f;
            foreach (var anchor in roomCalibrationAnchors)
            {
                var point = ApplyControlPreviewPoint(anchor.targetPoint, aspect);
                maxHorizontal = Mathf.Max(maxHorizontal, Mathf.Abs(point.x - 0.5f));
                maxVertical = Mathf.Max(maxVertical, Mathf.Abs(point.y - 0.5f));
            }
            controlPreviewScale = previousScale;
            controlPreviewScale = Mathf.Clamp(Mathf.Min(0.44f / maxHorizontal,
                0.39f / maxVertical), 0.01f, 1f);
        }

        private void SolveRoomProjectorFromAnchors(float aspect)
        {
            if (roomCalibrationAnchors.Count < 4)
            {
                status = "多点校正には画面内の室内交点が4点以上必要です";
                return;
            }

            var parameters = GetRoomProjectorParameters();
            var startParameters = GetRoomCalibrationStartParameters(parameters);
            var scales = new[] { 0.08, 0.08, 0.08, 1.0, 1.0, 1.0, 1.5 };
            var priorSigmas = new[]
            {
                Math.Max(0.30, settings.roomWidth * 0.12),
                Math.Max(0.20, settings.roomHeight * 0.12),
                Math.Max(0.30, settings.roomDepth * 0.12),
                15.0, 15.0, 12.0, 18.0
            };
            var adjustedCount = CountAdjustedRoomCalibrationAnchors();
            // With only one or two observations, seven free projector
            // parameters have many mathematically valid but physically absurd
            // solutions. Keep the estimate near the measured/rough starting
            // pose until enough independent room intersections are supplied.
            var priorWeight = adjustedCount switch
            {
                0 => 8.0,
                1 => 5.0,
                2 => 2.5,
                3 => 1.0,
                _ => 0.12
            };
            var lambda = 0.003;
            var bestCost = EvaluateRoomCalibration(parameters, aspect, startParameters,
                priorSigmas, priorWeight, adjustedCount, out var baseProjection);
            if (double.IsInfinity(bestCost)) return;
            for (var iteration = 0; iteration < 10; iteration++)
            {
                const double epsilon = 0.02;
                var jacobian = new double[roomCalibrationAnchors.Count * 2, 7];
                for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++)
                {
                    var perturbed = (double[])parameters.Clone();
                    perturbed[parameterIndex] += scales[parameterIndex] * epsilon;
                    ClampRoomProjectorParameters(perturbed);
                    if (double.IsInfinity(EvaluateRoomCalibration(perturbed, aspect,
                            startParameters, priorSigmas, priorWeight, adjustedCount,
                            out var perturbedProjection))) continue;
                    for (var anchorIndex = 0; anchorIndex < roomCalibrationAnchors.Count;
                         anchorIndex++)
                    {
                        jacobian[anchorIndex * 2, parameterIndex] =
                            (perturbedProjection[anchorIndex].x - baseProjection[anchorIndex].x)
                            / epsilon;
                        jacobian[anchorIndex * 2 + 1, parameterIndex] =
                            (perturbedProjection[anchorIndex].y - baseProjection[anchorIndex].y)
                            / epsilon;
                    }
                }

                var normal = new double[7, 7];
                var right = new double[7];
                for (var anchorIndex = 0; anchorIndex < roomCalibrationAnchors.Count;
                     anchorIndex++)
                {
                    var anchor = roomCalibrationAnchors[anchorIndex];
                    var weight = GetRoomAnchorWeight(anchor, adjustedCount);
                    var residualX = anchor.targetPoint.x - baseProjection[anchorIndex].x;
                    var residualY = anchor.targetPoint.y - baseProjection[anchorIndex].y;
                    for (var column = 0; column < 7; column++)
                    {
                        var jx = jacobian[anchorIndex * 2, column];
                        var jy = jacobian[anchorIndex * 2 + 1, column];
                        right[column] += weight * (jx * residualX + jy * residualY);
                        for (var row = 0; row < 7; row++)
                            normal[column, row] += weight *
                                (jx * jacobian[anchorIndex * 2, row]
                                 + jy * jacobian[anchorIndex * 2 + 1, row]);
                    }
                }
                for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++)
                {
                    var derivative = scales[parameterIndex] / priorSigmas[parameterIndex];
                    var residual = (startParameters[parameterIndex]
                                    - parameters[parameterIndex]) / priorSigmas[parameterIndex];
                    normal[parameterIndex, parameterIndex] +=
                        priorWeight * derivative * derivative;
                    right[parameterIndex] += priorWeight * derivative * residual;
                }
                for (var diagonal = 0; diagonal < 7; diagonal++)
                    normal[diagonal, diagonal] += lambda;
                if (!TrySolveLinearSystem(normal, right, out var delta)) break;

                var candidate = (double[])parameters.Clone();
                for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++)
                    candidate[parameterIndex] += scales[parameterIndex]
                                                 * Math.Max(-1.5, Math.Min(1.5, delta[parameterIndex]));
                ClampRoomProjectorParameters(candidate);
                var candidateCost = EvaluateRoomCalibration(candidate, aspect,
                    startParameters, priorSigmas, priorWeight, adjustedCount,
                    out var candidateProjection);
                if (candidateCost < bestCost)
                {
                    parameters = candidate;
                    baseProjection = candidateProjection;
                    bestCost = candidateCost;
                    lambda = Math.Max(0.00001, lambda * 0.35);
                }
                else
                {
                    SetRoomProjectorParameters(parameters);
                    lambda *= 8.0;
                }
            }
            SetRoomProjectorParameters(parameters);
            settings.Clamp();
            status = $"室内多点校正中：{adjustedCount}点を指定 / 誤差 {Math.Sqrt(bestCost):0.0000}";
        }

        private double EvaluateRoomCalibration(double[] parameters, float aspect,
            double[] startParameters, double[] priorSigmas, double priorWeight,
            int adjustedCount, out Vector2[] projected)
        {
            SetRoomProjectorParameters(parameters);
            projected = new Vector2[roomCalibrationAnchors.Count];
            var cost = 0.0;
            for (var index = 0; index < roomCalibrationAnchors.Count; index++)
            {
                var anchor = roomCalibrationAnchors[index];
                if (!DomeProjectionRenderer.TryProjectRoomPoint(settings, aspect,
                        anchor.worldPoint, out projected[index]))
                    return double.PositiveInfinity;
                var difference = anchor.targetPoint - projected[index];
                var weight = GetRoomAnchorWeight(anchor, adjustedCount);
                cost += weight * difference.sqrMagnitude;
            }
            for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++)
            {
                var difference = (parameters[parameterIndex]
                                  - startParameters[parameterIndex])
                                 / priorSigmas[parameterIndex];
                cost += priorWeight * difference * difference;
            }
            return cost;
        }

        private int CountAdjustedRoomCalibrationAnchors()
        {
            var count = 0;
            foreach (var anchor in roomCalibrationAnchors)
                if (anchor.adjusted) count++;
            return count;
        }

        private static double GetRoomAnchorWeight(RoomCalibrationAnchor anchor,
            int adjustedCount)
        {
            if (anchor.adjusted) return 8.0;
            // Unedited points describe the current plausible projection. They
            // are deliberately stronger during the first few drags so one
            // point cannot send FOV or pitch to its hard limit.
            return adjustedCount < 2 ? 2.0 : adjustedCount < 4 ? 0.9 : 0.30;
        }

        private double[] GetRoomProjectorParameters()
        {
            return new[]
            {
                (double)settings.roomProjectorX, settings.roomProjectorY,
                settings.roomProjectorZ, settings.roomProjectorYaw,
                settings.roomProjectorPitch, settings.roomProjectorRoll,
                settings.roomProjectorHorizontalFov
            };
        }

        private double[] GetRoomCalibrationStartParameters(double[] fallback)
        {
            if (roomCalibrationStartProfile == null) return (double[])fallback.Clone();
            return new[]
            {
                (double)roomCalibrationStartProfile.roomProjectorX,
                roomCalibrationStartProfile.roomProjectorY,
                roomCalibrationStartProfile.roomProjectorZ,
                roomCalibrationStartProfile.roomProjectorYaw,
                roomCalibrationStartProfile.roomProjectorPitch,
                roomCalibrationStartProfile.roomProjectorRoll,
                roomCalibrationStartProfile.roomProjectorHorizontalFov
            };
        }

        private void SetRoomProjectorParameters(double[] values)
        {
            settings.roomProjectorX = (float)values[0];
            settings.roomProjectorY = (float)values[1];
            settings.roomProjectorZ = (float)values[2];
            settings.roomProjectorYaw = (float)values[3];
            settings.roomProjectorPitch = (float)values[4];
            settings.roomProjectorRoll = (float)values[5];
            settings.roomProjectorHorizontalFov = (float)values[6];
        }

        private static void ClampRoomProjectorParameters(double[] values)
        {
            values[0] = Math.Max(-30.0, Math.Min(30.0, values[0]));
            values[1] = Math.Max(-2.0, Math.Min(12.0, values[1]));
            values[2] = Math.Max(-5.0, Math.Min(35.0, values[2]));
            values[3] = Math.Max(-180.0, Math.Min(180.0, values[3]));
            values[4] = Math.Max(-89.0, Math.Min(89.0, values[4]));
            values[5] = Math.Max(-180.0, Math.Min(180.0, values[5]));
            values[6] = Math.Max(10.0, Math.Min(170.0, values[6]));
        }

        private static bool TrySolveLinearSystem(double[,] matrix, double[] vector,
            out double[] solution)
        {
            const int size = 7;
            var augmented = new double[size, size + 1];
            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                    augmented[row, column] = matrix[row, column];
                augmented[row, size] = vector[row];
            }
            for (var pivot = 0; pivot < size; pivot++)
            {
                var bestRow = pivot;
                for (var row = pivot + 1; row < size; row++)
                    if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[bestRow, pivot]))
                        bestRow = row;
                if (Math.Abs(augmented[bestRow, pivot]) < 1e-12)
                {
                    solution = null;
                    return false;
                }
                if (bestRow != pivot)
                    for (var column = pivot; column <= size; column++)
                    {
                        var swap = augmented[pivot, column];
                        augmented[pivot, column] = augmented[bestRow, column];
                        augmented[bestRow, column] = swap;
                    }
                var divisor = augmented[pivot, pivot];
                for (var column = pivot; column <= size; column++)
                    augmented[pivot, column] /= divisor;
                for (var row = 0; row < size; row++)
                {
                    if (row == pivot) continue;
                    var factor = augmented[row, pivot];
                    for (var column = pivot; column <= size; column++)
                        augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
            solution = new double[size];
            for (var row = 0; row < size; row++) solution[row] = augmented[row, size];
            return true;
        }

        private void DrawPreviewAngleControl(float x, float y, string label,
            ref float value, float minimum, float maximum, bool wrap)
        {
            GUI.Label(new Rect(x, y + 5f, 74f, 24f), $"{label} {value:0}°", smallStyle);
            if (GUI.Button(new Rect(x + 76f, y, 40f, 30f), "－5"))
                value = wrap ? NormalizeControlPreviewRotation(value - 5f)
                    : Mathf.Clamp(value - 5f, minimum, maximum);
            value = GUI.HorizontalSlider(new Rect(x + 122f, y + 7f, 100f, 18f),
                value, minimum, maximum);
            if (GUI.Button(new Rect(x + 228f, y, 40f, 30f), "+5"))
                value = wrap ? NormalizeControlPreviewRotation(value + 5f)
                    : Mathf.Clamp(value + 5f, minimum, maximum);
            if (GUI.Button(new Rect(x + 274f, y, 40f, 30f), "0°")) value = 0f;
        }

        private void DrawRoomWarpHandle(int index, Vector2 screenPoint, Vector2 idealPoint,
            float logicalWidth, float logicalHeight)
        {
            const float hitSize = 34f;
            // Never substitute a screen-edge point for a real room-face
            // corner. That made the displayed handle disagree with the value
            // being edited and caused large jumps. "全体" / auto-fit makes
            // the true point visible instead.
            if (screenPoint.x < -hitSize || screenPoint.x > logicalWidth + hitSize
                || screenPoint.y < -hitSize || screenPoint.y > logicalHeight + hitSize)
                return;
            var visiblePoint = screenPoint;
            var hitRect = new Rect(visiblePoint.x - hitSize * 0.5f,
                visiblePoint.y - hitSize * 0.5f, hitSize, hitSize);
            if (showControls && panelAreaRect.Contains(visiblePoint)) return;

            // Let IMGUI perform the drag itself. Its native window drag path
            // already accounts for Retina/DPI scaling and GUI.matrix, unlike
            // manually comparing Event.mousePosition in platform-dependent
            // coordinate spaces.
            var movedRect = DrawDraggableHandleWindow(62010 + index, hitRect);
            if ((movedRect.position - hitRect.position).sqrMagnitude > 0.01f)
            {
                visiblePoint = movedRect.center;
                var displayPoint = new Vector2(
                    visiblePoint.x / Mathf.Max(logicalWidth, 1f),
                    1f - visiblePoint.y / Mathf.Max(logicalHeight, 1f));
                var aspect = GetProjectionAspect();
                var targetPoint = InverseControlPreviewPoint(displayPoint, aspect);
                targetPoint.x = Mathf.Clamp(targetPoint.x, -2f, 3f);
                targetPoint.y = Mathf.Clamp(targetPoint.y, -2f, 3f);
                var previousPoint = GetRoomWarpPoint(index);
                SetRoomWarpPoint(index, targetPoint - idealPoint);
                if (!IsCurrentRoomWarpQuadValid(aspect))
                {
                    SetRoomWarpPoint(index, previousPoint);
                    status = "4隅が交差する位置には移動できません";
                }
            }

            var handleColor = index < 2
                ? new Color(1f, 0.76f, 0.12f, 1f)
                : new Color(0.10f, 0.82f, 1f, 1f);
            DrawSolidGuiRect(new Rect(visiblePoint.x - 13f, visiblePoint.y - 2f, 26f, 4f), handleColor);
            DrawSolidGuiRect(new Rect(visiblePoint.x - 2f, visiblePoint.y - 13f, 4f, 26f), handleColor);
            DrawSolidGuiRect(new Rect(visiblePoint.x - 6f, visiblePoint.y - 6f, 12f, 12f), Color.white);
            DrawSolidGuiRect(new Rect(visiblePoint.x - 3f, visiblePoint.y - 3f, 6f, 6f), handleColor);
            var face = GetRoomReferenceFaceLabel();
            var labels = new[] { face + " 左上", face + " 右上", face + " 左下", face + " 右下" };
            GUI.Label(new Rect(visiblePoint.x + 14f, visiblePoint.y - 12f, 78f, 24f),
                labels[index], smallStyle);
        }

        private Vector2 GetRoomWarpPoint(int index)
        {
            return index switch
            {
                0 => new Vector2(settings.roomWarpTopLeftX, settings.roomWarpTopLeftY),
                1 => new Vector2(settings.roomWarpTopRightX, settings.roomWarpTopRightY),
                2 => new Vector2(settings.roomWarpBottomLeftX, settings.roomWarpBottomLeftY),
                _ => new Vector2(settings.roomWarpBottomRightX, settings.roomWarpBottomRightY)
            };
        }

        private void SetRoomWarpPoint(int index, Vector2 value)
        {
            switch (index)
            {
                case 0:
                    settings.roomWarpTopLeftX = value.x;
                    settings.roomWarpTopLeftY = value.y;
                    break;
                case 1:
                    settings.roomWarpTopRightX = value.x;
                    settings.roomWarpTopRightY = value.y;
                    break;
                case 2:
                    settings.roomWarpBottomLeftX = value.x;
                    settings.roomWarpBottomLeftY = value.y;
                    break;
                default:
                    settings.roomWarpBottomRightX = value.x;
                    settings.roomWarpBottomRightY = value.y;
                    break;
            }
        }

        private bool IsCurrentRoomWarpQuadValid(float aspect)
        {
            DomeProjectionRenderer.CalculateRoomReferenceCorners(settings, aspect,
                out var topLeft, out var topRight, out var bottomLeft, out var bottomRight);
            return DomeProjectionRenderer.IsValidRoomQuad(
                topLeft + GetRoomWarpPoint(0), topRight + GetRoomWarpPoint(1),
                bottomLeft + GetRoomWarpPoint(2), bottomRight + GetRoomWarpPoint(3));
        }

        private Vector2 RoomWarpPointToGui(Vector2 point, float width, float height, float aspect)
        {
            var displayed = ApplyControlPreviewPoint(point, aspect);
            return new Vector2(displayed.x * width, (1f - displayed.y) * height);
        }

        private Vector2 ApplyControlPreviewPoint(Vector2 point, float aspect)
        {
            aspect = Mathf.Max(aspect, 0.01f);
            var offset = point - Vector2.one * 0.5f;
            offset.x *= aspect;
            var ray = new Vector3(offset.x, offset.y, 0.5f);
            ray = RotateControlPreviewYaw(ray, -controlPreviewYaw);
            ray = RotateControlPreviewPitch(ray, controlPreviewPitch);
            if (ray.z <= 0.0001f)
                return new Vector2(Mathf.Sign(ray.x) * 1000f, Mathf.Sign(ray.y) * 1000f);
            offset = new Vector2(ray.x, ray.y) * (0.5f / ray.z);
            var angle = controlPreviewRotation * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            offset = new Vector2(cosine * offset.x - sine * offset.y,
                sine * offset.x + cosine * offset.y);
            offset *= Mathf.Clamp(controlPreviewScale, 0.01f, 1f);
            offset.x /= aspect;
            return offset + Vector2.one * 0.5f;
        }

        private Vector2 InverseControlPreviewPoint(Vector2 point, float aspect)
        {
            aspect = Mathf.Max(aspect, 0.01f);
            var offset = point - Vector2.one * 0.5f;
            offset.x *= aspect;
            offset /= Mathf.Clamp(controlPreviewScale, 0.01f, 1f);
            var angle = controlPreviewRotation * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            offset = new Vector2(cosine * offset.x + sine * offset.y,
                -sine * offset.x + cosine * offset.y);
            var ray = new Vector3(offset.x, offset.y, 0.5f);
            ray = RotateControlPreviewPitch(ray, -controlPreviewPitch);
            ray = RotateControlPreviewYaw(ray, controlPreviewYaw);
            if (ray.z <= 0.0001f)
                return new Vector2(Mathf.Sign(ray.x) * 1000f, Mathf.Sign(ray.y) * 1000f);
            offset = new Vector2(ray.x, ray.y) * (0.5f / ray.z);
            offset.x /= aspect;
            return offset + Vector2.one * 0.5f;
        }

        private static Vector3 RotateControlPreviewYaw(Vector3 ray, float degrees)
        {
            var angle = degrees * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            return new Vector3(cosine * ray.x + sine * ray.z, ray.y,
                -sine * ray.x + cosine * ray.z);
        }

        private static Vector3 RotateControlPreviewPitch(Vector3 ray, float degrees)
        {
            var angle = degrees * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            return new Vector3(ray.x, cosine * ray.y - sine * ray.z,
                sine * ray.y + cosine * ray.z);
        }

        private bool IsControlPreviewTransformed()
        {
            return controlPreviewScale < 0.999f
                   || Mathf.Abs(controlPreviewRotation) > 0.01f
                   || Mathf.Abs(controlPreviewYaw) > 0.01f
                   || Mathf.Abs(controlPreviewPitch) > 0.01f;
        }

        private void ResetControlPreview()
        {
            controlPreviewScale = 1f;
            controlPreviewRotation = 0f;
            controlPreviewYaw = 0f;
            controlPreviewPitch = 0f;
        }

        private static float NormalizeControlPreviewRotation(float value)
        {
            return Mathf.Repeat(value + 180f, 360f) - 180f;
        }

        private static float GetProjectionAspect()
        {
            var outputAspect = ProjectionProcessBridge.OutputAspect;
            if (outputAspect > 0.1f) return outputAspect;
            return Screen.height > 0 ? (float)Screen.width / Screen.height : 1f;
        }

        private void DrawControlPreviewFrame(float logicalWidth, float logicalHeight,
            float margin, bool showResetButton)
        {
            if (showResetButton)
            {
                const float width = 520f;
                var x = (logicalWidth - width) * 0.5f;
                var bar = new Rect(x, margin, width, 38f);
                DrawSolidGuiRect(bar, new Color(0.12f, 0.14f, 0.17f, 0.94f));
                GUI.Label(new Rect(x + 10f, margin + 8f, 335f, 24f),
                    $"Control確認 {controlPreviewScale * 100f:0}% / 左右{controlPreviewYaw:0}° / 上下{controlPreviewPitch:0}° / 傾き{controlPreviewRotation:0}°",
                    smallStyle);
                if (GUI.Button(new Rect(x + 350f, margin + 5f, 160f, 28f),
                        "Output表示に戻す"))
                    ResetControlPreview();
                return;
            }

            var aspect = GetProjectionAspect();
            var topLeft = RoomWarpPointToGui(new Vector2(0f, 1f), logicalWidth, logicalHeight, aspect);
            var topRight = RoomWarpPointToGui(new Vector2(1f, 1f), logicalWidth, logicalHeight, aspect);
            var bottomRight = RoomWarpPointToGui(new Vector2(1f, 0f), logicalWidth, logicalHeight, aspect);
            var bottomLeft = RoomWarpPointToGui(new Vector2(0f, 0f), logicalWidth, logicalHeight, aspect);
            var frameColor = new Color(0.2f, 0.85f, 1f, 0.92f);
            DrawGuiSegment(topLeft, topRight, frameColor, 2f);
            DrawGuiSegment(topRight, bottomRight, frameColor, 2f);
            DrawGuiSegment(bottomRight, bottomLeft, frameColor, 2f);
            DrawGuiSegment(bottomLeft, topLeft, frameColor, 2f);

        }

        private void FitRoomWarpPreview(float logicalWidth, float logicalHeight)
        {
            var aspect = GetProjectionAspect();
            DomeProjectionRenderer.CalculateRoomReferenceCorners(settings, aspect,
                out var topLeft, out var topRight, out var bottomLeft, out var bottomRight);
            var points = new[]
            {
                topLeft + GetRoomWarpPoint(0), topRight + GetRoomWarpPoint(1),
                bottomLeft + GetRoomWarpPoint(2), bottomRight + GetRoomWarpPoint(3)
            };
            var maxHorizontal = 0.001f;
            var maxVertical = 0.001f;
            foreach (var sourcePoint in points)
            {
                // Fit the already-rotated view, then apply the calculated
                // uniform scale. Rotation therefore never pushes a handle out
                // of the Control window after "全体" is pressed.
                var previousScale = controlPreviewScale;
                controlPreviewScale = 1f;
                var point = ApplyControlPreviewPoint(sourcePoint, aspect);
                controlPreviewScale = previousScale;
                maxHorizontal = Mathf.Max(maxHorizontal, Mathf.Abs(point.x - 0.5f));
                maxVertical = Mathf.Max(maxVertical, Mathf.Abs(point.y - 0.5f));
            }

            // Leave room for labels and the toolbar while keeping all four
            // projected corners inside the Control window.
            var horizontalScale = 0.44f / maxHorizontal;
            var verticalScale = 0.39f / maxVertical;
            controlPreviewScale = Mathf.Clamp(Mathf.Min(horizontalScale, verticalScale), 0.01f, 1f);
        }

        private string GetRoomReferenceFaceLabel()
        {
            return settings.roomWarpReferenceFace switch
            {
                1 => "天井",
                2 => "床",
                3 => "左壁",
                4 => "右壁",
                _ => "前面"
            };
        }

        private void DrawDomeSimulationEditor(float logicalWidth, float logicalHeight, float margin)
        {
            var available = new Rect(margin, 58f, logicalWidth - margin * 2f,
                logicalHeight - 78f);
            if (showControls)
            {
                const float panelButtonAllowance = 165f;
                if (settings.controlPanelOnRight)
                    available.xMax = panelAreaRect.x - panelButtonAllowance;
                else
                {
                    available.xMin = panelAreaRect.xMax + panelButtonAllowance;
                    available.width = logicalWidth - margin - available.xMin;
                }
            }
            // During physical calibration the toolbar owns the bottom of the
            // window.  Previously the 3D diagram continued underneath it and
            // consumed slider drags as orbit gestures before the sliders saw
            // the event.
            if (domeGuiEditing)
            {
                var toolbarBounds = GetDomePhysicalCalibrationToolbarRect(
                    logicalWidth, logicalHeight, margin);
                available.yMax = Mathf.Min(available.yMax,
                    toolbarBounds.y - 8f);
            }
            if (available.width < 280f || available.height < 260f) return;

            DrawSolidGuiRect(available, new Color(0.025f, 0.034f, 0.048f, 1f));
            var projectionAspectForInset = Mathf.Max(GetProjectionAspect(), 0.25f);
            Rect diagram;
            Rect outputPreviewPanel;
            if (available.width >= 760f)
            {
                var previewColumnWidth = Mathf.Clamp(available.width * 0.28f, 220f, 360f);
                diagram = new Rect(available.x + 24f, available.y + 44f,
                    available.width - previewColumnWidth - 64f,
                    available.height - 80f);
                outputPreviewPanel = new Rect(diagram.xMax + 16f,
                    available.y + 54f, previewColumnWidth,
                    available.height - 86f);
            }
            else
            {
                diagram = new Rect(available.x + 24f, available.y + 44f,
                    available.width - 48f, available.height - 80f);
                var insetWidth = Mathf.Clamp(available.width * 0.34f, 180f, 260f);
                var insetHeight = insetWidth / projectionAspectForInset + 38f;
                outputPreviewPanel = new Rect(diagram.xMax - insetWidth - 8f,
                    diagram.yMax - insetHeight - 8f, insetWidth, insetHeight);
            }
            var projector = settings.projectionRegionMode == 1
                ? new Vector3(settings.offAxisProjectorX, settings.offAxisProjectorHeight,
                    -settings.offAxisProjectorDistance)
                : Vector3.zero;
            var worldExtent = Mathf.Max(1.15f, projector.magnitude + 0.25f);
            var baseScale = Mathf.Min(diagram.width, diagram.height)
                            / (worldExtent * 2.5f) * domeSimulationZoom;
            var viewRotation = Quaternion.Euler(domeSimulationPitch,
                -domeSimulationYaw, 0f);
            Vector2 Project(Vector3 world)
            {
                var view = viewRotation * world;
                return diagram.center + new Vector2(view.x, -view.y) * baseScale;
            }

            var current = Event.current;
            var mouse = GetLogicalMousePosition();
            var pointerOverOutputPreview = outputPreviewPanel.Contains(mouse);
            var calibrationToolbar = domeGuiEditing
                ? GetDomePhysicalCalibrationToolbarRect(logicalWidth,
                    logicalHeight, margin)
                : Rect.zero;
            var pointerOverCalibrationToolbar = domeGuiEditing
                                                && calibrationToolbar.Contains(mouse);
            if (pointerOverCalibrationToolbar
                && current.type == EventType.MouseDown)
            {
                domeSimulationOrbitDragging = false;
                domeSimulationProjectorDragging = false;
                domeSimulationAimDragging = false;
            }
            var resetView = new Rect(available.xMax - 132f, available.y + 8f, 116f, 28f);
            var projectorPoint = Project(projector);
            var hasAxisHit = DomeProjectionRenderer.TryGetDomeHitAtOpticalPoint(settings,
                Vector2.zero, out _, out var axisHit);
            var axisPoint = hasAxisHit ? Project(axisHit) : Vector2.zero;
            var physicalEditing = domeGuiEditing && settings.projectionRegionMode == 1;
            var projectorHandleHovered = physicalEditing
                                         && Vector2.Distance(mouse, projectorPoint) <= 24f;
            var axisHandleHovered = physicalEditing && hasAxisHit
                                    && Vector2.Distance(mouse, axisPoint) <= 22f;
            if (GUI.Button(resetView, "3D視点をリセット"))
            {
                domeSimulationYaw = 35f;
                domeSimulationPitch = 22f;
                domeSimulationZoom = 1f;
            }
            if (!pointerOverCalibrationToolbar
                && current.type == EventType.ScrollWheel && diagram.Contains(mouse)
                && !pointerOverOutputPreview && !resetView.Contains(mouse))
            {
                domeSimulationZoom = Mathf.Clamp(domeSimulationZoom
                    * Mathf.Exp(-current.delta.y * 0.08f), 0.25f, 5f);
                current.Use();
            }
            else if (!pointerOverCalibrationToolbar
                     && current.type == EventType.MouseDown && current.button == 0
                     && domeGuiEditing && settings.projectionRegionMode == 1
                     && axisHandleHovered)
            {
                PushDomeGuiUndoState();
                domeSimulationAimDragging = true;
                domeSimulationAimLastMouse = mouse;
                current.Use();
            }
            else if (!pointerOverCalibrationToolbar
                     && current.type == EventType.MouseDown && current.button == 0
                     && domeGuiEditing && settings.projectionRegionMode == 1
                     && projectorHandleHovered)
            {
                PushDomeGuiUndoState();
                domeSimulationProjectorDragging = true;
                domeSimulationProjectorLastMouse = mouse;
                current.Use();
            }
            else if (!pointerOverCalibrationToolbar
                     && current.type == EventType.MouseDown && current.button == 0
                     && diagram.Contains(mouse) && !pointerOverOutputPreview
                     && !resetView.Contains(mouse)
                     && !(settings.projectionRegionMode == 1
                          && (projectorHandleHovered || axisHandleHovered)))
            {
                domeSimulationOrbitDragging = true;
                domeSimulationLastMouse = mouse;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag
                     && domeSimulationProjectorDragging)
            {
                var delta = mouse - domeSimulationProjectorLastMouse;
                domeSimulationProjectorLastMouse = mouse;
                var viewDelta = new Vector3(delta.x / Mathf.Max(baseScale, 0.001f),
                    -delta.y / Mathf.Max(baseScale, 0.001f), 0f);
                var worldDelta = Quaternion.Inverse(viewRotation) * viewDelta;
                settings.offAxisProjectorX = Mathf.Clamp(
                    settings.offAxisProjectorX + worldDelta.x, -2f, 2f);
                settings.offAxisProjectorHeight = Mathf.Clamp(
                    settings.offAxisProjectorHeight + worldDelta.y, -1.5f, 1.5f);
                settings.offAxisProjectorDistance = Mathf.Clamp(
                    settings.offAxisProjectorDistance - worldDelta.z, 0f, 2f);
                status = $"端置き設置　X {settings.offAxisProjectorX:0.00} / 距離 {settings.offAxisProjectorDistance:0.00} / 高さ {settings.offAxisProjectorHeight:0.00} R";
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && domeSimulationAimDragging)
            {
                // Convert the pointer back to the visible unit sphere. The
                // optical axis can therefore be placed directly on a guide
                // landmark (for example the zenith) instead of being adjusted
                // by an unrelated screen-space offset.
                var viewX = (mouse.x - diagram.center.x) / Mathf.Max(baseScale, 0.001f);
                var viewY = -(mouse.y - diagram.center.y) / Mathf.Max(baseScale, 0.001f);
                var radialSquared = viewX * viewX + viewY * viewY;
                if (radialSquared <= 1f)
                {
                    var currentAxisView = viewRotation * axisHit;
                    var viewZ = Mathf.Sqrt(Mathf.Max(0f, 1f - radialSquared));
                    if (currentAxisView.z < 0f) viewZ = -viewZ;
                    var target = Quaternion.Inverse(viewRotation)
                                 * new Vector3(viewX, viewY, viewZ);
                    AimOffAxisProjectorAt(target);
                    status = $"光軸先端を球面上で調整　Yaw {settings.offAxisAimYaw:0.0}° / 仰角 {settings.offAxisAimElevation:0.0}°";
                }
                domeSimulationAimLastMouse = mouse;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && domeSimulationOrbitDragging)
            {
                var delta = mouse - domeSimulationLastMouse;
                domeSimulationLastMouse = mouse;
                // Treat the sphere like an object held by the pointer: when
                // the pointer moves right, the visible sphere follows right.
                domeSimulationYaw = NormalizeControlPreviewRotation(
                    domeSimulationYaw - delta.x * 0.3f);
                domeSimulationPitch = Mathf.Clamp(domeSimulationPitch
                    - delta.y * 0.3f, -89f, 89f);
                current.Use();
            }
            if (current.rawType == EventType.MouseUp)
            {
                domeSimulationOrbitDragging = false;
                domeSimulationProjectorDragging = false;
                domeSimulationAimDragging = false;
            }

            GUI.Label(new Rect(available.x + 16f, available.y + 10f,
                    available.width - 164f, 28f),
                settings.projectionRegionMode == 1
                    ? "半球・端置き 3D投影シミュレーション"
                    : "半球・中心 3D投影シミュレーション",
                sectionTitleStyle);
            GUI.Label(new Rect(available.x + 16f, available.y + 31f,
                    available.width - 164f, 22f),
                settings.projectionRegionMode == 1
                    ? "黄色：Output矩形基準面／桃色：光軸／水色：投影範囲のみ明るい半球ガイド／空き領域：視点回転"
                    : "黄色：Output矩形基準面／水色：投影範囲のみ明るい半球ガイド／空き領域：視点回転",
                smallStyle);

            var viewToggle = new Rect(available.xMax - 390f, available.y + 8f, 116f, 28f);
            if (GUI.Button(viewToggle,
                    domeSimulationShowFullSphere ? "範囲：全球確認" : "範囲：実設定"))
                domeSimulationShowFullSphere = !domeSimulationShowFullSphere;
            var materialToggle = new Rect(viewToggle.x - 112f, viewToggle.y, 104f, 28f);
            if (GUI.Button(materialToggle,
                    domeSimulationShowMaterial ? "素材：表示" : "素材：非表示"))
                domeSimulationShowMaterial = !domeSimulationShowMaterial;
            var guideToggle = new Rect(materialToggle.x - 144f, viewToggle.y, 136f, 28f);
            if (GUI.Button(guideToggle,
                    domeSimulationShowGuide ? "半球ガイド：表示" : "半球ガイド：非表示"))
                domeSimulationShowGuide = !domeSimulationShowGuide;

            if ((domeSimulationShowMaterial || domeSimulationShowGuide)
                && domeSimulationPreviewRenderer != null)
            {
                GetRoomContentPreviewSource(out var previewTexture, out var previewFlipY,
                    out var previewContentMode, out var previewWidth, out var previewHeight,
                    out _);
                if (!domeSimulationShowMaterial) previewTexture = null;
                var configuredMinimumElevation = settings.projectionRegionMode == 1
                    ? settings.offAxisMinimumElevation
                    : 90f - settings.domeFov * 0.5f;
                var previewMinimumElevation = domeSimulationShowFullSphere
                    ? -90f : Mathf.Clamp(configuredMinimumElevation, -90f, 89f);
                var rendered = domeSimulationPreviewRenderer.Render(settings,
                    previewTexture, previewFlipY, previewContentMode, previewWidth,
                    previewHeight, domeSimulationYaw, domeSimulationPitch, worldExtent,
                    domeSimulationZoom, previewMinimumElevation,
                    domeSimulationShowGuide, Mathf.RoundToInt(diagram.width),
                    Mathf.RoundToInt(diagram.height));
                if (rendered != null)
                    GUI.DrawTexture(diagram, rendered, ScaleMode.StretchToFill, false);
            }

            var configuredScreenMinimum = domeSimulationShowFullSphere
                ? -90f
                : settings.projectionRegionMode == 1
                    ? settings.offAxisMinimumElevation
                    : 90f - settings.domeFov * 0.5f;

            // Fixed physical screen reference. This intentionally does not
            // move with projector aim. The Output-matched guide is rendered
            // directly on the shaded sphere by DomeSimulationPreview.
            // The shaded sphere already communicates the physical screen.
            // Keep only a very faint, sparse reference cage so it does not
            // compete with the real projected footprint or hemisphere guide.
            var screenReferenceColor = new Color(0.76f, 0.80f, 0.86f, 0.055f);
            void DrawPhysicalLatitude(float elevation, float thickness)
            {
                const int segments = 96;
                Vector2? previous = null;
                var elevationRadians = elevation * Mathf.Deg2Rad;
                var radius = Mathf.Cos(elevationRadians);
                var y = Mathf.Sin(elevationRadians);
                for (var segment = 0; segment <= segments; segment++)
                {
                    var azimuth = segment * Mathf.PI * 2f / segments;
                    var point = Project(new Vector3(radius * Mathf.Sin(azimuth), y,
                        radius * Mathf.Cos(azimuth)));
                    if (previous.HasValue)
                        DrawGuiSegment(previous.Value, point, screenReferenceColor, thickness);
                    previous = point;
                }
            }
            void DrawPhysicalMeridian(float azimuthDegrees)
            {
                const int segments = 36;
                Vector2? previous = null;
                var azimuth = azimuthDegrees * Mathf.Deg2Rad;
                for (var segment = 0; segment <= segments; segment++)
                {
                    var elevation = Mathf.Lerp(configuredScreenMinimum, 90f,
                        segment / (float)segments) * Mathf.Deg2Rad;
                    var radius = Mathf.Cos(elevation);
                    var point = Project(new Vector3(radius * Mathf.Sin(azimuth),
                        Mathf.Sin(elevation), radius * Mathf.Cos(azimuth)));
                    if (previous.HasValue)
                        DrawGuiSegment(previous.Value, point, screenReferenceColor, 0.35f);
                    previous = point;
                }
            }
            DrawPhysicalLatitude(configuredScreenMinimum, 0.55f);
            if (configuredScreenMinimum < -0.01f) DrawPhysicalLatitude(0f, 0.45f);
            for (var azimuth = 0; azimuth < 360; azimuth += 90)
                DrawPhysicalMeridian(azimuth);

            if (domeSimulationShowGuide)
            {
                var physicalZenith = Project(Vector3.up);
                DrawCrossHandle(physicalZenith, Color.white);
                GUI.Label(new Rect(physicalZenith.x + 13f, physicalZenith.y - 13f,
                    230f, 24f), "実スクリーン天頂（固定）", smallStyle);
                var horizonFront = Project(Vector3.forward);
                DrawCrossHandle(horizonFront, new Color(0.76f, 0.84f, 0.92f, 0.85f));
                GUI.Label(new Rect(horizonFront.x + 13f, horizonFront.y - 13f,
                    190f, 24f), "実スクリーン水平0°（固定）", smallStyle);
            }

            // World axes make it clear which way is up and where the optical
            // axis travels when the edge-mounted projector is aimed.
            DrawGuiSegment(Project(new Vector3(-1.25f, 0f, 0f)),
                Project(new Vector3(1.25f, 0f, 0f)),
                new Color(1f, 0.20f, 0.18f, 0.75f), 2f);
            DrawGuiSegment(Project(new Vector3(0f, -1.25f, 0f)),
                Project(new Vector3(0f, 1.25f, 0f)),
                new Color(0.20f, 1f, 0.30f, 0.75f), 2f);
            DrawGuiSegment(Project(new Vector3(0f, 0f, -1.25f)),
                Project(new Vector3(0f, 0f, 1.25f)),
                new Color(0.18f, 0.45f, 1f, 0.75f), 2f);

            // Output is a flat rectangular raster while the dome is curved.
            // A sphere-intersection outline cannot remain the same rectangle
            // the operator sees in Output. Place an aspect- and calibration-
            // matched reference plane at the optical-axis/sphere intersection
            // instead; it can be compared directly with the Output inset.
            var rasterEdge = new Color(1f, 0.78f, 0.12f, 1f);
            var projectionAspect = GetProjectionAspect();
            if (hasAxisHit)
            {
                Vector3 rasterRight;
                Vector3 rasterUp;
                if (settings.projectionRegionMode == 1)
                {
                    var yaw = settings.offAxisAimYaw * Mathf.Deg2Rad;
                    rasterRight = new Vector3(Mathf.Cos(yaw), 0f,
                        -Mathf.Sin(yaw)).normalized;
                    var rasterForward = (axisHit - projector).normalized;
                    rasterUp = Vector3.Cross(rasterForward, rasterRight).normalized;
                }
                else
                {
                    rasterRight = Vector3.right;
                    rasterUp = Vector3.forward;
                }

                var halfFov = Mathf.Clamp(settings.domeFov * 0.5f, 0.1f, 85f)
                              * Mathf.Deg2Rad;
                var circlePlaneRadius = Vector3.Distance(projector, axisHit)
                                        * Mathf.Tan(halfFov);
                Vector3 RasterPlanePoint(float u, float v)
                {
                    var point = new Vector2(
                        (u - settings.centerX) * projectionAspect
                        / Mathf.Max(settings.radius * settings.ellipseX, 0.0001f),
                        (v - settings.centerY)
                        / Mathf.Max(settings.radius * settings.ellipseY, 0.0001f));
                    var rotation = settings.outputRotation * Mathf.Deg2Rad;
                    var cosine = Mathf.Cos(rotation);
                    var sine = Mathf.Sin(rotation);
                    point = new Vector2(cosine * point.x - sine * point.y,
                        sine * point.x + cosine * point.y);
                    return axisHit + rasterRight * (point.x * circlePlaneRadius)
                           + rasterUp * (point.y * circlePlaneRadius);
                }

                var rasterCorners = new[]
                {
                    RasterPlanePoint(0f, 1f), RasterPlanePoint(1f, 1f),
                    RasterPlanePoint(1f, 0f), RasterPlanePoint(0f, 0f)
                };
                var rasterCornerGui = new Vector2[rasterCorners.Length];
                for (var cornerIndex = 0; cornerIndex < rasterCorners.Length;
                     cornerIndex++)
                    rasterCornerGui[cornerIndex] = Project(rasterCorners[cornerIndex]);
                for (var edgeIndex = 0; edgeIndex < rasterCornerGui.Length; edgeIndex++)
                    DrawGuiSegment(rasterCornerGui[edgeIndex],
                        rasterCornerGui[(edgeIndex + 1) % rasterCornerGui.Length],
                        rasterEdge, 3f);

                // The Output inset contains a circular domemaster inside the
                // rectangular projector raster. Draw that same circle on the
                // reference plane so its centre/size can be compared directly
                // with the inset; the outer yellow quad alone cannot show this
                // relationship.
                const int outputCircleSegments = 96;
                var outputCircleColor = new Color(1f, 0.92f, 0.52f, 0.92f);
                Vector2? previousCirclePoint = null;
                for (var circleIndex = 0; circleIndex <= outputCircleSegments;
                     circleIndex++)
                {
                    var angle = circleIndex * Mathf.PI * 2f / outputCircleSegments;
                    var worldPoint = axisHit
                                     + rasterRight * (Mathf.Cos(angle) * circlePlaneRadius)
                                     + rasterUp * (Mathf.Sin(angle) * circlePlaneRadius);
                    var guiPoint = Project(worldPoint);
                    if (previousCirclePoint.HasValue)
                        DrawGuiSegment(previousCirclePoint.Value, guiPoint,
                            outputCircleColor, 1.8f);
                    previousCirclePoint = guiPoint;
                }
                DrawCrossHandle(axisPoint, outputCircleColor, 0.72f);
                var frustumEdge = new Color(1f, 0.78f, 0.12f, 0.22f);
                for (var cornerIndex = 0; cornerIndex < rasterCornerGui.Length;
                     cornerIndex++)
                {
                    DrawGuiSegment(projectorPoint, rasterCornerGui[cornerIndex],
                        frustumEdge, 1f);
                    DrawCrossHandle(rasterCornerGui[cornerIndex], rasterEdge, 0.72f);
                }
                GUI.Label(new Rect(rasterCornerGui[1].x + 10f,
                    rasterCornerGui[1].y - 13f, 230f, 24f),
                    "Output矩形基準面（円内が実投影）", smallStyle);
            }
            if (hasAxisHit)
            {
                var opticalAxisColor = new Color(1f, 0.32f, 0.72f, 1f);
                DrawGuiSegment(projectorPoint, axisPoint,
                    opticalAxisColor, 3f);
                if (settings.projectionRegionMode == 1)
                    DrawCrossHandle(axisPoint, opticalAxisColor,
                        axisHandleHovered ? 1.55f : 1f);
                // Keep the label outside the circular handle hit area. Text
                // itself must never start an aim drag.
                GUI.Label(new Rect(axisPoint.x + 30f, axisPoint.y - 13f, 190f, 24f),
                    settings.projectionRegionMode == 1
                        ? axisHandleHovered ? "● 掴めます：光軸先端"
                                            : "光軸先端（点をドラッグ）"
                        : "光軸", smallStyle);
            }

            DrawSolidGuiRect(new Rect(projectorPoint.x - 8f, projectorPoint.y - 8f,
                16f, 16f), new Color(0.12f, 0.76f, 1f, 1f));
            GUI.Label(new Rect(projectorPoint.x + 30f, projectorPoint.y - 13f,
                190f, 24f), settings.projectionRegionMode == 1
                    ? projectorHandleHovered ? "● 掴めます：プロジェクター"
                                             : "プロジェクター（点をドラッグ）"
                    : "プロジェクター", smallStyle);
            if (settings.projectionRegionMode == 1)
                DrawCrossHandle(projectorPoint, new Color(0.12f, 0.76f, 1f, 1f),
                    projectorHandleHovered ? 1.45f : 1f);
            var centrePoint = Project(Vector3.zero);
            DrawCrossHandle(centrePoint, new Color(1f, 0.25f, 0.78f, 1f));
            GUI.Label(new Rect(centrePoint.x + 12f, centrePoint.y + 8f, 120f, 24f),
                "球の中心", smallStyle);

            // Keep a live 2D Output view beside the 3D model. This is rendered
            // with the projector display aspect and without Control-only
            // inspection pan/zoom, so parameter changes can be judged in both
            // representations at the same time.
            DrawSolidGuiRect(outputPreviewPanel, new Color(0.07f, 0.08f, 0.10f, 0.98f));
            GUI.Label(new Rect(outputPreviewPanel.x + 9f, outputPreviewPanel.y + 7f,
                outputPreviewPanel.width - 18f, 22f), "Output投影画面", sectionTitleStyle);
            var outputPreviewArea = new Rect(outputPreviewPanel.x + 9f,
                outputPreviewPanel.y + 33f, outputPreviewPanel.width - 18f,
                outputPreviewPanel.height - 42f);
            var outputImageRect = outputPreviewArea;
            if (outputPreviewArea.width / Mathf.Max(outputPreviewArea.height, 1f)
                > projectionAspectForInset)
            {
                outputImageRect.width = outputPreviewArea.height * projectionAspectForInset;
                outputImageRect.x += (outputPreviewArea.width - outputImageRect.width) * 0.5f;
            }
            else
            {
                outputImageRect.height = outputPreviewArea.width / projectionAspectForInset;
                outputImageRect.y += (outputPreviewArea.height - outputImageRect.height) * 0.5f;
            }
            DrawSolidGuiRect(outputImageRect, Color.black);
            if (outputRenderer?.OperatorPreviewTexture != null)
                GUI.DrawTexture(outputImageRect, outputRenderer.OperatorPreviewTexture,
                    ScaleMode.StretchToFill, false);
            var previewBorder = new Color(0.72f, 0.78f, 0.84f, 0.95f);
            DrawGuiSegment(outputImageRect.min,
                new Vector2(outputImageRect.xMax, outputImageRect.yMin), previewBorder, 1f);
            DrawGuiSegment(new Vector2(outputImageRect.xMax, outputImageRect.yMin),
                outputImageRect.max, previewBorder, 1f);
            DrawGuiSegment(outputImageRect.max,
                new Vector2(outputImageRect.xMin, outputImageRect.yMax), previewBorder, 1f);
            DrawGuiSegment(new Vector2(outputImageRect.xMin, outputImageRect.yMax),
                outputImageRect.min, previewBorder, 1f);

            GUI.Label(new Rect(available.x + 16f, available.yMax - 31f,
                    available.width - 32f, 24f),
                settings.projectionRegionMode == 1
                    ? $"黄：Output矩形基準面　桃：光軸　設置 ({settings.offAxisProjectorX:0.00}, {settings.offAxisProjectorHeight:0.00}, {-settings.offAxisProjectorDistance:0.00}) R　Yaw {settings.offAxisAimYaw:0.0}°／仰角 {settings.offAxisAimElevation:0.0}°　画角 {settings.domeFov:0}°"
                    : $"黄：Output矩形基準面　球中心から投影　画角 {settings.domeFov:0}°",
                smallStyle);
        }

        private void DrawRoomObserverEditor(float logicalWidth, float logicalHeight, float margin)
        {
            var available = new Rect(margin, 58f, logicalWidth - margin * 2f,
                logicalHeight - 78f);
            if (showControls)
            {
                const float panelButtonAllowance = 165f;
                if (settings.controlPanelOnRight)
                    available.xMax = panelAreaRect.x - panelButtonAllowance;
                else
                {
                    available.xMin = panelAreaRect.xMax + panelButtonAllowance;
                    available.width = logicalWidth - margin - available.xMin;
                }
            }
            if (available.width < 260f || available.height < 240f) return;

            // Keep the diagnostic diagram opaque. Otherwise the projected
            // room grid underneath reads as unrelated diagonal lines inside
            // the third-person editor.
            DrawSolidGuiRect(available, new Color(0.035f, 0.045f, 0.06f, 1f));
            var overview = settings.roomControlViewMode == 1;
            var topView = settings.roomControlViewMode == 2;
            GUI.Label(new Rect(available.x + 16f, available.y + 12f,
                    available.width - 32f, 28f),
                overview
                    ? roomOverviewShowContent
                        ? "観測者プレビュー　ドラッグで見回す / ホイールで視野角を変更"
                        : "第三者視点・俯瞰3D　空き領域をドラッグして視点回転 / ホイールで拡大縮小"
                    : topView
                        ? "第三者視点・上面図　観測者をドラッグ：X / Z"
                        : "第三者視点・側面図　観測者をドラッグ：Z / Y",
                sectionTitleStyle);

            if (overview)
            {
                DrawRoomOverviewEditor(available);
                return;
            }

            var worldWidth = topView ? settings.roomWidth : settings.roomDepth;
            var worldHeight = topView ? settings.roomDepth : settings.roomHeight;
            var fit = new Rect(available.x + 54f, available.y + 58f,
                available.width - 108f, available.height - 112f);
            var worldAspect = worldWidth / Mathf.Max(worldHeight, 0.01f);
            if (fit.width / fit.height > worldAspect)
            {
                var width = fit.height * worldAspect;
                fit.x += (fit.width - width) * 0.5f;
                fit.width = width;
            }
            else
            {
                var height = fit.width / worldAspect;
                fit.y += (fit.height - height) * 0.5f;
                fit.height = height;
            }

            DrawSolidGuiRect(fit, new Color(0.10f, 0.12f, 0.15f, 1f));
            var xAxis = new Color(1f, 0.12f, 0.12f, 1f);
            var yAxis = new Color(0.12f, 1f, 0.22f, 1f);
            var zAxis = new Color(0.12f, 0.38f, 1f, 1f);
            if (topView)
            {
                DrawSolidGuiRect(new Rect(fit.x, fit.y, fit.width, 4f), xAxis);
                DrawSolidGuiRect(new Rect(fit.x, fit.yMax - 4f, fit.width, 4f), xAxis);
                DrawSolidGuiRect(new Rect(fit.x, fit.y, 4f, fit.height), zAxis);
                DrawSolidGuiRect(new Rect(fit.xMax - 4f, fit.y, 4f, fit.height), zAxis);
                GUI.Label(new Rect(fit.x + 8f, fit.y + 8f, 120f, 24f), "前壁 Z=奥行き", smallStyle);
                GUI.Label(new Rect(fit.x + 8f, fit.yMax - 28f, 100f, 24f), "後方 Z=0", smallStyle);
            }
            else
            {
                DrawSolidGuiRect(new Rect(fit.x, fit.y, fit.width, 4f), zAxis);
                DrawSolidGuiRect(new Rect(fit.x, fit.yMax - 4f, fit.width, 4f), zAxis);
                DrawSolidGuiRect(new Rect(fit.x, fit.y, 4f, fit.height), yAxis);
                DrawSolidGuiRect(new Rect(fit.xMax - 4f, fit.y, 4f, fit.height), yAxis);
                GUI.Label(new Rect(fit.x + 8f, fit.y + 8f, 100f, 24f), "天井 Y=高さ", smallStyle);
                GUI.Label(new Rect(fit.x + 8f, fit.yMax - 28f, 100f, 24f), "床 Y=0", smallStyle);
            }

            var observer = topView
                ? new Vector2(Mathf.InverseLerp(-settings.roomWidth * 0.5f,
                        settings.roomWidth * 0.5f, settings.roomViewerX),
                    Mathf.InverseLerp(settings.roomDepth, 0f, settings.roomViewerZ))
                : new Vector2(Mathf.InverseLerp(0f, settings.roomDepth, settings.roomViewerZ),
                    Mathf.InverseLerp(settings.roomHeight, 0f, settings.roomViewerY));
            var projector = topView
                ? new Vector2(Mathf.InverseLerp(-settings.roomWidth * 0.5f,
                        settings.roomWidth * 0.5f, settings.roomProjectorX),
                    Mathf.InverseLerp(settings.roomDepth, 0f, settings.roomProjectorZ))
                : new Vector2(Mathf.InverseLerp(0f, settings.roomDepth, settings.roomProjectorZ),
                    Mathf.InverseLerp(settings.roomHeight, 0f, settings.roomProjectorY));
            var observerPoint = new Vector2(Mathf.Lerp(fit.x, fit.xMax, observer.x),
                Mathf.Lerp(fit.y, fit.yMax, observer.y));
            var projectorPoint = new Vector2(Mathf.Lerp(fit.x, fit.xMax, projector.x),
                Mathf.Lerp(fit.y, fit.yMax, projector.y));

            DrawSolidGuiRect(new Rect(projectorPoint.x - 8f, projectorPoint.y - 8f, 16f, 16f),
                new Color(0.15f, 0.72f, 1f, 1f));
            GUI.Label(new Rect(projectorPoint.x + 11f, projectorPoint.y - 12f, 110f, 24f),
                "プロジェクター", smallStyle);

            const float observerHitSize = 42f;
            var observerHit = new Rect(observerPoint.x - observerHitSize * 0.5f,
                observerPoint.y - observerHitSize * 0.5f, observerHitSize, observerHitSize);
            var movedObserver = DrawDraggableHandleWindow(62020, observerHit);
            if ((movedObserver.position - observerHit.position).sqrMagnitude > 0.01f)
            {
                observerPoint = movedObserver.center;
                var mousePosition = observerPoint;
                var normalizedX = Mathf.Clamp01(Mathf.InverseLerp(fit.x, fit.xMax, mousePosition.x));
                var normalizedY = Mathf.Clamp01(Mathf.InverseLerp(fit.y, fit.yMax, mousePosition.y));
                if (topView)
                {
                    settings.roomViewerX = Mathf.Lerp(-settings.roomWidth * 0.5f,
                        settings.roomWidth * 0.5f, normalizedX);
                    settings.roomViewerZ = Mathf.Lerp(settings.roomDepth, 0f, normalizedY);
                }
                else
                {
                    settings.roomViewerZ = Mathf.Lerp(0f, settings.roomDepth, normalizedX);
                    settings.roomViewerY = Mathf.Lerp(settings.roomHeight, 0f, normalizedY);
                }
            }

            var observerColor = new Color(1f, 0.24f, 0.78f, 1f);
            DrawSolidGuiRect(new Rect(observerPoint.x - 14f, observerPoint.y - 3f, 28f, 6f), observerColor);
            DrawSolidGuiRect(new Rect(observerPoint.x - 3f, observerPoint.y - 14f, 6f, 28f), observerColor);
            DrawSolidGuiRect(new Rect(observerPoint.x - 7f, observerPoint.y - 7f, 14f, 14f), Color.white);
            DrawSolidGuiRect(new Rect(observerPoint.x - 4f, observerPoint.y - 4f, 8f, 8f), observerColor);
            GUI.Label(new Rect(observerPoint.x + 16f, observerPoint.y - 12f, 130f, 24f),
                "観測者（基準視点）", smallStyle);
            GUI.Label(new Rect(available.x + 16f, available.yMax - 36f,
                    available.width - 32f, 24f),
                $"観測者  X {settings.roomViewerX:0.00} m / Y {settings.roomViewerY:0.00} m / Z {settings.roomViewerZ:0.00} m",
                smallStyle);
        }

        private void DrawRoomOverviewEditor(Rect available)
        {
            var diagram = new Rect(available.x + 44f, available.y + 52f,
                available.width - 88f, available.height - 104f);
            var projectionModeButton = new Rect(diagram.x + 8f, diagram.y + 6f, 112f, 28f);
            var contentModeButton = new Rect(diagram.x + 126f, diagram.y + 6f, 132f, 28f);
            if (GUI.Button(projectionModeButton, "投影範囲",
                    roomOverviewShowContent ? tabStyle : activeTabStyle))
                roomOverviewShowContent = false;
            if (GUI.Button(contentModeButton, "観測者プレビュー",
                    roomOverviewShowContent ? activeTabStyle : tabStyle))
                roomOverviewShowContent = true;
            if (roomOverviewShowContent)
            {
                DrawRoomContentObserverPreview(diagram, projectionModeButton, contentModeButton);
                return;
            }
            var roomWidth = Mathf.Max(settings.roomWidth, 0.01f);
            var roomHeight = Mathf.Max(settings.roomHeight, 0.01f);
            var roomDepth = Mathf.Max(settings.roomDepth, 0.01f);
            var worldCentre = new Vector3(0f, roomHeight * 0.5f, roomDepth * 0.5f);
            var diagonal = Mathf.Sqrt(roomWidth * roomWidth + roomHeight * roomHeight
                                                            + roomDepth * roomDepth);
            var baseScale = Mathf.Min(diagram.width, diagram.height)
                            / Mathf.Max(diagonal, 0.01f) * 0.82f * roomOverviewZoom;
            var viewRotation = Quaternion.Euler(roomOverviewPitch, -roomOverviewYaw, 0f);

            Vector2 Project(float x, float y, float z)
            {
                var view = viewRotation * (new Vector3(x, y, z) - worldCentre);
                return diagram.center + new Vector2(view.x, -view.y) * baseScale;
            }

            var current = Event.current;
            var mouse = GetLogicalMousePosition();
            var resetView = new Rect(diagram.xMax - 116f, diagram.y + 6f, 108f, 28f);
            if (GUI.Button(resetView, "3D視点をリセット"))
            {
                roomOverviewYaw = 35f;
                roomOverviewPitch = 24f;
                roomOverviewZoom = 1f;
            }
            if (current.type == EventType.ScrollWheel && diagram.Contains(mouse)
                && !resetView.Contains(mouse))
            {
                roomOverviewZoom = Mathf.Clamp(roomOverviewZoom
                                                * Mathf.Exp(-current.delta.y * 0.08f), 0.2f, 5f);
                current.Use();
            }
            else if (current.type == EventType.MouseDown && current.button == 0
                     && diagram.Contains(mouse) && !resetView.Contains(mouse))
            {
                roomOverviewOrbitDragging = true;
                roomOverviewLastMouse = mouse;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && roomOverviewOrbitDragging)
            {
                var delta = mouse - roomOverviewLastMouse;
                roomOverviewLastMouse = mouse;
                roomOverviewYaw = NormalizeControlPreviewRotation(roomOverviewYaw + delta.x * 0.3f);
                roomOverviewPitch = Mathf.Clamp(roomOverviewPitch - delta.y * 0.3f, -89f, 89f);
                current.Use();
            }
            if (current.rawType == EventType.MouseUp) roomOverviewOrbitDragging = false;

            var x0 = -roomWidth * 0.5f;
            var x1 = roomWidth * 0.5f;
            var p000 = Project(x0, 0f, 0f);
            var p100 = Project(x1, 0f, 0f);
            var p001 = Project(x0, 0f, roomDepth);
            var p101 = Project(x1, 0f, roomDepth);
            var p010 = Project(x0, roomHeight, 0f);
            var p110 = Project(x1, roomHeight, 0f);
            var p011 = Project(x0, roomHeight, roomDepth);
            var p111 = Project(x1, roomHeight, roomDepth);
            var xAxis = new Color(1f, 0.12f, 0.12f, 1f);
            var yAxis = new Color(0.12f, 1f, 0.22f, 1f);
            var zAxis = new Color(0.12f, 0.38f, 1f, 1f);

            DrawGuiSegment(p000, p100, xAxis, 3f);
            DrawGuiSegment(p001, p101, xAxis, 3f);
            DrawGuiSegment(p010, p110, xAxis, 3f);
            DrawGuiSegment(p011, p111, xAxis, 3f);
            DrawGuiSegment(p000, p010, yAxis, 3f);
            DrawGuiSegment(p100, p110, yAxis, 3f);
            DrawGuiSegment(p001, p011, yAxis, 3f);
            DrawGuiSegment(p101, p111, yAxis, 3f);
            DrawGuiSegment(p000, p001, zAxis, 3f);
            DrawGuiSegment(p100, p101, zAxis, 3f);
            DrawGuiSegment(p010, p011, zAxis, 3f);
            DrawGuiSegment(p110, p111, zAxis, 3f);

            var projectorWorld = new Vector3(settings.roomProjectorX,
                settings.roomProjectorY, settings.roomProjectorZ);
            var projectorPoint = Project(projectorWorld.x, projectorWorld.y,
                projectorWorld.z);

            // Draw the actual output rectangle back into the room. This is not
            // a decorative camera pyramid: every cyan vertex is obtained with
            // the same projector/lens/room intersection used by the renderer.
            // It therefore makes an incorrect pose, FOV, enabled face, or lens
            // correction immediately visible while calibrating.
            const int projectionColumns = 13;
            const int projectionRows = 9;
            var projectionHits = new Vector3[projectionColumns, projectionRows];
            var projectionFaces = new int[projectionColumns, projectionRows];
            var projectionValid = new bool[projectionColumns, projectionRows];
            var projectionAspect = GetProjectionAspect();
            for (var column = 0; column < projectionColumns; column++)
            {
                for (var row = 0; row < projectionRows; row++)
                {
                    var outputPoint = new Vector2(column / (float)(projectionColumns - 1),
                        row / (float)(projectionRows - 1));
                    projectionValid[column, row] =
                        DomeProjectionRenderer.TryGetRoomHitAtOutputPoint(settings,
                            projectionAspect, outputPoint, out projectionFaces[column, row],
                            out projectionHits[column, row]);
                }
            }

            var projectionGrid = new Color(0.10f, 0.82f, 1f, 0.72f);
            var projectionEdge = new Color(0.10f, 0.92f, 1f, 1f);
            for (var column = 0; column < projectionColumns; column++)
            {
                for (var row = 0; row < projectionRows; row++)
                {
                    if (column + 1 < projectionColumns
                        && projectionValid[column, row]
                        && projectionValid[column + 1, row]
                        && projectionFaces[column, row] == projectionFaces[column + 1, row])
                    {
                        var boundary = row == 0 || row == projectionRows - 1;
                        DrawGuiSegment(Project(projectionHits[column, row].x,
                                projectionHits[column, row].y, projectionHits[column, row].z),
                            Project(projectionHits[column + 1, row].x,
                                projectionHits[column + 1, row].y,
                                projectionHits[column + 1, row].z),
                            boundary ? projectionEdge : projectionGrid, boundary ? 2.5f : 1.2f);
                    }
                    if (row + 1 < projectionRows
                        && projectionValid[column, row]
                        && projectionValid[column, row + 1]
                        && projectionFaces[column, row] == projectionFaces[column, row + 1])
                    {
                        var boundary = column == 0 || column == projectionColumns - 1;
                        DrawGuiSegment(Project(projectionHits[column, row].x,
                                projectionHits[column, row].y, projectionHits[column, row].z),
                            Project(projectionHits[column, row + 1].x,
                                projectionHits[column, row + 1].y,
                                projectionHits[column, row + 1].z),
                            boundary ? projectionEdge : projectionGrid, boundary ? 2.5f : 1.2f);
                    }
                }
            }

            var raySamples = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(projectionColumns - 1, 0),
                new Vector2Int(0, projectionRows - 1),
                new Vector2Int(projectionColumns - 1, projectionRows - 1),
                new Vector2Int((projectionColumns - 1) / 2, (projectionRows - 1) / 2)
            };
            for (var index = 0; index < raySamples.Length; index++)
            {
                var sample = raySamples[index];
                if (!projectionValid[sample.x, sample.y]) continue;
                var hit = projectionHits[sample.x, sample.y];
                DrawGuiSegment(projectorPoint, Project(hit.x, hit.y, hit.z),
                    index == raySamples.Length - 1
                        ? new Color(1f, 0.78f, 0.12f, 1f)
                        : new Color(0.10f, 0.82f, 1f, 0.38f),
                    index == raySamples.Length - 1 ? 3f : 1.2f);
            }

            if (roomWarpEditing)
            {
                foreach (var anchor in roomCalibrationAnchors)
                {
                    var anchorPoint = Project(anchor.worldPoint.x, anchor.worldPoint.y,
                        anchor.worldPoint.z);
                    var anchorColor = anchor.adjusted
                        ? new Color(1f, 0.60f, 0.08f, 1f)
                        : new Color(0.30f, 1f, 0.72f, 1f);
                    DrawSolidGuiRect(new Rect(anchorPoint.x - 4f, anchorPoint.y - 4f,
                        8f, 8f), anchorColor);
                }
            }

            DrawSolidGuiRect(new Rect(projectorPoint.x - 8f, projectorPoint.y - 8f, 16f, 16f),
                new Color(0.15f, 0.72f, 1f, 1f));
            GUI.Label(new Rect(projectorPoint.x + 11f, projectorPoint.y - 12f, 110f, 24f),
                "プロジェクター", smallStyle);

            var observerPoint = Project(settings.roomViewerX, settings.roomViewerY,
                settings.roomViewerZ);
            var observerColor = new Color(1f, 0.24f, 0.78f, 1f);
            DrawSolidGuiRect(new Rect(observerPoint.x - 14f, observerPoint.y - 3f, 28f, 6f), observerColor);
            DrawSolidGuiRect(new Rect(observerPoint.x - 3f, observerPoint.y - 14f, 6f, 28f), observerColor);
            DrawSolidGuiRect(new Rect(observerPoint.x - 7f, observerPoint.y - 7f, 14f, 14f), Color.white);
            DrawSolidGuiRect(new Rect(observerPoint.x - 4f, observerPoint.y - 4f, 8f, 8f), observerColor);
            GUI.Label(new Rect(observerPoint.x + 16f, observerPoint.y - 12f, 130f, 24f),
                "観測者（基準視点）", smallStyle);
            GUI.Label(new Rect(diagram.x + 8f, diagram.y + 8f,
                    Mathf.Max(100f, diagram.width - 132f), 24f),
                "水色：実際の投影範囲　黄：光軸　緑/橙：未調整/調整済み交点", smallStyle);
            GUI.Label(new Rect(diagram.x + 8f, diagram.y + 30f,
                    Mathf.Max(100f, diagram.width - 16f), 24f),
                $"投影位置 ({settings.roomProjectorX:0.00}, {settings.roomProjectorY:0.00}, {settings.roomProjectorZ:0.00}) m　姿勢 Y{settings.roomProjectorYaw:0.0}° / P{settings.roomProjectorPitch:0.0}° / R{settings.roomProjectorRoll:0.0}°　水平画角 {settings.roomProjectorHorizontalFov:0}°",
                smallStyle);
            if (Mathf.Abs(settings.roomProjectorPitch) > 75f
                || settings.roomProjectorHorizontalFov > 160f
                || settings.roomProjectorY < 0.05f
                || settings.roomProjectorY > settings.roomHeight - 0.05f)
            {
                var previousColor = GUI.color;
                GUI.color = new Color(1f, 0.66f, 0.16f, 1f);
                GUI.Label(new Rect(diagram.x + 8f, diagram.y + 52f,
                        Mathf.Max(100f, diagram.width - 16f), 24f),
                    "注意：姿勢・画角・高さが物理限界付近です。調整キャンセル後、概算値を見直してください。",
                    smallStyle);
                GUI.color = previousColor;
            }
            GUI.Label(new Rect(available.x + 16f, available.yMax - 36f,
                    available.width - 32f, 24f),
                $"空き領域ドラッグ：3D視点を回転 / ホイール：拡大縮小　　観測者 X {settings.roomViewerX:0.00} / Y {settings.roomViewerY:0.00} / Z {settings.roomViewerZ:0.00} m",
                smallStyle);
        }

        private void DrawRoomContentObserverPreview(Rect diagram, Rect projectionModeButton,
            Rect contentModeButton)
        {
            var resetView = new Rect(diagram.xMax - 116f, diagram.y + 6f, 108f, 28f);
            var guideButton = new Rect(diagram.xMax - 238f, diagram.y + 6f, 116f, 28f);
            var current = Event.current;
            var mouse = GetLogicalMousePosition();
            if (GUI.Button(resetView, "視点を正面へ"))
            {
                roomContentPreviewYaw = 0f;
                roomContentPreviewPitch = 0f;
                roomContentPreviewFov = 95f;
            }
            if (GUI.Button(guideButton,
                    roomContentPreviewShowGuide ? "面ガイド：表示" : "面ガイド：非表示",
                    roomContentPreviewShowGuide ? activeTabStyle : tabStyle))
                roomContentPreviewShowGuide = !roomContentPreviewShowGuide;
            if (current.type == EventType.ScrollWheel && diagram.Contains(mouse)
                && !projectionModeButton.Contains(mouse) && !contentModeButton.Contains(mouse)
                && !resetView.Contains(mouse) && !guideButton.Contains(mouse))
            {
                roomContentPreviewFov = Mathf.Clamp(roomContentPreviewFov
                    + current.delta.y * 2.5f, 30f, 140f);
                current.Use();
            }
            else if (current.type == EventType.MouseDown && current.button == 0
                     && diagram.Contains(mouse)
                     && !projectionModeButton.Contains(mouse) && !contentModeButton.Contains(mouse)
                     && !resetView.Contains(mouse) && !guideButton.Contains(mouse))
            {
                roomContentPreviewDragging = true;
                roomContentPreviewLastMouse = mouse;
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && roomContentPreviewDragging)
            {
                var delta = mouse - roomContentPreviewLastMouse;
                roomContentPreviewLastMouse = mouse;
                roomContentPreviewYaw = NormalizeControlPreviewRotation(
                    roomContentPreviewYaw + delta.x * 0.22f);
                roomContentPreviewPitch = Mathf.Clamp(roomContentPreviewPitch
                    - delta.y * 0.22f, -89f, 89f);
                current.Use();
            }
            if (current.rawType == EventType.MouseUp) roomContentPreviewDragging = false;

            GetRoomContentPreviewSource(out var texture, out var flipY, out var contentMode,
                out var sourceWidth, out var sourceHeight, out var sourceLabel);
            if (texture != null && current.type == EventType.Repaint
                && roomContentPreviewRenderer != null)
            {
                var rendered = roomContentPreviewRenderer.Render(settings, texture, flipY,
                    contentMode, sourceWidth, sourceHeight, roomContentPreviewYaw,
                    roomContentPreviewPitch, roomContentPreviewFov, roomContentPreviewShowGuide,
                    Mathf.RoundToInt(diagram.width), Mathf.RoundToInt(diagram.height));
                if (rendered != null)
                    GUI.DrawTexture(diagram, rendered, ScaleMode.StretchToFill, false);
            }
            else if (texture == null)
            {
                DrawSolidGuiRect(diagram, new Color(0.025f, 0.032f, 0.045f, 1f));
                GUI.Label(new Rect(diagram.x + 20f, diagram.center.y - 18f,
                        diagram.width - 40f, 36f),
                    "プレビューする動画・画像を読み込んでください",
                    sectionTitleStyle);
            }

            // Draw controls again because the render texture occupies the
            // complete preview area and would otherwise cover them.
            GUI.Button(projectionModeButton, "投影範囲",
                roomOverviewShowContent ? tabStyle : activeTabStyle);
            GUI.Button(contentModeButton, "観測者プレビュー", activeTabStyle);
            GUI.Button(guideButton,
                roomContentPreviewShowGuide ? "面ガイド：表示" : "面ガイド：非表示",
                roomContentPreviewShowGuide ? activeTabStyle : tabStyle);
            GUI.Button(resetView, "視点を正面へ");
            GUI.Label(new Rect(diagram.x + 12f, diagram.y + 40f,
                    Mathf.Max(140f, diagram.width - 24f), 24f),
                $"観測者位置からの素材方向　ドラッグ：見回す / ホイール：視野角　Yaw {roomContentPreviewYaw:0}° / Pitch {roomContentPreviewPitch:0}° / FOV {roomContentPreviewFov:0}°",
                smallStyle);
            if (texture != null)
            {
                GUI.Label(new Rect(diagram.x + 12f, diagram.y + 62f,
                        Mathf.Max(140f, diagram.width - 24f), 24f),
                    $"表示素材：{sourceLabel}", smallStyle);
                DrawRoomSourceOverview(diagram, texture, flipY, contentMode,
                    sourceWidth, sourceHeight);
            }
            GUI.Label(new Rect(diagram.x + 12f, diagram.yMax - 30f,
                    Mathf.Max(140f, diagram.width - 24f), 24f),
                "素材の方向と観測者からの見え方を確認します。実際の到達範囲は「投影範囲」で確認してください。",
                smallStyle);
            var crossColor = new Color(1f, 1f, 1f, 0.72f);
            DrawSolidGuiRect(new Rect(diagram.center.x - 12f, diagram.center.y - 1f,
                24f, 2f), crossColor);
            DrawSolidGuiRect(new Rect(diagram.center.x - 1f, diagram.center.y - 12f,
                2f, 24f), crossColor);
        }

        private void GetRoomContentPreviewSource(out Texture texture, out bool flipY,
            out int contentMode, out int sourceWidth, out int sourceHeight,
            out string sourceLabel)
        {
            texture = null;
            flipY = false;
            contentMode = 0;
            sourceWidth = 0;
            sourceHeight = 0;
            sourceLabel = "";

            // This is a material inspection view, not a mirror of the standby
            // state. Keep showing the selected source even while Output is on
            // black/default/guide standby so calibration can be prepared first.
            if (settings.sourceMode == 1 && windowCapture?.Texture != null)
            {
                texture = windowCapture.Texture;
                contentMode = 1;
                sourceWidth = windowCapture.Width;
                sourceHeight = windowCapture.Height;
                sourceLabel = string.IsNullOrWhiteSpace(settings.capturedWindowTitle)
                    ? "外部ウィンドウ" : settings.capturedWindowTitle;
                return;
            }
            if (video?.Texture != null)
            {
                texture = video.Texture;
                flipY = video.RequiresVerticalFlip;
                contentMode = settings.fileProjectionMode;
                sourceWidth = video.Width > 0 ? video.Width : texture.width;
                sourceHeight = video.Height > 0 ? video.Height : texture.height;
                var sourcePath = string.IsNullOrWhiteSpace(video.SourcePath)
                    ? settings.videoPath : video.SourcePath;
                sourceLabel = string.IsNullOrWhiteSpace(sourcePath)
                    ? "選択素材" : Path.GetFileName(sourcePath);
                return;
            }
            if (windowCapture?.Texture != null)
            {
                texture = windowCapture.Texture;
                contentMode = 1;
                sourceWidth = windowCapture.Width;
                sourceHeight = windowCapture.Height;
                sourceLabel = "外部ウィンドウ";
                return;
            }
            if (settings.idleDisplayMode == 1 && idleImage?.IsPrepared == true)
            {
                texture = idleImage.Texture;
                flipY = idleImage.RequiresVerticalFlip;
                contentMode = settings.idleImageIsFlat ? 1 : 0;
                sourceWidth = idleImage.Width;
                sourceHeight = idleImage.Height;
                sourceLabel = "既定画像（素材未読込）";
            }
        }

        private void DrawRoomSourceOverview(Rect diagram, Texture texture, bool decoderFlipY,
            int contentMode, int sourceWidth, int sourceHeight)
        {
            var aspect = sourceHeight > 0
                ? sourceWidth / (float)sourceHeight
                : texture.width / (float)Mathf.Max(texture.height, 1);
            aspect = Mathf.Clamp(aspect, 0.75f, 3f);
            var width = Mathf.Min(330f, diagram.width * 0.30f);
            var height = Mathf.Clamp(width / aspect, 90f, 190f);
            var frame = new Rect(diagram.xMax - width - 16f,
                diagram.yMax - height - 42f, width, height);
            DrawSolidGuiRect(new Rect(frame.x - 3f, frame.y - 25f,
                frame.width + 6f, frame.height + 28f), new Color(0.03f, 0.04f, 0.055f, 0.92f));
            GUI.Label(new Rect(frame.x + 4f, frame.y - 23f, frame.width - 8f, 22f),
                contentMode == 0 ? "素材全体（白十字：現在の視線）" : "素材全体",
                smallStyle);
            var uv = new Rect(settings.flipInputX ? 1f : 0f,
                settings.flipInputY ^ decoderFlipY ? 1f : 0f,
                settings.flipInputX ? -1f : 1f,
                settings.flipInputY ^ decoderFlipY ? -1f : 1f);
            GUI.DrawTextureWithTexCoords(frame, texture, uv, true);

            if (contentMode != 0) return;
            var direction = Quaternion.Euler(-roomContentPreviewPitch,
                roomContentPreviewYaw, 0f) * Vector3.forward;
            direction = Quaternion.AngleAxis(settings.yaw, Vector3.up) * direction;
            direction = Quaternion.AngleAxis(settings.pitch, Vector3.right) * direction;
            direction = Quaternion.AngleAxis(settings.roll, Vector3.forward) * direction;
            var markerU = Mathf.Repeat(Mathf.Atan2(direction.x, direction.z)
                / (Mathf.PI * 2f) + 0.5f, 1f);
            var markerV = 0.5f - Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) / Mathf.PI;
            if (settings.flipInputX) markerU = 1f - markerU;
            if (settings.flipInputY ^ decoderFlipY) markerV = 1f - markerV;
            var marker = new Vector2(frame.x + markerU * frame.width,
                frame.y + markerV * frame.height);
            var color = new Color(1f, 1f, 1f, 0.96f);
            DrawSolidGuiRect(new Rect(marker.x - 10f, marker.y - 1f, 20f, 2f), color);
            DrawSolidGuiRect(new Rect(marker.x - 1f, marker.y - 10f, 2f, 20f), color);
        }

        private static void DrawGuiSegment(Vector2 start, Vector2 end, Color color, float thickness)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length < 0.5f) return;
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            GUI.color = color;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            // Compose in logical GUI space. RotateAroundPivot pre-multiplies
            // the Retina/UI scale and displaced diagonal segments away from
            // their endpoints on high-DPI displays.
            GUI.matrix = savedMatrix * Matrix4x4.TRS(
                new Vector3(start.x, start.y, 0f), Quaternion.Euler(0f, 0f, angle), Vector3.one);
            GUI.DrawTexture(new Rect(0f, -thickness * 0.5f, length, thickness),
                Texture2D.whiteTexture);
            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
        }

        private static void DrawSolidGuiRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawGuiRectOutline(Rect rect, Color color, float thickness)
        {
            DrawSolidGuiRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawSolidGuiRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawSolidGuiRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawSolidGuiRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Rect DrawDraggableHandleWindow(int windowId, Rect rect)
        {
            return GUI.Window(windowId, rect,
                _ => GUI.DragWindow(new Rect(0f, 0f, rect.width, rect.height)),
                GUIContent.none, GUIStyle.none);
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
            var guideName = settings.projectionRegionMode == 2 ? "室内ガイド" : "半球ガイド";
            if (GUILayout.Button(guideVisible ? guideName + "：表示中" : guideName + "：非表示",
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
            var dropdownRect = GetWindowDropdownRect(visibleHeight);
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
            if (!showWindowDropdown) return;

            const float rowHeight = 27f;
            var count = windowCapture.Windows.Count;
            var contentHeight = count * rowHeight;
            var visibleHeight = count == 0 ? 32f : Mathf.Min(270f, contentHeight);
            var dropdownRect = GetWindowDropdownRect(visibleHeight);
            var rawEventType = Event.current.rawType;
            var logicalMousePosition = GetLogicalMousePosition();
            var directMousePosition = Event.current.mousePosition;
            var mouseInside = dropdownRect.Contains(logicalMousePosition)
                              || dropdownRect.Contains(directMousePosition);

            if (rawEventType == EventType.ScrollWheel && mouseInside)
            {
                var maxScroll = Mathf.Max(0f, contentHeight - visibleHeight);
                windowDropdownScroll = Mathf.Clamp(windowDropdownScroll + Event.current.delta.y * rowHeight,
                    0f, maxScroll);
                Event.current.Use();
                return;
            }

            // Process the rows before drawing the normal panel. Unity's own
            // button hit-testing then accounts for GUI.matrix/UI scaling and
            // captures the click before controls underneath the popup can do so.
            if (count > 0)
            {
                var first = Mathf.Max(0, Mathf.FloorToInt(windowDropdownScroll / rowHeight));
                var last = Mathf.Min(count - 1,
                    Mathf.CeilToInt((windowDropdownScroll + visibleHeight) / rowHeight));
                for (var index = first; index <= last; index++)
                {
                    var rowY = dropdownRect.y + 8f + index * rowHeight - windowDropdownScroll;
                    var rowRect = new Rect(dropdownRect.x + 8f, rowY,
                        dropdownRect.width - 26f, 25f);
                    if (!GUI.Button(rowRect, GUIContent.none, GUIStyle.none)) continue;
                    SelectWindowCandidate(index);
                    return;
                }
            }

            if (rawEventType == EventType.MouseDown && Event.current.button == 0 && !mouseInside)
            {
                showWindowDropdown = false;
                Event.current.Use();
            }
        }

        private Rect GetWindowDropdownRect(float visibleHeight)
        {
            return new Rect(panelAreaRect.x + windowDropdownAnchor.x,
                panelAreaRect.y + windowDropdownAnchor.yMax + 2f,
                Mathf.Min(windowDropdownAnchor.width, PanelWidth - 24f), visibleHeight + 16f);
        }

        private void SelectWindowCandidate(int index)
        {
            if (index < 0 || index >= windowCapture.Windows.Count) return;
            selectedWindowIndex = index;
            status = "選択: " + windowCapture.Windows[index].DisplayName;
            showWindowDropdown = false;
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
            DrawProjectionRegionControls();

            if (IsFlatContent()) DrawFlatPlacementControls();
            else if (IsAllSkyCircularContent()) DrawAllSkyPlacementControls();
            else
            {
                BeginSection("表示方向");
                settings.yaw = ContinuousYawControl("Yaw", settings.yaw, 0f);
                GUILayout.Label("Yawは端のない横ドラッグです。投影画面上でも Option/Alt＋横ドラッグ、Q/Eで微調整できます。",
                    smallStyle);
                settings.materialYawKeyDegreesPerSecond = FineSlider("Q/E長押し速度",
                    settings.materialYawKeyDegreesPerSecond, 1f, 60f, 1f,
                    settings.materialYawKeyDegreesPerSecond.ToString("0°/秒"), 15f);
                settings.pitch = Slider("Pitch", settings.pitch, -180f, 180f, settings.pitch.ToString("0.0°"), 180f);
                settings.roll = Slider("Roll", settings.roll, -180f, 180f, settings.roll.ToString("0.0°"), 0f);
                EndSection();
            }

        }

        private void DrawProjectionRegionControls()
        {
            BeginSection("投影範囲");
            GUILayout.BeginHorizontal();
            DrawProjectionRegionModeButton(0, "半球・中心");
            DrawProjectionRegionModeButton(1, "半球・端置き");
            DrawProjectionRegionModeButton(2, "室内コーナー");
            GUILayout.EndHorizontal();

            var offAxis = settings.projectionRegionMode == 1;
            var room = settings.projectionRegionMode == 2;
            GUILayout.Label(room
                ? "前壁・側壁・天井への光線交点を求め、基準視点から継ぎ目なく見えるよう逆算します。寸法と位置の単位はmです。"
                : offAxis
                    ? "プロジェクターをドーム端付近に置く想定です。各光線と球面の交点を計算し、上半球に当たる範囲を可能な限り表示します。"
                    : "プロジェクターを球の中心に置く通常の半球投影です。", smallStyle);

            GUILayout.Space(4f);
            GUILayout.Label("Control確認表示（Outputには影響しません）", smallStyle);
            controlPreviewScale = FineSlider("確認倍率", controlPreviewScale,
                0.01f, 1f, 0.05f, (controlPreviewScale * 100f).ToString("0") + "%", 1f);
            controlPreviewYaw = FineSlider("確認・左右", controlPreviewYaw,
                -75f, 75f, 1f, controlPreviewYaw.ToString("0°"), 0f);
            controlPreviewPitch = FineSlider("確認・上下", controlPreviewPitch,
                -75f, 75f, 1f, controlPreviewPitch.ToString("0°"), 0f);
            controlPreviewRotation = FineSlider("確認・傾き", controlPreviewRotation,
                -180f, 180f, 1f, controlPreviewRotation.ToString("0°"), 0f);
            if (GUILayout.Button("Outputと同じ表示に戻す")) ResetControlPreview();
            if (!room)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("確認画面", rowLabelStyle, GUILayout.Width(82f));
                if (GUILayout.Button("投影結果",
                        domeSimulationVisible ? tabStyle : activeTabStyle))
                    domeSimulationVisible = false;
                if (GUILayout.Button("3Dシミュレーション",
                        domeSimulationVisible ? activeTabStyle : tabStyle))
                    domeSimulationVisible = true;
                GUILayout.EndHorizontal();
                GUILayout.Label("3Dでは球面・プロジェクター位置・光軸・実際の球面交点を確認できます。ドラッグで回転、ホイールで拡大縮小します。",
                    smallStyle);
            }
            if (offAxis)
            {
                settings.domeFov = FineSlider("レンズ投影角", settings.domeFov,
                    30f, 360f, 1f, settings.domeFov.ToString("0°"), 120f);
                GUILayout.Label("光軸を中心とする円錐状の全画角です。例：140°は光軸から片側約70°。実機の投影範囲を合わせる概算値です。",
                    smallStyle);
                settings.offAxisProjectorDistance = FineSlider("中心→設置位置",
                    settings.offAxisProjectorDistance, 0f, 2f, 0.01f,
                    settings.offAxisProjectorDistance.ToString("0.00 R"), 0.95f);
                settings.offAxisProjectorX = FineSlider("設置位置 X",
                    settings.offAxisProjectorX, -2f, 2f, 0.01f,
                    settings.offAxisProjectorX.ToString("0.00 R"), 0f);
                settings.offAxisProjectorHeight = FineSlider("設置高さ",
                    settings.offAxisProjectorHeight, -1.5f, 1.5f, 0.01f,
                    settings.offAxisProjectorHeight.ToString("0.00 R"), 0f);
                settings.offAxisAimYaw = FineSlider("光軸の左右",
                    settings.offAxisAimYaw, -180f, 180f, 0.5f,
                    settings.offAxisAimYaw.ToString("0.0°"), 0f);
                settings.offAxisAimElevation = FineSlider("光軸の仰角",
                    settings.offAxisAimElevation, -90f, 90f, 0.5f,
                    settings.offAxisAimElevation.ToString("0.0°"), 15f);
                settings.offAxisMinimumElevation = FineSlider("下端の仰角",
                    settings.offAxisMinimumElevation, -90f, 0f, 1f,
                    settings.offAxisMinimumElevation.ToString("0°"), -20f);
                GUILayout.Label("Rはドーム半径です。半円へ固定せず、設定した下端仰角以上の球面交点を可能な限り表示します。", smallStyle);
                if (GUILayout.Button("端置き投影を既定へ")) settings.ResetOffAxisProjection();
            }
            else if (room)
            {
                GUILayout.Label("室内寸法", smallStyle);
                settings.roomWidth = FineSlider("幅 X", settings.roomWidth,
                    1f, 30f, 0.05f, settings.roomWidth.ToString("0.00 m"), 5f);
                settings.roomHeight = FineSlider("高さ Y", settings.roomHeight,
                    1f, 10f, 0.05f, settings.roomHeight.ToString("0.00 m"), 2.5f);
                settings.roomDepth = FineSlider("奥行き Z", settings.roomDepth,
                    1f, 30f, 0.05f, settings.roomDepth.ToString("0.00 m"), 5f);

                GUILayout.BeginHorizontal();
                GUILayout.Label("投影する角", rowLabelStyle, GUILayout.Width(82f));
                if (GUILayout.Button("左隅", settings.roomCornerMode == 1 ? activeTabStyle : tabStyle))
                {
                    settings.roomCornerMode = 1;
                    settings.roomUseLeftCorner = true;
                    settings.roomProjectorYaw = settings.roomProjectorYaw > -1f
                        ? -29f : settings.roomProjectorYaw;
                }
                if (GUILayout.Button("両隅", settings.roomCornerMode == 2 ? activeTabStyle : tabStyle))
                {
                    settings.roomCornerMode = 2;
                    settings.roomUseLeftCorner = false;
                    settings.roomProjectorYaw = 0f;
                    if (settings.roomProjectorHorizontalFov < 100f)
                        settings.roomProjectorHorizontalFov = 120f;
                }
                if (GUILayout.Button("右隅", settings.roomCornerMode == 0 ? activeTabStyle : tabStyle))
                {
                    settings.roomCornerMode = 0;
                    settings.roomUseLeftCorner = false;
                    settings.roomProjectorYaw = settings.roomProjectorYaw < 1f
                        ? 29f : settings.roomProjectorYaw;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("投影面", rowLabelStyle, GUILayout.Width(82f));
                settings.roomShowFrontWall = GUILayout.Toggle(settings.roomShowFrontWall,
                    "前壁", GUI.skin.button);
                settings.roomShowSideWall = GUILayout.Toggle(settings.roomShowSideWall,
                    "側壁", GUI.skin.button);
                settings.roomShowCeiling = GUILayout.Toggle(settings.roomShowCeiling,
                    "天井", GUI.skin.button);
                settings.roomShowFloor = GUILayout.Toggle(settings.roomShowFloor,
                    "床", GUI.skin.button);
                GUILayout.EndHorizontal();

                GUILayout.Label("プロジェクター位置", smallStyle);
                settings.roomProjectorX = FineSlider("位置 X", settings.roomProjectorX,
                    -15f, 15f, 0.01f, settings.roomProjectorX.ToString("0.00 m"), 0f);
                settings.roomProjectorY = FineSlider("位置 Y", settings.roomProjectorY,
                    -2f, 10f, 0.01f, settings.roomProjectorY.ToString("0.00 m"), 1.2f);
                settings.roomProjectorZ = FineSlider("位置 Z", settings.roomProjectorZ,
                    -5f, 30f, 0.01f, settings.roomProjectorZ.ToString("0.00 m"), 0.5f);
                settings.roomProjectorYaw = FineSlider("光軸 Yaw", settings.roomProjectorYaw,
                    -180f, 180f, 0.5f, settings.roomProjectorYaw.ToString("0.0°"), 29f);
                settings.roomProjectorPitch = FineSlider("光軸 Pitch", settings.roomProjectorPitch,
                    -89f, 89f, 0.5f, settings.roomProjectorPitch.ToString("0.0°"), 14f);
                settings.roomProjectorRoll = FineSlider("光軸 Roll", settings.roomProjectorRoll,
                    -180f, 180f, 0.5f, settings.roomProjectorRoll.ToString("0.0°"), 0f);
                settings.roomProjectorHorizontalFov = FineSlider("水平画角", settings.roomProjectorHorizontalFov,
                    10f, 170f, 1f, settings.roomProjectorHorizontalFov.ToString("0°"), 80f);
                if (GUILayout.Button("前面4隅ワープを調整", activeTabStyle))
                    StartRoomWarpEditing();
                GUILayout.Label("前面の四隅だけを最終補正します。ドラッグしても位置・姿勢・画角や、ほかの頂点は変化しません。", smallStyle);

                GUILayout.Label("基準視点（ここから見ると正しい形）", smallStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("確認表示", rowLabelStyle, GUILayout.Width(82f));
                DrawRoomControlViewButton(0, "投影");
                DrawRoomControlViewButton(1, "俯瞰3D");
                DrawRoomControlViewButton(2, "上面");
                DrawRoomControlViewButton(3, "側面");
                GUILayout.EndHorizontal();
                GUILayout.Label("俯瞰3Dは空き領域ドラッグで回転、ホイールで拡大縮小。上面／側面では観測者を移動できます。", smallStyle);
                settings.roomViewerX = FineSlider("視点 X", settings.roomViewerX,
                    -15f, 15f, 0.01f, settings.roomViewerX.ToString("0.00 m"), 0f);
                settings.roomViewerY = FineSlider("視点 Y", settings.roomViewerY,
                    -2f, 10f, 0.01f, settings.roomViewerY.ToString("0.00 m"), 1.6f);
                settings.roomViewerZ = FineSlider("視点 Z", settings.roomViewerZ,
                    -5f, 30f, 0.01f, settings.roomViewerZ.ToString("0.00 m"), 1.5f);
                settings.roomGridSpacing = FineSlider("格子間隔", settings.roomGridSpacing,
                    0.1f, 5f, 0.1f, settings.roomGridSpacing.ToString("0.0 m"), 0.5f);
                GUILayout.Label("辺の色：X方向＝赤 / Y方向＝緑 / Z方向＝青", smallStyle);
                GUILayout.Label("座標は部屋中央がX=0、床がY=0、後方がZ=0、前壁がZ=奥行きです。", smallStyle);
                if (GUILayout.Button("室内コーナー投影を既定へ")) settings.ResetRoomProjection();
            }
            else
            {
                settings.domeFov = FineSlider("ドーム画角", settings.domeFov,
                    30f, 360f, 1f, settings.domeFov.ToString("0°"), 180f);
                GUILayout.Label("360°で素材上は北極から南極まで球全体を扱います。実際の到達範囲はレンズと設置条件に依存します。", smallStyle);
            }
            EndSection();
        }

        private void DrawProjectionRegionModeButton(int mode, string label)
        {
            var selected = settings.projectionRegionMode == mode;
            if (!GUILayout.Button(label,
                    selected ? activeTabStyle : tabStyle, GUILayout.ExpandWidth(true))) return;
            if (settings.projectionRegionMode == mode) return;
            settings.projectionRegionMode = mode;
            if (mode == 2) domeSimulationVisible = false;
            // Selecting a mode is not a reset operation. Operators commonly
            // switch between centre and rim layouts while comparing them; the
            // old behaviour silently destroyed the calibrated distance, aim,
            // FOV and output circle every time. Explicit reset buttons remain
            // available inside each mode when defaults are actually wanted.
        }

        private void DrawRoomControlViewButton(int mode, string label)
        {
            if (!GUILayout.Button(label,
                    settings.roomControlViewMode == mode ? activeTabStyle : tabStyle,
                    GUILayout.ExpandWidth(true))) return;
            settings.roomControlViewMode = mode;
            roomWarpEditing = false;
        }

        private void DrawRoomWarpReferenceButton(int face, string label)
        {
            if (!GUILayout.Button(label,
                    settings.roomWarpReferenceFace == face ? activeTabStyle : tabStyle,
                    GUILayout.ExpandWidth(true))) return;
            if (settings.roomWarpReferenceFace == face) return;
            settings.roomWarpReferenceFace = face;
            if (face == 0) settings.roomShowFrontWall = true;
            else if (face == 1) settings.roomShowCeiling = true;
            else if (face == 2) settings.roomShowFloor = true;
            else settings.roomShowSideWall = true;
            settings.ResetRoomWarp();
            roomWarpAutoFitPending = true;
        }

        private void StartRoomWarpEditing()
        {
            settings.roomControlViewMode = 0;
            roomCalibrationStartProfile = LensCalibrationProfile.Capture(
                "__room_calibration_start__", settings);
            // Four-point adjustment is deliberately independent from the
            // physical projector model. Moving one corner must never re-fit
            // position, rotation or FOV and therefore cannot move a corner
            // that the operator has already aligned.
            settings.roomWarpReferenceFace = 0;
            roomWarpEditing = true;
            roomWarpAutoFitPending = true;
            roomCalibrationViewDragging = false;
            roomCalibrationShowOverview = false;
            showControls = false;
            status = "前面の四隅を実際の壁の角へ合わせてください";
        }

        private void FinishRoomWarpEditing()
        {
            roomWarpEditing = false;
            roomCalibrationViewDragging = false;
            roomCalibrationStartProfile = null;
            showControls = true;
            SaveSettings();
            status = "前面4隅ワープを保存しました";
        }

        private void CancelRoomWarpEditing()
        {
            if (roomCalibrationStartProfile != null)
                roomCalibrationStartProfile.ApplyTo(settings);
            roomCalibrationStartProfile = null;
            roomCalibrationAnchors.Clear();
            roomWarpEditing = false;
            roomCalibrationViewDragging = false;
            roomCalibrationShowOverview = false;
            showControls = true;
            ResetControlPreview();
            status = "前面4隅ワープをキャンセルし、開始前の設定へ戻しました";
        }

        private void StartDomeGuiCalibration()
        {
            if (settings.projectionRegionMode >= 2) return;
            domeGuiCalibrationStartProfile = LensCalibrationProfile.Capture(
                "__dome_gui_calibration_start__", settings);
            domeGuiUndoHistory.Clear();
            domeGuiEditing = true;
            showControls = false;
            domeSimulationVisible = true;
            ResetControlPreview();
            status = settings.projectionRegionMode == 1
                ? "プロジェクター位置と光軸を、3D上で実スクリーンへ合わせてください"
                : "中心配置の投影範囲を3Dで確認し、既存の画角を調整できます";
        }

        private void FinishDomeGuiCalibration()
        {
            domeGuiEditing = false;
            domeGuiCalibrationStartProfile = null;
            domeGuiUndoHistory.Clear();
            roomCalibrationViewDragging = false;
            showControls = true;
            SaveSettings();
            status = "半球の既存物理パラメータを保存しました";
        }

        private void CancelDomeGuiCalibration()
        {
            if (domeGuiCalibrationStartProfile != null)
                domeGuiCalibrationStartProfile.ApplyTo(settings);
            domeGuiCalibrationStartProfile = null;
            domeGuiUndoHistory.Clear();
            domeGuiEditing = false;
            roomCalibrationViewDragging = false;
            showControls = true;
            ResetControlPreview();
            status = "物理配置調整をキャンセルし、開始前の設定へ戻しました";
        }

        private void PushDomeGuiUndoState()
        {
            if (!domeGuiEditing) return;
            domeGuiUndoHistory.Push(LensCalibrationProfile.Capture(
                "__dome_gui_undo__", settings));
        }

        private void UndoDomeGuiAdjustment()
        {
            if (!domeGuiEditing || domeGuiUndoHistory.Count == 0)
            {
                status = "戻せる3D調整操作はありません";
                return;
            }
            domeGuiUndoHistory.Pop().ApplyTo(settings);
            domeSimulationOrbitDragging = false;
            domeSimulationProjectorDragging = false;
            domeSimulationAimDragging = false;
            status = $"直前の3D調整を戻しました（残り {domeGuiUndoHistory.Count}）";
        }

        private static Rect GetDomePhysicalCalibrationToolbarRect(float logicalWidth,
            float logicalHeight, float margin)
        {
            var width = Mathf.Min(1260f, logicalWidth - margin * 2f);
            return new Rect((logicalWidth - width) * 0.5f,
                logicalHeight - 154f, width, 136f);
        }

        private void DrawDomePhysicalCalibrationToolbar(float logicalWidth,
            float logicalHeight, float margin)
        {
            var toolbar = GetDomePhysicalCalibrationToolbarRect(logicalWidth,
                logicalHeight, margin);
            // A drag that started in the diagram must never keep ownership
            // after the pointer reaches this toolbar. Otherwise IMGUI buttons
            // and sliders appear visible but receive no click/drag events.
            if (Event.current.type == EventType.MouseDown
                && toolbar.Contains(GetLogicalMousePosition()))
            {
                domeSimulationOrbitDragging = false;
                domeSimulationProjectorDragging = false;
                domeSimulationAimDragging = false;
                // One toolbar gesture (button or slider drag) is one undo
                // step. Capturing on MouseDown avoids hundreds of snapshots
                // while a slider is moving.
                PushDomeGuiUndoState();
            }
            DrawSolidGuiRect(toolbar, new Color(0.10f, 0.12f, 0.15f, 0.97f));
            GUI.Label(new Rect(toolbar.x + 14f, toolbar.y + 8f, 330f, 26f),
                settings.projectionRegionMode == 1
                    ? "半球・端置き　3D現場合わせ"
                    : "半球・中心　3D現場合わせ", sectionTitleStyle);
            GUI.Label(new Rect(toolbar.x + 14f, toolbar.y + 33f,
                    toolbar.width - 500f, 22f),
                settings.projectionRegionMode == 1
                    ? "桃点：光軸方向。点・プロジェクターをドラッグすると既存の物理配置値だけを変更／Cmd・Ctrl+Z：元に戻す。"
                    : "中心配置は確認表示。レンズ投影角を調整／空き領域：視点回転／Cmd・Ctrl+Z：元に戻す。",
                smallStyle);

            var aimZenithRect = new Rect(toolbar.xMax - 438f, toolbar.y + 8f, 104f, 30f);
            var cancelRect = new Rect(toolbar.xMax - 326f, toolbar.y + 8f, 100f, 30f);
            var resetRect = new Rect(toolbar.xMax - 218f, toolbar.y + 8f, 100f, 30f);
            var finishRect = new Rect(toolbar.xMax - 110f, toolbar.y + 8f, 96f, 30f);

            bool ToolbarButton(Rect rect, string label)
            {
                // Standard IMGUI buttons fire on MouseUp. A stale hotControl
                // from a 3D drag can swallow that MouseUp, leaving a button
                // that visibly depresses but does nothing. Register the same
                // action directly on MouseDown as a deterministic fallback.
                var current = Event.current;
                var directPress = current.type == EventType.MouseDown
                                  && current.button == 0
                                  && rect.Contains(GetLogicalMousePosition());
                var standardPress = GUI.Button(rect, label);
                if (directPress && current.type != EventType.Used) current.Use();
                return directPress || standardPress;
            }

            if (ToolbarButton(cancelRect, "調整キャンセル"))
                CancelDomeGuiCalibration();
            if (!domeGuiEditing) return;
            if (settings.projectionRegionMode == 1
                && ToolbarButton(aimZenithRect, "光軸を天頂へ"))
            {
                AimOffAxisProjectorAt(Vector3.up);
                status = "プロジェクターの光軸を半球ガイドの天頂へ合わせました";
            }
            if (ToolbarButton(resetRect, "物理配置を既定"))
            {
                if (settings.projectionRegionMode == 1) settings.ResetOffAxisProjection();
                else settings.domeFov = 180f;
                status = "物理投影パラメータを既定値へ戻しました";
            }
            if (ToolbarButton(finishRect, "調整完了"))
            {
                FinishDomeGuiCalibration();
                return;
            }

            float PhysicalSlider(string label, float value, float minimum,
                float maximum, string format, Rect rect)
            {
                GUI.Label(new Rect(rect.x, rect.y, 92f, 24f), label, smallStyle);
                value = GUI.HorizontalSlider(new Rect(rect.x + 94f, rect.y + 4f,
                    rect.width - 164f, 18f), value, minimum, maximum);
                GUI.Label(new Rect(rect.xMax - 64f, rect.y, 64f, 24f),
                    value.ToString(format), smallStyle);
                return value;
            }

            var columnWidth = (toolbar.width - 42f) / 3f;
            var row1Y = toolbar.y + 66f;
            var row2Y = toolbar.y + 98f;
            settings.domeFov = PhysicalSlider("レンズ投影角", settings.domeFov,
                30f, 360f, "0°", new Rect(toolbar.x + 14f, row1Y, columnWidth, 24f));
            if (settings.projectionRegionMode == 1)
            {
                settings.offAxisProjectorX = PhysicalSlider("設置位置 X",
                    settings.offAxisProjectorX, -2f, 2f, "0.00 R",
                    new Rect(toolbar.x + 14f, row2Y, columnWidth, 24f));
                settings.offAxisProjectorDistance = PhysicalSlider("中心→設置",
                    settings.offAxisProjectorDistance, 0f, 2f, "0.00 R",
                    new Rect(toolbar.x + 21f + columnWidth, row1Y, columnWidth, 24f));
                settings.offAxisProjectorHeight = PhysicalSlider("設置高さ",
                    settings.offAxisProjectorHeight, -1.5f, 1.5f, "0.00 R",
                    new Rect(toolbar.x + 21f + columnWidth, row2Y, columnWidth, 24f));
                settings.offAxisAimYaw = PhysicalSlider("光軸 Yaw",
                    settings.offAxisAimYaw, -180f, 180f, "0.0°",
                    new Rect(toolbar.x + 28f + columnWidth * 2f, row1Y,
                        columnWidth, 24f));
                settings.offAxisAimElevation = PhysicalSlider("光軸 仰角",
                    settings.offAxisAimElevation, -90f, 90f, "0.0°",
                    new Rect(toolbar.x + 28f + columnWidth * 2f, row2Y,
                        columnWidth, 24f));
            }
        }

        private void AimOffAxisProjectorAt(Vector3 target)
        {
            var projector = new Vector3(settings.offAxisProjectorX,
                settings.offAxisProjectorHeight, -settings.offAxisProjectorDistance);
            var direction = (target - projector).normalized;
            var horizontal = Mathf.Sqrt(direction.x * direction.x
                                        + direction.z * direction.z);
            settings.offAxisAimYaw = Mathf.Clamp(
                Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                -180f, 180f);
            settings.offAxisAimElevation = Mathf.Clamp(
                Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg,
                -90f, 90f);
        }

        private void DrawCrossHandle(Vector2 point, Color color)
        {
            DrawCrossHandle(point, color, 1f);
        }

        private void DrawCrossHandle(Vector2 point, Color color, float scale)
        {
            scale = Mathf.Max(scale, 0.5f);
            DrawSolidGuiRect(new Rect(point.x - 12f * scale, point.y - 2f * scale,
                24f * scale, 4f * scale), color);
            DrawSolidGuiRect(new Rect(point.x - 2f * scale, point.y - 12f * scale,
                4f * scale, 24f * scale), color);
            DrawSolidGuiRect(new Rect(point.x - 5f * scale, point.y - 5f * scale,
                10f * scale, 10f * scale), Color.white);
            DrawSolidGuiRect(new Rect(point.x - 3f * scale, point.y - 3f * scale,
                6f * scale, 6f * scale), color);
        }

        private void DrawCalibrationPanel()
        {
            BeginSection("校正表示");
            GUILayout.BeginHorizontal();
            GUILayout.Label("校正表示", GUILayout.Width(82f));
            if (GUILayout.Button(gridModeNames[settings.gridMode]))
                settings.gridMode = (settings.gridMode + 1) % 3;
            GUILayout.EndHorizontal();

            if (settings.projectionRegionMode == 2)
                GUILayout.Label("室内グリッド間隔は「配置」タブの室内コーナー設定で変更できます。", smallStyle);
            else
            {
                GUILayout.BeginHorizontal(GUILayout.Height(28f));
                GUILayout.Label("グリッド間隔", rowLabelStyle, GUILayout.Width(82f));
                if (GUILayout.Button(settings.gridStepDegrees.ToString("0°"), GUILayout.Width(54f)))
                    settings.gridStepDegrees = NextGridStep(settings.gridStepDegrees);
                GUILayout.Label("押すたびに 1° / 2° / 5° / 10° / 15°", smallStyle,
                    GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            settings.gridOpacity = FineSlider("グリッド濃さ", settings.gridOpacity, 0.15f, 1f,
                0.05f, settings.gridOpacity.ToString("0.00"), 1f);
            settings.gridLineWidth = FineSlider("線の太さ", settings.gridLineWidth, 0.25f, 1.5f, 0.05f, settings.gridLineWidth.ToString("0.00"), 0.65f);
            settings.showOrientationMarkers = GUILayout.Toggle(settings.showOrientationMarkers, "方位色マーカー（上:赤 右:緑 下:青 左:黄）");
            if (settings.projectionRegionMode < 2)
            {
                GUILayout.Space(6f);
                const float domeGuiButtonHeight = 32f;
                if (GUILayout.Button(settings.projectionRegionMode == 1
                        ? "端置きの物理配置を3Dで調整"
                        : "中心配置の投影範囲を3Dで確認", activeTabStyle,
                        GUILayout.Height(domeGuiButtonHeight)))
                    StartDomeGuiCalibration();
                GUILayout.Label(settings.projectionRegionMode == 1
                    ? "既存の設置位置・光軸・画角だけを3D上で確認・調整します。追加のメッシュ歪みは使いません。"
                    : "既存の画角と投影範囲を3Dで確認します。追加のメッシュ歪みは使いません。",
                    smallStyle);
            }
            EndSection();

            BeginSection(settings.projectionRegionMode == 2 ? "出力範囲" : "出力円");
            GUILayout.Label(settings.projectionRegionMode == 2
                ? "室内モードでは円形に切らず、プロジェクターの長方形画面全体を使います。位置・倍率・レンズ補正は共通です。"
                : "画面外へはみ出す位置・大きさまで調整できます。", smallStyle);
            settings.centerX = FineSlider("中心 X", settings.centerX, -1f, 2f, 0.001f, settings.centerX.ToString("0.000"), 0.5f);
            settings.centerY = FineSlider("中心 Y", settings.centerY, -1f, 2f, 0.001f, settings.centerY.ToString("0.000"), 0.5f);
            settings.radius = FineSlider("円半径", settings.radius, 0.05f, 3f, 0.001f, settings.radius.ToString("0.000"), 0.485f);
            settings.ellipseX = FineSlider("横倍率", settings.ellipseX, 0.1f, 3f, 0.001f, settings.ellipseX.ToString("0.000"), 1f);
            settings.ellipseY = FineSlider("縦倍率", settings.ellipseY, 0.1f, 3f, 0.001f, settings.ellipseY.ToString("0.000"), 1f);
            settings.outputRotation = FineSlider("出力回転", settings.outputRotation, -180f, 180f, 0.1f, settings.outputRotation.ToString("0.0°"), 0f);
            if (GUILayout.Button(settings.projectionRegionMode == 2 ? "出力範囲をリセット" : "出力円をリセット"))
            {
                settings.ResetOutputCircle();
                if (settings.projectionRegionMode == 1) settings.radius = 1f;
                else if (settings.projectionRegionMode == 2) settings.radius = 0.5f;
            }
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
                settings.projectionRegionMode = 0;
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
            GUILayout.Label("投影方式・プロジェクター位置・出力円・画角・K1～K3・P1/P2・方向別補正をまとめて保存します。素材の向きや再生設定は変わりません。", smallStyle);

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
                playlistNameField = string.Empty;
                CancelMaterialNameEdit();
                CancelPlaylistNameEdit();
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
            GUI.enabled = canAdd;
            var destination = canAdd
                ? playlistCatalog.playlists[selectedPlaylistIndex].name
                : "プレイリスト未選択";
            if (GUILayout.Button("↓  選択素材を「" + TruncateLabel(destination, 22) + "」の末尾へ追加",
                    GUILayout.Height(32f)))
                AddSelectedMaterialToPlaylist();
            GUI.enabled = true;
        }

        private void DrawMaterialLibraryControls()
        {
            BeginSection("素材リスト（materials.json）");
            GUILayout.Label("素材名（新規登録・現在値更新用）", smallStyle);
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
            var materialEditClicked = -1;
            var saveMaterialEdit = false;
            var cancelMaterialEdit = false;
            for (var i = 0; i < materialCatalog.materials.Count; i++)
            {
                var item = materialCatalog.materials[i];
                var label = (i + 1) + ". " + TruncateLabel(item.name, 38);
                GUILayout.BeginHorizontal(GUILayout.Height(26f));
                if (string.Equals(item.id, editingMaterialId, StringComparison.Ordinal))
                {
                    GUI.SetNextControlName(MaterialRenameInputName);
                    editingMaterialName = GUILayout.TextField(editingMaterialName ?? string.Empty,
                        GUILayout.Height(26f), GUILayout.ExpandWidth(true));
                    FocusPendingNameEdit(MaterialRenameInputName);
                    if (GUILayout.Button(new GUIContent("✓", "素材名を保存"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        saveMaterialEdit = true;
                    if (GUILayout.Button(new GUIContent("↶", "編集をキャンセル"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        cancelMaterialEdit = true;
                }
                else
                {
                    if (GUILayout.Button(label,
                            i == selectedMaterialIndex ? selectedListButtonStyle : dropdownButtonStyle,
                            GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                        materialClicked = i;
                    if (GUILayout.Button(new GUIContent("✎", "この素材名を編集"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        materialEditClicked = i;
                }
                GUILayout.EndHorizontal();
            }
            if (materialClicked >= 0) SelectMaterial(materialClicked);
            if (materialEditClicked >= 0) BeginMaterialNameEdit(materialEditClicked);
            if (saveMaterialEdit) SaveMaterialNameEdit();
            if (cancelMaterialEdit) CancelMaterialNameEdit();

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
            GUILayout.Label("新規プレイリスト名", smallStyle);
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
            var playlistEditClicked = -1;
            var savePlaylistEdit = false;
            var cancelPlaylistEdit = false;
            var playlistDeleteClicked = -1;
            for (var i = 0; i < playlistCatalog.playlists.Count; i++)
            {
                var item = playlistCatalog.playlists[i];
                var label = (i + 1) + ". " + TruncateLabel(item.name, 38);
                GUILayout.BeginHorizontal(GUILayout.Height(26f));
                if (string.Equals(item.id, editingPlaylistId, StringComparison.Ordinal))
                {
                    GUI.SetNextControlName(PlaylistRenameInputName);
                    editingPlaylistName = GUILayout.TextField(editingPlaylistName ?? string.Empty,
                        GUILayout.Height(26f), GUILayout.ExpandWidth(true));
                    FocusPendingNameEdit(PlaylistRenameInputName);
                    if (GUILayout.Button(new GUIContent("✓", "プレイリスト名を保存"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        savePlaylistEdit = true;
                    if (GUILayout.Button(new GUIContent("↶", "編集をキャンセル"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        cancelPlaylistEdit = true;
                }
                else
                {
                    if (GUILayout.Button(label,
                            i == selectedPlaylistIndex ? selectedListButtonStyle : dropdownButtonStyle,
                            GUILayout.Height(26f), GUILayout.ExpandWidth(true)))
                        playlistClicked = i;
                    if (GUILayout.Button(new GUIContent("✎", "このプレイリスト名を編集"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        playlistEditClicked = i;
                    if (GUILayout.Button(new GUIContent("✕", "このプレイリストを削除"),
                            GUILayout.Width(30f), GUILayout.Height(26f)))
                        playlistDeleteClicked = i;
                }
                GUILayout.EndHorizontal();
            }
            if (playlistClicked >= 0) SelectPlaylist(playlistClicked);
            if (playlistEditClicked >= 0) BeginPlaylistNameEdit(playlistEditClicked);
            if (savePlaylistEdit) SavePlaylistNameEdit();
            if (cancelPlaylistEdit) CancelPlaylistNameEdit();
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
                if (selectedPlaylistEntryIndex >= 0
                    && selectedPlaylistEntryIndex < playlist.materialIds.Count)
                {
                    var selectedEntryMaterial = FindMaterial(
                        playlist.materialIds[selectedPlaylistEntryIndex]);
                    if (IsStillImageMaterial(selectedEntryMaterial))
                    {
                        var previousDuration = playlist.entryDurationSeconds[selectedPlaylistEntryIndex];
                        var duration = FineSlider("選択画像の表示時間", previousDuration,
                            0.5f, 120f, 0.5f, previousDuration.ToString("0.0秒"),
                            DomePlaylistDefinition.DefaultStillImageDurationSeconds);
                        if (Mathf.Abs(duration - previousDuration) > 0.001f)
                        {
                            playlist.entryDurationSeconds[selectedPlaylistEntryIndex] = duration;
                            SavePlaylistCatalog();
                        }
                    }
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
                var duration = PlaylistEntryDuration(playlist, i, material);
                if (duration <= 0f) continue;
                knownDurationTotal += duration;
                knownDurationCount++;
            }
            var fallbackDuration = knownDurationCount > 0
                ? knownDurationTotal / knownDurationCount
                : 1f;
            for (var i = 0; i < count; i++)
            {
                var material = FindMaterial(playlist.materialIds[i]);
                var duration = PlaylistEntryDuration(playlist, i, material);
                weights[i] = duration > 0f
                    ? duration
                    : fallbackDuration;
                totalWeight += weights[i];
            }

            var activeThisPlaylist = activePlaylistIndex == playlistIndex;
            var x = bar.x;
            var thumbX = -1f;
            for (var i = 0; i < count; i++)
            {
                var material = FindMaterial(playlist.materialIds[i]);
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
                    else if (video != null && video.IsPrepared && video.IsStillImage)
                        progress = Mathf.Clamp01(activeStillImageElapsed
                            / Mathf.Max(0.5f, PlaylistEntryDuration(playlist, i, material)));
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
            var time = string.Empty;
            if (video != null && video.IsPrepared && !video.IsStillImage)
                time = "  " + FormatTime(video.Time) + " / " + FormatTime(video.Duration);
            else if (video != null && video.IsPrepared && video.IsStillImage)
            {
                var duration = PlaylistEntryDuration(playlist, activePlaylistEntryIndex, currentMaterial);
                time = "  " + FormatTime(activeStillImageElapsed) + " / " + FormatTime(duration);
            }
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
            if (video != null && video.IsPrepared && video.IsStillImage)
                return activeStillImageElapsed;
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
            if (video.IsStillImage)
            {
                var material = FindMaterial(materialId);
                activeStillImageElapsed = PlaylistEntryDuration(playlist, entryIndex, material) * fraction;
                mediaShouldPlayWhenVisible = final && resumePlaylistAfterSeek;
                ApplyControlMediaPlaybackState();
                return;
            }
            if (video.Duration <= 0d) return;

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
            CancelMaterialNameEdit();
            CancelContentDelete();
        }

        private void BeginMaterialNameEdit(int index)
        {
            if (index < 0 || index >= materialCatalog.materials.Count) return;
            selectedMaterialIndex = index;
            var material = materialCatalog.materials[index];
            editingMaterialId = material.id;
            editingMaterialName = material.name;
            pendingNameEditFocus = MaterialRenameInputName;
            CancelContentDelete();
            status = "素材名を編集し、✓で保存してください";
        }

        private void SaveMaterialNameEdit()
        {
            var index = materialCatalog.materials.FindIndex(item =>
                string.Equals(item.id, editingMaterialId, StringComparison.Ordinal));
            if (index < 0) { CancelMaterialNameEdit(); return; }
            var name = (editingMaterialName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                status = "素材名を入力してください";
                return;
            }

            var previousName = materialCatalog.materials[index].name;
            materialCatalog.materials[index].name = name;
            selectedMaterialIndex = index;
            materialNameField = name;
            if (!SaveMaterialCatalog())
            {
                materialCatalog.materials[index].name = previousName;
                materialNameField = previousName;
                return;
            }
            CancelMaterialNameEdit();
            status = "素材名を更新しました: " + name;
        }

        private void CancelMaterialNameEdit()
        {
            editingMaterialId = string.Empty;
            editingMaterialName = string.Empty;
            if (pendingNameEditFocus == MaterialRenameInputName) pendingNameEditFocus = string.Empty;
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
            {
                for (var entryIndex = playlist.materialIds.Count - 1; entryIndex >= 0; entryIndex--)
                {
                    if (!string.Equals(playlist.materialIds[entryIndex], deleted.id,
                            StringComparison.Ordinal)) continue;
                    playlist.materialIds.RemoveAt(entryIndex);
                    if (entryIndex < playlist.entryDurationSeconds.Count)
                        playlist.entryDurationSeconds.RemoveAt(entryIndex);
                }
            }
            selectedMaterialIndex = materialCatalog.materials.Count == 0
                ? -1
                : Mathf.Clamp(selectedMaterialIndex, 0, materialCatalog.materials.Count - 1);
            materialNameField = selectedMaterialIndex >= 0
                ? materialCatalog.materials[selectedMaterialIndex].name
                : string.Empty;
            activePlaylistIndex = -1;
            activePlaylistEntryIndex = -1;
            activePlaylistCompleted = false;
            CancelMaterialNameEdit();
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
            if (!SavePlaylistCatalog()) return;
            playlistNameField = string.Empty;
            status = "プレイリストを作成しました: " + name;
        }

        private void BeginPlaylistNameEdit(int index)
        {
            if (index < 0 || index >= playlistCatalog.playlists.Count) return;
            selectedPlaylistIndex = index;
            var playlist = playlistCatalog.playlists[index];
            editingPlaylistId = playlist.id;
            editingPlaylistName = playlist.name;
            pendingNameEditFocus = PlaylistRenameInputName;
            selectedPlaylistEntryIndex = playlist.materialIds.Count == 0 ? -1 : 0;
            CancelContentDelete();
            status = "プレイリスト名を編集し、✓で保存してください";
        }

        private void SavePlaylistNameEdit()
        {
            var index = playlistCatalog.playlists.FindIndex(item =>
                string.Equals(item.id, editingPlaylistId, StringComparison.Ordinal));
            if (index < 0) { CancelPlaylistNameEdit(); return; }
            var name = (editingPlaylistName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                status = "プレイリスト名を入力してください";
                return;
            }

            var previousName = playlistCatalog.playlists[index].name;
            playlistCatalog.playlists[index].name = name;
            selectedPlaylistIndex = index;
            if (!SavePlaylistCatalog())
            {
                playlistCatalog.playlists[index].name = previousName;
                return;
            }
            CancelPlaylistNameEdit();
            status = "プレイリスト名を更新しました: " + name;
        }

        private void CancelPlaylistNameEdit()
        {
            editingPlaylistId = string.Empty;
            editingPlaylistName = string.Empty;
            if (pendingNameEditFocus == PlaylistRenameInputName) pendingNameEditFocus = string.Empty;
        }

        private void FocusPendingNameEdit(string controlName)
        {
            if (!string.Equals(pendingNameEditFocus, controlName, StringComparison.Ordinal)) return;
            GUI.FocusControl(controlName);
            pendingNameEditFocus = string.Empty;
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
            selectedPlaylistEntryIndex = playlist.materialIds.Count == 0 ? -1 : 0;
            CancelPlaylistNameEdit();
            CancelContentDelete();
        }

        private void AddSelectedMaterialToPlaylist()
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count
                || selectedMaterialIndex < 0 || selectedMaterialIndex >= materialCatalog.materials.Count) return;
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            playlist.materialIds.Add(materialCatalog.materials[selectedMaterialIndex].id);
            playlist.entryDurationSeconds.Add(DomePlaylistDefinition.DefaultStillImageDurationSeconds);
            selectedPlaylistEntryIndex = playlist.materialIds.Count - 1;
            SavePlaylistCatalog();
            status = "プレイリストへ追加しました: " + materialCatalog.materials[selectedMaterialIndex].name;
        }

        private void MovePlaylistEntry(int direction)
        {
            if (selectedPlaylistIndex < 0 || selectedPlaylistIndex >= playlistCatalog.playlists.Count) return;
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            var entries = playlist.materialIds;
            if (selectedPlaylistEntryIndex < 0 || selectedPlaylistEntryIndex >= entries.Count) return;
            var source = selectedPlaylistEntryIndex;
            var destination = Mathf.Clamp(source + direction, 0, entries.Count - 1);
            if (destination == source) return;
            (entries[source], entries[destination]) = (entries[destination], entries[source]);
            (playlist.entryDurationSeconds[source], playlist.entryDurationSeconds[destination]) =
                (playlist.entryDurationSeconds[destination], playlist.entryDurationSeconds[source]);

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
            var playlist = playlistCatalog.playlists[selectedPlaylistIndex];
            var entries = playlist.materialIds;
            if (selectedPlaylistEntryIndex < 0 || selectedPlaylistEntryIndex >= entries.Count) return;
            entries.RemoveAt(selectedPlaylistEntryIndex);
            playlist.entryDurationSeconds.RemoveAt(selectedPlaylistEntryIndex);
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

            if (video != null && video.IsPrepared && video.IsStillImage)
            {
                var material = FindMaterial(playlist.materialIds[activePlaylistEntryIndex]);
                var duration = PlaylistEntryDuration(playlist, activePlaylistEntryIndex, material);
                if (activeStillImageElapsed >= duration - 0.01f) activeStillImageElapsed = 0f;
            }

            var effectivelyPlaying = mediaShouldPlayWhenVisible && !settings.showIdleScreen;
            mediaShouldPlayWhenVisible = !effectivelyPlaying;
            if (mediaShouldPlayWhenVisible) settings.showIdleScreen = false;
            ApplyControlMediaPlaybackState();
        }

        private void UpdatePlaylistTransition()
        {
            if (activePlaylistIndex >= 0 && activePlaylistIndex < playlistCatalog.playlists.Count
                && video != null && video.IsPrepared && video.IsStillImage
                && mediaShouldPlayWhenVisible && !settings.showIdleScreen
                && !playlistSeekDragging && !activePlaylistCompleted)
                activeStillImageElapsed += Time.unscaledDeltaTime;

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
                    || video == null || !video.IsPrepared)
                {
                    ResetPlaylistTransition();
                    return;
                }
                var playlist = playlistCatalog.playlists[activePlaylistIndex];
                var halfDuration = Mathf.Max(0.1f, playlist.transitionSeconds * 0.5f);
                var remaining = ActivePlaylistEntryRemainingSeconds(playlist);
                playlistTransitionOpacity = Mathf.Clamp01(remaining / halfDuration);
                if (remaining <= 0.02f)
                {
                    playlistTransitionOpacity = 0f;
                    if (AdvancePlaylistAfterCompletion())
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
                || video == null || !video.IsPrepared) return;
            var activePlaylist = playlistCatalog.playlists[activePlaylistIndex];
            if (!activePlaylist.autoAdvance) return;
            if (!video.IsStillImage && (!video.IsPlaying || video.Duration <= 0d)) return;
            var remainingTime = ActivePlaylistEntryRemainingSeconds(activePlaylist);
            var nextIndex = GetNextPlaylistEntryIndex(activePlaylist, activePlaylistEntryIndex);
            if (nextIndex < 0)
            {
                if (video.IsStillImage && remainingTime <= 0f) FinishCompletedPlaylist();
                return;
            }
            var preloadLeadTime = Mathf.Max(3f, activePlaylist.transitionSeconds + 1f);
            if (remainingTime <= preloadLeadTime && !IsPlaylistEntryPreloaded(activePlaylist, nextIndex))
                PrepareNextPlaylistEntry();
            if (activePlaylist.transitionMode != 1
                || !IsPlaylistEntryPreloaded(activePlaylist, nextIndex))
            {
                if (video.IsStillImage && remainingTime <= 0f) AdvancePlaylistAfterCompletion();
                return;
            }
            var fadeOutDuration = Mathf.Max(0.1f, activePlaylist.transitionSeconds * 0.5f);
            if (remainingTime <= fadeOutDuration)
                playlistTransitionPhase = 1;
        }

        private float ActivePlaylistEntryRemainingSeconds(DomePlaylistDefinition playlist)
        {
            if (playlist == null || activePlaylistEntryIndex < 0
                || activePlaylistEntryIndex >= playlist.materialIds.Count || video == null) return 0f;
            if (!video.IsStillImage)
                return video.Duration > 0d ? Mathf.Max(0f, (float)(video.Duration - video.Time)) : 0f;
            var material = FindMaterial(playlist.materialIds[activePlaylistEntryIndex]);
            return Mathf.Max(0f,
                PlaylistEntryDuration(playlist, activePlaylistEntryIndex, material) - activeStillImageElapsed);
        }

        private void FinishCompletedPlaylist()
        {
            if (AdvancePlaylistAfterCompletion()) return;
            mediaShouldPlayWhenVisible = false;
            SetIdleScreenVisible(true);
            status = "プレイリストの再生が完了しました。中央の再生ボタンで先頭から再生できます";
            SaveSettings();
        }

        private void ResetPlaylistTransition()
        {
            playlistTransitionPhase = 0;
            playlistTransitionOpacity = 1f;
        }

        private int GetNextPlaylistEntryIndex(DomePlaylistDefinition playlist, int currentIndex)
        {
            if (playlist == null || playlist.materialIds.Count == 0) return -1;
            if (playlist.loopMode == 2)
                return Mathf.Clamp(currentIndex, 0, playlist.materialIds.Count - 1);
            var next = currentIndex + 1;
            if (next < playlist.materialIds.Count) return next;
            return playlist.loopMode == 1 ? 0 : -1;
        }

        private bool AdvancePlaylistAfterCompletion()
        {
            if (activePlaylistIndex < 0 || activePlaylistIndex >= playlistCatalog.playlists.Count)
                return false;
            var playlist = playlistCatalog.playlists[activePlaylistIndex];
            if (playlist.loopMode != 2) return AdvanceActivePlaylist(1);
            if (activePlaylistEntryIndex < 0
                || activePlaylistEntryIndex >= playlist.materialIds.Count) return false;

            activePlaylistCompleted = false;
            activeStillImageElapsed = 0f;
            pendingPlaylistSeekFraction = -1f;
            pendingPlaylistSeekMaterialId = string.Empty;
            mediaShouldPlayWhenVisible = true;
            settings.showIdleScreen = false;
            return LoadActivePlaylistEntry();
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
                if (playlist.loopMode != 1)
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
            activeStillImageElapsed = 0f;
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

        private static bool IsStillImageMaterial(DomeMaterialDefinition material)
        {
            if (material == null) return false;
            var extension = Path.GetExtension(material.path ?? string.Empty).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        private static float PlaylistEntryDuration(DomePlaylistDefinition playlist, int entryIndex,
            DomeMaterialDefinition material)
        {
            if (playlist == null || entryIndex < 0 || entryIndex >= playlist.materialIds.Count) return 0f;
            if (IsStillImageMaterial(material))
            {
                if (playlist.entryDurationSeconds != null
                    && entryIndex < playlist.entryDurationSeconds.Count)
                    return Mathf.Clamp(playlist.entryDurationSeconds[entryIndex], 0.5f, 3600f);
                return DomePlaylistDefinition.DefaultStillImageDurationSeconds;
            }
            return material != null ? Mathf.Max(0f, material.durationSeconds) : 0f;
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
            CancelPlaylistNameEdit();
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

        private bool SaveMaterialCatalog()
        {
            try
            {
                configurationStore.SaveMaterials(materialCatalog);
                return true;
            }
            catch (Exception exception)
            {
                status = "materials.jsonの保存エラー: " + exception.Message;
                return false;
            }
        }

        private bool SavePlaylistCatalog()
        {
            try
            {
                configurationStore.SavePlaylists(playlistCatalog);
                return true;
            }
            catch (Exception exception)
            {
                status = "playlists.jsonの保存エラー: " + exception.Message;
                return false;
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
            var guideName = settings.projectionRegionMode == 2 ? "室内ガイド" : "半球ガイド";
            GUILayout.Label(guideName + "（現在の表示内容に重ねる）");
            var guideVisible = settings.gridMode == 1;
            if (GUILayout.Button(guideVisible ? guideName + "：表示中" : guideName + "：非表示",
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

        private IEnumerator ConfigureProjectorDisplay()
        {
            // -monitor is 1-based. Display.main always describes the OS main
            // monitor on macOS, even when Unity has placed this player on the
            // second monitor. Using it caused a 3024x1964 backbuffer to be
            // letterboxed inside a 3840x2160 projector screen.
            yield return null;
            var requestedDisplay = GetRequestedMonitorIndex();
            var displays = Display.displays;
            var selectedDisplay = displays != null && displays.Length > 0
                ? displays[Mathf.Clamp(requestedDisplay, 0, displays.Length - 1)]
                : Display.main;
            var width = selectedDisplay != null ? selectedDisplay.systemWidth : 0;
            var height = selectedDisplay != null ? selectedDisplay.systemHeight : 0;
            if (width < 640 || height < 360)
            {
                var current = Screen.currentResolution;
                width = Mathf.Max(640, current.width);
                height = Mathf.Max(360, current.height);
            }

            Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            // Apply again after the native fullscreen window has reached the
            // requested monitor, then force the camera to occupy the complete
            // drawable area regardless of the Control window's aspect ratio.
            yield return null;
            yield return new WaitForEndOfFrame();
            Screen.SetResolution(width, height, FullScreenMode.FullScreenWindow);
            if (outputCamera != null)
            {
                outputCamera.rect = new Rect(0f, 0f, 1f, 1f);
                outputCamera.ResetAspect();
            }
            UnityEngine.Debug.Log($"Projector output configured on monitor {requestedDisplay + 1} "
                                  + $"at {width} x {height}; drawable {Screen.width} x {Screen.height}");
        }

        private static int GetRequestedMonitorIndex()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length - 1; index++)
            {
                if (!string.Equals(arguments[index], "-monitor",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(arguments[index + 1], out var monitor))
                    return Mathf.Max(0, monitor - 1);
            }
            return 0;
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

        private float ContinuousYawControl(string label, float value, float defaultValue)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(30f));
            GUILayout.Label(label, rowLabelStyle, GUILayout.Width(82f));
            if (GUILayout.Button("−", GUILayout.Width(24f))) value -= 1f;
            var rect = GUILayoutUtility.GetRect(132f, 28f,
                GUILayout.Width(132f), GUILayout.Height(28f));
            value = DrawContinuousAngleEncoder(rect, value);
            if (GUILayout.Button("+", GUILayout.Width(24f))) value += 1f;
            GUILayout.Label(value.ToString("0.0°"), rowLabelStyle, GUILayout.Width(66f));
            if (GUILayout.Button("既定", GUILayout.Width(40f))) value = defaultValue;
            GUILayout.EndHorizontal();
            return SanitizeContinuousYaw(value);
        }

        private float DrawContinuousAngleEncoder(Rect rect, float value)
        {
            var controlId = GUIUtility.GetControlID(0x79A011, FocusType.Passive, rect);
            var current = Event.current;
            if (GUI.enabled)
            {
                if (current.type == EventType.MouseDown && current.button == 0
                    && rect.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    materialYawLastMouse = current.mousePosition;
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    var sensitivity = current.shift ? 0.05f : 0.25f;
                    // Treat the encoder like a surface held by the pointer:
                    // dragging it right pulls the displayed panorama right.
                    value -= (current.mousePosition.x - materialYawLastMouse.x) * sensitivity;
                    materialYawLastMouse = current.mousePosition;
                    current.Use();
                }
                else if (current.rawType == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }

            GUI.Box(rect, GUIContent.none, sliderStyle);
            var phase = Mathf.Repeat(value, 20f) / 20f;
            for (var index = -1; index <= 6; index++)
            {
                var x = rect.x + (index + phase) * rect.width / 5f;
                if (x <= rect.x + 2f || x >= rect.xMax - 2f) continue;
                DrawSolidGuiRect(new Rect(x - 1f, rect.y + 5f, 2f, rect.height - 10f),
                    new Color(0.42f, 0.70f, 0.88f, 0.72f));
            }
            DrawSolidGuiRect(new Rect(rect.center.x - 1.5f, rect.y + 2f, 3f, rect.height - 4f),
                new Color(0.72f, 0.92f, 1f, 1f));
            GUI.Label(rect, "◀  連続回転  ▶", chapterLabelStyle);
            return SanitizeContinuousYaw(value);
        }

        private static float SanitizeContinuousYaw(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private bool CanAdjustMaterialYaw()
        {
            return settings.sourceMode == 0 && !IsFlatContent() && !IsAllSkyCircularContent()
                   && settings.projectionRegionMode != 2 && !domeGuiEditing;
        }

        private void HandleMaterialYawDragGesture()
        {
            var current = Event.current;
            if (!CanAdjustMaterialYaw())
            {
                materialYawDragging = false;
                return;
            }

            var mouse = GetLogicalMousePosition();
            var overControlPanel = showControls && panelAreaRect.Contains(mouse);
            if (current.type == EventType.MouseDown && current.button == 0 && current.alt
                && !overControlPanel)
            {
                materialYawDragging = true;
                materialYawLastMouse = mouse;
                current.Use();
                return;
            }
            if (current.type == EventType.MouseDrag && materialYawDragging)
            {
                var sensitivity = current.shift ? 0.05f : 0.25f;
                settings.yaw = SanitizeContinuousYaw(settings.yaw
                    - (mouse.x - materialYawLastMouse.x) * sensitivity);
                materialYawLastMouse = mouse;
                status = $"表示Yaw {settings.yaw:0.0}°";
                current.Use();
                return;
            }
            if (current.rawType == EventType.MouseUp) materialYawDragging = false;
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
            if (current.keyCode == KeyCode.Z && (current.control || current.command)
                && domeGuiEditing)
            {
                UndoDomeGuiAdjustment();
                current.Use();
                return;
            }
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
                case KeyCode.W:
                    if (settings.projectionRegionMode != 2) break;
                    if (roomWarpEditing) FinishRoomWarpEditing();
                    else StartRoomWarpEditing();
                    current.Use();
                    break;
                case KeyCode.V:
                    if (settings.projectionRegionMode == 2)
                    {
                        if (roomWarpEditing)
                            roomCalibrationShowOverview = !roomCalibrationShowOverview;
                        else
                            settings.roomControlViewMode = settings.roomControlViewMode == 1 ? 0 : 1;
                    }
                    else
                        domeSimulationVisible = !domeSimulationVisible;
                    current.Use();
                    break;
                case KeyCode.C:
                    if (settings.projectionRegionMode != 2 || !roomWarpEditing) break;
                    CancelRoomWarpEditing();
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
                case KeyCode.R when current.shift && (current.control || current.command):
                    settings.ResetGeometry();
                    status = "投影ジオメトリ全体を初期化しました";
                    current.Use();
                    break;
                case KeyCode.O when current.control || current.command:
                    BrowseMedia();
                    current.Use();
                    break;
                case KeyCode.Escape:
                    if (domeGuiEditing)
                    {
                        CancelDomeGuiCalibration();
                        current.Use();
                        break;
                    }
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

        private void UpdateMaterialYawKeyHold()
        {
            if (textInputFocused || !CanAdjustMaterialYaw())
            {
                materialYawQHeld = materialYawEHeld = false;
                return;
            }

            // Read the physical key state every frame. IMGUI KeyDown repeats
            // arrive only at the OS repeat interval and make one-degree steps
            // visibly jump; frame-time integration stays smooth and keeps the
            // same speed on macOS and Windows regardless of repeat settings.
            var qDown = Input.GetKey(KeyCode.Q);
            var eDown = Input.GetKey(KeyCode.E);
            if (qDown && !materialYawQHeld)
            {
                materialYawQHeld = true;
                materialYawQHoldStarted = Time.unscaledTime;
            }
            else if (!qDown && materialYawQHeld)
            {
                if (Time.unscaledTime - materialYawQHoldStarted < MaterialYawKeyHoldDelay)
                    ApplyMaterialYawShortPress(-1f);
                materialYawQHeld = false;
            }

            if (eDown && !materialYawEHeld)
            {
                materialYawEHeld = true;
                materialYawEHoldStarted = Time.unscaledTime;
            }
            else if (!eDown && materialYawEHeld)
            {
                if (Time.unscaledTime - materialYawEHoldStarted < MaterialYawKeyHoldDelay)
                    ApplyMaterialYawShortPress(1f);
                materialYawEHeld = false;
            }

            var direction = 0f;
            if (materialYawQHeld
                && Time.unscaledTime - materialYawQHoldStarted >= MaterialYawKeyHoldDelay)
                direction -= 1f;
            if (materialYawEHeld
                && Time.unscaledTime - materialYawEHoldStarted >= MaterialYawKeyHoldDelay)
                direction += 1f;
            if (Mathf.Approximately(direction, 0f)) return;

            settings.yaw = SanitizeContinuousYaw(settings.yaw
                + direction * settings.materialYawKeyDegreesPerSecond * Time.unscaledDeltaTime);
            status = $"表示Yaw {settings.yaw:0.0}°";
        }

        private void ApplyMaterialYawShortPress(float delta)
        {
            settings.yaw = SanitizeContinuousYaw(settings.yaw + delta);
            status = $"表示Yaw {settings.yaw:0.0}°";
        }

        private static bool IsTextInputFocused()
        {
            var focused = GUI.GetNameOfFocusedControl();
            return focused == MediaPathInputName
                   || focused == LensProfileInputName
                   || focused == MaterialNameInputName
                   || focused == PlaylistNameInputName
                   || focused == MaterialRenameInputName
                   || focused == PlaylistRenameInputName
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
                var loadedSettingsVersion = settings.settingsVersion;
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

                // Rewrite migrated Control settings immediately. Besides
                // persisting normal migrations, this physically removes the
                // deleted GUI residual-warp keys from old JSON instead of
                // merely ignoring them in memory.
                if (!projectorMode && loadedSettingsVersion < settings.settingsVersion)
                    SaveSettings();
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

