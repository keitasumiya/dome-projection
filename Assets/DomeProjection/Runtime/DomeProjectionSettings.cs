using System;
using UnityEngine;

namespace DomeProjection
{
    [Serializable]
    public sealed class DomeProjectionSettings
    {
        public int settingsVersion = 11;
        public string videoPath = string.Empty;

        // Input source. 0 = video/image file, 1 = captured desktop window.
        public int sourceMode;
        // Files can be interpreted as 360 equirectangular or as a flat panel.
        // Captured windows always use the flat projection.
        public bool fileIsFlat;
        public string capturedWindowOwner = string.Empty;
        public string capturedWindowTitle = string.Empty;
        // Native window handle/CGWindowID. Control and Output are separate
        // processes, but this identifier refers to the same OS window in both.
        // Owner/title remain as a restart fallback because IDs are not stable.
        public long capturedWindowId;
        public int windowCaptureFps = 30;
        public bool focusWindowOnCapture;
        public bool initialMacPermissionsRequested;

        // Flat content is placed on a virtual tangent plane inside the dome.
        // Polar angle is measured from the dome centre (zenith) toward its rim.
        public float flatAzimuth;
        public float flatPolarAngle = 45f;
        public float flatRoll = 180f;
        public float flatHorizontalFov = 100f;
        public float flatHeightScale = 1f;
        public float flatCropLeft;
        public float flatCropRight;
        public float flatCropTop;
        public float flatCropBottom;

        // View orientation in the source sphere.
        public float yaw;
        public float pitch = 180f;
        public float roll;
        public float domeFov = 180f;

        // Output circle. Center is in normalized output coordinates; radius is
        // relative to output height, so 0.5 gives a height-filling circle.
        public float centerX = 0.5f;
        public float centerY = 0.5f;
        public float radius = 0.485f;
        public float ellipseX = 1f;
        public float ellipseY = 1f;
        public float outputRotation;

        // Brown-Conrady style lens calibration. The radial coefficients correct
        // symmetric distortion; P1/P2 correct decentering and asymmetry.
        public float radialK1;
        public float radialK2;
        public float radialK3;
        public float tangentialP1;
        public float tangentialP2;
        public float warpTop;
        public float warpBottom;
        public float warpLeft;
        public float warpRight;

        public float brightness = 1f;
        public float gamma = 1f;
        public float edgeFade;
        public bool flipInputX;
        public bool flipInputY;
        public bool loop = true;
        public bool audioMuted;
        // Control -> Output state update rate. 60 Hz is the balanced default;
        // 30 Hz saves resources and 120 Hz minimizes adjustment latency.
        public int bridgeUpdateHz = 60;

        public bool startMaximized = true;
        public int windowWidth = 1600;
        public int windowHeight = 900;
        // Keep the operator UI on display 0 and render a clean dome output on
        // display 1 (or another selected extended display).
        public bool useExtendedDisplayOutput;

        // 0 = off, 1 = overlay, 2 = calibration pattern only.
        public int gridMode = 2;
        public float gridStepDegrees = 5f;
        public float gridOpacity = 0.65f;
        public float gridLineWidth = 0.65f;
        public bool showOrientationMarkers;
        public int outputDisplay;

        public void ResetGeometry()
        {
            ResetPlacement();
            ResetLensCalibration();
            brightness = gamma = 1f;
            edgeFade = 0f;
            flipInputX = flipInputY = false;
        }

        public void ResetPlacement()
        {
            yaw = roll = 0f;
            pitch = 180f;
            domeFov = 180f;
            ResetOutputCircle();
        }

        public void ResetOutputCircle()
        {
            centerX = centerY = 0.5f;
            radius = 0.485f;
            ellipseX = ellipseY = 1f;
            outputRotation = 0f;
        }

        public void ResetFlatPlacement()
        {
            flatAzimuth = 0f;
            flatPolarAngle = 45f;
            flatRoll = 180f;
            flatHorizontalFov = 100f;
            flatHeightScale = 1f;
            flatCropLeft = flatCropRight = flatCropTop = flatCropBottom = 0f;
        }

        public void ResetLensCalibration()
        {
            radialK1 = radialK2 = radialK3 = 0f;
            tangentialP1 = tangentialP2 = 0f;
            warpTop = warpBottom = warpLeft = warpRight = 0f;
        }

        public void Migrate()
        {
            if (settingsVersion < 2)
            {
                // Fields absent from settings written by the first prototype are
                // deserialized as zero, so restore their intended defaults.
                domeFov = 180f;
                gridStepDegrees = 5f;
                gridOpacity = 0.85f;
            }

            if (settingsVersion < 3)
            {
                gridOpacity = 0.65f;
                gridLineWidth = 0.65f;
                showOrientationMarkers = false;
            }

            if (settingsVersion < 4)
            {
                // Earlier builds looked toward the nadir at the default zero
                // orientation. Preserve deliberate adjustments, but point an
                // untouched setup toward the zenith.
                if (Mathf.Abs(pitch) < 0.001f) pitch = 180f;
                startMaximized = true;
                windowWidth = 1600;
                windowHeight = 900;
            }

            if (settingsVersion < 5)
            {
                sourceMode = 0;
                fileIsFlat = false;
                windowCaptureFps = 30;
                focusWindowOnCapture = true;
                ResetFlatPlacement();
            }

            if (settingsVersion < 6)
            {
                // Earlier flat-content builds defaulted to 0 degrees. Upper-
                // hemisphere projection needs the material rotated by 180°.
                if (Mathf.Abs(flatRoll) < 0.001f) flatRoll = 180f;
                useExtendedDisplayOutput = true;
            }

            if (settingsVersion < 7)
            {
                // Never start an upgraded installation in projection-only mode.
                // Let the operator confirm the picture in the UI first, then
                // explicitly start output to the extended display.
                useExtendedDisplayOutput = false;
                outputDisplay = 0;
            }

            if (settingsVersion < 8)
            {
                audioMuted = false;
            }

            if (settingsVersion < 9)
            {
                bridgeUpdateHz = 60;
            }

            if (settingsVersion < 10)
            {
                // Capturing must never steal the operator's focus. Moving focus
                // remains available only through the explicit button or T key.
                focusWindowOnCapture = false;
                // Existing installations should see the consolidated permission
                // request once after upgrading to this version.
                initialMacPermissionsRequested = false;
            }

            if (settingsVersion < 11)
            {
                // IDs from an older app session must never be synthesized from
                // owner/title. The next explicit selection fills this value.
                capturedWindowId = 0L;
            }

            settingsVersion = 11;
        }

        public void Clamp()
        {
            yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch, -180f, 180f);
            roll = Mathf.Clamp(roll, -180f, 180f);
            domeFov = Mathf.Clamp(domeFov, 90f, 220f);
            centerX = Mathf.Clamp01(centerX);
            centerY = Mathf.Clamp01(centerY);
            radius = Mathf.Clamp(radius, 0.05f, 1f);
            ellipseX = Mathf.Clamp(ellipseX, 0.25f, 2f);
            ellipseY = Mathf.Clamp(ellipseY, 0.25f, 2f);
            outputRotation = Mathf.Repeat(outputRotation + 180f, 360f) - 180f;
            radialK1 = Mathf.Clamp(radialK1, -0.5f, 0.5f);
            radialK2 = Mathf.Clamp(radialK2, -0.5f, 0.5f);
            radialK3 = Mathf.Clamp(radialK3, -0.5f, 0.5f);
            tangentialP1 = Mathf.Clamp(tangentialP1, -0.25f, 0.25f);
            tangentialP2 = Mathf.Clamp(tangentialP2, -0.25f, 0.25f);
            warpTop = Mathf.Clamp(warpTop, -0.5f, 0.5f);
            warpBottom = Mathf.Clamp(warpBottom, -0.5f, 0.5f);
            warpLeft = Mathf.Clamp(warpLeft, -0.5f, 0.5f);
            warpRight = Mathf.Clamp(warpRight, -0.5f, 0.5f);
            brightness = Mathf.Clamp(brightness, 0f, 3f);
            gamma = Mathf.Clamp(gamma, 0.2f, 3f);
            edgeFade = Mathf.Clamp(edgeFade, 0f, 0.25f);
            gridMode = Mathf.Clamp(gridMode, 0, 2);
            gridStepDegrees = Mathf.Clamp(gridStepDegrees, 1f, 15f);
            gridOpacity = Mathf.Clamp01(gridOpacity);
            gridLineWidth = Mathf.Clamp(gridLineWidth, 0.25f, 1.5f);
            windowWidth = Mathf.Clamp(windowWidth, 800, 7680);
            windowHeight = Mathf.Clamp(windowHeight, 450, 4320);
            outputDisplay = Mathf.Max(0, outputDisplay);
            sourceMode = Mathf.Clamp(sourceMode, 0, 1);
            windowCaptureFps = Mathf.Clamp(windowCaptureFps, 5, 60);
            flatAzimuth = Mathf.Repeat(flatAzimuth + 180f, 360f) - 180f;
            flatPolarAngle = Mathf.Clamp(flatPolarAngle, 0f, 89f);
            flatRoll = Mathf.Repeat(flatRoll + 180f, 360f) - 180f;
            flatHorizontalFov = Mathf.Clamp(flatHorizontalFov, 10f, 160f);
            flatHeightScale = Mathf.Clamp(flatHeightScale, 0.25f, 3f);
            flatCropLeft = Mathf.Clamp(flatCropLeft, 0f, 0.45f);
            flatCropRight = Mathf.Clamp(flatCropRight, 0f, 0.45f);
            flatCropTop = Mathf.Clamp(flatCropTop, 0f, 0.45f);
            flatCropBottom = Mathf.Clamp(flatCropBottom, 0f, 0.45f);
            bridgeUpdateHz = Mathf.Clamp(bridgeUpdateHz, 15, 120);
        }
    }
}

