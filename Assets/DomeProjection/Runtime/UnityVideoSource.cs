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

        private VideoPlayer player;
        private string sourcePath = string.Empty;

        public Texture Texture => player != null ? player.texture : null;
        public bool IsPrepared => player != null && player.isPrepared;
        public bool IsPlaying => player != null && player.isPlaying;
        public bool IsLooping
        {
            get => player != null && player.isLooping;
            set { if (player != null) player.isLooping = value; }
        }

        public double Time
        {
            get => IsPrepared ? player.time : 0d;
            set { if (IsPrepared && player.canSetTime) player.time = Math.Max(0d, Math.Min(value, Duration)); }
        }

        public double Duration => IsPrepared && double.IsFinite(player.length) ? player.length : 0d;
        public int Width => IsPrepared ? (int)player.width : 0;
        public int Height => IsPrepared ? (int)player.height : 0;
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
            player.errorReceived += OnError;
        }

        private void OnDestroy()
        {
            if (player == null) return;
            player.prepareCompleted -= OnPrepared;
            player.errorReceived -= OnError;
        }

        public void Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Failed?.Invoke("動画ファイルを指定してください。");
                return;
            }

            path = path.Trim().Trim('"');
            if (!path.Contains("://") && !File.Exists(path))
            {
                Failed?.Invoke("ファイルが見つかりません: " + path);
                return;
            }

            player.Stop();
            sourcePath = path;
            player.url = path;
            player.Prepare();
        }

        public void Play() { if (IsPrepared) player.Play(); }
        public void Pause() { if (player != null) player.Pause(); }
        public void Stop() { if (player != null) player.Stop(); }

        private void OnPrepared(VideoPlayer _)
        {
            Prepared?.Invoke();
        }

        private void OnError(VideoPlayer _, string message)
        {
            Failed?.Invoke(message);
        }
    }
}

