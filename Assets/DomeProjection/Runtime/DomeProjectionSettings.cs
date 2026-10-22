using System;
using UnityEngine;

namespace DomeProjection
{
    [Serializable]
    public sealed class LensCalibrationProfile
    {
        public int profileVersion = 10;
        public string displayName = string.Empty;
        public string savedUtc = string.Empty;

        // A lens/setup profile intentionally contains only the values fixed by
        // the projector, conversion lens, and their physical alignment. Source
        // orientation, media paths, brightness, and playback state stay intact.
        public float domeFov = 180f;
        public int projectionRegionMode;
        public float offAxisProjectorDistance = 0.95f;
        public float offAxisProjectorHeight;
        public float offAxisAimElevation = 15f;
        public float offAxisMinimumElevation = -20f;
        public float roomWidth = 5f;
        public float roomHeight = 2.5f;
        public float roomDepth = 5f;
        public int roomCornerMode;
        // Kept in JSON for profiles written by version 6.
        public bool roomUseLeftCorner;
        public bool roomShowFrontWall = true;
        public bool roomShowSideWall = true;
        public bool roomShowCeiling = true;
        public bool roomShowFloor = true;
        public float roomProjectorX;
        public float roomProjectorY = 1.2f;
        public float roomProjectorZ = 0.5f;
        public float roomProjectorYaw = 29f;
        public float roomProjectorPitch = 14f;
        public float roomProjectorRoll;
        public float roomProjectorHorizontalFov = 80f;
        public float roomViewerX;
        public float roomViewerY = 1.6f;
        public float roomViewerZ = 1.5f;
        public float roomGridSpacing = 0.5f;
        // Face whose four physical corners anchor room warp calibration.
        // 0 = front, 1 = ceiling, 2 = floor, 3 = left wall, 4 = right wall.
        public int roomWarpReferenceFace;
        public float roomWarpTopLeftX;
        public float roomWarpTopLeftY;
        public float roomWarpTopRightX;
        public float roomWarpTopRightY;
        public float roomWarpBottomLeftX;
        public float roomWarpBottomLeftY;
        public float roomWarpBottomRightX;
        public float roomWarpBottomRightY;
        public float centerX = 0.5f;
        public float centerY = 0.5f;
        public float radius = 0.485f;
        public float ellipseX = 1f;
        public float ellipseY = 1f;
        public float outputRotation;
        public float radialK1;
        public float radialK2;
        public float radialK3;
        public float tangentialP1;
        public float tangentialP2;
        public float warpTop;
        public float warpBottom;
        public float warpLeft;
        public float warpRight;

        public static LensCalibrationProfile Capture(string name, DomeProjectionSettings settings)
        {
            return new LensCalibrationProfile
            {
                displayName = name,
                savedUtc = DateTime.UtcNow.ToString("O"),
                domeFov = settings.domeFov,
                projectionRegionMode = settings.projectionRegionMode,
                offAxisProjectorDistance = settings.offAxisProjectorDistance,
                offAxisProjectorHeight = settings.offAxisProjectorHeight,
                offAxisAimElevation = settings.offAxisAimElevation,
                offAxisMinimumElevation = settings.offAxisMinimumElevation,
                roomWidth = settings.roomWidth,
                roomHeight = settings.roomHeight,
                roomDepth = settings.roomDepth,
                roomCornerMode = settings.roomCornerMode,
                roomUseLeftCorner = settings.roomUseLeftCorner,
                roomShowFrontWall = settings.roomShowFrontWall,
                roomShowSideWall = settings.roomShowSideWall,
                roomShowCeiling = settings.roomShowCeiling,
                roomShowFloor = settings.roomShowFloor,
                roomProjectorX = settings.roomProjectorX,
                roomProjectorY = settings.roomProjectorY,
                roomProjectorZ = settings.roomProjectorZ,
                roomProjectorYaw = settings.roomProjectorYaw,
                roomProjectorPitch = settings.roomProjectorPitch,
                roomProjectorRoll = settings.roomProjectorRoll,
                roomProjectorHorizontalFov = settings.roomProjectorHorizontalFov,
                roomViewerX = settings.roomViewerX,
                roomViewerY = settings.roomViewerY,
                roomViewerZ = settings.roomViewerZ,
                roomGridSpacing = settings.roomGridSpacing,
                roomWarpReferenceFace = settings.roomWarpReferenceFace,
                roomWarpTopLeftX = settings.roomWarpTopLeftX,
                roomWarpTopLeftY = settings.roomWarpTopLeftY,
                roomWarpTopRightX = settings.roomWarpTopRightX,
                roomWarpTopRightY = settings.roomWarpTopRightY,
                roomWarpBottomLeftX = settings.roomWarpBottomLeftX,
                roomWarpBottomLeftY = settings.roomWarpBottomLeftY,
                roomWarpBottomRightX = settings.roomWarpBottomRightX,
                roomWarpBottomRightY = settings.roomWarpBottomRightY,
                centerX = settings.centerX,
                centerY = settings.centerY,
                radius = settings.radius,
                ellipseX = settings.ellipseX,
                ellipseY = settings.ellipseY,
                outputRotation = settings.outputRotation,
                radialK1 = settings.radialK1,
                radialK2 = settings.radialK2,
                radialK3 = settings.radialK3,
                tangentialP1 = settings.tangentialP1,
                tangentialP2 = settings.tangentialP2,
                warpTop = settings.warpTop,
                warpBottom = settings.warpBottom,
                warpLeft = settings.warpLeft,
                warpRight = settings.warpRight
            };
        }

        public void ApplyTo(DomeProjectionSettings settings)
        {
            settings.domeFov = domeFov;
            settings.projectionRegionMode = projectionRegionMode;
            settings.offAxisProjectorDistance = offAxisProjectorDistance;
            settings.offAxisProjectorHeight = offAxisProjectorHeight;
            settings.offAxisAimElevation = offAxisAimElevation;
            settings.offAxisMinimumElevation = offAxisMinimumElevation;
            settings.roomWidth = roomWidth;
            settings.roomHeight = roomHeight;
            settings.roomDepth = roomDepth;
            settings.roomCornerMode = profileVersion >= 7
                ? roomCornerMode
                : roomUseLeftCorner ? 1 : 0;
            settings.roomUseLeftCorner = settings.roomCornerMode == 1;
            settings.roomShowFrontWall = roomShowFrontWall;
            settings.roomShowSideWall = roomShowSideWall;
            settings.roomShowCeiling = roomShowCeiling;
            settings.roomShowFloor = profileVersion < 8 || roomShowFloor;
            settings.roomProjectorX = roomProjectorX;
            settings.roomProjectorY = roomProjectorY;
            settings.roomProjectorZ = roomProjectorZ;
            settings.roomProjectorYaw = roomProjectorYaw;
            settings.roomProjectorPitch = roomProjectorPitch;
            settings.roomProjectorRoll = roomProjectorRoll;
            settings.roomProjectorHorizontalFov = roomProjectorHorizontalFov;
            settings.roomViewerX = roomViewerX;
            settings.roomViewerY = roomViewerY;
            settings.roomViewerZ = roomViewerZ;
            settings.roomGridSpacing = roomGridSpacing;
            settings.roomWarpReferenceFace = profileVersion >= 10
                ? roomWarpReferenceFace
                : 0;
            if (profileVersion >= 9)
            {
                settings.roomWarpTopLeftX = roomWarpTopLeftX;
                settings.roomWarpTopLeftY = roomWarpTopLeftY;
                settings.roomWarpTopRightX = roomWarpTopRightX;
                settings.roomWarpTopRightY = roomWarpTopRightY;
                settings.roomWarpBottomLeftX = roomWarpBottomLeftX;
                settings.roomWarpBottomLeftY = roomWarpBottomLeftY;
                settings.roomWarpBottomRightX = roomWarpBottomRightX;
                settings.roomWarpBottomRightY = roomWarpBottomRightY;
            }
            else
            {
                settings.ResetRoomWarp();
            }
            settings.centerX = centerX;
            settings.centerY = centerY;
            settings.radius = radius;
            settings.ellipseX = ellipseX;
            settings.ellipseY = ellipseY;
            settings.outputRotation = outputRotation;
            settings.radialK1 = radialK1;
            settings.radialK2 = radialK2;
            settings.radialK3 = radialK3;
            settings.tangentialP1 = tangentialP1;
            settings.tangentialP2 = tangentialP2;
            settings.warpTop = warpTop;
            settings.warpBottom = warpBottom;
            settings.warpLeft = warpLeft;
            settings.warpRight = warpRight;
            if (projectionRegionMode == 1 && profileVersion < 4)
            {
                // Profiles saved by the temporary semicircle implementation do
                // not contain a physical projector position. Give them the new
                // rim-projector defaults instead of interpreting zeros.
                settings.domeFov = 120f;
                settings.offAxisProjectorDistance = 0.95f;
                settings.offAxisProjectorHeight = 0f;
                settings.offAxisAimElevation = 15f;
                if (settings.radius < 0.75f) settings.radius = 1f;
            }
            if (projectionRegionMode == 1 && profileVersion < 5)
                settings.offAxisMinimumElevation = -20f;
            if (projectionRegionMode == 2 && profileVersion < 6)
                settings.ResetRoomProjection();
            settings.Clamp();
        }
    }

    [Serializable]
    public sealed class DomeProjectionSettings
    {
        public int settingsVersion = 29;
        public string videoPath = string.Empty;

        // Input source. 0 = video/image file, 1 = captured desktop window.
        public int sourceMode;
        // Legacy compatibility flag. fileProjectionMode is authoritative.
        public bool fileIsFlat;
        // File projection: 0 = 2:1 equirectangular, 1 = flat panel,
        // 2 = circular all-sky/fisheye/domemaster passed directly to the dome.
        public int fileProjectionMode;
        // Circular source placement inside the source frame. Radius 1 fills
        // the shorter image dimension while preserving a true pixel circle.
        public float allSkySourceCenterX = 0.5f;
        public float allSkySourceCenterY = 0.5f;
        public float allSkySourceRadius = 1f;
        public float allSkySourceRotation;
        // Input-lens radial correction, separate from projector-lens K1-K3.
        public float allSkySourceRadialK1;
        public float allSkySourceRadialK2;
        public string capturedWindowOwner = string.Empty;
        public string capturedWindowTitle = string.Empty;
        // Native window handle/CGWindowID. Control and Output are separate
        // processes, but this identifier refers to the same OS window in both.
        // Owner/title remain as a restart fallback because IDs are not stable.
        public long capturedWindowId;
        public int windowCaptureFps = 30;
        public bool focusWindowOnCapture;
        public bool initialMacPermissionsRequested;

        // Screen shown when no source is available or the operator explicitly
        // ends a presentation. 0 = black, 1 = selected image. The dome guide
        // is an independent overlay controlled by gridMode.
        public int idleDisplayMode;
        public string idleImagePath = string.Empty;
        public bool idleImageIsFlat = true;
        public bool showIdleScreen;

        // Optional 2:1 equirectangular image drawn behind a captured flat window.
        public string windowBackgroundImagePath = string.Empty;

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
        // Projection geometry. 0 = projector at the sphere centre; 1 = an
        // off-axis projector near the rear rim, aimed across the dome; 2 = a
        // rectangular room corner made of a front wall, side wall and ceiling.
        public int projectionRegionMode;
        public float domeFov = 180f;
        // Off-axis position is expressed in dome-radius units. The default is
        // just inside the rear rim at (0, height, -distance).
        public float offAxisProjectorDistance = 0.95f;
        public float offAxisProjectorHeight;
        public float offAxisAimElevation = 15f;
        // Lowest sphere elevation retained by the off-axis renderer. A value
        // of -20 degrees corresponds to the lower extent of a 220-degree
        // zenith-centred projection.
        public float offAxisMinimumElevation = -20f;

        // Rectangular-room coordinates are metres. X is centred across the
        // room, Y rises from the floor, and Z runs from the rear toward the
        // front wall. Rays hit the nearest enabled surface, then the texture
        // direction is calculated from the reference viewer position.
        public float roomWidth = 5f;
        public float roomHeight = 2.5f;
        public float roomDepth = 5f;
        // 0 = right corner, 1 = left corner, 2 = both front corners.
        public int roomCornerMode;
        // Legacy version-22 compatibility; roomCornerMode is authoritative.
        public bool roomUseLeftCorner;
        public bool roomShowFrontWall = true;
        public bool roomShowSideWall = true;
        public bool roomShowCeiling = true;
        public bool roomShowFloor = true;
        public float roomProjectorX;
        public float roomProjectorY = 1.2f;
        public float roomProjectorZ = 0.5f;
        public float roomProjectorYaw = 29f;
        public float roomProjectorPitch = 14f;
        public float roomProjectorRoll;
        public float roomProjectorHorizontalFov = 80f;
        public float roomViewerX;
        public float roomViewerY = 1.6f;
        public float roomViewerZ = 1.5f;
        public float roomGridSpacing = 0.5f;
        // Surface used as the primary four-corner calibration anchor.
        // 0 = front, 1 = ceiling, 2 = floor, 3 = left wall, 4 = right wall.
        public int roomWarpReferenceFace;
        // Normalized offsets applied to the four projected corners of the
        // selected reference face. Zero leaves the calculated projector
        // geometry unchanged; dragging a handle moves that face corner only.
        public float roomWarpTopLeftX;
        public float roomWarpTopLeftY;
        public float roomWarpTopRightX;
        public float roomWarpTopRightY;
        public float roomWarpBottomLeftX;
        public float roomWarpBottomLeftY;
        public float roomWarpBottomRightX;
        public float roomWarpBottomRightY;
        // Control-only room editor: 0 = projected result, 1 = top view,
        // 2 = side view. Output ignores this field.
        // 0: projected result, 1: third-person overview, 2: top, 3: side.
        // This is only a Control-app aid and is never sent to the projector.
        public int roomControlViewMode;

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
        // Operator panel can be docked to either side so the projected-circle
        // edge remains visible while calibrating it.
        public bool controlPanelOnRight;
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
            projectionRegionMode = 0;
            domeFov = 180f;
            ResetAllSkyPlacement();
            ResetOutputCircle();
        }

        public void ResetAllSkyPlacement()
        {
            allSkySourceCenterX = allSkySourceCenterY = 0.5f;
            allSkySourceRadius = 1f;
            allSkySourceRotation = 0f;
            allSkySourceRadialK1 = allSkySourceRadialK2 = 0f;
        }

        public void ResetOutputCircle()
        {
            centerX = centerY = 0.5f;
            radius = 0.485f;
            ellipseX = ellipseY = 1f;
            outputRotation = 0f;
        }

        public void ResetOffAxisProjection()
        {
            domeFov = 120f;
            offAxisProjectorDistance = 0.95f;
            offAxisProjectorHeight = 0f;
            offAxisAimElevation = 15f;
            offAxisMinimumElevation = -20f;
            centerX = centerY = 0.5f;
            // In this geometry only a cropped portion of the conversion-lens
            // circle is normally useful, so start with a radius equal to the
            // complete short side rather than half of it.
            radius = 1f;
            ellipseX = ellipseY = 1f;
            outputRotation = 0f;
        }

        public void ResetRoomProjection()
        {
            roomWidth = roomDepth = 5f;
            roomHeight = 2.5f;
            roomCornerMode = 0;
            roomUseLeftCorner = false;
            roomShowFrontWall = roomShowSideWall = roomShowCeiling = roomShowFloor = true;
            roomProjectorX = 0f;
            roomProjectorY = 1.2f;
            roomProjectorZ = 0.5f;
            roomProjectorYaw = 29f;
            roomProjectorPitch = 14f;
            roomProjectorRoll = 0f;
            roomProjectorHorizontalFov = 80f;
            roomViewerX = 0f;
            roomViewerY = 1.6f;
            roomViewerZ = 1.5f;
            roomGridSpacing = 0.5f;
            roomWarpReferenceFace = 0;
            ResetRoomWarp();
            centerX = centerY = 0.5f;
            radius = 0.5f;
            ellipseX = ellipseY = 1f;
            outputRotation = 0f;
        }

        public void ResetRoomWarp()
        {
            roomWarpTopLeftX = roomWarpTopLeftY = 0f;
            roomWarpTopRightX = roomWarpTopRightY = 0f;
            roomWarpBottomLeftX = roomWarpBottomLeftY = 0f;
            roomWarpBottomRightX = roomWarpBottomRightY = 0f;
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

            if (settingsVersion < 12)
            {
                // Older versions always fell back to the calibration dome.
                // Default upgraded installations to a clean black standby.
                idleDisplayMode = 0;
                idleImagePath = string.Empty;
                idleImageIsFlat = true;
                showIdleScreen = false;
            }

            if (settingsVersion < 13)
            {
                // The old third standby option replaced the selected standby
                // image with a calibration-only screen. Preserve the visible
                // guide, but move it to the independent overlay layer.
                if (idleDisplayMode == 2)
                {
                    idleDisplayMode = 0;
                    gridMode = 1;
                }
                windowBackgroundImagePath = string.Empty;
                // Upgraded installations should use a maximized ordinary
                // window by default, never an OS fullscreen Space.
                startMaximized = true;
            }

            if (settingsVersion < 14)
            {
                // Guide toggling in v12 and earlier could silently replace a
                // configured default image with black. If the copied image is
                // still present in the settings, restore it as the standby.
                if (!string.IsNullOrWhiteSpace(idleImagePath)) idleDisplayMode = 1;
                startMaximized = true;
            }

            if (settingsVersion < 15)
            {
                // Preserve the old two-way 360/flat choice while introducing
                // circular all-sky media as an explicit third projection.
                fileProjectionMode = fileIsFlat ? 1 : 0;
                ResetAllSkyPlacement();
            }

            if (settingsVersion < 16)
            {
                // New source-lens controls default to an unchanged circular
                // image, including settings already saved by v15.
                allSkySourceRotation = 0f;
                allSkySourceRadialK1 = allSkySourceRadialK2 = 0f;
            }

            if (settingsVersion < 17)
            {
                controlPanelOnRight = false;
            }

            if (settingsVersion < 18)
            {
                // Existing installations keep their previous complete-dome
                // behavior. The quarter-sphere mode is selected explicitly.
                projectionRegionMode = 0;
            }

            if (settingsVersion < 19 && projectionRegionMode == 1)
            {
                // Version 18 briefly defined this mode as a 120-degree circular
                // cap. Replace it with the intended 180 x 90-degree half dome.
                domeFov = 180f;
            }

            if (settingsVersion < 20 && projectionRegionMode == 1)
            {
                // Version 19 used a centre-of-sphere semicircle mask. Migrate
                // it to the physical rim-projector model and its useful crop.
                ResetOffAxisProjection();
            }

            if (settingsVersion < 21)
                offAxisMinimumElevation = -20f;

            if (settingsVersion < 22)
            {
                // Version 22 introduces a separately selected room-corner
                // geometry. Existing dome setups remain untouched.
                if (projectionRegionMode == 2) ResetRoomProjection();
            }

            if (settingsVersion < 23)
            {
                roomCornerMode = roomUseLeftCorner ? 1 : 0;
            }

            if (settingsVersion < 24)
            {
                roomShowFloor = true;
                ResetRoomWarp();
            }

            if (settingsVersion < 25)
                roomControlViewMode = 0;

            if (settingsVersion < 26)
            {
                // Versions 24-25 warped the four corners of the complete
                // output raster. Version 26 instead calibrates the projected
                // front-wall corners, so the old values are not compatible.
                ResetRoomWarp();
            }

            if (settingsVersion < 27)
                roomWarpReferenceFace = 0;

            if (settingsVersion < 28)
            {
                // Version 28 briefly used a global multi-point solve. Keep the
                // established front-face warp when upgrading instead of
                // silently discarding an operator's completed calibration.
                roomWarpReferenceFace = 0;
            }

            settingsVersion = 29;
        }

        public void Clamp()
        {
            yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch, -180f, 180f);
            roll = Mathf.Clamp(roll, -180f, 180f);
            projectionRegionMode = Mathf.Clamp(projectionRegionMode, 0, 2);
            domeFov = Mathf.Clamp(domeFov, 30f, 360f);
            offAxisProjectorDistance = Mathf.Clamp(offAxisProjectorDistance, 0f, 2f);
            offAxisProjectorHeight = Mathf.Clamp(offAxisProjectorHeight, -1.5f, 1.5f);
            offAxisAimElevation = Mathf.Clamp(offAxisAimElevation, -90f, 90f);
            offAxisMinimumElevation = Mathf.Clamp(offAxisMinimumElevation, -90f, 0f);
            roomWidth = Mathf.Clamp(roomWidth, 1f, 30f);
            roomHeight = Mathf.Clamp(roomHeight, 1f, 10f);
            roomDepth = Mathf.Clamp(roomDepth, 1f, 30f);
            roomCornerMode = Mathf.Clamp(roomCornerMode, 0, 2);
            roomUseLeftCorner = roomCornerMode == 1;
            roomProjectorX = Mathf.Clamp(roomProjectorX, -30f, 30f);
            roomProjectorY = Mathf.Clamp(roomProjectorY, -2f, 12f);
            roomProjectorZ = Mathf.Clamp(roomProjectorZ, -5f, 35f);
            roomProjectorYaw = Mathf.Clamp(roomProjectorYaw, -180f, 180f);
            roomProjectorPitch = Mathf.Clamp(roomProjectorPitch, -89f, 89f);
            roomProjectorRoll = Mathf.Clamp(roomProjectorRoll, -180f, 180f);
            roomProjectorHorizontalFov = Mathf.Clamp(roomProjectorHorizontalFov, 10f, 170f);
            roomViewerX = Mathf.Clamp(roomViewerX, -30f, 30f);
            roomViewerY = Mathf.Clamp(roomViewerY, -2f, 12f);
            roomViewerZ = Mathf.Clamp(roomViewerZ, -5f, 35f);
            roomGridSpacing = Mathf.Clamp(roomGridSpacing, 0.1f, 5f);
            // Final room correction is intentionally a single stable front
            // quad. Hidden ceiling/side reference modes made the same saved
            // offsets mean different things and are no longer supported.
            roomWarpReferenceFace = 0;
            roomWarpTopLeftX = Mathf.Clamp(roomWarpTopLeftX, -2f, 2f);
            roomWarpTopLeftY = Mathf.Clamp(roomWarpTopLeftY, -2f, 2f);
            roomWarpTopRightX = Mathf.Clamp(roomWarpTopRightX, -2f, 2f);
            roomWarpTopRightY = Mathf.Clamp(roomWarpTopRightY, -2f, 2f);
            roomWarpBottomLeftX = Mathf.Clamp(roomWarpBottomLeftX, -2f, 2f);
            roomWarpBottomLeftY = Mathf.Clamp(roomWarpBottomLeftY, -2f, 2f);
            roomWarpBottomRightX = Mathf.Clamp(roomWarpBottomRightX, -2f, 2f);
            roomWarpBottomRightY = Mathf.Clamp(roomWarpBottomRightY, -2f, 2f);
            roomControlViewMode = Mathf.Clamp(roomControlViewMode, 0, 3);
            // Oversized and off-screen circles are useful when only the front
            // portion of the physical dome is used as a large window.
            centerX = Mathf.Clamp(centerX, -1f, 2f);
            centerY = Mathf.Clamp(centerY, -1f, 2f);
            radius = Mathf.Clamp(radius, 0.05f, 3f);
            ellipseX = Mathf.Clamp(ellipseX, 0.1f, 3f);
            ellipseY = Mathf.Clamp(ellipseY, 0.1f, 3f);
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
            fileProjectionMode = Mathf.Clamp(fileProjectionMode, 0, 2);
            fileIsFlat = fileProjectionMode == 1;
            allSkySourceCenterX = Mathf.Clamp01(allSkySourceCenterX);
            allSkySourceCenterY = Mathf.Clamp01(allSkySourceCenterY);
            allSkySourceRadius = Mathf.Clamp(allSkySourceRadius, 0.1f, 1.5f);
            allSkySourceRotation = Mathf.Repeat(allSkySourceRotation + 180f, 360f) - 180f;
            allSkySourceRadialK1 = Mathf.Clamp(allSkySourceRadialK1, -0.5f, 0.5f);
            allSkySourceRadialK2 = Mathf.Clamp(allSkySourceRadialK2, -0.5f, 0.5f);
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
            idleDisplayMode = Mathf.Clamp(idleDisplayMode, 0, 1);
        }
    }
}

