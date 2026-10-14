// Kept in Resources so Unity's player build does not strip this runtime-created shader.
Shader "Hidden/DomeProjection/EquirectangularToDomemaster"
{
    Properties
    {
        _VideoTex ("360 Video", 2D) = "black" {}
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
            float _OutputAspect;
            float2 _Center;
            float _Radius;
            float2 _Ellipse;
            float3 _Orientation;
            float _OutputRotation;
            float2 _Radial;
            float2 _ColorAdjust;
            float _EdgeFade;
            float2 _InputFlip;
            float _GridMode;

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

            float3 CalibrationGrid(float radius, float azimuth, float normalizedAngle)
            {
                float ringWidth = max(fwidth(normalizedAngle) * 2.0, 0.006);
                float spokeWidth = max(fwidth(azimuth / (2.0 * PI)) * 2.0, 0.003);
                float rings = Line(normalizedAngle * 6.0, ringWidth * 6.0);
                float spokes = Line(azimuth / (2.0 * PI) * 24.0, spokeWidth * 24.0);
                float outer = 1.0 - smoothstep(0.006, 0.016, abs(radius - 1.0));
                float center = 1.0 - smoothstep(0.0, 0.025, radius);

                float3 grid = float3(0.12, 0.65, 1.0) * saturate(max(rings, spokes));
                grid = max(grid, float3(outer, outer, outer));
                grid = max(grid, float3(1.0, 1.0, 1.0) * center);

                // Colored orientation marks just inside the horizon:
                // North red, East green, South blue, West yellow.
                float markerRadius = 1.0 - smoothstep(0.035, 0.06, abs(radius - 0.90));
                float north = 1.0 - smoothstep(0.025, 0.055, abs(sin(azimuth * 0.5)));
                float east = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI * 0.5) * 0.5)));
                float south = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI) * 0.5)));
                float west = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth + PI * 0.5) * 0.5)));
                grid = max(grid, markerRadius * (north * float3(1, 0.1, 0.1) + east * float3(0.1, 1, 0.1)
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

                float radius = length(p);
                if (radius > 1.0) return fixed4(0, 0, 0, 1);

                float normalizedAngle = saturate(radius + _Radial.x * radius * radius * radius
                                                       + _Radial.y * radius * radius * radius * radius * radius);
                float theta = normalizedAngle * PI * 0.5;
                float azimuth = atan2(p.x, p.y);
                float sinTheta = sin(theta);
                float3 direction = float3(sinTheta * sin(azimuth), cos(theta), sinTheta * cos(azimuth));
                direction = RotateDirection(direction, _Orientation);

                float longitude = atan2(direction.x, direction.z);
                float latitude = asin(clamp(direction.y, -1.0, 1.0));
                float2 videoUV = float2(frac(longitude / (2.0 * PI) + 0.5), 0.5 - latitude / PI);
                if (_InputFlip.x > 0.5) videoUV.x = 1.0 - videoUV.x;
                if (_InputFlip.y > 0.5) videoUV.y = 1.0 - videoUV.y;

                float3 videoColor = _HasVideo > 0.5 ? tex2D(_VideoTex, videoUV).rgb : float3(0, 0, 0);
                videoColor = pow(max(videoColor * _ColorAdjust.x, 0.0), 1.0 / max(_ColorAdjust.y, 0.001));

                float3 grid = CalibrationGrid(radius, azimuth, normalizedAngle);
                float gridAmount = max(grid.r, max(grid.g, grid.b));
                float3 color = videoColor;
                if (_GridMode > 1.5) color = float3(0.008, 0.012, 0.02) + grid;
                else if (_GridMode > 0.5) color = lerp(color, grid, saturate(gridAmount * 0.85));

                float mask = _EdgeFade > 0.0001 ? 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, radius) : 1.0;
                return fixed4(saturate(color) * mask, 1.0);
            }
            ENDCG
        }
    }
}

