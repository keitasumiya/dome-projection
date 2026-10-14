using System;
using UnityEngine;

namespace DomeProjection
{
    /// <summary>
    /// Keeps the dome renderer independent from Unity VideoPlayer or AVPro Video.
    /// </summary>
    public interface IVideoSource
    {
        event Action Prepared;
        event Action<string> Failed;
        event Action Ended;

        Texture Texture { get; }
        bool IsPrepared { get; }
        bool IsStillImage { get; }
        bool IsPlaying { get; }
        bool AudioMuted { get; set; }
        bool IsLooping { get; set; }
        double Time { get; set; }
        double Duration { get; }
        int Width { get; }
        int Height { get; }
        string SourcePath { get; }

        void Open(string path);
        void Play();
        void Pause();
        void Seek(double time, bool resumePlayback);
        void Stop();
        void Clear();
    }
}

