using UnityEngine;

namespace DomeProjection
{
    [RequireComponent(typeof(Camera))]
    public sealed class DomeProjectionRenderer : MonoBehaviour
    {
        private static readonly int VideoTex = Shader.PropertyToID("_VideoTex");
        private static readonly int HasVideo = Shader.PropertyToID("_HasVideo");
        private static readonly int OutputAspect = Shader.PropertyToID("_OutputAspect");
        private static readonly int Center = Shader.PropertyToID("_Center");
        private static readonly int Radius = Shader.PropertyToID("_Radius");
        private static readonly int Ellipse = Shader.PropertyToID("_Ellipse");
        private static readonly int Orientation = Shader.PropertyToID("_Orientation");
        private static readonly int OutputRotation = Shader.PropertyToID("_OutputRotation");
        private static readonly int Radial = Shader.PropertyToID("_Radial");
        private static readonly int ColorAdjust = Shader.PropertyToID("_ColorAdjust");
        private static readonly int EdgeFade = Shader.PropertyToID("_EdgeFade");
        private static readonly int InputFlip = Shader.PropertyToID("_InputFlip");
        private static readonly int GridMode = Shader.PropertyToID("_GridMode");

        private Material material;
        private Camera outputCamera;

        public DomeProjectionSettings Settings { get; set; }
        public IVideoSource VideoSource { get; set; }

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
            var texture = VideoSource?.Texture;
            var hasVideo = texture != null && VideoSource.IsPrepared;

            material.SetTexture(VideoTex, hasVideo ? texture : Texture2D.blackTexture);
            material.SetFloat(HasVideo, hasVideo ? 1f : 0f);
            var width = destination != null ? destination.width : outputCamera.pixelWidth;
            var height = destination != null ? destination.height : outputCamera.pixelHeight;
            material.SetFloat(OutputAspect, height > 0 ? (float)width / height : 1f);
            material.SetVector(Center, new Vector4(Settings.centerX, Settings.centerY, 0f, 0f));
            material.SetFloat(Radius, Settings.radius);
            material.SetVector(Ellipse, new Vector4(Settings.ellipseX, Settings.ellipseY, 0f, 0f));
            material.SetVector(Orientation, new Vector4(Settings.yaw, Settings.pitch, Settings.roll, 0f));
            material.SetFloat(OutputRotation, Settings.outputRotation);
            material.SetVector(Radial, new Vector4(Settings.radialK1, Settings.radialK2, 0f, 0f));
            material.SetVector(ColorAdjust, new Vector4(Settings.brightness, Settings.gamma, 0f, 0f));
            material.SetFloat(EdgeFade, Settings.edgeFade);
            material.SetVector(InputFlip, new Vector4(Settings.flipInputX ? 1f : 0f, Settings.flipInputY ? 1f : 0f, 0f, 0f));
            material.SetFloat(GridMode, hasVideo ? Settings.gridMode : 2f);

            Graphics.Blit(source, destination, material, 0);
        }
    }
}

