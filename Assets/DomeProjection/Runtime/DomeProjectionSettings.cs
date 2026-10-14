using System;
using UnityEngine;

namespace DomeProjection
{
    [Serializable]
    public sealed class DomeProjectionSettings
    {
        public string videoPath = string.Empty;

        // View orientation in the source sphere.
        public float yaw;
        public float pitch;
        public float roll;

        // Output circle. Center is in normalized output coordinates; radius is
        // relative to output height, so 0.5 gives a height-filling circle.
        public float centerX = 0.5f;
        public float centerY = 0.5f;
        public float radius = 0.485f;
        public float ellipseX = 1f;
        public float ellipseY = 1f;
        public float outputRotation;

        // Radial calibration: normalized dome angle = r + k1*r^3 + k2*r^5.
        public float radialK1;
        public float radialK2;

        public float brightness = 1f;
        public float gamma = 1f;
        public float edgeFade;
        public bool flipInputX;
        public bool flipInputY;
        public bool loop = true;

        // 0 = off, 1 = overlay, 2 = calibration pattern only.
        public int gridMode = 2;
        public int outputDisplay;

        public void ResetGeometry()
        {
            yaw = pitch = roll = 0f;
            centerX = centerY = 0.5f;
            radius = 0.485f;
            ellipseX = ellipseY = 1f;
            outputRotation = 0f;
            radialK1 = radialK2 = 0f;
            brightness = gamma = 1f;
            edgeFade = 0f;
            flipInputX = flipInputY = false;
        }

        public void Clamp()
        {
            yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch, -180f, 180f);
            roll = Mathf.Clamp(roll, -180f, 180f);
            centerX = Mathf.Clamp01(centerX);
            centerY = Mathf.Clamp01(centerY);
            radius = Mathf.Clamp(radius, 0.05f, 1f);
            ellipseX = Mathf.Clamp(ellipseX, 0.25f, 2f);
            ellipseY = Mathf.Clamp(ellipseY, 0.25f, 2f);
            outputRotation = Mathf.Repeat(outputRotation + 180f, 360f) - 180f;
            radialK1 = Mathf.Clamp(radialK1, -0.5f, 0.5f);
            radialK2 = Mathf.Clamp(radialK2, -0.5f, 0.5f);
            brightness = Mathf.Clamp(brightness, 0f, 3f);
            gamma = Mathf.Clamp(gamma, 0.2f, 3f);
            edgeFade = Mathf.Clamp(edgeFade, 0f, 0.25f);
            gridMode = Mathf.Clamp(gridMode, 0, 2);
            outputDisplay = Mathf.Max(0, outputDisplay);
        }
    }
}

