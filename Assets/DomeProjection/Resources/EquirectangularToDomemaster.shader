// Kept in Resources so Unity's player build does not strip this runtime-created shader.
Shader "Hidden/DomeProjection/EquirectangularToDomemaster"
{
    Properties
    {
        _VideoTex ("360 Video", 2D) = "black" {}
        _BackgroundTex ("Window Background", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _VideoTex;
            float _HasVideo;
            sampler2D _BackgroundTex;
            float _HasBackground;
            float _OutputAspect;
            float2 _Center;
            float _Radius;
            float2 _Ellipse;
            float3 _Orientation;
            float _DomeFov;
            float _OutputRotation;
            float3 _Radial;
            float2 _Tangential;
            float4 _DirectionalWarp;
            float2 _ColorAdjust;
            float _EdgeFade;
            float2 _InputFlip;
            float _GridMode;
            float4 _GridSettings;
            float4 _GridAppearance;
            float _ContentMode;
            float _SourceAspect;
            float4 _FlatPlacement;
            float4 _FlatSize;
            float4 _FlatCrop;

            static const float PI = 3.14159265358979323846;

            float3 RotateDirection(float3 direction, float3 degrees)
            {
                float3 a = radians(degrees);

                float sy = sin(a.x);
                float cy = cos(a.x);
                direction = float3(cy * direction.x + sy * direction.z,
                                   direction.y,
                                  -sy * direction.x + cy * direction.z);

                float sp = sin(a.y);
                float cp = cos(a.y);
                direction = float3(direction.x,
                                   cp * direction.y - sp * direction.z,
                                   sp * direction.y + cp * direction.z);

                float sr = sin(a.z);
                float cr = cos(a.z);
                return float3(cr * direction.x - sr * direction.y,
                              sr * direction.x + cr * direction.y,
                              direction.z);
            }

            float Line(float value, float width)
            {
                float distanceToLine = abs(frac(value + 0.5) - 0.5);
                return 1.0 - smoothstep(width, width * 2.0, distanceToLine);
            }

            float3 CalibrationGrid(float outputRadius, float azimuth, float angleDegrees)
            {
                float minorStep = max(_GridSettings.x, 1.0);
                float majorStep = max(_GridSettings.z, minorStep);
                float ringMinorValue = angleDegrees / minorStep;
                float ringMajorValue = angleDegrees / majorStep;
                float azimuthDegrees = degrees(azimuth);
                float spokeMinorValue = azimuthDegrees / minorStep;
                float spokeMajorValue = azimuthDegrees / majorStep;

                float lineWidth = max(_GridAppearance.x, 0.25);
                float minorRings = Line(ringMinorValue, max(fwidth(ringMinorValue) * 0.35, 0.004) * lineWidth);
                float majorRings = Line(ringMajorValue, max(fwidth(ringMajorValue) * 0.50, 0.006) * lineWidth);
                float minorSpokes = Line(spokeMinorValue, max(fwidth(spokeMinorValue) * 0.35, 0.004) * lineWidth);
                float majorSpokes = Line(spokeMajorValue, max(fwidth(spokeMajorValue) * 0.50, 0.006) * lineWidth);

                // Suppress the densest minor spokes around the pole while keeping
                // the 15-degree major axes visible all the way to the center.
                minorSpokes *= smoothstep(0.07, 0.16, outputRadius);
                float minorGrid = saturate(max(minorRings, minorSpokes));
                float majorGrid = saturate(max(majorRings, majorSpokes));
                float outer = 1.0 - smoothstep(0.006, 0.016, abs(outputRadius - 1.0));
                float center = 1.0 - smoothstep(0.0, 0.025, outputRadius);

                float3 grid = float3(0.025, 0.12, 0.19) * minorGrid;
                grid = max(grid, float3(0.08, 0.46, 0.72) * majorGrid);
                grid = max(grid, float3(outer, outer, outer));
                grid = max(grid, float3(1.0, 1.0, 1.0) * center);

                // Colored orientation marks just inside the horizon:
                // North red, East green, South blue, West yellow.
                float markerRadius = 1.0 - smoothstep(0.035, 0.06, abs(outputRadius - 0.90));
                float north = 1.0 - smoothstep(0.025, 0.055, abs(sin(azimuth * 0.5)));
                float east = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI * 0.5) * 0.5)));
                float south = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI) * 0.5)));
                float west = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth + PI * 0.5) * 0.5)));
                grid = max(grid, _GridSettings.w * markerRadius * (north * float3(1, 0.1, 0.1) + east * float3(0.1, 1, 0.1)
                                                                  + south * float3(0.1, 0.3, 1) + west * float3(1, 0.8, 0.1)));
                return saturate(grid);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 p = input.uv - _Center;
                p.x *= _OutputAspect;
                p /= max(_Radius * _Ellipse, float2(0.0001, 0.0001));

                float rotation = radians(_OutputRotation);
                float cs = cos(rotation);
                float sn = sin(rotation);
                p = float2(cs * p.x - sn * p.y, sn * p.x + cs * p.y);

                float outputRadius = length(p);
                if (outputRadius > 1.0) return fixed4(0, 0, 0, 1);

                float directionalScale = 1.0
                    + _DirectionalWarp.x * pow(max(p.y, 0.0), 2.0)
                    + _DirectionalWarp.y * pow(max(-p.y, 0.0), 2.0)
                    + _DirectionalWarp.z * pow(max(-p.x, 0.0), 2.0)
                    + _DirectionalWarp.w * pow(max(p.x, 0.0), 2.0);
                float2 shapedPoint = p * directionalScale;
                float r2 = dot(shapedPoint, shapedPoint);
                float radialScale = 1.0 + _Radial.x * r2 + _Radial.y * r2 * r2 + _Radial.z * r2 * r2 * r2;
                float2 lensPoint = shapedPoint * radialScale;
                lensPoint += float2(2.0 * _Tangential.x * shapedPoint.x * shapedPoint.y + _Tangential.y * (r2 + 2.0 * shapedPoint.x * shapedPoint.x),
                                    _Tangential.x * (r2 + 2.0 * shapedPoint.y * shapedPoint.y) + 2.0 * _Tangential.y * shapedPoint.x * shapedPoint.y);

                float normalizedAngle = length(lensPoint);
                float theta = clamp(normalizedAngle * radians(_DomeFov * 0.5), 0.0, PI);
                float azimuth = atan2(lensPoint.x, lensPoint.y);
                float sinTheta = sin(theta);
                float3 direction = float3(sinTheta * sin(azimuth), cos(theta), sinTheta * cos(azimuth));
                float3 backgroundDirection = RotateDirection(direction, _Orientation);
                float2 backgroundUV = float2(frac(atan2(backgroundDirection.x, backgroundDirection.z) / (2.0 * PI) + 0.5),
                                             0.5 - asin(clamp(backgroundDirection.y, -1.0, 1.0)) / PI);
                if (_InputFlip.x > 0.5) backgroundUV.x = 1.0 - backgroundUV.x;
                if (_InputFlip.y > 0.5) backgroundUV.y = 1.0 - backgroundUV.y;
                float2 videoUV;
                float contentMask = 1.0;
                if (_ContentMode > 0.5)
                {
                    float azimuthCenter = radians(_FlatPlacement.x);
                    float polarCenter = radians(_FlatPlacement.y);
                    float3 panelCenter = float3(sin(polarCenter) * sin(azimuthCenter),
                                                cos(polarCenter),
                                                sin(polarCenter) * cos(azimuthCenter));
                    float3 panelRight = float3(cos(azimuthCenter), 0.0, -sin(azimuthCenter));
                    float3 panelUp = normalize(cross(panelRight, panelCenter));
                    float panelRoll = radians(_FlatPlacement.z);
                    float rollCos = cos(panelRoll);
                    float rollSin = sin(panelRoll);
                    float3 rolledRight = panelRight * rollCos + panelUp * rollSin;
                    float3 rolledUp = -panelRight * rollSin + panelUp * rollCos;

                    float denominator = dot(direction, panelCenter);
                    contentMask = step(0.0001, denominator);
                    float3 hit = direction / max(denominator, 0.0001);
                    float3 offset = hit - panelCenter;
                    float halfWidth = tan(radians(_FlatPlacement.w * 0.5));
                    float halfHeight = halfWidth / max(_SourceAspect, 0.01) * _FlatSize.x;
                    videoUV = float2(dot(offset, rolledRight) / max(halfWidth * 2.0, 0.0001) + 0.5,
                                     dot(offset, rolledUp) / max(halfHeight * 2.0, 0.0001) + 0.5);
                    contentMask *= step(0.0, videoUV.x) * step(videoUV.x, 1.0)
                                 * step(0.0, videoUV.y) * step(videoUV.y, 1.0);
                    videoUV = float2(lerp(_FlatCrop.x, 1.0 - _FlatCrop.y, videoUV.x),
                                     lerp(_FlatCrop.z, 1.0 - _FlatCrop.w, videoUV.y));
                }
                else
                {
                    direction = RotateDirection(direction, _Orientation);
                    float longitude = atan2(direction.x, direction.z);
                    float latitude = asin(clamp(direction.y, -1.0, 1.0));
                    videoUV = float2(frac(longitude / (2.0 * PI) + 0.5), 0.5 - latitude / PI);
                }
                if (_InputFlip.x > 0.5) videoUV.x = 1.0 - videoUV.x;
                if (_InputFlip.y > 0.5) videoUV.y = 1.0 - videoUV.y;

                float3 backgroundColor = _HasBackground > 0.5
                    ? tex2D(_BackgroundTex, backgroundUV).rgb
                    : float3(0, 0, 0);
                float3 videoColor = backgroundColor;
                if (_HasVideo > 0.5)
                {
                    float3 primaryColor = tex2D(_VideoTex, videoUV).rgb;
                    videoColor = _HasBackground > 0.5
                        ? lerp(backgroundColor, primaryColor, contentMask)
                        : primaryColor * contentMask;
                }
                videoColor = pow(max(videoColor * _ColorAdjust.x, 0.0), 1.0 / max(_ColorAdjust.y, 0.001));

                float3 grid = CalibrationGrid(outputRadius, azimuth, degrees(theta));
                float gridAmount = max(grid.r, max(grid.g, grid.b));
                float3 color = videoColor;
                if (_GridMode > 1.5) color = float3(0.008, 0.012, 0.02) + grid;
                else if (_GridMode > 0.5) color = lerp(color, grid, saturate(gridAmount * _GridSettings.y));

                float mask = _EdgeFade > 0.0001 ? 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, outputRadius) : 1.0;
                return fixed4(saturate(color) * mask, 1.0);
            }
            ENDCG
        }
    }
}

