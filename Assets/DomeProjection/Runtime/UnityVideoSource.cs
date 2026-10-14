using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace DomeProjection
{
    [RequireComponent(typeof(VideoPlayer))]
    public sealed class UnityVideoSource : MonoBehaviour, IVideoSource
    {
        public event Action Prepared;
        public event Action<string> Failed;
        public event Action Ended;

        private VideoPlayer player;
        private Texture2D imageTexture;
        private bool imagePrepared;
        private bool audioMuted;
        private bool seekInProgress;
        private bool resumeAfterSeek;
        private double requestedSeekTime;
        private string sourcePath = string.Empty;

        public Texture Texture => imagePrepared ? imageTexture : player != null ? player.texture : null;
        public bool IsPrepared => imagePrepared || player != null && player.isPrepared;
        public bool IsStillImage => imagePrepared;
        public bool IsPlaying => !imagePrepared && player != null && player.isPlaying;
        public bool AudioMuted
        {
            get => audioMuted;
            set
            {
                audioMuted = value;
                ApplyAudioMute();
            }
        }
        public bool IsLooping
        {
            get => !imagePrepared && player != null && player.isLooping;
            set { if (!imagePrepared && player != null) player.isLooping = value; }
        }

        public double Time
        {
            get => !imagePrepared && IsPrepared ? seekInProgress ? requestedSeekTime : player.time : 0d;
            set => Seek(value, IsPlaying || seekInProgress && resumeAfterSeek);
        }

        public double Duration => !imagePrepared && IsPrepared && double.IsFinite(player.length) ? player.length : 0d;
        public int Width => imagePrepared ? imageTexture.width : IsPrepared ? (int)player.width : 0;
        public int Height => imagePrepared ? imageTexture.height : IsPrepared ? (int)player.height : 0;
        public string SourcePath => sourcePath;

        private void Awake()
        {
            player = GetComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.renderMode = VideoRenderMode.APIOnly;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.prepareCompleted += OnPrepared;
            player.seekCompleted += OnSeekCompleted;
            player.errorReceived += OnError;
            player.loopPointReached += OnLoopPointReached;
        }

        private void OnDestroy()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.seekCompleted -= OnSeekCompleted;
                player.errorReceived -= OnError;
                player.loopPointReached -= OnLoopPointReached;
            }
            DestroyImageTexture();
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

            player.Stop();
            seekInProgress = false;
            resumeAfterSeek = false;
            imagePrepared = false;
            DestroyImageTexture();
            sourcePath = path;

            if (isLocalFile && IsSupportedImage(path))
            {
                OpenImage(path);
                return;
            }

            // A properly escaped file URI is reliable with spaces and Japanese
            // filenames on both Windows and macOS.
            player.url = isLocalFile ? new Uri(Path.GetFullPath(path)).AbsoluteUri : path;
            player.Prepare();
        }

        public void Play()
        {
            if (imagePrepared || !IsPrepared) return;
            if (seekInProgress)
            {
                resumeAfterSeek = true;
                return;
            }
            player.Play();
            ApplyAudioMute();
        }

        public void Pause()
        {
            if (imagePrepared || player == null) return;
            resumeAfterSeek = false;
            player.Pause();
        }

        public void Seek(double time, bool resumePlayback)
        {
            if (imagePrepared || !IsPrepared || !player.canSetTime) return;
            requestedSeekTime = Math.Max(0d, Math.Min(time, Duration));
            resumeAfterSeek = resumePlayback;
            seekInProgress = true;
            player.Pause();
            player.time = requestedSeekTime;
        }

        public void Stop()
        {
            if (imagePrepared || player == null) return;
            seekInProgress = false;
            resumeAfterSeek = false;
            player.Stop();
        }

        public void Clear()
        {
            seekInProgress = false;
            resumeAfterSeek = false;
            imagePrepared = false;
            sourcePath = string.Empty;
            DestroyImageTexture();
            if (player == null) return;
            player.Stop();
            player.url = string.Empty;
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
                imageTexture = null;
                imagePrepared = false;
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

        private void OnPrepared(VideoPlayer _)
        {
            seekInProgress = false;
            resumeAfterSeek = false;
            ApplyAudioMute();
            Prepared?.Invoke();
        }

        private void OnSeekCompleted(VideoPlayer _)
        {
            if (!seekInProgress) return;
            seekInProgress = false;
            var shouldResume = resumeAfterSeek;
            resumeAfterSeek = false;
            ApplyAudioMute();
            if (!shouldResume) return;
            player.Play();
            // Direct audio can be reset internally by a platform decoder seek.
            // Reapply the requested mute state after playback has resumed.
            ApplyAudioMute();
        }

        private void ApplyAudioMute()
        {
            if (player == null || player.audioOutputMode != VideoAudioOutputMode.Direct) return;
            try { player.SetDirectAudioMute(0, audioMuted); }
            catch { }
        }

        private void OnError(VideoPlayer _, string message)
        {
            seekInProgress = false;
            resumeAfterSeek = false;
            Failed?.Invoke(message);
        }

        private void OnLoopPointReached(VideoPlayer _)
        {
            if (player != null && !player.isLooping) Ended?.Invoke();
        }
    }
}

