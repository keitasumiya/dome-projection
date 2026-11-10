Shader "Hidden/DomeProjection/RoomContentPreview"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _HasTexture;
            float3 _ObserverPosition;
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
            float _GridSpacing;
            float4 _FaceVisibility;
            float _FloorVisible;
            float _GuideStrength;
            static const float PI = 3.14159265358979323846;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 face : TEXCOORD1;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float face : TEXCOORD1;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.face = input.face.x;
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

            float GridLine(float coordinate)
            {
                float value = coordinate / max(_GridSpacing, 0.01);
                float distanceToLine = abs(frac(value + 0.5) - 0.5);
                float width = max(fwidth(value) * 0.8, 0.006);
                return 1.0 - smoothstep(width, width * 1.8, distanceToLine);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float face = floor(input.face + 0.5);
                if ((face < 0.5 && _FaceVisibility.x < 0.5)
                    || (face > 0.5 && face < 1.5 && _FaceVisibility.y < 0.5)
                    || (face > 1.5 && face < 2.5 && _FaceVisibility.z < 0.5)
                    || (face > 2.5 && face < 3.5 && _FaceVisibility.w < 0.5)
                    || (face > 3.5 && _FloorVisible < 0.5)) discard;
                float3 direction = normalize(input.worldPosition - _ObserverPosition);
                float2 uv = 0.5;
                float mask = 1.0;

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
                        ? float2(0.5 / aspect, 0.5)
                        : float2(0.5, 0.5 * aspect);
                    uv = _CircularSource.xy + sourcePoint * sourceRadius * _CircularSource.z;
                    mask = step(length(sourcePoint), 1.0)
                        * step(0.0, uv.x) * step(uv.x, 1.0)
                        * step(0.0, uv.y) * step(uv.y, 1.0);
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
                    mask = step(0.0001, denominator);
                    float3 hit = direction / max(denominator, 0.0001);
                    float3 offset = hit - panelCenter;
                    float halfWidth = tan(radians(_FlatPlacement.w * 0.5));
                    float halfHeight = halfWidth / max(_SourceAspect, 0.01)
                        * _FlatHeightScale;
                    uv = float2(dot(offset, rolledRight) / max(halfWidth * 2.0, 0.0001) + 0.5,
                        dot(offset, rolledUp) / max(halfHeight * 2.0, 0.0001) + 0.5);
                    mask *= step(0.0, uv.x) * step(uv.x, 1.0)
                        * step(0.0, uv.y) * step(uv.y, 1.0);
                    uv = float2(lerp(_FlatCrop.x, 1.0 - _FlatCrop.y, uv.x),
                        lerp(_FlatCrop.z, 1.0 - _FlatCrop.w, uv.y));
                }
                else
                {
                    direction = RotateDirection(direction, _Orientation);
                    uv = float2(frac(0.5 - atan2(direction.x, direction.z) / (2.0 * PI)),
                        0.5 - asin(clamp(direction.y, -1.0, 1.0)) / PI);
                }

                if (_InputFlip.x > 0.5) uv.x = 1.0 - uv.x;
                if (_InputFlip.y > 0.5) uv.y = 1.0 - uv.y;
                float3 color = _HasTexture > 0.5 ? tex2D(_MainTex, uv).rgb * mask : 0.025;
                color = pow(max(color * _ColorAdjust.x, 0.0),
                    1.0 / max(_ColorAdjust.y, 0.001));

                float grid = face < 0.5
                    ? max(GridLine(input.worldPosition.x), GridLine(input.worldPosition.y))
                    : face < 2.5
                        ? max(GridLine(input.worldPosition.z), GridLine(input.worldPosition.y))
                        : max(GridLine(input.worldPosition.x), GridLine(input.worldPosition.z));
                float3 faceTint = face < 0.5 ? float3(0.08, 0.60, 0.82)
                    : face < 2.5 ? float3(0.92, 0.52, 0.10)
                    : face < 3.5 ? float3(0.52, 0.24, 0.78)
                    : float3(0.12, 0.66, 0.34);
                // The material is the subject of this preview. Face tint and
                // grid are optional diagnostics and must never hide it.
                float baseTint = _HasTexture > 0.5 ? 0.0 : 0.06;
                color = lerp(color, faceTint, baseTint);
                color = lerp(color, faceTint, grid * 0.22 * _GuideStrength);
                return fixed4(saturate(color), 1.0);
            }
            ENDCG
        }
    }
}
