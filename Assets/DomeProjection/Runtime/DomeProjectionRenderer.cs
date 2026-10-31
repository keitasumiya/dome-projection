using UnityEngine;

namespace DomeProjection
{
    [RequireComponent(typeof(Camera))]
    public sealed class DomeProjectionRenderer : MonoBehaviour
    {
        private static readonly int VideoTex = Shader.PropertyToID("_VideoTex");
        private static readonly int HasVideo = Shader.PropertyToID("_HasVideo");
        private static readonly int BackgroundTex = Shader.PropertyToID("_BackgroundTex");
        private static readonly int HasBackground = Shader.PropertyToID("_HasBackground");
        private static readonly int OutputAspect = Shader.PropertyToID("_OutputAspect");
        private static readonly int Center = Shader.PropertyToID("_Center");
        private static readonly int Radius = Shader.PropertyToID("_Radius");
        private static readonly int Ellipse = Shader.PropertyToID("_Ellipse");
        private static readonly int Orientation = Shader.PropertyToID("_Orientation");
        private static readonly int DomeFov = Shader.PropertyToID("_DomeFov");
        private static readonly int ProjectionRegionMode = Shader.PropertyToID("_ProjectionRegionMode");
        private static readonly int OffAxisProjection = Shader.PropertyToID("_OffAxisProjection");
        private static readonly int OffAxisHorizontal = Shader.PropertyToID("_OffAxisHorizontal");
        private static readonly int RoomDimensions = Shader.PropertyToID("_RoomDimensions");
        private static readonly int RoomProjectorPosition = Shader.PropertyToID("_RoomProjectorPosition");
        private static readonly int RoomProjectorRotation = Shader.PropertyToID("_RoomProjectorRotation");
        private static readonly int RoomViewerPosition = Shader.PropertyToID("_RoomViewerPosition");
        private static readonly int RoomFaces = Shader.PropertyToID("_RoomFaces");
        private static readonly int RoomFloor = Shader.PropertyToID("_RoomFloor");
        private static readonly int RoomReferenceTop = Shader.PropertyToID("_RoomReferenceTop");
        private static readonly int RoomReferenceBottom = Shader.PropertyToID("_RoomReferenceBottom");
        private static readonly int RoomWarpTop = Shader.PropertyToID("_RoomWarpTop");
        private static readonly int RoomWarpBottom = Shader.PropertyToID("_RoomWarpBottom");
        private static readonly int RoomWarpH0 = Shader.PropertyToID("_RoomWarpH0");
        private static readonly int RoomWarpH1 = Shader.PropertyToID("_RoomWarpH1");
        private static readonly int RoomWarpH2 = Shader.PropertyToID("_RoomWarpH2");
        private static readonly int ControlPreviewId = Shader.PropertyToID("_ControlPreview");
        private static readonly int OutputRotation = Shader.PropertyToID("_OutputRotation");
        private static readonly int Radial = Shader.PropertyToID("_Radial");
        private static readonly int Tangential = Shader.PropertyToID("_Tangential");
        private static readonly int DirectionalWarp = Shader.PropertyToID("_DirectionalWarp");
        private static readonly int ColorAdjust = Shader.PropertyToID("_ColorAdjust");
        private static readonly int EdgeFade = Shader.PropertyToID("_EdgeFade");
        private static readonly int InputFlip = Shader.PropertyToID("_InputFlip");
        private static readonly int GridMode = Shader.PropertyToID("_GridMode");
        private static readonly int GridSettings = Shader.PropertyToID("_GridSettings");
        private static readonly int GridAppearance = Shader.PropertyToID("_GridAppearance");
        private static readonly int ContentMode = Shader.PropertyToID("_ContentMode");
        private static readonly int SourceAspect = Shader.PropertyToID("_SourceAspect");
        private static readonly int FlatPlacement = Shader.PropertyToID("_FlatPlacement");
        private static readonly int FlatSize = Shader.PropertyToID("_FlatSize");
        private static readonly int FlatCrop = Shader.PropertyToID("_FlatCrop");
        private static readonly int CircularSource = Shader.PropertyToID("_CircularSource");
        private static readonly int CircularRadial = Shader.PropertyToID("_CircularRadial");
        private static readonly int ContentOpacityId = Shader.PropertyToID("_ContentOpacity");

        private Material material;
        private Camera outputCamera;
        private RenderTexture operatorPreviewTexture;
        public DomeProjectionSettings Settings { get; set; }
        public float ContentOpacity { get; set; } = 1f;
        public bool CaptureOperatorPreview { get; set; }
        public Texture OperatorPreviewTexture => operatorPreviewTexture;
        // Control-only inspection transform. Output always keeps 1 / 0 degrees.
        public float ControlPreviewScale { get; set; } = 1f;
        public float ControlPreviewRotation { get; set; }
        public float ControlPreviewYaw { get; set; }
        public float ControlPreviewPitch { get; set; }
        // Control can have a different window aspect from the projector. When
        // Output is alive it reports its drawable aspect over the heartbeat,
        // so the operator preview uses the exact same projection geometry.
        public float ProjectionAspectOverride { get; set; }
        public IVideoSource VideoSource { get; set; }
        public IVideoSource IdleImageSource { get; set; }
        public IVideoSource WindowBackgroundImageSource { get; set; }
        public IWindowCaptureSource WindowSource { get; set; }

        private void Awake()
        {
            outputCamera = GetComponent<Camera>();
            var shader = Resources.Load<Shader>("EquirectangularToDomemaster");
            if (shader == null)
                shader = Shader.Find("Hidden/DomeProjection/EquirectangularToDomemaster");
            if (shader == null)
            {
                Debug.LogError("Dome projection shader was not found.");
                enabled = false;
                return;
            }

            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        private void OnDestroy()
        {
            if (operatorPreviewTexture != null)
            {
                operatorPreviewTexture.Release();
                Destroy(operatorPreviewTexture);
            }
            if (material != null) Destroy(material);
        }

        private void EnsureOperatorPreviewTexture(float aspect)
        {
            aspect = Mathf.Clamp(aspect, 0.25f, 4f);
            const int targetHeight = 270;
            var targetWidth = Mathf.Clamp(Mathf.RoundToInt(targetHeight * aspect), 180, 720);
            if (operatorPreviewTexture != null
                && operatorPreviewTexture.width == targetWidth
                && operatorPreviewTexture.height == targetHeight) return;
            if (operatorPreviewTexture != null)
            {
                operatorPreviewTexture.Release();
                Destroy(operatorPreviewTexture);
            }
            operatorPreviewTexture = new RenderTexture(targetWidth, targetHeight, 0,
                RenderTextureFormat.ARGB32)
            {
                name = "Control Output Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            operatorPreviewTexture.Create();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (material == null || Settings == null)
            {
                Graphics.Blit(source, destination);
                return;
            }

            Settings.Clamp();
            var useWindow = Settings.sourceMode == 1;
            var primaryTexture = useWindow ? WindowSource?.Texture : VideoSource?.Texture;
            var primaryPrepared = primaryTexture != null
                && (useWindow ? WindowSource.IsPrepared : VideoSource.IsPrepared);
            var usePrimary = primaryPrepared && !Settings.showIdleScreen;
            var useIdleImage = !usePrimary && Settings.idleDisplayMode == 1
                && IdleImageSource?.Texture != null && IdleImageSource.IsPrepared;
            var texture = usePrimary ? primaryTexture : useIdleImage ? IdleImageSource.Texture : null;
            var hasVideo = texture != null;
            var sourceWidth = usePrimary
                ? useWindow ? WindowSource?.Width ?? 0 : VideoSource?.Width ?? 0
                : useIdleImage ? IdleImageSource.Width : 0;
            var sourceHeight = usePrimary
                ? useWindow ? WindowSource?.Height ?? 0 : VideoSource?.Height ?? 0
                : useIdleImage ? IdleImageSource.Height : 0;
            var contentMode = usePrimary
                ? useWindow ? 1 : Settings.fileProjectionMode
                : useIdleImage && Settings.idleImageIsFlat ? 1 : 0;
            var flatContent = contentMode == 1;
            var useWindowBackground = usePrimary && useWindow
                && WindowBackgroundImageSource?.Texture != null
                && WindowBackgroundImageSource.IsPrepared;
            var selectedMediaSource = usePrimary && !useWindow
                ? VideoSource
                : !usePrimary && useIdleImage ? IdleImageSource : null;
            // AVPro's native decoders can expose top-down textures. Combine the
            // backend correction with the operator's explicit flip as XOR so
            // image and video orientation use the same dome convention.
            var effectiveFlipY = Settings.flipInputY
                ^ (selectedMediaSource?.RequiresVerticalFlip ?? false);

            material.SetTexture(VideoTex, hasVideo ? texture : Texture2D.blackTexture);
            material.SetFloat(HasVideo, hasVideo ? 1f : 0f);
            material.SetTexture(BackgroundTex, useWindowBackground
                ? WindowBackgroundImageSource.Texture : Texture2D.blackTexture);
            material.SetFloat(HasBackground, useWindowBackground ? 1f : 0f);
            var width = destination != null ? destination.width : outputCamera.pixelWidth;
            var height = destination != null ? destination.height : outputCamera.pixelHeight;
            var outputAspect = ProjectionAspectOverride > 0.1f
                ? ProjectionAspectOverride
                : height > 0 ? (float)width / height : 1f;
            material.SetFloat(OutputAspect, outputAspect);
            material.SetVector(Center, new Vector4(Settings.centerX, Settings.centerY, 0f, 0f));
            material.SetFloat(Radius, Settings.radius);
            material.SetVector(Ellipse, new Vector4(Settings.ellipseX, Settings.ellipseY, 0f, 0f));
            material.SetVector(Orientation, new Vector4(Settings.yaw, Settings.pitch, Settings.roll, 0f));
            material.SetFloat(DomeFov, Settings.domeFov);
            material.SetFloat(ProjectionRegionMode, Settings.projectionRegionMode);
            material.SetVector(OffAxisProjection, new Vector4(Settings.offAxisProjectorDistance,
                Settings.offAxisProjectorHeight, Settings.offAxisAimElevation,
                Settings.offAxisMinimumElevation));
            material.SetVector(OffAxisHorizontal, new Vector4(Settings.offAxisProjectorX,
                Settings.offAxisAimYaw, 0f, 0f));
            material.SetVector(RoomDimensions, new Vector4(Settings.roomWidth,
                Settings.roomHeight, Settings.roomDepth, Settings.roomGridSpacing));
            material.SetVector(RoomProjectorPosition, new Vector4(Settings.roomProjectorX,
                Settings.roomProjectorY, Settings.roomProjectorZ, 0f));
            material.SetVector(RoomProjectorRotation, new Vector4(Settings.roomProjectorYaw,
                Settings.roomProjectorPitch, Settings.roomProjectorRoll,
                Settings.roomProjectorHorizontalFov));
            material.SetVector(RoomViewerPosition, new Vector4(Settings.roomViewerX,
                Settings.roomViewerY, Settings.roomViewerZ, 0f));
            material.SetVector(RoomFaces, new Vector4(Settings.roomShowFrontWall ? 1f : 0f,
                Settings.roomShowSideWall ? 1f : 0f,
                Settings.roomShowCeiling ? 1f : 0f,
                Settings.roomCornerMode == 2 ? 0f
                    : Settings.roomCornerMode == 1 ? -1f : 1f));
            material.SetFloat(RoomFloor, Settings.roomShowFloor ? 1f : 0f);
            CalculateRoomReferenceCorners(Settings, outputAspect, out var frontTopLeft,
                out var frontTopRight, out var frontBottomLeft, out var frontBottomRight);
            material.SetVector(RoomReferenceTop, new Vector4(frontTopLeft.x, frontTopLeft.y,
                frontTopRight.x, frontTopRight.y));
            material.SetVector(RoomReferenceBottom, new Vector4(frontBottomLeft.x, frontBottomLeft.y,
                frontBottomRight.x, frontBottomRight.y));
            var warpedTopLeft = frontTopLeft + new Vector2(Settings.roomWarpTopLeftX,
                Settings.roomWarpTopLeftY);
            var warpedTopRight = frontTopRight + new Vector2(Settings.roomWarpTopRightX,
                Settings.roomWarpTopRightY);
            var warpedBottomLeft = frontBottomLeft + new Vector2(Settings.roomWarpBottomLeftX,
                Settings.roomWarpBottomLeftY);
            var warpedBottomRight = frontBottomRight + new Vector2(Settings.roomWarpBottomRightX,
                Settings.roomWarpBottomRightY);
            material.SetVector(RoomWarpTop, new Vector4(warpedTopLeft.x, warpedTopLeft.y,
                warpedTopRight.x, warpedTopRight.y));
            material.SetVector(RoomWarpBottom, new Vector4(warpedBottomLeft.x, warpedBottomLeft.y,
                warpedBottomRight.x, warpedBottomRight.y));
            // Convert a pixel inside the operator-adjusted output quad back to
            // the original front-wall projection. This is an output-only warp:
            // it never changes the physical projector parameters.
            if (!TryCalculateHomography(warpedTopLeft, warpedTopRight,
                    warpedBottomLeft, warpedBottomRight,
                    frontTopLeft, frontTopRight, frontBottomLeft, frontBottomRight,
                    out var homography0, out var homography1, out var homography2))
            {
                homography0 = new Vector3(1f, 0f, 0f);
                homography1 = new Vector3(0f, 1f, 0f);
                homography2 = new Vector3(0f, 0f, 1f);
            }
            material.SetVector(RoomWarpH0, new Vector4(homography0.x, homography0.y,
                homography0.z, 0f));
            material.SetVector(RoomWarpH1, new Vector4(homography1.x, homography1.y,
                homography1.z, 0f));
            material.SetVector(RoomWarpH2, new Vector4(homography2.x, homography2.y,
                homography2.z, 0f));
            material.SetVector(ControlPreviewId, new Vector4(
                Mathf.Clamp(ControlPreviewScale, 0.01f, 1f),
                ControlPreviewRotation, ControlPreviewYaw, ControlPreviewPitch));
            material.SetFloat(OutputRotation, Settings.outputRotation);
            material.SetVector(Radial, new Vector4(Settings.radialK1, Settings.radialK2, Settings.radialK3, 0f));
            material.SetVector(Tangential, new Vector4(Settings.tangentialP1, Settings.tangentialP2, 0f, 0f));
            material.SetVector(DirectionalWarp, new Vector4(Settings.warpTop, Settings.warpBottom, Settings.warpLeft, Settings.warpRight));
            material.SetVector(ColorAdjust, new Vector4(Settings.brightness, Settings.gamma, 0f, 0f));
            material.SetFloat(EdgeFade, Settings.edgeFade);
            material.SetVector(InputFlip, new Vector4(Settings.flipInputX ? 1f : 0f, effectiveFlipY ? 1f : 0f, 0f, 0f));
            material.SetFloat(GridMode, Settings.gridMode);
            material.SetVector(GridSettings, new Vector4(Settings.gridStepDegrees, Settings.gridOpacity, 15f, Settings.showOrientationMarkers ? 1f : 0f));
            material.SetVector(GridAppearance, new Vector4(Settings.gridLineWidth, 0f, 0f, 0f));
            material.SetFloat(ContentMode, contentMode);
            material.SetFloat(SourceAspect, sourceHeight > 0 ? (float)sourceWidth / sourceHeight : 16f / 9f);
            material.SetVector(CircularSource, new Vector4(Settings.allSkySourceCenterX,
                Settings.allSkySourceCenterY, Settings.allSkySourceRadius,
                Settings.allSkySourceRotation));
            material.SetVector(CircularRadial, new Vector4(Settings.allSkySourceRadialK1,
                Settings.allSkySourceRadialK2, 0f, 0f));
            material.SetVector(FlatPlacement, new Vector4(Settings.flatAzimuth, Settings.flatPolarAngle,
                Settings.flatRoll, Settings.flatHorizontalFov));
            material.SetVector(FlatSize, new Vector4(Settings.flatHeightScale, 0f, 0f, 0f));
            material.SetVector(FlatCrop, new Vector4(Settings.flatCropLeft, Settings.flatCropRight,
                Settings.flatCropBottom, Settings.flatCropTop));
            material.SetFloat(ContentOpacityId, Mathf.Clamp01(ContentOpacity));

            Graphics.Blit(source, destination, material, 0);

            if (CaptureOperatorPreview)
            {
                EnsureOperatorPreviewTexture(outputAspect);
                // The inset is an Output preview, not the operator's temporary
                // pan/zoom/rotation inspection view.
                material.SetVector(ControlPreviewId, new Vector4(1f, 0f, 0f, 0f));
                Graphics.Blit(source, operatorPreviewTexture, material, 0);
                material.SetVector(ControlPreviewId, new Vector4(
                    Mathf.Clamp(ControlPreviewScale, 0.01f, 1f),
                    ControlPreviewRotation, ControlPreviewYaw, ControlPreviewPitch));
            }
        }

        public static bool TryGetDomeHitAtOpticalPoint(DomeProjectionSettings settings,
            Vector2 opticalPoint, out Vector3 projector, out Vector3 sphereHit)
        {
            projector = settings.projectionRegionMode == 1
                ? new Vector3(settings.offAxisProjectorX, settings.offAxisProjectorHeight,
                    -settings.offAxisProjectorDistance)
                : Vector3.zero;
            sphereHit = Vector3.zero;

            var halfFov = Mathf.Max(settings.domeFov * 0.5f * Mathf.Deg2Rad, 0.001f);
            // Match the Output shader exactly. Very wide values can otherwise
            // wrap past the back pole on CPU while the shader clamps at PI.
            var theta = Mathf.Clamp(opticalPoint.magnitude * halfFov, 0f, Mathf.PI);
            var azimuth = Mathf.Atan2(opticalPoint.x, opticalPoint.y);
            var sinTheta = Mathf.Sin(theta);
            var ray = new Vector3(sinTheta * Mathf.Sin(azimuth),
                sinTheta * Mathf.Cos(azimuth), Mathf.Cos(theta));

            if (settings.projectionRegionMode != 1)
            {
                // Centre projection uses zenithal/equidistant coordinates:
                // optical radius zero is the dome zenith (+Y), not the
                // projector-forward direction (+Z) used by edge placement.
                sphereHit = new Vector3(sinTheta * Mathf.Sin(azimuth),
                    Mathf.Cos(theta), sinTheta * Mathf.Cos(azimuth)).normalized;
                return true;
            }

            var aim = settings.offAxisAimElevation * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(aim);
            var sine = Mathf.Sin(aim);
            var pitchedRay = new Vector3(ray.x, cosine * ray.y + sine * ray.z,
                -sine * ray.y + cosine * ray.z);
            var yaw = settings.offAxisAimYaw * Mathf.Deg2Rad;
            var yawCosine = Mathf.Cos(yaw);
            var yawSine = Mathf.Sin(yaw);
            ray = new Vector3(yawCosine * pitchedRay.x + yawSine * pitchedRay.z,
                pitchedRay.y, -yawSine * pitchedRay.x + yawCosine * pitchedRay.z).normalized;

            var projectorDotRay = Vector3.Dot(projector, ray);
            var discriminant = projectorDotRay * projectorDotRay
                               - (projector.sqrMagnitude - 1f);
            if (discriminant < 0f) return false;
            // Use the far root. When a projector is at or just outside the
            // sphere this deliberately ignores the near-side intersection and
            // reaches the intended opposite dome surface, matching the shader.
            var distance = -projectorDotRay + Mathf.Sqrt(discriminant);
            if (distance <= 0.0001f) return false;
            sphereHit = projector + ray * distance;
            var elevation = Mathf.Asin(Mathf.Clamp(sphereHit.normalized.y, -1f, 1f))
                            * Mathf.Rad2Deg;
            return elevation >= settings.offAxisMinimumElevation - 0.001f;
        }

        public static bool TryGetDomeHitAtOutputPoint(DomeProjectionSettings settings,
            float aspect, Vector2 outputPoint,
            out Vector3 projector, out Vector3 sphereHit)
        {
            projector = Vector3.zero;
            sphereHit = Vector3.zero;
            if (!TryGetLensPointAtOutputPoint(settings, aspect, outputPoint,
                    out var lensPoint))
                return false;

            return TryGetDomeHitAtOpticalPoint(settings, lensPoint,
                out projector, out sphereHit);
        }

        private static bool TryGetLensPointAtOutputPoint(
            DomeProjectionSettings settings, float aspect, Vector2 outputPoint,
            out Vector2 lensPoint)
        {
            aspect = Mathf.Max(aspect, 0.01f);
            var point = outputPoint - new Vector2(settings.centerX, settings.centerY);
            point.x *= aspect;
            point.x /= Mathf.Max(settings.radius * settings.ellipseX, 0.0001f);
            point.y /= Mathf.Max(settings.radius * settings.ellipseY, 0.0001f);
            var rotation = settings.outputRotation * Mathf.Deg2Rad;
            var cosine = Mathf.Cos(rotation);
            var sine = Mathf.Sin(rotation);
            point = new Vector2(cosine * point.x - sine * point.y,
                sine * point.x + cosine * point.y);
            if (point.magnitude > 1f)
            {
                lensPoint = Vector2.zero;
                return false;
            }
            var directionalScale = 1f
                + settings.warpTop * Mathf.Pow(Mathf.Max(point.y, 0f), 2f)
                + settings.warpBottom * Mathf.Pow(Mathf.Max(-point.y, 0f), 2f)
                + settings.warpLeft * Mathf.Pow(Mathf.Max(-point.x, 0f), 2f)
                + settings.warpRight * Mathf.Pow(Mathf.Max(point.x, 0f), 2f);
            var shaped = point * directionalScale;
            var radiusSquared = shaped.sqrMagnitude;
            var radialScale = 1f + settings.radialK1 * radiusSquared
                + settings.radialK2 * radiusSquared * radiusSquared
                + settings.radialK3 * radiusSquared * radiusSquared * radiusSquared;
            lensPoint = shaped * radialScale;
            lensPoint += new Vector2(
                2f * settings.tangentialP1 * shaped.x * shaped.y
                    + settings.tangentialP2 * (radiusSquared + 2f * shaped.x * shaped.x),
                settings.tangentialP1 * (radiusSquared + 2f * shaped.y * shaped.y)
                    + 2f * settings.tangentialP2 * shaped.x * shaped.y);
            return true;
        }

        public static bool CalculateRoomReferenceCorners(DomeProjectionSettings settings, float aspect,
            out Vector2 topLeft, out Vector2 topRight,
            out Vector2 bottomLeft, out Vector2 bottomRight)
        {
            var halfWidth = settings.roomWidth * 0.5f;
            var depth = settings.roomDepth;
            Vector3 topLeftPoint;
            Vector3 topRightPoint;
            Vector3 bottomLeftPoint;
            Vector3 bottomRightPoint;
            switch (settings.roomWarpReferenceFace)
            {
                case 1: // Ceiling: far edge above, projector-side edge below.
                    topLeftPoint = new Vector3(-halfWidth, settings.roomHeight, depth);
                    topRightPoint = new Vector3(halfWidth, settings.roomHeight, depth);
                    bottomLeftPoint = new Vector3(-halfWidth, settings.roomHeight, 0f);
                    bottomRightPoint = new Vector3(halfWidth, settings.roomHeight, 0f);
                    break;
                case 2: // Floor: far edge above, projector-side edge below.
                    topLeftPoint = new Vector3(-halfWidth, 0f, depth);
                    topRightPoint = new Vector3(halfWidth, 0f, depth);
                    bottomLeftPoint = new Vector3(-halfWidth, 0f, 0f);
                    bottomRightPoint = new Vector3(halfWidth, 0f, 0f);
                    break;
                case 3: // Left wall: rear edge is screen-left, front edge screen-right.
                    topLeftPoint = new Vector3(-halfWidth, settings.roomHeight, 0f);
                    topRightPoint = new Vector3(-halfWidth, settings.roomHeight, depth);
                    bottomLeftPoint = new Vector3(-halfWidth, 0f, 0f);
                    bottomRightPoint = new Vector3(-halfWidth, 0f, depth);
                    break;
                case 4: // Right wall: front edge is screen-left, rear edge screen-right.
                    topLeftPoint = new Vector3(halfWidth, settings.roomHeight, depth);
                    topRightPoint = new Vector3(halfWidth, settings.roomHeight, 0f);
                    bottomLeftPoint = new Vector3(halfWidth, 0f, depth);
                    bottomRightPoint = new Vector3(halfWidth, 0f, 0f);
                    break;
                default:
                    topLeftPoint = new Vector3(-halfWidth, settings.roomHeight, depth);
                    topRightPoint = new Vector3(halfWidth, settings.roomHeight, depth);
                    bottomLeftPoint = new Vector3(-halfWidth, 0f, depth);
                    bottomRightPoint = new Vector3(halfWidth, 0f, depth);
                    break;
            }

            var topLeftValid = TryProjectRoomPoint(settings, aspect, topLeftPoint, out topLeft);
            var topRightValid = TryProjectRoomPoint(settings, aspect, topRightPoint, out topRight);
            var bottomLeftValid = TryProjectRoomPoint(settings, aspect, bottomLeftPoint, out bottomLeft);
            var bottomRightValid = TryProjectRoomPoint(settings, aspect, bottomRightPoint, out bottomRight);
            var valid = topLeftValid && topRightValid && bottomLeftValid && bottomRightValid
                        && IsValidRoomQuad(topLeft, topRight, bottomLeft, bottomRight);
            if (valid) return true;

            // A physical face can extend behind the projector (typically the
            // near half of the ceiling/floor). In that case the face's literal
            // room corners are not valid projector-image points. Use the
            // footprint of the part that is actually visible in the current
            // projector raster. This keeps every calibration handle finite,
            // on-screen and attached to the selected surface.
            if (TryCalculateVisibleRoomFaceFootprint(settings, aspect, out topLeft,
                    out topRight, out bottomLeft, out bottomRight))
                return true;

            // Last-resort identity quad. This is only used when no usable
            // portion of the selected physical face is in front of the lens.
            topLeft = new Vector2(0.15f, 0.75f);
            topRight = new Vector2(0.85f, 0.75f);
            bottomLeft = new Vector2(0.15f, 0.25f);
            bottomRight = new Vector2(0.85f, 0.25f);
            return true;
        }

        private static bool TryCalculateVisibleRoomFaceFootprint(DomeProjectionSettings settings,
            float aspect, out Vector2 topLeft, out Vector2 topRight,
            out Vector2 bottomLeft, out Vector2 bottomRight)
        {
            topLeft = topRight = bottomLeft = bottomRight = Vector2.zero;
            const int samples = 28;
            var found = false;
            var minimumY = float.MaxValue;
            var maximumY = float.MinValue;
            for (var yIndex = 0; yIndex <= samples; yIndex++)
            {
                var y = yIndex / (float)samples;
                for (var xIndex = 0; xIndex <= samples; xIndex++)
                {
                    var x = xIndex / (float)samples;
                    if (!TryGetRoomHitAtOutputPoint(settings, aspect,
                            new Vector2(x, y), out var face, out var hit)
                        || !MatchesReferenceFace(settings.roomWarpReferenceFace, face, hit.x))
                        continue;
                    found = true;
                    minimumY = Mathf.Min(minimumY, y);
                    maximumY = Mathf.Max(maximumY, y);
                }
            }
            if (!found || maximumY - minimumY < 2f / samples) return false;

            var band = Mathf.Max(2f / samples, (maximumY - minimumY) * 0.14f);
            var topLeftFound = false;
            var topRightFound = false;
            var bottomLeftFound = false;
            var bottomRightFound = false;
            topLeft = bottomLeft = new Vector2(float.MaxValue, 0f);
            topRight = bottomRight = new Vector2(float.MinValue, 0f);
            for (var yIndex = 0; yIndex <= samples; yIndex++)
            {
                var y = yIndex / (float)samples;
                for (var xIndex = 0; xIndex <= samples; xIndex++)
                {
                    var x = xIndex / (float)samples;
                    if (!TryGetRoomHitAtOutputPoint(settings, aspect,
                            new Vector2(x, y), out var face, out var hit)
                        || !MatchesReferenceFace(settings.roomWarpReferenceFace, face, hit.x))
                        continue;
                    var point = new Vector2(x, y);
                    if (y >= maximumY - band)
                    {
                        if (point.x < topLeft.x) { topLeft = point; topLeftFound = true; }
                        if (point.x > topRight.x) { topRight = point; topRightFound = true; }
                    }
                    if (y <= minimumY + band)
                    {
                        if (point.x < bottomLeft.x) { bottomLeft = point; bottomLeftFound = true; }
                        if (point.x > bottomRight.x) { bottomRight = point; bottomRightFound = true; }
                    }
                }
            }
            return topLeftFound && topRightFound && bottomLeftFound && bottomRightFound
                   && IsValidRoomQuad(topLeft, topRight, bottomLeft, bottomRight);
        }

        private static bool MatchesReferenceFace(int referenceFace, int hitFace, float hitX)
        {
            return referenceFace switch
            {
                1 => hitFace == 2,
                2 => hitFace == 3,
                3 => hitFace == 1 && hitX < 0f,
                4 => hitFace == 1 && hitX > 0f,
                _ => hitFace == 0
            };
        }

        public static bool TryGetRoomHitAtOutputPoint(DomeProjectionSettings settings,
            float aspect, Vector2 outputPoint, out int face, out Vector3 roomHit)
        {
            face = -1;
            roomHit = Vector3.zero;
            aspect = Mathf.Max(aspect, 0.01f);
            var point = outputPoint - new Vector2(settings.centerX, settings.centerY);
            point.x *= aspect;
            point.x /= Mathf.Max(settings.radius * settings.ellipseX, 0.0001f);
            point.y /= Mathf.Max(settings.radius * settings.ellipseY, 0.0001f);
            var outputRotation = settings.outputRotation * Mathf.Deg2Rad;
            var outputCos = Mathf.Cos(outputRotation);
            var outputSin = Mathf.Sin(outputRotation);
            point = new Vector2(outputCos * point.x - outputSin * point.y,
                outputSin * point.x + outputCos * point.y);
            var calibrationPoint = new Vector2(point.x / aspect, point.y);
            var lensPoint = ApplyRoomLens(settings, calibrationPoint);
            var tangent = Mathf.Max(Mathf.Tan(settings.roomProjectorHorizontalFov
                                              * 0.5f * Mathf.Deg2Rad), 0.0001f);
            var ray = new Vector3(lensPoint.x * tangent,
                lensPoint.y * tangent / aspect, 1f).normalized;

            var roll = settings.roomProjectorRoll * Mathf.Deg2Rad;
            var rollCos = Mathf.Cos(roll);
            var rollSin = Mathf.Sin(roll);
            ray = new Vector3(rollCos * ray.x - rollSin * ray.y,
                rollSin * ray.x + rollCos * ray.y, ray.z);
            var pitch = settings.roomProjectorPitch * Mathf.Deg2Rad;
            var pitchCos = Mathf.Cos(pitch);
            var pitchSin = Mathf.Sin(pitch);
            ray = new Vector3(ray.x, pitchCos * ray.y + pitchSin * ray.z,
                -pitchSin * ray.y + pitchCos * ray.z);
            var yaw = settings.roomProjectorYaw * Mathf.Deg2Rad;
            var yawCos = Mathf.Cos(yaw);
            var yawSin = Mathf.Sin(yaw);
            ray = new Vector3(yawCos * ray.x + yawSin * ray.z, ray.y,
                -yawSin * ray.x + yawCos * ray.z).normalized;

            var projector = new Vector3(settings.roomProjectorX, settings.roomProjectorY,
                settings.roomProjectorZ);
            var halfWidth = settings.roomWidth * 0.5f;
            var closest = float.MaxValue;
            if (settings.roomShowFrontWall && ray.z > 0.0001f)
            {
                var distance = (settings.roomDepth - projector.z) / ray.z;
                var hit = projector + ray * distance;
                if (distance > 0.0001f && Mathf.Abs(hit.x) <= halfWidth
                    && hit.y >= 0f && hit.y <= settings.roomHeight)
                {
                    closest = distance;
                    roomHit = hit;
                    face = 0;
                }
            }
            if (settings.roomShowSideWall && settings.roomCornerMode != 1 && ray.x > 0.0001f)
            {
                var distance = (halfWidth - projector.x) / ray.x;
                var hit = projector + ray * distance;
                if (distance > 0.0001f && distance < closest && hit.z >= 0f
                    && hit.z <= settings.roomDepth && hit.y >= 0f && hit.y <= settings.roomHeight)
                {
                    closest = distance;
                    roomHit = hit;
                    face = 1;
                }
            }
            if (settings.roomShowSideWall && settings.roomCornerMode != 0 && ray.x < -0.0001f)
            {
                var distance = (-halfWidth - projector.x) / ray.x;
                var hit = projector + ray * distance;
                if (distance > 0.0001f && distance < closest && hit.z >= 0f
                    && hit.z <= settings.roomDepth && hit.y >= 0f && hit.y <= settings.roomHeight)
                {
                    closest = distance;
                    roomHit = hit;
                    face = 1;
                }
            }
            if (settings.roomShowCeiling && ray.y > 0.0001f)
            {
                var distance = (settings.roomHeight - projector.y) / ray.y;
                var hit = projector + ray * distance;
                if (distance > 0.0001f && distance < closest && Mathf.Abs(hit.x) <= halfWidth
                    && hit.z >= 0f && hit.z <= settings.roomDepth)
                {
                    closest = distance;
                    roomHit = hit;
                    face = 2;
                }
            }
            if (settings.roomShowFloor && ray.y < -0.0001f)
            {
                var distance = -projector.y / ray.y;
                var hit = projector + ray * distance;
                if (distance > 0.0001f && distance < closest && Mathf.Abs(hit.x) <= halfWidth
                    && hit.z >= 0f && hit.z <= settings.roomDepth)
                {
                    roomHit = hit;
                    face = 3;
                }
            }
            return face >= 0;
        }

        public static bool TryProjectRoomPoint(DomeProjectionSettings settings, float aspect,
            Vector3 worldPoint, out Vector2 projectedPoint)
        {
            projectedPoint = Vector2.zero;
            aspect = Mathf.Max(aspect, 0.01f);
            var projector = new Vector3(settings.roomProjectorX, settings.roomProjectorY,
                settings.roomProjectorZ);
            var ray = (worldPoint - projector).normalized;

            // Undo the shader's yaw -> pitch -> roll world rotation so the
            // world-space corner becomes a local projector ray.
            var yaw = settings.roomProjectorYaw * Mathf.Deg2Rad;
            var yawCos = Mathf.Cos(yaw);
            var yawSin = Mathf.Sin(yaw);
            ray = new Vector3(yawCos * ray.x - yawSin * ray.z, ray.y,
                yawSin * ray.x + yawCos * ray.z);
            var pitch = settings.roomProjectorPitch * Mathf.Deg2Rad;
            var pitchCos = Mathf.Cos(pitch);
            var pitchSin = Mathf.Sin(pitch);
            ray = new Vector3(ray.x, pitchCos * ray.y - pitchSin * ray.z,
                pitchSin * ray.y + pitchCos * ray.z);
            var roll = settings.roomProjectorRoll * Mathf.Deg2Rad;
            var rollCos = Mathf.Cos(roll);
            var rollSin = Mathf.Sin(roll);
            ray = new Vector3(rollCos * ray.x + rollSin * ray.y,
                -rollSin * ray.x + rollCos * ray.y, ray.z);

            // Rectilinear projectors cannot address points on or behind their
            // image plane. Never divide by a negative/near-zero forward value.
            if (ray.z <= 0.01f) return false;

            var tangent = Mathf.Max(Mathf.Tan(settings.roomProjectorHorizontalFov
                                              * 0.5f * Mathf.Deg2Rad), 0.0001f);
            var lensPoint = new Vector2(ray.x / (ray.z * tangent),
                ray.y * aspect / (ray.z * tangent));
            var calibrationPoint = InvertRoomLens(settings, lensPoint);

            var rotated = new Vector2(calibrationPoint.x * aspect, calibrationPoint.y);
            var outputRotation = settings.outputRotation * Mathf.Deg2Rad;
            var outputCos = Mathf.Cos(outputRotation);
            var outputSin = Mathf.Sin(outputRotation);
            var unrotated = new Vector2(outputCos * rotated.x + outputSin * rotated.y,
                -outputSin * rotated.x + outputCos * rotated.y);
            projectedPoint = new Vector2(
                settings.centerX + unrotated.x * settings.radius * settings.ellipseX / aspect,
                settings.centerY + unrotated.y * settings.radius * settings.ellipseY);
            if (!IsFinite(projectedPoint.x) || !IsFinite(projectedPoint.y)) return false;
            // Values farther away than this only reduce Control auto-fit to its
            // minimum and are not practically draggable.
            return projectedPoint.x >= -2f && projectedPoint.x <= 3f
                   && projectedPoint.y >= -2f && projectedPoint.y <= 3f;
        }

        public static bool IsValidRoomQuad(Vector2 topLeft, Vector2 topRight,
            Vector2 bottomLeft, Vector2 bottomRight)
        {
            var points = new[] { topLeft, topRight, bottomRight, bottomLeft };
            var sign = 0f;
            for (var index = 0; index < points.Length; index++)
            {
                var edgeA = points[(index + 1) % points.Length] - points[index];
                var edgeB = points[(index + 2) % points.Length] - points[(index + 1) % points.Length];
                var cross = edgeA.x * edgeB.y - edgeA.y * edgeB.x;
                if (Mathf.Abs(cross) < 0.0001f) return false;
                if (index == 0) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return false;
            }
            return true;
        }

        private static bool TryCalculateHomography(Vector2 sourceTopLeft,
            Vector2 sourceTopRight, Vector2 sourceBottomLeft, Vector2 sourceBottomRight,
            Vector2 destinationTopLeft, Vector2 destinationTopRight,
            Vector2 destinationBottomLeft, Vector2 destinationBottomRight,
            out Vector3 row0, out Vector3 row1, out Vector3 row2)
        {
            row0 = new Vector3(1f, 0f, 0f);
            row1 = new Vector3(0f, 1f, 0f);
            row2 = new Vector3(0f, 0f, 1f);
            var source = new[]
            {
                sourceTopLeft, sourceTopRight, sourceBottomLeft, sourceBottomRight
            };
            var destination = new[]
            {
                destinationTopLeft, destinationTopRight,
                destinationBottomLeft, destinationBottomRight
            };
            var system = new double[8, 9];
            for (var index = 0; index < 4; index++)
            {
                var x = source[index].x;
                var y = source[index].y;
                var u = destination[index].x;
                var v = destination[index].y;
                var firstRow = index * 2;
                system[firstRow, 0] = x;
                system[firstRow, 1] = y;
                system[firstRow, 2] = 1.0;
                system[firstRow, 6] = -u * x;
                system[firstRow, 7] = -u * y;
                system[firstRow, 8] = u;
                system[firstRow + 1, 3] = x;
                system[firstRow + 1, 4] = y;
                system[firstRow + 1, 5] = 1.0;
                system[firstRow + 1, 6] = -v * x;
                system[firstRow + 1, 7] = -v * y;
                system[firstRow + 1, 8] = v;
            }

            for (var column = 0; column < 8; column++)
            {
                var pivot = column;
                for (var row = column + 1; row < 8; row++)
                    if (System.Math.Abs(system[row, column])
                        > System.Math.Abs(system[pivot, column])) pivot = row;
                if (System.Math.Abs(system[pivot, column]) < 1e-10) return false;
                if (pivot != column)
                    for (var entry = column; entry <= 8; entry++)
                        (system[column, entry], system[pivot, entry])
                            = (system[pivot, entry], system[column, entry]);
                var divisor = system[column, column];
                for (var entry = column; entry <= 8; entry++)
                    system[column, entry] /= divisor;
                for (var row = 0; row < 8; row++)
                {
                    if (row == column) continue;
                    var factor = system[row, column];
                    for (var entry = column; entry <= 8; entry++)
                        system[row, entry] -= factor * system[column, entry];
                }
            }

            row0 = new Vector3((float)system[0, 8], (float)system[1, 8],
                (float)system[2, 8]);
            row1 = new Vector3((float)system[3, 8], (float)system[4, 8],
                (float)system[5, 8]);
            row2 = new Vector3((float)system[6, 8], (float)system[7, 8], 1f);
            return IsFinite(row0.x) && IsFinite(row0.y) && IsFinite(row0.z)
                   && IsFinite(row1.x) && IsFinite(row1.y) && IsFinite(row1.z)
                   && IsFinite(row2.x) && IsFinite(row2.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Vector2 InvertRoomLens(DomeProjectionSettings settings, Vector2 target)
        {
            var estimate = target;
            const float epsilon = 0.0005f;
            for (var iteration = 0; iteration < 8; iteration++)
            {
                var mapped = ApplyRoomLens(settings, estimate);
                var error = mapped - target;
                if (error.sqrMagnitude < 0.00000001f) break;
                var dx = (ApplyRoomLens(settings, estimate + new Vector2(epsilon, 0f)) - mapped)
                         / epsilon;
                var dy = (ApplyRoomLens(settings, estimate + new Vector2(0f, epsilon)) - mapped)
                         / epsilon;
                var determinant = dx.x * dy.y - dx.y * dy.x;
                if (Mathf.Abs(determinant) < 0.000001f) break;
                estimate -= new Vector2(
                    (error.x * dy.y - error.y * dy.x) / determinant,
                    (dx.x * error.y - dx.y * error.x) / determinant);
            }

            return estimate;
        }

        private static Vector2 ApplyRoomLens(DomeProjectionSettings settings, Vector2 point)
        {
            var directionalScale = 1f
                + settings.warpTop * Mathf.Pow(Mathf.Max(point.y, 0f), 2f)
                + settings.warpBottom * Mathf.Pow(Mathf.Max(-point.y, 0f), 2f)
                + settings.warpLeft * Mathf.Pow(Mathf.Max(-point.x, 0f), 2f)
                + settings.warpRight * Mathf.Pow(Mathf.Max(point.x, 0f), 2f);
            var shaped = point * directionalScale;
            var r2 = Vector2.Dot(shaped, shaped);
            var radialScale = 1f + settings.radialK1 * r2
                + settings.radialK2 * r2 * r2 + settings.radialK3 * r2 * r2 * r2;
            var lens = shaped * radialScale;
            lens.x += 2f * settings.tangentialP1 * shaped.x * shaped.y
                      + settings.tangentialP2 * (r2 + 2f * shaped.x * shaped.x);
            lens.y += settings.tangentialP1 * (r2 + 2f * shaped.y * shaped.y)
                      + 2f * settings.tangentialP2 * shaped.x * shaped.y;
            return lens;
        }
    }
}

