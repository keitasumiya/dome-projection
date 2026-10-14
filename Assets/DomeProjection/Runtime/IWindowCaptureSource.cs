using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomeProjection
{
    public sealed class CapturableWindow
    {
        public long Id;
        public int ProcessId;
        public string OwnerName;
        public string Title;

        public string DisplayName
        {
            get
            {
                var owner = string.IsNullOrWhiteSpace(OwnerName) ? "不明なアプリ" : OwnerName.Trim();
                var title = string.IsNullOrWhiteSpace(Title) ? "（タイトルなし）" : Title.Trim();
                return owner + " — " + title;
            }
        }
    }

    public interface IWindowCaptureSource
    {
        event Action Prepared;
        event Action<string> Failed;

        IReadOnlyList<CapturableWindow> Windows { get; }
        CapturableWindow SelectedWindow { get; }
        Texture Texture { get; }
        bool IsPrepared { get; }
        int Width { get; }
        int Height { get; }
        int CaptureFps { get; set; }
        bool HasCapturePermission { get; }
        bool HasAccessibilityPermission { get; }

        void RefreshWindows();
        bool RequestCapturePermission();
        bool RequestAccessibilityPermission();
        bool SelectWindow(long id);
        void StopCapture();
        bool ActivateSelectedWindow();
        bool SendPrevious();
        bool SendNext();
    }
}
