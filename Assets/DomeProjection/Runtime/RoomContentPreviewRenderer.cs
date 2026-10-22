using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomeProjection
{
    /// <summary>
    /// Renders an observer-eye preview of the current source mapped onto the
    /// physical room surfaces. It is intentionally diagnostic and never feeds
    /// the projector output or modifies calibration values.
    /// </summary>
    public sealed class RoomContentPreviewRenderer : IDisposable
    {
        private const int PreviewLayer = 31;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly Mesh mesh;
        private readonly Material material;
        private RenderTexture target;
        private Vector3 lastDimensions = new Vector3(-1f, -1f, -1f);

        public RoomContentPreviewRenderer(Transform parent)
        {
            root = new GameObject("Room Observer Content Preview")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            root.transform.SetParent(parent, false);

            var shader = Resources.Load<Shader>("RoomContentPreview");
            if (shader == null) shader = Shader.Find("Hidden/DomeProjection/RoomContentPreview");
            material = shader != null ? new Material(shader) { hideFlags = HideFlags.HideAndDontSave } : null;
            mesh = new Mesh { name = "Room Content Preview Mesh", hideFlags = HideFlags.HideAndDontSave };
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var cameraObject = new GameObject("Room Observer Preview Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = PreviewLayer
            };
            cameraObject.transform.SetParent(parent, false);
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.008f, 0.012f, 0.02f, 1f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
        }

        public RenderTexture Render(DomeProjectionSettings settings, Texture texture,
            bool flipY, int contentMode, int sourceWidth, int sourceHeight,
            float yaw, float pitch, float fieldOfView, bool showGuide,
            int width, int height)
        {
            if (material == null || settings == null) return null;
            EnsureTarget(width, height);
            var dimensions = new Vector3(settings.roomWidth, settings.roomHeight,
                settings.roomDepth);
            if ((dimensions - lastDimensions).sqrMagnitude > 0.000001f)
            {
                BuildRoomMesh(dimensions);
                lastDimensions = dimensions;
            }

            var observer = new Vector3(settings.roomViewerX, settings.roomViewerY,
                settings.roomViewerZ);
            camera.transform.position = observer;
            camera.transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
            camera.fieldOfView = Mathf.Clamp(fieldOfView, 30f, 140f);
            camera.aspect = target.width / (float)Mathf.Max(target.height, 1);
            camera.targetTexture = target;

            material.SetTexture("_MainTex", texture != null ? texture : Texture2D.blackTexture);
            material.SetFloat("_HasTexture", texture != null ? 1f : 0f);
            material.SetVector("_ObserverPosition", observer);
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
            material.SetFloat("_GridSpacing", settings.roomGridSpacing);
            material.SetVector("_FaceVisibility", new Vector4(
                settings.roomShowFrontWall ? 1f : 0f,
                settings.roomShowSideWall && settings.roomCornerMode != 0 ? 1f : 0f,
                settings.roomShowSideWall && settings.roomCornerMode != 1 ? 1f : 0f,
                settings.roomShowCeiling ? 1f : 0f));
            material.SetFloat("_FloorVisible", settings.roomShowFloor ? 1f : 0f);
            material.SetFloat("_GuideStrength", showGuide ? 1f : 0f);
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
                name = "Room Observer Content Preview",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear
            };
            target.Create();
        }

        private void BuildRoomMesh(Vector3 dimensions)
        {
            var vertices = new List<Vector3>(20);
            var faceIds = new List<Vector2>(20);
            var triangles = new List<int>(30);
            var halfWidth = dimensions.x * 0.5f;
            var x0 = -halfWidth;
            var x1 = halfWidth;
            var y0 = 0f;
            var y1 = dimensions.y;
            var z0 = 0f;
            var z1 = dimensions.z;

            AddQuad(vertices, faceIds, triangles, 0,
                new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
                new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
            AddQuad(vertices, faceIds, triangles, 1,
                new Vector3(x0, y0, z0), new Vector3(x0, y0, z1),
                new Vector3(x0, y1, z1), new Vector3(x0, y1, z0));
            AddQuad(vertices, faceIds, triangles, 2,
                new Vector3(x1, y0, z1), new Vector3(x1, y0, z0),
                new Vector3(x1, y1, z0), new Vector3(x1, y1, z1));
            AddQuad(vertices, faceIds, triangles, 3,
                new Vector3(x0, y1, z1), new Vector3(x1, y1, z1),
                new Vector3(x1, y1, z0), new Vector3(x0, y1, z0));
            AddQuad(vertices, faceIds, triangles, 4,
                new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                new Vector3(x1, y0, z1), new Vector3(x0, y0, z1));

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(1, faceIds);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static void AddQuad(List<Vector3> vertices, List<Vector2> faceIds,
            List<int> triangles, int faceId, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (var index = 0; index < 4; index++) faceIds.Add(new Vector2(faceId, 0f));
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
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
