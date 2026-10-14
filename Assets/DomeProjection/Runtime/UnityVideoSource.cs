using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace DomeProjection
{
    [RequireComponent(typeof(VideoPlayer), typeof(AudioSource))]
    public sealed class UnityVideoSource : MonoBehaviour, IVideoSource
    {
        public event Action Prepared;
        public event Action<string> Failed;
        public event Action Ended;

        private VideoPlayer player;
        private AudioSource audioSource;
        private Texture2D imageTexture;
        private bool imagePrepared;
        private bool audioMuted;
        private bool seekInProgress;
        private bool resumePending;
        private bool resumeAfterSeek;
        private double requestedSeekTime;
        private double activeSeekTime;
        private int seekGeneration;
        private string sourcePath = string.Empty;

        public Texture Texture => imagePrepared ? imageTexture : player != null ? player.texture : null;
        public bool IsPrepared => imagePrepared || player != null && player.isPrepared;
        public bool IsStillImage => imagePrepared;
        public bool IsPlaying => !imagePrepared && player != null && player.isPlaying;
        public bool IsSeeking => !imagePrepared && (seekInProgress || resumePending);
        // Unity's VideoPlayer and ImageConversion textures already use the same
        // UV convention as the dome shader.
        public bool RequiresVerticalFlip => false;
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
            set => Seek(value, IsPlaying || resumePending || seekInProgress && resumeAfterSeek);
        }

        public double Duration => !imagePrepared && IsPrepared && double.IsFinite(player.length) ? player.length : 0d;
        public int Width => imagePrepared ? imageTexture.width : IsPrepared ? (int)player.width : 0;
        public int Height => imagePrepared ? imageTexture.height : IsPrepared ? (int)player.height : 0;
        public string SourcePath => sourcePath;

        private void Awake()
        {
            player = GetComponent<VideoPlayer>();
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
            audioSource.volume = 1f;
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.renderMode = VideoRenderMode.APIOnly;
            // Route video audio through Unity's AudioSource instead of Direct.
            // The platform-native Direct path can remain silent after a seek;
            // AudioSource gives us a stable mute and restartable audio route.
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;
            player.SetTargetAudioSource(0, audioSource);
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
            seekGeneration++;
            seekInProgress = false;
            resumePending = false;
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
            player.controlledAudioTrackCount = 1;
            player.EnableAudioTrack(0, true);
            player.SetTargetAudioSource(0, audioSource);
            player.Prepare();
        }

        public void Play()
        {
            if (imagePrepared || !IsPrepared) return;
            if (seekInProgress || resumePending)
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
            if (resumePending)
            {
                resumePending = false;
                seekGeneration++;
            }
            resumeAfterSeek = false;
            player.Pause();
        }

        public void Seek(double time, bool resumePlayback)
        {
            if (imagePrepared || !IsPrepared || !player.canSetTime) return;
            requestedSeekTime = Math.Max(0d, Math.Min(time, Duration));
            resumeAfterSeek = resumePlayback;
            if (seekInProgress) return;
            BeginPendingSeek();
        }

        public void SeekPreview(double time)
        {
            Seek(time, false);
        }

        private void BeginPendingSeek()
        {
            resumePending = false;
            seekInProgress = true;
            activeSeekTime = requestedSeekTime;
            seekGeneration++;
            player.Pause();
            player.time = activeSeekTime;
        }

        public void Stop()
        {
            if (imagePrepared || player == null) return;
            seekGeneration++;
            seekInProgress = false;
            resumePending = false;
            resumeAfterSeek = false;
            player.Stop();
        }

        public void Clear()
        {
            seekInProgress = false;
            resumePending = false;
            resumeAfterSeek = false;
            seekGeneration++;
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
            seekGeneration++;
            seekInProgress = false;
            resumePending = false;
            resumeAfterSeek = false;
            if (player.audioTrackCount > 0)
            {
                // Re-assert the route after the native backend has inspected the
                // file. AVFoundation may recreate its audio track during Prepare.
                player.EnableAudioTrack(0, true);
                player.SetTargetAudioSource(0, audioSource);
            }
            ApplyAudioMute();
            Debug.Log(
                $"Video prepared: {player.width}x{player.height}, " +
                $"audioTracks={player.audioTrackCount}, " +
                $"track0Enabled={(player.audioTrackCount > 0 && player.IsAudioTrackEnabled(0))}, " +
                $"audioListener={FindAnyObjectByType<AudioListener>() != null}, " +
                $"muted={audioSource.mute}");
            Prepared?.Invoke();
        }

        private void OnSeekCompleted(VideoPlayer _)
        {
            if (!seekInProgress) return;
            // Coalesce rapid scrub requests. Never stack native decoder seeks;
            // finish the active one, then jump directly to the latest target.
            if (Math.Abs(requestedSeekTime - activeSeekTime) > 0.02d)
            {
                BeginPendingSeek();
                return;
            }

            seekInProgress = false;
            var shouldResume = resumeAfterSeek;
            resumeAfterSeek = false;
            ApplyAudioMute();
            if (shouldResume)
            {
                resumePending = true;
                StartCoroutine(ResumeAfterSeek(seekGeneration));
            }
        }

        private IEnumerator ResumeAfterSeek(int generation)
        {
            // Let the platform decoder publish the sought video frame and refill
            // its audio queue before resuming. Immediate Play from within the
            // seek callback is unreliable on AVFoundation.
            yield return null;
            if (generation != seekGeneration || seekInProgress || !resumePending || !IsPrepared) yield break;
            resumePending = false;
            player.Play();
            ApplyAudioMute();
            yield return null;
            if (generation == seekGeneration) ApplyAudioMute();
        }

        private void ApplyAudioMute()
        {
            if (audioSource != null) audioSource.mute = audioMuted;
        }

        private void OnError(VideoPlayer _, string message)
        {
            seekInProgress = false;
            resumePending = false;
            resumeAfterSeek = false;
            seekGeneration++;
            Failed?.Invoke(message);
        }

        private void OnLoopPointReached(VideoPlayer _)
        {
            if (player != null && !player.isLooping) Ended?.Invoke();
        }
    }
}

