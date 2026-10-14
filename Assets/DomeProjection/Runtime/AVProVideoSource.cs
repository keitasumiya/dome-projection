using System;
using System.IO;
using RenderHeads.Media.AVProVideo;
using UnityEngine;

namespace DomeProjection
{
    /// <summary>
    /// AVPro-backed primary media source. Preview seeks are deliberately
    /// approximate and non-blocking; release seeks are exact.
    /// </summary>
    public sealed class AVProVideoSource : MonoBehaviour, IVideoSource
    {
        public event Action Prepared;
        public event Action<string> Failed;
        public event Action Ended;

        private MediaPlayer player;
        private Texture2D imageTexture;
        private bool imagePrepared;
        private bool prepared;
        private bool audioMuted;
        private bool resumeAfterSeek;
        private bool finalSeekPending;
        private int finalSeekIssuedFrame;
        private double requestedSeekTime;
        private string sourcePath = string.Empty;

        public Texture Texture => imagePrepared
            ? imageTexture
            : player != null && player.TextureProducer != null
                ? player.TextureProducer.GetTexture()
                : null;
        public bool IsPrepared => imagePrepared || prepared;
        public bool IsStillImage => imagePrepared;
        public bool IsPlaying => !imagePrepared && player?.Control?.IsPlaying() == true;
        public bool IsSeeking => !imagePrepared && (finalSeekPending || player?.Control?.IsSeeking() == true);
        // Native AVPro backends expose their texture orientation at runtime.
        // Still images loaded by ImageConversion follow Unity's normal UVs.
        public bool RequiresVerticalFlip => !imagePrepared
            && player?.TextureProducer?.RequiresVerticalFlip() == true;
        public bool AudioMuted
        {
            get => audioMuted;
            set
            {
                audioMuted = value;
                if (player != null) player.AudioMuted = value;
            }
        }
        public bool IsLooping
        {
            get => !imagePrepared && player != null && player.Loop;
            set { if (player != null) player.Loop = value; }
        }
        public double Time
        {
            get
            {
                if (imagePrepared || !prepared || player?.Control == null) return 0d;
                return finalSeekPending ? requestedSeekTime : player.Control.GetCurrentTime();
            }
            set => Seek(value, IsPlaying || resumeAfterSeek);
        }
        public double Duration => !imagePrepared && prepared && player?.Info != null
            ? player.Info.GetDuration()
            : 0d;
        public int Width => imagePrepared
            ? imageTexture.width
            : prepared && player?.Info != null ? player.Info.GetVideoWidth() : 0;
        public int Height => imagePrepared
            ? imageTexture.height
            : prepared && player?.Info != null ? player.Info.GetVideoHeight() : 0;
        public string SourcePath => sourcePath;

        private void Awake()
        {
            player = gameObject.AddComponent<MediaPlayer>();
            player.AutoOpen = false;
            player.AutoStart = false;
            player.AudioVolume = 1f;
            player.AudioMuted = audioMuted;
            player.TextureFilterMode = FilterMode.Bilinear;
            player.TextureWrapMode = TextureWrapMode.Repeat;
            player.Events.AddListener(OnMediaEvent);
        }

        private void OnDestroy()
        {
            if (player != null) player.Events.RemoveListener(OnMediaEvent);
            DestroyImageTexture();
        }

        private void Update()
        {
            if (!finalSeekPending || player?.Control == null
                || UnityEngine.Time.frameCount <= finalSeekIssuedFrame || player.Control.IsSeeking()) return;

            // Very short seeks can complete before AVPro exposes a seeking=true
            // frame, so its FinishedSeeking event is not guaranteed. Complete
            // once the clock reaches the target, with a timeout to avoid leaving
            // playback permanently paused on a backend-specific failure.
            var closeEnough = Math.Abs(player.Control.GetCurrentTime() - requestedSeekTime) < 0.1d;
            if (closeEnough || UnityEngine.Time.frameCount - finalSeekIssuedFrame > 60)
                CompleteFinalSeek();
        }

        public void Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Failed?.Invoke("動画または画像ファイルを指定してください。");
                return;
            }

            path = path.Trim().Trim('"');
            var isLocalFile = !path.Contains("://");
            if (isLocalFile && !File.Exists(path))
            {
                Failed?.Invoke("ファイルが見つかりません: " + path);
                return;
            }

            ClearMediaState();
            sourcePath = path;
            if (isLocalFile && IsSupportedImage(path))
            {
                OpenImage(path);
                return;
            }

            var resolvedPath = isLocalFile ? Path.GetFullPath(path) : path;
            if (!player.OpenMedia(MediaPathType.AbsolutePathOrURL, resolvedPath, false))
                Failed?.Invoke("AVPro Videoで動画を開けませんでした: " + path);
        }

        public void Play()
        {
            if (imagePrepared || !prepared || player?.Control == null) return;
            if (finalSeekPending)
            {
                resumeAfterSeek = true;
                return;
            }
            player.AudioMuted = audioMuted;
            player.Play();
        }

        public void Pause()
        {
            resumeAfterSeek = false;
            if (!imagePrepared && player?.Control != null) player.Pause();
        }

        public void SeekPreview(double time)
        {
            if (imagePrepared || !prepared || player?.Control == null) return;
            requestedSeekTime = ClampTime(time);
            resumeAfterSeek = false;
            finalSeekPending = false;
            if (player.Control.IsPlaying()) player.Pause();
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // A small tolerance lets AVFoundation return a nearby decoded frame
            // quickly while remaining much closer than a keyframe-only seek.
            const double tolerance = 1d / 30d;
            player.Control.SeekWithTolerance(requestedSeekTime, tolerance, tolerance);
#else
            player.Control.SeekFast(requestedSeekTime);
#endif
        }

        public void Seek(double time, bool resumePlayback)
        {
            if (imagePrepared || !prepared || player?.Control == null) return;
            requestedSeekTime = ClampTime(time);
            resumeAfterSeek = resumePlayback;
            finalSeekPending = true;
            finalSeekIssuedFrame = UnityEngine.Time.frameCount;
            if (player.Control.IsPlaying()) player.Pause();
            player.Control.Seek(requestedSeekTime);
        }

        public void Stop()
        {
            resumeAfterSeek = false;
            finalSeekPending = false;
            if (!imagePrepared && player?.Control != null) player.Stop();
        }

        public void Clear()
        {
            sourcePath = string.Empty;
            ClearMediaState();
        }

        private void ClearMediaState()
        {
            resumeAfterSeek = false;
            finalSeekPending = false;
            prepared = false;
            imagePrepared = false;
            DestroyImageTexture();
            if (player != null && player.MediaOpened) player.CloseMedia();
        }

        private void OnMediaEvent(MediaPlayer _, MediaPlayerEvent.EventType eventType, ErrorCode errorCode)
        {
            switch (eventType)
            {
                case MediaPlayerEvent.EventType.FirstFrameReady:
                    if (prepared) break;
                    prepared = true;
                    player.AudioMuted = audioMuted;
                    Debug.Log(
                        $"AVPro prepared: {player.Info.GetVideoWidth()}x{player.Info.GetVideoHeight()}, " +
                        $"duration={player.Info.GetDuration():F3}s, audio={player.Info.HasAudio()}, " +
                        $"flipY={RequiresVerticalFlip}, backend={player.Info.GetPlayerDescription()}");
                    Prepared?.Invoke();
                    break;
                case MediaPlayerEvent.EventType.FinishedSeeking:
                    if (!finalSeekPending) break;
                    CompleteFinalSeek();
                    break;
                case MediaPlayerEvent.EventType.FinishedPlaying:
                    Ended?.Invoke();
                    break;
                case MediaPlayerEvent.EventType.Error:
                    finalSeekPending = false;
                    resumeAfterSeek = false;
                    Failed?.Invoke("AVPro Videoエラー: " + Helper.GetErrorMessage(errorCode));
                    break;
            }
        }

        private void CompleteFinalSeek()
        {
            if (!finalSeekPending) return;
            finalSeekPending = false;
            var shouldResume = resumeAfterSeek;
            resumeAfterSeek = false;
            player.AudioMuted = audioMuted;
            if (shouldResume) player.Play();
        }

        private double ClampTime(double time)
        {
            return Math.Max(0d, Math.Min(time, Duration));
        }

        private void OpenImage(string path)
        {
            Texture2D texture = null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, true))
                    throw new InvalidDataException("JPEG/PNG画像として読み込めませんでした。");

                texture.name = Path.GetFileName(path);
                texture.wrapMode = TextureWrapMode.Repeat;
                texture.filterMode = FilterMode.Bilinear;
                imageTexture = texture;
                imagePrepared = true;
                Prepared?.Invoke();
            }
            catch (Exception exception)
            {
                if (texture != null) Destroy(texture);
                Failed?.Invoke("画像読込エラー: " + exception.Message);
            }
        }

        private static bool IsSupportedImage(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        private void DestroyImageTexture()
        {
            if (imageTexture == null) return;
            Destroy(imageTexture);
            imageTexture = null;
        }
    }
}
