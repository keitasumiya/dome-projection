using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DomeProjection
{
    /// <summary>
    /// Captures one desktop window into a Unity texture without requiring a
    /// package. Windows uses PrintWindow and macOS uses CoreGraphics. Both are
    /// deliberately isolated behind IWindowCaptureSource so a GPU capture
    /// backend can replace them later without changing the renderer or UI.
    /// </summary>
    public sealed class NativeWindowCaptureSource : MonoBehaviour, IWindowCaptureSource
    {
        public event Action Prepared;
        public event Action<string> Failed;

        private readonly List<CapturableWindow> windows = new List<CapturableWindow>();
        private CapturableWindow selectedWindow;
        private Texture2D captureTexture;
        private byte[] pixelBuffer;
        private bool isPrepared;
        private float nextCaptureTime;
        private string lastError = string.Empty;
        private int captureFps = 30;
        public bool RequestPermissionAutomatically { get; set; }

        public IReadOnlyList<CapturableWindow> Windows => windows;
        public CapturableWindow SelectedWindow => selectedWindow;
        public Texture Texture => captureTexture;
        public bool IsPrepared => isPrepared && captureTexture != null;
        public int Width => captureTexture != null ? captureTexture.width : 0;
        public int Height => captureTexture != null ? captureTexture.height : 0;
        public bool HasCapturePermission
        {
            get
            {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                return CGPreflightScreenCaptureAccess();
#else
                return true;
#endif
            }
        }
        public bool HasAccessibilityPermission
        {
            get
            {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                return AXIsProcessTrusted();
#else
                return true;
#endif
            }
        }
        public int CaptureFps
        {
            get => captureFps;
            set => captureFps = Mathf.Clamp(value, 5, 60);
        }

        private void Update()
        {
            if (selectedWindow == null || Time.unscaledTime < nextCaptureTime) return;
            nextCaptureTime = Time.unscaledTime + 1f / Mathf.Max(5, captureFps);

            if (!CaptureSelectedWindow(out var error))
            {
                isPrepared = false;
                ReportErrorOnce(error);
            }
            else
            {
                lastError = string.Empty;
                if (!isPrepared)
                {
                    isPrepared = true;
                    Prepared?.Invoke();
                }
            }
        }

        private void OnDestroy()
        {
            if (captureTexture != null) Destroy(captureTexture);
            captureTexture = null;
        }

        public void RefreshWindows()
        {
            var selectedId = selectedWindow?.Id ?? 0L;
            windows.Clear();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            EnumerateWindowsWindows(windows);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (!CGPreflightScreenCaptureAccess())
            {
                if (RequestPermissionAutomatically) CGRequestScreenCaptureAccess();
                ReportErrorOnce("macOSの「画面収録」を許可してから、アプリを再起動してください。");
            }
            EnumerateWindowsMac(windows);
#endif
            windows.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName,
                StringComparison.CurrentCultureIgnoreCase));
            selectedWindow = windows.Find(item => item.Id == selectedId);
        }

        public bool SelectWindow(long id)
        {
            var candidate = windows.Find(item => item.Id == id);
            if (candidate == null)
            {
                ReportErrorOnce("選択したウィンドウが見つかりません。ウィンドウ一覧を更新してください。");
                return false;
            }

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            // macOS can enumerate stale, minimized, or Finder desktop entries
            // that CoreGraphics cannot image. Validate before saving the choice.
            if (!CaptureWindowMac((uint)candidate.Id, out var validationError))
            {
                ReportErrorOnce(validationError);
                return false;
            }
#endif
            selectedWindow = candidate;
            isPrepared = captureTexture != null;
            lastError = string.Empty;
            nextCaptureTime = 0f;
            UnityEngine.Debug.Log($"Window capture selected: {candidate.DisplayName} (id {candidate.Id})");
            if (isPrepared) Prepared?.Invoke();
            return true;
        }

        public bool RequestCapturePermission()
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (CGPreflightScreenCaptureAccess()) return true;
            return CGRequestScreenCaptureAccess();
#else
            return true;
#endif
        }

        public bool RequestAccessibilityPermission()
        {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            if (AXIsProcessTrusted()) return true;

            // AXIsProcessTrustedWithOptions shows macOS's Accessibility prompt
            // only when the documented AXTrustedCheckOptionPrompt flag is true.
            var key = CFStringCreateWithCString(IntPtr.Zero,
                "AXTrustedCheckOptionPrompt", CfStringEncodingUtf8);
            var numberClass = objc_getClass("NSNumber");
            var trueValue = objc_msgSend_Byte_IntPtr(numberClass,
                sel_registerName("numberWithBool:"), 1);
            var dictionaryClass = objc_getClass("NSDictionary");
            var options = objc_msgSend_TwoIntPtr(dictionaryClass,
                sel_registerName("dictionaryWithObject:forKey:"), trueValue, key);
            try { return options != IntPtr.Zero && AXIsProcessTrustedWithOptions(options); }
            finally { if (key != IntPtr.Zero) CFRelease(key); }
#else
            return true;
#endif
        }

        public void StopCapture()
        {
            selectedWindow = null;
            isPrepared = false;
            lastError = string.Empty;
        }

        public bool ActivateSelectedWindow()
        {
            if (selectedWindow == null) return false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var handle = new IntPtr(selectedWindow.Id);
            if (!IsWindow(handle)) return false;
            if (IsIconic(handle)) ShowWindow(handle, ShowWindowRestore);
            BringWindowToTop(handle);
            return SetForegroundWindow(handle);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var applicationClass = objc_getClass("NSRunningApplication");
            var application = objc_msgSend_Int(applicationClass,
                sel_registerName("runningApplicationWithProcessIdentifier:"), selectedWindow.ProcessId);
            return application != IntPtr.Zero && objc_msgSend_ULong(application,
                sel_registerName("activateWithOptions:"), 3UL);
#else
            return false;
#endif
        }

        public bool SendPrevious()
        {
            return SendNavigation(false);
        }

        public bool SendNext()
        {
            return SendNavigation(true);
        }

        private bool SendNavigation(bool forward)
        {
            if (selectedWindow == null) return false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var handle = new IntPtr(selectedWindow.Id);
            var virtualKey = forward ? VirtualKeyRight : VirtualKeyLeft;
            return PostMessageW(handle, WindowMessageKeyDown, new IntPtr(virtualKey), IntPtr.Zero)
                && PostMessageW(handle, WindowMessageKeyUp, new IntPtr(virtualKey), IntPtr.Zero);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            var keyCode = (ushort)(forward ? MacKeyRightArrow : MacKeyLeftArrow);
            var keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, true);
            var keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, false);
            if (keyDown == IntPtr.Zero || keyUp == IntPtr.Zero)
            {
                if (keyDown != IntPtr.Zero) CFRelease(keyDown);
                if (keyUp != IntPtr.Zero) CFRelease(keyUp);
                return false;
            }

            CGEventPostToPid(selectedWindow.ProcessId, keyDown);
            CGEventPostToPid(selectedWindow.ProcessId, keyUp);
            CFRelease(keyDown);
            CFRelease(keyUp);
            return true;
#else
            return false;
#endif
        }

        private void EnsureTexture(int width, int height)
        {
            if (captureTexture != null && captureTexture.width == width && captureTexture.height == height) return;
            if (captureTexture != null) Destroy(captureTexture);
            captureTexture = new Texture2D(width, height, TextureFormat.BGRA32, false, false)
            {
                name = "Captured Window",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            pixelBuffer = null;
            isPrepared = false;
        }

        private void ReportErrorOnce(string message)
        {
            if (string.IsNullOrWhiteSpace(message) || message == lastError) return;
            lastError = message;
            UnityEngine.Debug.LogWarning("Window capture: " + message);
            Failed?.Invoke(message);
        }

        private bool CaptureSelectedWindow(out string error)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return CaptureWindowWindows(new IntPtr(selectedWindow.Id), out error);
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return CaptureWindowMac((uint)selectedWindow.Id, out error);
#else
            error = "このOSではウィンドウキャプチャを利用できません。";
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void EnumerateWindowsWindows(List<CapturableWindow> destination)
        {
            var ownProcessId = Process.GetCurrentProcess().Id;
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) || IsIconic(window)) return true;
                GetWindowThreadProcessId(window, out var processIdValue);
                if (processIdValue == 0 || processIdValue == ownProcessId) return true;

                var titleLength = GetWindowTextLengthW(window);
                if (titleLength <= 0) return true;
                var titleBuilder = new StringBuilder(titleLength + 1);
                GetWindowTextW(window, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString().Trim();
                if (title.Length == 0) return true;

                if (!GetWindowRect(window, out var rect) || rect.Right - rect.Left < 160 || rect.Bottom - rect.Top < 100)
                    return true;

                var owner = string.Empty;
                try { owner = Process.GetProcessById((int)processIdValue).ProcessName; }
                catch { /* The process may have ended or be elevated. */ }
                destination.Add(new CapturableWindow
                {
                    Id = window.ToInt64(),
                    ProcessId = (int)processIdValue,
                    OwnerName = owner,
                    Title = title
                });
                return true;
            }, IntPtr.Zero);
        }

        private bool CaptureWindowWindows(IntPtr window, out string error)
        {
            error = string.Empty;
            if (!IsWindow(window))
            {
                error = "キャプチャ対象のウィンドウが閉じられました。";
                return false;
            }
            if (IsIconic(window))
            {
                error = "対象ウィンドウが最小化されています。復元してください。";
                return false;
            }
            if (!GetWindowRect(window, out var rect))
            {
                error = "対象ウィンドウのサイズを取得できません。";
                return false;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 2 || height < 2)
            {
                error = "対象ウィンドウのサイズが小さすぎます。";
                return false;
            }

            var windowDc = GetWindowDC(window);
            if (windowDc == IntPtr.Zero)
            {
                error = "対象ウィンドウの描画領域を取得できません。";
                return false;
            }

            var memoryDc = CreateCompatibleDC(windowDc);
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    // Positive height creates a bottom-up bitmap, which matches
                    // Unity's raw texture row order.
                    Height = height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BitmapCompressionRgb
                }
            };
            var bitmap = CreateDIBSection(windowDc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            var previous = bitmap != IntPtr.Zero ? SelectObject(memoryDc, bitmap) : IntPtr.Zero;

            try
            {
                if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                {
                    error = "キャプチャ用バッファを作成できません。";
                    return false;
                }

                var rendered = PrintWindow(window, memoryDc, PrintWindowRenderFullContent);
                if (!rendered)
                    rendered = BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, SourceCopy);
                if (!rendered)
                {
                    error = "ウィンドウの画像を取得できません。";
                    return false;
                }

                EnsureTexture(width, height);
                captureTexture.LoadRawTextureData(bits, width * height * 4);
                captureTexture.Apply(false, false);
                return true;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
                ReleaseDC(window, windowDc);
            }
        }

        private const int ShowWindowRestore = 9;
        private const int VirtualKeyLeft = 0x25;
        private const int VirtualKeyRight = 0x27;
        private const uint WindowMessageKeyDown = 0x0100;
        private const uint WindowMessageKeyUp = 0x0101;
        private const uint PrintWindowRenderFullContent = 0x00000002;
        private const uint DibRgbColors = 0;
        private const uint BitmapCompressionRgb = 0;
        private const uint SourceCopy = 0x00CC0020;

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColorsUsed;
            public uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colors;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int maximumCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private void EnumerateWindowsMac(List<CapturableWindow> destination)
        {
            var array = CGWindowListCopyWindowInfo(WindowListOptionAll | WindowListExcludeDesktopElements, 0);
            if (array == IntPtr.Zero) return;
            try
            {
                var ownProcessId = Process.GetCurrentProcess().Id;
                var count = CFArrayGetCount(array).ToInt64();
                for (long index = 0; index < count; index++)
                {
                    var dictionary = CFArrayGetValueAtIndex(array, new IntPtr(index));
                    if (dictionary == IntPtr.Zero) continue;
                    var windowId = GetDictionaryInt(dictionary, "kCGWindowNumber");
                    var processId = GetDictionaryInt(dictionary, "kCGWindowOwnerPID");
                    var layer = GetDictionaryInt(dictionary, "kCGWindowLayer");
                    if (windowId <= 0 || processId <= 0 || processId == ownProcessId || layer != 0) continue;

                    var owner = GetDictionaryString(dictionary, "kCGWindowOwnerName");
                    var title = GetDictionaryString(dictionary, "kCGWindowName");
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    if (IsKnownUncapturableMacWindow(owner, title)) continue;
                    destination.Add(new CapturableWindow
                    {
                        Id = windowId,
                        ProcessId = processId,
                        OwnerName = owner,
                        Title = title
                    });
                }
            }
            finally
            {
                CFRelease(array);
            }
        }

        private static bool IsKnownUncapturableMacWindow(string owner, string title)
        {
            if (string.Equals(owner, "Dock", StringComparison.OrdinalIgnoreCase)
                || string.Equals(owner, "Window Server", StringComparison.OrdinalIgnoreCase)
                || string.Equals(owner, "SystemUIServer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(owner, "Control Center", StringComparison.OrdinalIgnoreCase))
                return true;

            // Finder's desktop can appear as a layer-zero pseudo window even
            // with ExcludeDesktopElements, but CGWindowListCreateImage cannot
            // capture it as a normal application window.
            return string.Equals(owner, "Finder", StringComparison.OrdinalIgnoreCase)
                && (title.IndexOf("デスクトップ", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(title.Trim(), "Desktop", StringComparison.OrdinalIgnoreCase));
        }

        private bool CaptureWindowMac(uint windowId, out string error)
        {
            error = string.Empty;
            var nullBounds = new CGRect
            {
                Origin = new CGPoint { X = double.PositiveInfinity, Y = double.PositiveInfinity },
                Size = new CGSize { Width = 0d, Height = 0d }
            };
            var image = CGWindowListCreateImage(nullBounds, WindowListIncludingWindow, windowId,
                WindowImageBoundsIgnoreFraming | WindowImageBestResolution);
            if (image == IntPtr.Zero)
            {
                error = "対象ウィンドウの画像を取得できません。最小化中のウィンドウやデスクトップ等の特殊ウィンドウは取り込めません。";
                return false;
            }

            try
            {
                var width = checked((int)CGImageGetWidth(image).ToInt64());
                var height = checked((int)CGImageGetHeight(image).ToInt64());
                var bytesPerRow = checked((int)CGImageGetBytesPerRow(image).ToInt64());
                if (width < 2 || height < 2 || bytesPerRow < width * 4)
                {
                    error = "対象ウィンドウの画像サイズが不正です。";
                    return false;
                }

                var provider = CGImageGetDataProvider(image);
                var data = provider != IntPtr.Zero ? CGDataProviderCopyData(provider) : IntPtr.Zero;
                if (data == IntPtr.Zero)
                {
                    error = "ウィンドウの画像データを取得できません。";
                    return false;
                }

                try
                {
                    var source = CFDataGetBytePtr(data);
                    var requiredBytes = width * height * 4;
                    if (source == IntPtr.Zero || CFDataGetLength(data).ToInt64() < (long)bytesPerRow * height)
                    {
                        error = "ウィンドウの画像データが不足しています。";
                        return false;
                    }

                    EnsureTexture(width, height);
                    if (pixelBuffer == null || pixelBuffer.Length != requiredBytes)
                        pixelBuffer = new byte[requiredBytes];

                    // CoreGraphics starts at the top row; Unity raw textures start
                    // at the bottom row.
                    for (var row = 0; row < height; row++)
                    {
                        var sourceRow = IntPtr.Add(source, row * bytesPerRow);
                        var destinationOffset = (height - 1 - row) * width * 4;
                        Marshal.Copy(sourceRow, pixelBuffer, destinationOffset, width * 4);
                    }
                    captureTexture.LoadRawTextureData(pixelBuffer);
                    captureTexture.Apply(false, false);
                    return true;
                }
                finally
                {
                    CFRelease(data);
                }
            }
            finally
            {
                CGImageRelease(image);
            }
        }

        private static int GetDictionaryInt(IntPtr dictionary, string keyName)
        {
            var key = CFStringCreateWithCString(IntPtr.Zero, keyName, CfStringEncodingUtf8);
            if (key == IntPtr.Zero) return 0;
            try
            {
                var value = CFDictionaryGetValue(dictionary, key);
                return value != IntPtr.Zero && CFNumberGetValue(value, CfNumberSInt32Type, out var result) ? result : 0;
            }
            finally { CFRelease(key); }
        }

        private static string GetDictionaryString(IntPtr dictionary, string keyName)
        {
            var key = CFStringCreateWithCString(IntPtr.Zero, keyName, CfStringEncodingUtf8);
            if (key == IntPtr.Zero) return string.Empty;
            try
            {
                var value = CFDictionaryGetValue(dictionary, key);
                if (value == IntPtr.Zero) return string.Empty;
                var length = CFStringGetLength(value);
                var maximumBytes = CFStringGetMaximumSizeForEncoding(length, CfStringEncodingUtf8).ToInt64() + 1;
                if (maximumBytes <= 1 || maximumBytes > 1024 * 1024) return string.Empty;
                var buffer = new byte[maximumBytes];
                if (!CFStringGetCString(value, buffer, new IntPtr(maximumBytes), CfStringEncodingUtf8)) return string.Empty;
                var zero = Array.IndexOf(buffer, (byte)0);
                return Encoding.UTF8.GetString(buffer, 0, zero >= 0 ? zero : buffer.Length);
            }
            finally { CFRelease(key); }
        }

        private const uint WindowListOptionAll = 0;
        private const uint WindowListIncludingWindow = 1u << 3;
        private const uint WindowListExcludeDesktopElements = 1u << 4;
        private const uint WindowImageBoundsIgnoreFraming = 1u << 0;
        private const uint WindowImageBestResolution = 1u << 3;
        private const uint CfStringEncodingUtf8 = 0x08000100;
        private const int CfNumberSInt32Type = 3;
        private const int MacKeyLeftArrow = 123;
        private const int MacKeyRightArrow = 124;

        [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct CGSize { public double Width, Height; }
        [StructLayout(LayoutKind.Sequential)] private struct CGRect { public CGPoint Origin; public CGSize Size; }

        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

        [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
        [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowId, uint imageOption);
        [DllImport(CoreGraphics)] private static extern IntPtr CGImageGetWidth(IntPtr image);
        [DllImport(CoreGraphics)] private static extern IntPtr CGImageGetHeight(IntPtr image);
        [DllImport(CoreGraphics)] private static extern IntPtr CGImageGetBytesPerRow(IntPtr image);
        [DllImport(CoreGraphics)] private static extern IntPtr CGImageGetDataProvider(IntPtr image);
        [DllImport(CoreGraphics)] private static extern void CGImageRelease(IntPtr image);
        [DllImport(CoreGraphics)] private static extern IntPtr CGDataProviderCopyData(IntPtr provider);
        [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGPreflightScreenCaptureAccess();
        [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGRequestScreenCaptureAccess();
        [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool AXIsProcessTrusted();
        [DllImport(ApplicationServices)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);
        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);
        [DllImport(CoreGraphics)] private static extern void CGEventPostToPid(int processId, IntPtr cgEvent);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetCount(IntPtr array);
        [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, IntPtr index);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringGetLength(IntPtr value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFStringGetMaximumSizeForEncoding(IntPtr length, uint encoding);
        [DllImport(CoreFoundation)] private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, IntPtr bufferSize, uint encoding);
        [DllImport(CoreFoundation)] private static extern bool CFNumberGetValue(IntPtr number, int numberType, out int value);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
        [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetLength(IntPtr data);
        [DllImport(ObjectiveC)] private static extern IntPtr objc_getClass(string name);
        [DllImport(ObjectiveC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend_Int(IntPtr receiver, IntPtr selector, int argument);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend_Byte_IntPtr(IntPtr receiver, IntPtr selector, byte argument);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend_TwoIntPtr(IntPtr receiver, IntPtr selector, IntPtr argument1, IntPtr argument2);
        [DllImport(ObjectiveC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool objc_msgSend_ULong(IntPtr receiver, IntPtr selector, ulong argument);
#endif
    }
}
