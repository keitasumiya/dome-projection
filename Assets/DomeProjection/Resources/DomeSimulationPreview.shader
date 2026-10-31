Shader "Hidden/DomeProjection/DomeSimulationPreview"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        // The operator normally inspects the screen from outside while the
        // audience sees the same surface from inside.  Rendering both faces
        // keeps the material visible in both views and avoids the old
        // outside-only hemisphere.
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _HasTexture;
            float3 _Orientation;
            float2 _InputFlip;
            float _ContentMode;
            float _SourceAspect;
            float4 _CircularSource;
            float2 _CircularRadial;
            float4 _FlatPlacement;
            float _FlatHeightScale;
            float4 _FlatCrop;
            float2 _ColorAdjust;
            float _ProjectionRegionMode;
            float _DomeFov;
            float3 _ProjectorPosition;
            float3 _ProjectorAim;
            float _GuideStrength;
            float4 _GridSettings;
            float _GridLineWidth;
            static const float PI = 3.14159265358979323846;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 position : SV_POSITION; float3 world : TEXCOORD0; };
            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float3 RotateDirection(float3 direction, float3 degrees)
            {
                float3 angle = radians(degrees);
                float sy = sin(angle.x), cy = cos(angle.x);
                direction = float3(cy * direction.x + sy * direction.z,
                    direction.y, -sy * direction.x + cy * direction.z);
                float sp = sin(angle.y), cp = cos(angle.y);
                direction = float3(direction.x,
                    cp * direction.y - sp * direction.z,
                    sp * direction.y + cp * direction.z);
                float sr = sin(angle.z), cr = cos(angle.z);
                return float3(cr * direction.x - sr * direction.y,
                    sr * direction.x + cr * direction.y, direction.z);
            }

            float ProjectorCoverage(float3 surfacePoint)
            {
                if (_ProjectionRegionMode < 0.5)
                {
                    float polar = acos(clamp(surfacePoint.y, -1.0, 1.0));
                    return step(polar, radians(_DomeFov * 0.5));
                }
                float3 worldRay = normalize(surfacePoint - _ProjectorPosition);
                float yaw = radians(_ProjectorAim.x);
                float cy = cos(yaw), sy = sin(yaw);
                float3 unyawed = float3(cy * worldRay.x - sy * worldRay.z,
                    worldRay.y, sy * worldRay.x + cy * worldRay.z);
                float pitch = radians(_ProjectorAim.y);
                float cp = cos(pitch), sp = sin(pitch);
                float3 optical = normalize(float3(unyawed.x,
                    cp * unyawed.y - sp * unyawed.z,
                    sp * unyawed.y + cp * unyawed.z));
                float theta = acos(clamp(optical.z, -1.0, 1.0));
                float elevation = degrees(asin(clamp(surfacePoint.y, -1.0, 1.0)));
                return step(theta, radians(_DomeFov * 0.5))
                    * step(_ProjectorAim.z, elevation);
            }

            void HemisphereGuideCoordinates(float3 surfacePoint,
                out float domeRadius, out float azimuth, out float polarDegrees)
            {
                // Match Output's CalibrationGrid exactly: its rings and
                // spokes are defined by the physical dome direction after the
                // projector ray has intersected the sphere. They are not a
                // separate optical-axis grid.
                float3 direction = normalize(surfacePoint);
                polarDegrees = degrees(acos(clamp(direction.y, -1.0, 1.0)));
                azimuth = atan2(direction.x, direction.z);
                domeRadius = polarDegrees / 90.0;
            }

            float GuideLine(float value, float width)
            {
                float distanceToLine = abs(frac(value + 0.5) - 0.5);
                return 1.0 - smoothstep(width, width * 2.0, distanceToLine);
            }

            // Keep this in step with CalibrationGrid in the Output shader.
            // This is the real material-overlay hemisphere guide, not a
            // decorative wireframe belonging only to the 3D viewer.
            float3 HemisphereCalibrationGrid(float domeRadius, float azimuth,
                float polarDegrees)
            {
                float minorStep = max(_GridSettings.x, 1.0);
                float majorStep = max(_GridSettings.z, minorStep);
                float width = max(_GridLineWidth, 0.25);
                float ringMinorValue = polarDegrees / minorStep;
                float ringMajorValue = polarDegrees / majorStep;
                float azimuthDegrees = degrees(azimuth);
                float spokeMinorValue = azimuthDegrees / minorStep;
                float spokeMajorValue = azimuthDegrees / majorStep;
                float minorRings = GuideLine(ringMinorValue,
                    max(fwidth(ringMinorValue) * 0.35, 0.004) * width);
                float majorRings = GuideLine(ringMajorValue,
                    max(fwidth(ringMajorValue) * 0.50, 0.006) * width);
                float minorSpokes = GuideLine(spokeMinorValue,
                    max(fwidth(spokeMinorValue) * 0.35, 0.004) * width)
                                    * smoothstep(0.07, 0.16, domeRadius);
                float majorSpokes = GuideLine(spokeMajorValue,
                    max(fwidth(spokeMajorValue) * 0.50, 0.006) * width);
                float minorGrid = saturate(max(minorRings, minorSpokes));
                float majorGrid = saturate(max(majorRings, majorSpokes));
                float center = 1.0 - smoothstep(0.0, 0.025, domeRadius);
                float3 grid = float3(0.025, 0.12, 0.19) * minorGrid;
                grid = max(grid, float3(0.08, 0.46, 0.72) * majorGrid);
                grid = max(grid, float3(center, center, center));

                float markerRadius = 1.0 - smoothstep(0.035, 0.06,
                    abs(domeRadius - 0.90));
                float north = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin(azimuth * 0.5)));
                float east = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth - PI * 0.5) * 0.5)));
                float south = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth - PI) * 0.5)));
                float west = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth + PI * 0.5) * 0.5)));
                grid = max(grid, _GridSettings.w * markerRadius
                    * (north * float3(1, 0.1, 0.1)
                       + east * float3(0.1, 1, 0.1)
                       + south * float3(0.1, 0.3, 1)
                       + west * float3(1, 0.8, 0.1)));
                return saturate(grid);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float3 direction = normalize(input.world);
                float2 uv = 0.5;
                float contentMask = 1.0;
                if (_ContentMode > 1.5)
                {
                    float azimuth = atan2(direction.x, direction.z);
                    float polar = degrees(acos(clamp(direction.y, -1.0, 1.0)));
                    float2 domePoint = float2(sin(azimuth), cos(azimuth)) * (polar / 90.0);
                    float rotation = radians(_CircularSource.w);
                    float cs = cos(rotation), sn = sin(rotation);
                    float2 sourcePoint = float2(cs * domePoint.x - sn * domePoint.y,
                        sn * domePoint.x + cs * domePoint.y);
                    float radius2 = dot(sourcePoint, sourcePoint);
                    sourcePoint *= 1.0 + _CircularRadial.x * radius2
                        + _CircularRadial.y * radius2 * radius2;
                    float aspect = max(_SourceAspect, 0.0001);
                    float2 sourceRadius = aspect >= 1.0
                        ? float2(0.5 / aspect, 0.5) : float2(0.5, 0.5 * aspect);
                    uv = _CircularSource.xy + sourcePoint * sourceRadius * _CircularSource.z;
                    contentMask = step(length(sourcePoint), 1.0);
                }
                else if (_ContentMode > 0.5)
                {
                    float azimuthCenter = radians(_FlatPlacement.x);
                    float polarCenter = radians(_FlatPlacement.y);
                    float3 panelCenter = float3(sin(polarCenter) * sin(azimuthCenter),
                        cos(polarCenter), sin(polarCenter) * cos(azimuthCenter));
                    float3 panelRight = float3(cos(azimuthCenter), 0.0, -sin(azimuthCenter));
                    float3 panelUp = normalize(cross(panelRight, panelCenter));
                    float roll = radians(_FlatPlacement.z);
                    float cs = cos(roll), sn = sin(roll);
                    float3 rolledRight = panelRight * cs + panelUp * sn;
                    float3 rolledUp = -panelRight * sn + panelUp * cs;
                    float denominator = dot(direction, panelCenter);
                    float3 hit = direction / max(denominator, 0.0001);
                    float3 offset = hit - panelCenter;
                    float halfWidth = tan(radians(_FlatPlacement.w * 0.5));
                    float halfHeight = halfWidth / max(_SourceAspect, 0.01) * _FlatHeightScale;
                    uv = float2(dot(offset, rolledRight) / max(halfWidth * 2.0, 0.0001) + 0.5,
                        dot(offset, rolledUp) / max(halfHeight * 2.0, 0.0001) + 0.5);
                    contentMask = step(0.0001, denominator) * step(0.0, uv.x)
                        * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                    uv = float2(lerp(_FlatCrop.x, 1.0 - _FlatCrop.y, uv.x),
                        lerp(_FlatCrop.z, 1.0 - _FlatCrop.w, uv.y));
                }
                else
                {
                    direction = RotateDirection(direction, _Orientation);
                    uv = float2(frac(atan2(direction.x, direction.z) / (2.0 * PI) + 0.5),
                        0.5 - asin(clamp(direction.y, -1.0, 1.0)) / PI);
                }
                if (_InputFlip.x > 0.5) uv.x = 1.0 - uv.x;
                if (_InputFlip.y > 0.5) uv.y = 1.0 - uv.y;
                float coverage = ProjectorCoverage(normalize(input.world));
                float3 sourceColor = _HasTexture > 0.5
                    ? tex2D(_MainTex, uv).rgb * contentMask : float3(0.10, 0.14, 0.18);
                sourceColor = pow(max(sourceColor * _ColorAdjust.x, 0.0),
                    1.0 / max(_ColorAdjust.y, 0.001));
                // Keep the source legible from the outside of the dome as a
                // contextual preview.  The physically illuminated footprint
                // remains full brightness, while the rest of the shell is
                // deliberately dimmer so it cannot be mistaken for coverage.
                float3 outsidePreview = lerp(float3(0.025, 0.035, 0.05),
                    sourceColor, 0.42);
                float3 color = lerp(outsidePreview, sourceColor, coverage);
                float azimuth, polarDegrees, domeRadius;
                HemisphereGuideCoordinates(normalize(input.world), domeRadius,
                    azimuth, polarDegrees);
                // The same guide must remain visible on both the inner and
                // outer wall. Outside the real footprint it is shown faintly;
                // inside the footprint it retains the Output brightness.
                float guideVisibility = lerp(0.12, 1.0, coverage);
                float3 guide = HemisphereCalibrationGrid(domeRadius, azimuth,
                    polarDegrees) * guideVisibility * _GuideStrength;
                float guideAmount = max(guide.r, max(guide.g, guide.b));
                color = lerp(color, guide,
                    saturate(guideAmount * _GridSettings.y));
                return fixed4(saturate(color), 1.0);
            }
            ENDCG
        }

        // Diagnostic x-ray overlay. The normal pass above keeps physically
        // correct depth occlusion, while this pass draws only guide fragments
        // that are behind the nearest dome surface. During setup this makes
        // the otherwise hidden outside wall traceable without turning the
        // whole shell transparent.
        Pass
        {
            Cull Off ZWrite Off ZTest Greater
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vertXray
            #pragma fragment fragXray
            #include "UnityCG.cginc"

            float _ProjectionRegionMode;
            float _DomeFov;
            float3 _ProjectorPosition;
            float3 _ProjectorAim;
            float _GuideStrength;
            float4 _GridSettings;
            float _GridLineWidth;
            static const float XRAY_PI = 3.14159265358979323846;

            struct xrayAppdata { float4 vertex : POSITION; };
            struct xrayV2f { float4 position : SV_POSITION; float3 world : TEXCOORD0; };

            xrayV2f vertXray(xrayAppdata input)
            {
                xrayV2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                return output;
            }

            float XrayProjectorCoverage(float3 surfacePoint)
            {
                if (_ProjectionRegionMode < 0.5)
                {
                    float polar = acos(clamp(surfacePoint.y, -1.0, 1.0));
                    return step(polar, radians(_DomeFov * 0.5));
                }
                float3 worldRay = normalize(surfacePoint - _ProjectorPosition);
                float yaw = radians(_ProjectorAim.x);
                float cy = cos(yaw), sy = sin(yaw);
                float3 unyawed = float3(cy * worldRay.x - sy * worldRay.z,
                    worldRay.y, sy * worldRay.x + cy * worldRay.z);
                float pitch = radians(_ProjectorAim.y);
                float cp = cos(pitch), sp = sin(pitch);
                float3 optical = normalize(float3(unyawed.x,
                    cp * unyawed.y - sp * unyawed.z,
                    sp * unyawed.y + cp * unyawed.z));
                float theta = acos(clamp(optical.z, -1.0, 1.0));
                float elevation = degrees(asin(clamp(surfacePoint.y, -1.0, 1.0)));
                return step(theta, radians(_DomeFov * 0.5))
                    * step(_ProjectorAim.z, elevation);
            }

            float XrayGuideLine(float value, float width)
            {
                float distanceToLine = abs(frac(value + 0.5) - 0.5);
                return 1.0 - smoothstep(width, width * 2.0, distanceToLine);
            }

            float3 XrayHemisphereGrid(float3 surfacePoint)
            {
                float3 direction = normalize(surfacePoint);
                float polarDegrees = degrees(acos(clamp(direction.y, -1.0, 1.0)));
                float azimuth = atan2(direction.x, direction.z);
                float domeRadius = polarDegrees / 90.0;
                float minorStep = max(_GridSettings.x, 1.0);
                float majorStep = max(_GridSettings.z, minorStep);
                float width = max(_GridLineWidth, 0.25);
                float ringMinorValue = polarDegrees / minorStep;
                float ringMajorValue = polarDegrees / majorStep;
                float azimuthDegrees = degrees(azimuth);
                float spokeMinorValue = azimuthDegrees / minorStep;
                float spokeMajorValue = azimuthDegrees / majorStep;
                float minorRings = XrayGuideLine(ringMinorValue,
                    max(fwidth(ringMinorValue) * 0.35, 0.004) * width);
                float majorRings = XrayGuideLine(ringMajorValue,
                    max(fwidth(ringMajorValue) * 0.50, 0.006) * width);
                float minorSpokes = XrayGuideLine(spokeMinorValue,
                    max(fwidth(spokeMinorValue) * 0.35, 0.004) * width)
                    * smoothstep(0.07, 0.16, domeRadius);
                float majorSpokes = XrayGuideLine(spokeMajorValue,
                    max(fwidth(spokeMajorValue) * 0.50, 0.006) * width);
                float minorGrid = saturate(max(minorRings, minorSpokes));
                float majorGrid = saturate(max(majorRings, majorSpokes));
                float center = 1.0 - smoothstep(0.0, 0.025, domeRadius);
                float3 grid = float3(0.025, 0.12, 0.19) * minorGrid;
                grid = max(grid, float3(0.08, 0.46, 0.72) * majorGrid);
                grid = max(grid, float3(center, center, center));

                float markerRadius = 1.0 - smoothstep(0.035, 0.06,
                    abs(domeRadius - 0.90));
                float north = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin(azimuth * 0.5)));
                float east = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth - XRAY_PI * 0.5) * 0.5)));
                float south = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth - XRAY_PI) * 0.5)));
                float west = 1.0 - smoothstep(0.025, 0.055,
                    abs(sin((azimuth + XRAY_PI * 0.5) * 0.5)));
                grid = max(grid, _GridSettings.w * markerRadius
                    * (north * float3(1, 0.1, 0.1)
                       + east * float3(0.1, 1, 0.1)
                       + south * float3(0.1, 0.3, 1)
                       + west * float3(1, 0.8, 0.1)));
                return saturate(grid);
            }

            fixed4 fragXray(xrayV2f input) : SV_Target
            {
                float coverage = XrayProjectorCoverage(normalize(input.world));
                float visibility = lerp(0.10, 1.0, coverage);
                float3 guide = XrayHemisphereGrid(input.world)
                    * _GuideStrength * visibility;
                float amount = max(guide.r, max(guide.g, guide.b));
                clip(amount - 0.01);
                float alpha = amount * lerp(0.16, 0.68, coverage);
                return fixed4(guide, saturate(alpha));
            }
            ENDCG
        }
    }
}
