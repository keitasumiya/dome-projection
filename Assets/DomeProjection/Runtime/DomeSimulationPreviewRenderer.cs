using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomeProjection
{
    /// <summary>
    /// Diagnostic third-person preview of the selected source on the physical
    /// dome.  It never feeds Output; it only makes the physical projector model
    /// and its illuminated footprint visible to the operator.
    /// </summary>
    public sealed class DomeSimulationPreviewRenderer : IDisposable
    {
        private const int PreviewLayer = 30;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly Mesh mesh;
        private readonly Material material;
        private RenderTexture target;
        private float lastMinimumElevation = float.NaN;
        private bool meshReady;

        public DomeSimulationPreviewRenderer(Transform parent)
        {
            root = new GameObject("Dome Simulation Content Preview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            root.transform.SetParent(parent, false);
            var shader = Resources.Load<Shader>("DomeSimulationPreview");
            if (shader == null)
                shader = Shader.Find("Hidden/DomeProjection/DomeSimulationPreview");
            material = shader != null
                ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave }
                : null;
            mesh = new Mesh
            {
                name = "Dome Simulation Preview Sphere",
                hideFlags = HideFlags.HideAndDontSave
            };
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;

            var cameraObject = new GameObject("Dome Simulation Preview Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            cameraObject.transform.SetParent(parent, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.034f, 0.048f, 1f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 20f;
        }

        public RenderTexture Render(DomeProjectionSettings settings, Texture texture,
            bool flipY, int contentMode, int sourceWidth, int sourceHeight,
            float viewYaw, float viewPitch, float worldExtent, float zoom,
            float minimumElevation, bool showGuide, int width, int height)
        {
            if (material == null || settings == null) return null;
            EnsureTarget(width, height);
            minimumElevation = Mathf.Clamp(minimumElevation, -90f, 89f);
            if (!meshReady || Mathf.Abs(lastMinimumElevation - minimumElevation) > 0.01f)
            {
                BuildSphere(minimumElevation);
                lastMinimumElevation = minimumElevation;
                meshReady = true;
            }

            var viewRotation = Quaternion.Euler(viewPitch, -viewYaw, 0f);
            camera.transform.rotation = Quaternion.Inverse(viewRotation);
            camera.transform.position = -camera.transform.forward * 4f;
            camera.orthographicSize = Mathf.Max(worldExtent * 1.25f
                / Mathf.Max(zoom, 0.01f), 0.1f);
            camera.aspect = target.width / (float)Mathf.Max(target.height, 1);
            camera.targetTexture = target;

            material.SetTexture("_MainTex", texture != null ? texture : Texture2D.blackTexture);
            material.SetFloat("_HasTexture", texture != null ? 1f : 0f);
            material.SetVector("_Orientation", new Vector4(settings.yaw, settings.pitch,
                settings.roll, 0f));
            material.SetVector("_InputFlip", new Vector4(settings.flipInputX ? 1f : 0f,
                settings.flipInputY ^ flipY ? 1f : 0f, 0f, 0f));
            material.SetFloat("_ContentMode", contentMode);
            material.SetFloat("_SourceAspect", sourceHeight > 0
                ? sourceWidth / (float)sourceHeight : 16f / 9f);
            material.SetVector("_CircularSource", new Vector4(settings.allSkySourceCenterX,
                settings.allSkySourceCenterY, settings.allSkySourceRadius,
                settings.allSkySourceRotation));
            material.SetVector("_CircularRadial", new Vector4(settings.allSkySourceRadialK1,
                settings.allSkySourceRadialK2, 0f, 0f));
            material.SetVector("_FlatPlacement", new Vector4(settings.flatAzimuth,
                settings.flatPolarAngle, settings.flatRoll, settings.flatHorizontalFov));
            material.SetFloat("_FlatHeightScale", settings.flatHeightScale);
            material.SetVector("_FlatCrop", new Vector4(settings.flatCropLeft,
                settings.flatCropRight, settings.flatCropBottom, settings.flatCropTop));
            material.SetVector("_ColorAdjust", new Vector4(settings.brightness,
                settings.gamma, 0f, 0f));
            material.SetFloat("_ProjectionRegionMode", settings.projectionRegionMode);
            material.SetFloat("_DomeFov", settings.domeFov);
            material.SetVector("_ProjectorPosition", new Vector4(settings.offAxisProjectorX,
                settings.offAxisProjectorHeight, -settings.offAxisProjectorDistance, 0f));
            material.SetVector("_ProjectorAim", new Vector4(settings.offAxisAimYaw,
                settings.offAxisAimElevation, settings.offAxisMinimumElevation, 0f));
            material.SetFloat("_GuideStrength", showGuide ? 1f : 0f);
            material.SetVector("_GridSettings", new Vector4(settings.gridStepDegrees,
                settings.gridOpacity, 15f, settings.showOrientationMarkers ? 1f : 0f));
            material.SetFloat("_GridLineWidth", settings.gridLineWidth);
            root.SetActive(true);
            camera.Render();
            return target;
        }

        private void EnsureTarget(int width, int height)
        {
            width = Mathf.Clamp(width, 320, 1600);
            height = Mathf.Clamp(height, 180, 1000);
            if (target != null && target.width == width && target.height == height) return;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "Dome Simulation Content Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            target.Create();
        }

        private void BuildSphere(float minimumElevation)
        {
            const int longitudeSegments = 64;
            const int latitudeSegments = 24;
            const float maximumElevation = 90f;
            var vertices = new List<Vector3>((longitudeSegments + 1) * (latitudeSegments + 1));
            var triangles = new List<int>(longitudeSegments * latitudeSegments * 6);
            for (var latitude = 0; latitude <= latitudeSegments; latitude++)
            {
                var elevation = Mathf.Lerp(minimumElevation, maximumElevation,
                    latitude / (float)latitudeSegments) * Mathf.Deg2Rad;
                var ringRadius = Mathf.Cos(elevation);
                var y = Mathf.Sin(elevation);
                for (var longitude = 0; longitude <= longitudeSegments; longitude++)
                {
                    var azimuth = longitude * Mathf.PI * 2f / longitudeSegments;
                    vertices.Add(new Vector3(ringRadius * Mathf.Sin(azimuth), y,
                        ringRadius * Mathf.Cos(azimuth)));
                }
            }
            for (var latitude = 0; latitude < latitudeSegments; latitude++)
            for (var longitude = 0; longitude < longitudeSegments; longitude++)
            {
                var a = latitude * (longitudeSegments + 1) + longitude;
                var b = a + 1;
                var c = a + longitudeSegments + 1;
                var d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (material != null) UnityEngine.Object.Destroy(material);
            if (mesh != null) UnityEngine.Object.Destroy(mesh);
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
