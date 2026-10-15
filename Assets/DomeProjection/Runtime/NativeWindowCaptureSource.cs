using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        private int consecutiveCaptureFailures;
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
                consecutiveCaptureFailures++;
                // PrintWindow can fail for a single frame while Windows is
                // updating/compositing the target. Keep the last good texture
                // briefly so Output does not flash black on a transient miss.
                // A sustained failure still falls back after about 0.5 s.
                if (captureTexture == null
                    || consecutiveCaptureFailures >= Mathf.Max(3, captureFps / 2))
                    isPrepared = false;
                ReportErrorOnce(error);
            }
            else
            {
                consecutiveCaptureFailures = 0;
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
            consecutiveCaptureFailures = 0;
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
            consecutiveCaptureFailures = 0;
            isPrepared = false;
            lastError = string.Empty;
            nextCaptureTime = 0f;

            // A captured 4K window occupies tens of MB of GPU memory. Keeping
            // it alive after switching back to AVPro can put Windows decoders
            // under memory/transfer pressure even though it is no longer
            // rendered. Release both GPU and CPU buffers with the capture job.
            if (captureTexture != null) Destroy(captureTexture);
            captureTexture = null;
            pixelBuffer = null;
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

            // PowerPoint does not reliably consume synthetic WM_KEYDOWN messages
            // while the Control application owns foreground focus. Its automation
            // API advances the selected slide show directly and does not steal focus.
            if (IsPowerPointWindow(selectedWindow)
                && TrySendPowerPointNavigation(forward))
                return true;

            return SendNavigationWindows(handle, virtualKey);
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
        private static bool IsPowerPointWindow(CapturableWindow window)
        {
            if (window == null || string.IsNullOrWhiteSpace(window.OwnerName)) return false;

            // Process.ProcessName normally omits the extension, but some
            // Windows/Mono combinations return POWERPNT.EXE. Normalize both
            // forms so the reliable PowerPoint automation path is selected.
            var ownerName = window.OwnerName.Trim();
            if (ownerName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ownerName = ownerName.Substring(0, ownerName.Length - 4);
            return string.Equals(ownerName, "POWERPNT", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TrySendPowerPointNavigation(bool forward)
        {
            object application = null;
            object slideShowWindows = null;
            var initializeResult = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
            var shouldUninitialize = initializeResult >= 0;
            if (initializeResult < 0 && initializeResult != RpcChangedMode)
            {
                UnityEngine.Debug.LogWarning(
                    $"PowerPoint navigation: CoInitializeEx failed (0x{initializeResult:X8}).");
                return false;
            }
            try
            {
                var classResult = CLSIDFromProgID("PowerPoint.Application", out var classId);
                if (classResult != 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"PowerPoint navigation: CLSIDFromProgID failed (0x{classResult:X8}).");
                    return false;
                }

                var activeObjectResult = GetActiveObject(ref classId, IntPtr.Zero, out application);
                if (activeObjectResult != 0 || application == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"PowerPoint navigation: GetActiveObject failed (0x{activeObjectResult:X8}).");
                    return false;
                }

                // Unity's Windows Mono runtime exposes Office objects as
                // System.__ComObject, but Type.InvokeMember cannot resolve
                // their members. Invoke the standard COM IDispatch interface
                // directly, without requiring an Office interop assembly.
                slideShowWindows = GetDispatchProperty(application, "SlideShowWindows");
                if (slideShowWindows == null)
                {
                    UnityEngine.Debug.LogWarning("PowerPoint navigation: SlideShowWindows was unavailable.");
                    return false;
                }
                var count = Convert.ToInt32(GetDispatchProperty(slideShowWindows, "Count"));
                if (count < 1)
                {
                    UnityEngine.Debug.LogWarning("PowerPoint navigation: no running slide show was found.");
                    return false;
                }
                for (var index = 1; index <= count; index++)
                {
                    object slideShowWindow = null;
                    object view = null;
                    try
                    {
                        slideShowWindow = InvokeDispatch(slideShowWindows, "Item",
                            DispatchMethod, index);
                        if (slideShowWindow == null) continue;

                        view = GetDispatchProperty(slideShowWindow, "View");
                        if (view == null) continue;
                        InvokeDispatch(view, forward ? "Next" : "Previous", DispatchMethod);
                        UnityEngine.Debug.Log(
                            $"PowerPoint slide navigation sent via COM: {(forward ? "Next" : "Previous")}");
                        return true;
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"PowerPoint navigation: slide show {index} failed: "
                            + $"{exception.GetType().Name}: {exception.Message}");
                    }
                    finally
                    {
                        ReleaseComObject(view);
                        ReleaseComObject(slideShowWindow);
                    }
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning(
                    $"PowerPoint navigation failed: {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                ReleaseComObject(slideShowWindows);
                ReleaseComObject(application);
                if (shouldUninitialize) CoUninitialize();
            }

            return false;
        }

        private static object GetDispatchProperty(object target, string name)
        {
            return InvokeDispatch(target, name, DispatchPropertyGet);
        }

        private static object InvokeDispatch(object target, string name, ushort flags,
            params object[] arguments)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var dispatch = target as IDispatch;
            if (dispatch == null)
                throw new InvalidCastException($"COM object does not expose IDispatch: {name}");

            var emptyGuid = Guid.Empty;
            var names = new[] { name };
            var identifiers = new int[1];
            var result = dispatch.GetIDsOfNames(ref emptyGuid, names, 1, 0, identifiers);
            if (result < 0) Marshal.ThrowExceptionForHR(result);

            var parameters = new DispatchParameters();
            var argumentMemory = IntPtr.Zero;
            try
            {
                if (arguments != null && arguments.Length > 0)
                {
                    argumentMemory = Marshal.AllocCoTaskMem(NativeVariantSize * arguments.Length);
                    // IDispatch expects arguments in reverse order.
                    for (var index = 0; index < arguments.Length; index++)
                    {
                        var destination = IntPtr.Add(argumentMemory,
                            NativeVariantSize * (arguments.Length - 1 - index));
                        Marshal.GetNativeVariantForObject(arguments[index], destination);
                    }
                    parameters.Arguments = argumentMemory;
                    parameters.ArgumentCount = (uint)arguments.Length;
                }

                var exceptionInfo = new DispatchExceptionInfo();
                result = dispatch.Invoke(identifiers[0], ref emptyGuid, 0, flags,
                    ref parameters, out var value, ref exceptionInfo, out var argumentError);
                if (result < 0)
                {
                    var detail = !string.IsNullOrWhiteSpace(exceptionInfo.Description)
                        ? exceptionInfo.Description
                        : $"HRESULT 0x{result:X8}, argument {argumentError}";
                    throw new COMException($"{name}: {detail}", result);
                }
                return value;
            }
            finally
            {
                if (argumentMemory != IntPtr.Zero)
                {
                    for (var index = 0; index < arguments.Length; index++)
                        VariantClear(IntPtr.Add(argumentMemory, NativeVariantSize * index));
                    Marshal.FreeCoTaskMem(argumentMemory);
                }
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.ReleaseComObject(value); }
            catch (COMException) { /* Office may already have released the object. */ }
        }

        private static bool SendNavigationWindows(IntPtr topLevelWindow, int virtualKey)
        {
            if (!IsWindow(topLevelWindow)) return false;

            var previousForeground = GetForegroundWindow();
            var currentThread = GetCurrentThreadId();
            var threadId = GetWindowThreadProcessId(topLevelWindow, out _);
            var threadsAttached = threadId != 0 && threadId != currentThread
                && AttachThreadInput(currentThread, threadId, true);
            try
            {
                if (IsIconic(topLevelWindow)) ShowWindow(topLevelWindow, ShowWindowRestore);
                if (!SetForegroundWindow(topLevelWindow)
                    && GetForegroundWindow() != topLevelWindow)
                    return SendNavigationWindowMessages(topLevelWindow, virtualKey);

                // Prefer the child control that owns keyboard focus after activation.
                var target = topLevelWindow;
                var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
                if (GetGUIThreadInfo(threadId, ref info) && info.Focus != IntPtr.Zero
                    && (info.Focus == topLevelWindow || IsChild(topLevelWindow, info.Focus)))
                {
                    target = info.Focus;
                    SetFocus(target);
                }

                var scanCode = (ushort)(MapVirtualKeyW((uint)virtualKey, MapVirtualKeyVkToVsc) & 0xffu);
                var inputs = new[]
                {
                    new NativeInput
                    {
                        Type = InputKeyboard,
                        Data = new NativeInputUnion
                        {
                            Keyboard = new NativeKeyboardInput
                            {
                                Scan = scanCode,
                                Flags = KeyEventExtendedKey | KeyEventScanCode
                            }
                        }
                    },
                    new NativeInput
                    {
                        Type = InputKeyboard,
                        Data = new NativeInputUnion
                        {
                            Keyboard = new NativeKeyboardInput
                            {
                                Scan = scanCode,
                                Flags = KeyEventExtendedKey | KeyEventScanCode | KeyEventKeyUp
                            }
                        }
                    }
                };

                var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
                if (sent == (uint)inputs.Length)
                {
                    // Give the target queue a chance to consume both events before
                    // foreground focus is restored to the Control application.
                    Thread.Sleep(20);
                    return true;
                }

                return SendNavigationWindowMessages(target, virtualKey);
            }
            finally
            {
                if (previousForeground != IntPtr.Zero && IsWindow(previousForeground))
                    SetForegroundWindow(previousForeground);
                if (threadsAttached) AttachThreadInput(currentThread, threadId, false);
            }
        }

        private static bool SendNavigationWindowMessages(IntPtr target, int virtualKey)
        {
            var scanCode = MapVirtualKeyW((uint)virtualKey, MapVirtualKeyVkToVsc) & 0xffu;
            var keyDownData = 1L | ((long)scanCode << 16) | (1L << 24);
            var keyUpData = keyDownData | (1L << 30) | (1L << 31);
            return PostMessageW(target, WindowMessageKeyDown,
                       new IntPtr(virtualKey), new IntPtr(keyDownData))
                && PostMessageW(target, WindowMessageKeyUp,
                       new IntPtr(virtualKey), new IntPtr(keyUpData));
        }

        private void EnumerateWindowsWindows(List<CapturableWindow> destination)
        {
            var ownProcessId = Process.GetCurrentProcess().Id;
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) || IsIconic(window)) return true;
                if (DwmGetWindowAttribute(window, DwmWindowAttributeCloaked,
                        out var cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0)
                    return true;
                // WDA_MONITOR and WDA_EXCLUDEFROMCAPTURE both deliberately
                // prevent normal desktop capture, so neither belongs in the UI.
                if (GetWindowDisplayAffinity(window, out var displayAffinity)
                    && displayAffinity != 0)
                    return true;
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
        private const uint MapVirtualKeyVkToVsc = 0;
        private const uint InputKeyboard = 1;
        private const uint KeyEventExtendedKey = 0x0001;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventScanCode = 0x0008;
        private const uint CoInitApartmentThreaded = 0x2;
        private const int RpcChangedMode = unchecked((int)0x80010106);
        private const ushort DispatchMethod = 0x1;
        private const ushort DispatchPropertyGet = 0x2;
        private static readonly int NativeVariantSize = IntPtr.Size == 8 ? 24 : 16;
        private const uint PrintWindowRenderFullContent = 0x00000002;
        private const int DwmWindowAttributeCloaked = 14;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            public int Size;
            public uint Flags;
            public IntPtr Active;
            public IntPtr Focus;
            public IntPtr Capture;
            public IntPtr MenuOwner;
            public IntPtr MoveSize;
            public IntPtr Caret;
            public NativeRect CaretRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            public uint Type;
            public NativeInputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] public NativeKeyboardInput Keyboard;
            // INPUT's union is sized by MOUSEINPUT (32 bytes on x64). Keeping
            // this field is required even though navigation only sends keys;
            // otherwise cbSize is too small and SendInput rejects the array.
            [FieldOffset(0)] public NativeMouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            public ushort VirtualKey;
            public ushort Scan;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            public int X;
            public int Y;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public IntPtr ExtraInfo;
        }

        [ComImport]
        [Guid("00020400-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDispatch
        {
            [PreserveSig] int GetTypeInfoCount(out uint count);
            [PreserveSig] int GetTypeInfo(uint index, uint locale, out IntPtr typeInfo);
            [PreserveSig] int GetIDsOfNames(ref Guid interfaceId,
                [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names,
                uint nameCount, uint locale, [Out] int[] identifiers);
            [PreserveSig] int Invoke(int memberId, ref Guid interfaceId, uint locale, ushort flags,
                ref DispatchParameters parameters,
                [MarshalAs(UnmanagedType.Struct)] out object result,
                ref DispatchExceptionInfo exceptionInfo, out uint argumentError);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatchParameters
        {
            public IntPtr Arguments;
            public IntPtr NamedArgumentIds;
            public uint ArgumentCount;
            public uint NamedArgumentCount;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DispatchExceptionInfo
        {
            public ushort Code;
            public ushort Reserved;
            [MarshalAs(UnmanagedType.BStr)] public string Source;
            [MarshalAs(UnmanagedType.BStr)] public string Description;
            [MarshalAs(UnmanagedType.BStr)] public string HelpFile;
            public uint HelpContext;
            public IntPtr ReservedPointer;
            public IntPtr DeferredFillIn;
            public int ErrorCode;
        }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(
            IntPtr window, int attribute, out int value, int valueSize);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int maximumCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("ole32.dll", CharSet = CharSet.Unicode)] private static extern int CLSIDFromProgID(string progId, out Guid classId);
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("oleaut32.dll")] private static extern int GetActiveObject(ref Guid classId, IntPtr reserved,
            [MarshalAs(UnmanagedType.Interface)] out object instance);
        [DllImport("oleaut32.dll")] private static extern int VariantClear(IntPtr variant);
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
            var array = CGWindowListCopyWindowInfo(
                WindowListOptionOnScreenOnly | WindowListExcludeDesktopElements, 0);
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
                    var sharingState = GetDictionaryInt(dictionary, "kCGWindowSharingState");
                    var isOnScreen = GetDictionaryInt(dictionary, "kCGWindowIsOnscreen");
                    if (windowId <= 0 || processId <= 0 || processId == ownProcessId || layer != 0
                        || sharingState <= 0 || isOnScreen == 0)
                        continue;

                    if (!TryGetDictionaryRect(dictionary, "kCGWindowBounds", out var bounds)
                        || bounds.Size.Width < 160d || bounds.Size.Height < 100d)
                        continue;

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
                || string.Equals(owner, "Control Center", StringComparison.OrdinalIgnoreCase)
                || owner.IndexOf("Dome Projection Output", StringComparison.OrdinalIgnoreCase) >= 0)
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

        private static bool TryGetDictionaryRect(IntPtr dictionary, string keyName, out CGRect rect)
        {
            rect = default;
            var key = CFStringCreateWithCString(IntPtr.Zero, keyName, CfStringEncodingUtf8);
            if (key == IntPtr.Zero) return false;
            try
            {
                var value = CFDictionaryGetValue(dictionary, key);
                return value != IntPtr.Zero && CGRectMakeWithDictionaryRepresentation(value, out rect);
            }
            finally { CFRelease(key); }
        }

        private const uint WindowListOptionOnScreenOnly = 1u << 0;
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
        [DllImport(CoreGraphics)] [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out CGRect rect);
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
