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

        private Material material;
        private Camera outputCamera;

        public DomeProjectionSettings Settings { get; set; }
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
            if (material != null) Destroy(material);
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
            material.SetFloat(OutputAspect, height > 0 ? (float)width / height : 1f);
            material.SetVector(Center, new Vector4(Settings.centerX, Settings.centerY, 0f, 0f));
            material.SetFloat(Radius, Settings.radius);
            material.SetVector(Ellipse, new Vector4(Settings.ellipseX, Settings.ellipseY, 0f, 0f));
            material.SetVector(Orientation, new Vector4(Settings.yaw, Settings.pitch, Settings.roll, 0f));
            material.SetFloat(DomeFov, Settings.domeFov);
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

            Graphics.Blit(source, destination, material, 0);
        }
    }
}

