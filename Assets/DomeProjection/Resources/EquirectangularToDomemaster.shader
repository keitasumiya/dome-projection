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
            float _ProjectionRegionMode;
            float4 _OffAxisProjection;
            float2 _OffAxisHorizontal;
            float4 _RoomDimensions;
            float4 _RoomProjectorPosition;
            float4 _RoomProjectorRotation;
            float4 _RoomViewerPosition;
            float4 _RoomFaces;
            float _RoomFloor;
            float4 _RoomReferenceTop;
            float4 _RoomReferenceBottom;
            float4 _RoomWarpTop;
            float4 _RoomWarpBottom;
            // Projective transform from the adjusted raster back to the
            // unadjusted projector raster. Unlike inverse bilinear iteration,
            // this remains exact at zero adjustment and does not diverge when
            // evaluating pixels outside the selected room-face quadrilateral.
            float3 _RoomWarpH0;
            float3 _RoomWarpH1;
            float3 _RoomWarpH2;
            // x = scale, y = screen roll, z = horizontal view rotation,
            // w = vertical view rotation. Control process only.
            float4 _ControlPreview;
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
            float4 _CircularSource;
            float2 _CircularRadial;
            float _ContentOpacity;

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

            float2 BilinearQuad(float4 topCorners, float4 bottomCorners, float2 uv)
            {
                float2 bottom = lerp(bottomCorners.xy, bottomCorners.zw, uv.x);
                float2 top = lerp(topCorners.xy, topCorners.zw, uv.x);
                return lerp(bottom, top, uv.y);
            }

            float2 InverseBilinearRoomWarp(float2 target)
            {
                float2 bottomLeft = _RoomWarpBottom.xy;
                float2 bottomRight = _RoomWarpBottom.zw;
                float2 topLeft = _RoomWarpTop.xy;
                float2 topRight = _RoomWarpTop.zw;
                float2 minimum = min(min(bottomLeft, bottomRight), min(topLeft, topRight));
                float2 maximum = max(max(bottomLeft, bottomRight), max(topLeft, topRight));
                float2 uv = (target - minimum) / max(maximum - minimum, float2(0.0001, 0.0001));
                // Newton iteration is inexpensive here and handles a general
                // convex four-point warp. UV intentionally remains unbounded:
                // pixels outside the front-wall quad are the adjacent walls,
                // ceiling and floor, not a black clipped border.
                [unroll]
                for (int iteration = 0; iteration < 8; iteration++)
                {
                    float2 bottom = lerp(bottomLeft, bottomRight, uv.x);
                    float2 top = lerp(topLeft, topRight, uv.x);
                    float2 mapped = lerp(bottom, top, uv.y);
                    float2 derivativeU = lerp(bottomRight - bottomLeft,
                        topRight - topLeft, uv.y);
                    float2 derivativeV = top - bottom;
                    float determinant = derivativeU.x * derivativeV.y
                        - derivativeU.y * derivativeV.x;
                    if (abs(determinant) < 0.000001) break;
                    float2 error = mapped - target;
                    float2 correction = float2(
                        (error.x * derivativeV.y - error.y * derivativeV.x) / determinant,
                        (derivativeU.x * error.y - derivativeU.y * error.x) / determinant);
                    uv -= correction;
                }
                return uv;
            }

            float2 ApplyRoomHomography(float2 target)
            {
                float3 homogeneous = float3(target, 1.0);
                float denominator = dot(_RoomWarpH2, homogeneous);
                if (abs(denominator) < 0.00001) return target;
                return float2(dot(_RoomWarpH0, homogeneous), dot(_RoomWarpH1, homogeneous))
                    / denominator;
            }

            float3 CalibrationGrid(float outputRadius, float domeRadius, float azimuth, float angleDegrees)
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
                minorSpokes *= smoothstep(0.07, 0.16, domeRadius);
                float minorGrid = saturate(max(minorRings, minorSpokes));
                float majorGrid = saturate(max(majorRings, majorSpokes));
                float outer = 1.0 - smoothstep(0.006, 0.016, abs(outputRadius - 1.0));
                float center = 1.0 - smoothstep(0.0, 0.025, domeRadius);

                float3 grid = float3(0.025, 0.12, 0.19) * minorGrid;
                grid = max(grid, float3(0.08, 0.46, 0.72) * majorGrid);
                grid = max(grid, float3(outer, outer, outer));
                grid = max(grid, float3(1.0, 1.0, 1.0) * center);

                // Colored orientation marks just inside the horizon:
                // North red, East green, South blue, West yellow.
                float markerRadius = 1.0 - smoothstep(0.035, 0.06, abs(domeRadius - 0.90));
                float north = 1.0 - smoothstep(0.025, 0.055, abs(sin(azimuth * 0.5)));
                float east = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI * 0.5) * 0.5)));
                float south = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth - PI) * 0.5)));
                float west = 1.0 - smoothstep(0.025, 0.055, abs(sin((azimuth + PI * 0.5) * 0.5)));
                grid = max(grid, _GridSettings.w * markerRadius * (north * float3(1, 0.1, 0.1) + east * float3(0.1, 1, 0.1)
                                                                  + south * float3(0.1, 0.3, 1) + west * float3(1, 0.8, 0.1)));
                return saturate(grid);
            }

            float3 RoomCalibrationGrid(float3 hit, float face)
            {
                float spacing = max(_RoomDimensions.w, 0.01);
                float2 coordinates = face < 0.5 ? hit.xy
                    : face < 1.5 ? hit.zy
                    : hit.xz;
                float2 minorValue = coordinates / spacing;
                float2 majorValue = coordinates;
                float lineWidth = max(_GridAppearance.x, 0.25);
                float minorX = Line(minorValue.x,
                    max(fwidth(minorValue.x) * 0.45, 0.004) * lineWidth);
                float minorY = Line(minorValue.y,
                    max(fwidth(minorValue.y) * 0.45, 0.004) * lineWidth);
                float majorX = Line(majorValue.x,
                    max(fwidth(majorValue.x) * 0.60, 0.006) * lineWidth);
                float majorY = Line(majorValue.y,
                    max(fwidth(majorValue.y) * 0.60, 0.006) * lineWidth);
                float minorGrid = saturate(max(minorX, minorY));
                float majorGrid = saturate(max(majorX, majorY));

                // Different tints make it obvious which physical surface each
                // ray reached while white metre lines remain continuous.
                float3 faceColor = face < 0.5 ? float3(0.08, 0.50, 0.82)
                    : face < 1.5 ? float3(0.88, 0.36, 0.08)
                    : face < 2.5 ? float3(0.52, 0.24, 0.82)
                    : float3(0.20, 0.62, 0.42);
                float3 grid = faceColor * 0.42 * minorGrid;
                grid = max(grid, float3(0.88, 0.92, 1.0) * majorGrid);

                // Box edges use the conventional axis colors. An edge running
                // along X is red, Y is green, and Z is blue. This remains easy
                // to identify even when two room corners are in the picture.
                float edgeWidth = max(max(fwidth(hit.x), max(fwidth(hit.y), fwidth(hit.z)))
                    * (1.2 + lineWidth * 0.35), 0.002);
                float edgeX = 0.0;
                float edgeY = 0.0;
                float edgeZ = 0.0;
                float halfWidth = _RoomDimensions.x * 0.5;
                if (face < 0.5)
                {
                    edgeY = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.x - halfWidth)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.x + halfWidth)));
                    edgeX = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0, abs(hit.y)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.y - _RoomDimensions.y)));
                }
                else if (face < 1.5)
                {
                    edgeY = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0, abs(hit.z)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.z - _RoomDimensions.z)));
                    edgeZ = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0, abs(hit.y)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.y - _RoomDimensions.y)));
                }
                else
                {
                    edgeZ = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.x - halfWidth)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.x + halfWidth)));
                    edgeX = max(1.0 - smoothstep(edgeWidth, edgeWidth * 2.0, abs(hit.z)),
                        1.0 - smoothstep(edgeWidth, edgeWidth * 2.0,
                            abs(hit.z - _RoomDimensions.z)));
                }
                grid = max(grid, edgeX * float3(1.0, 0.08, 0.08));
                grid = max(grid, edgeY * float3(0.08, 1.0, 0.16));
                grid = max(grid, edgeZ * float3(0.10, 0.35, 1.0));
                return saturate(grid);
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                // Control-only inspection transform. Its inverse is applied
                // before projection so the complete output raster can be
                // zoomed and rotated without changing the Output process.
                float previewScale = max(_ControlPreview.x, 0.1);
                float previewAngle = radians(_ControlPreview.y);
                float previewCos = cos(previewAngle);
                float previewSin = sin(previewAngle);
                float2 previewOffset = input.uv - 0.5;
                previewOffset.x *= max(_OutputAspect, 0.01);
                previewOffset /= previewScale;
                previewOffset = float2(
                    previewCos * previewOffset.x + previewSin * previewOffset.y,
                    -previewSin * previewOffset.x + previewCos * previewOffset.y);
                // Treat the output raster as a virtual 90-degree view plane.
                // Undo Control's vertical/horizontal inspection rotations so
                // ceiling and side-wall areas can be brought to the centre
                // without altering the actual projector output.
                float3 previewRay = float3(previewOffset, 0.5);
                float previewPitch = radians(_ControlPreview.w);
                float pitchCos = cos(previewPitch);
                float pitchSin = sin(previewPitch);
                previewRay.yz = float2(
                    pitchCos * previewRay.y + pitchSin * previewRay.z,
                    -pitchSin * previewRay.y + pitchCos * previewRay.z);
                float previewYaw = radians(_ControlPreview.z);
                float yawCos = cos(previewYaw);
                float yawSin = sin(previewYaw);
                previewRay.xz = float2(
                    yawCos * previewRay.x + yawSin * previewRay.z,
                    -yawSin * previewRay.x + yawCos * previewRay.z);
                if (previewRay.z <= 0.0001) return fixed4(0, 0, 0, 1);
                previewOffset = previewRay.xy * (0.5 / previewRay.z);
                previewOffset.x /= max(_OutputAspect, 0.01);
                float2 previewUV = previewOffset + 0.5;
                if (any(previewUV < 0.0) || any(previewUV > 1.0))
                    return fixed4(0, 0, 0, 1);
                float2 projectionUV = previewUV;
                if (_ProjectionRegionMode > 1.5)
                    projectionUV = ApplyRoomHomography(previewUV);
                float2 p = projectionUV - _Center;
                p.x *= _OutputAspect;
                p /= max(_Radius * _Ellipse, float2(0.0001, 0.0001));

                float rotation = radians(_OutputRotation);
                float cs = cos(rotation);
                float sn = sin(rotation);
                p = float2(cs * p.x - sn * p.y, sn * p.x + cs * p.y);

                float outputRadius = length(p);
                // Dome modes use a circular conversion-lens footprint. The
                // room mode uses the complete rectangular projector raster.
                if (_ProjectionRegionMode < 1.5 && outputRadius > 1.0)
                    return fixed4(0, 0, 0, 1);

                float2 calibrationPoint = _ProjectionRegionMode > 1.5
                    ? float2(p.x / max(_OutputAspect, 0.01), p.y)
                    : p;
                float directionalScale = 1.0
                    + _DirectionalWarp.x * pow(max(calibrationPoint.y, 0.0), 2.0)
                    + _DirectionalWarp.y * pow(max(-calibrationPoint.y, 0.0), 2.0)
                    + _DirectionalWarp.z * pow(max(-calibrationPoint.x, 0.0), 2.0)
                    + _DirectionalWarp.w * pow(max(calibrationPoint.x, 0.0), 2.0);
                float2 shapedPoint = calibrationPoint * directionalScale;
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
                float domeAzimuth = azimuth;
                float domePolarDegrees = degrees(theta);
                // Normalized physical polar distance: 1 is the horizon, 2 is
                // the south pole. This keeps grid landmarks meaningful when
                // centre projection is widened beyond 180 degrees.
                float domeRadius = domePolarDegrees / 90.0;
                float3 roomHit = float3(0.0, 0.0, 0.0);
                float roomFace = -1.0;

                if (_ProjectionRegionMode > 1.5)
                {
                    // Rectilinear projector ray. The existing centre, ellipse,
                    // rotation and lens-correction controls are retained, but
                    // the circular mask is disabled for this room geometry.
                    // Each app uses its own drawable aspect. Control may be a
                    // wide ordinary window while Output is a different
                    // fullscreen monitor; neither is forced to mimic the other.
                    float aspect = max(_OutputAspect, 0.01);
                    float tangentHalfHorizontal = tan(radians(_RoomProjectorRotation.w * 0.5));
                    float2 roomPoint = lensPoint;
                    float3 ray = normalize(float3(
                        roomPoint.x * tangentHalfHorizontal,
                        roomPoint.y * tangentHalfHorizontal / aspect,
                        1.0));

                    float roll = radians(_RoomProjectorRotation.z);
                    float rollCos = cos(roll);
                    float rollSin = sin(roll);
                    ray.xy = float2(rollCos * ray.x - rollSin * ray.y,
                                    rollSin * ray.x + rollCos * ray.y);
                    float pitch = radians(_RoomProjectorRotation.y);
                    float pitchCos = cos(pitch);
                    float pitchSin = sin(pitch);
                    ray.yz = float2(pitchCos * ray.y + pitchSin * ray.z,
                                   -pitchSin * ray.y + pitchCos * ray.z);
                    float yaw = radians(_RoomProjectorRotation.x);
                    float yawCos = cos(yaw);
                    float yawSin = sin(yaw);
                    ray.xz = float2(yawCos * ray.x + yawSin * ray.z,
                                   -yawSin * ray.x + yawCos * ray.z);
                    ray = normalize(ray);

                    float3 projectorPosition = _RoomProjectorPosition.xyz;
                    float halfWidth = _RoomDimensions.x * 0.5;
                    float roomHeight = _RoomDimensions.y;
                    float roomDepth = _RoomDimensions.z;
                    float closestDistance = 1e20;

                    // Front wall: z = room depth.
                    if (_RoomFaces.x > 0.5 && ray.z > 0.0001)
                    {
                        float distance = (roomDepth - projectorPosition.z) / ray.z;
                        float3 hit = projectorPosition + ray * distance;
                        if (distance > 0.0001 && abs(hit.x) <= halfWidth
                            && hit.y >= 0.0 && hit.y <= roomHeight)
                        {
                            closestDistance = distance;
                            roomHit = hit;
                            roomFace = 0.0;
                        }
                    }

                    // Side walls: w=+1 selects right, -1 selects left, and
                    // zero enables both front corners.
                    if (_RoomFaces.y > 0.5 && _RoomFaces.w >= 0.0 && ray.x > 0.0001)
                    {
                        float distance = (halfWidth - projectorPosition.x) / ray.x;
                        float3 hit = projectorPosition + ray * distance;
                        if (distance > 0.0001 && distance < closestDistance
                            && hit.z >= 0.0 && hit.z <= roomDepth
                            && hit.y >= 0.0 && hit.y <= roomHeight)
                        {
                            closestDistance = distance;
                            roomHit = hit;
                            roomFace = 1.0;
                        }
                    }
                    if (_RoomFaces.y > 0.5 && _RoomFaces.w <= 0.0 && ray.x < -0.0001)
                    {
                        float distance = (-halfWidth - projectorPosition.x) / ray.x;
                        float3 hit = projectorPosition + ray * distance;
                        if (distance > 0.0001 && distance < closestDistance
                            && hit.z >= 0.0 && hit.z <= roomDepth
                            && hit.y >= 0.0 && hit.y <= roomHeight)
                        {
                            closestDistance = distance;
                            roomHit = hit;
                            roomFace = 1.0;
                        }
                    }

                    // Ceiling: y = room height.
                    if (_RoomFaces.z > 0.5 && ray.y > 0.0001)
                    {
                        float distance = (roomHeight - projectorPosition.y) / ray.y;
                        float3 hit = projectorPosition + ray * distance;
                        if (distance > 0.0001 && distance < closestDistance
                            && abs(hit.x) <= halfWidth
                            && hit.z >= 0.0 && hit.z <= roomDepth)
                        {
                            closestDistance = distance;
                            roomHit = hit;
                            roomFace = 2.0;
                        }
                    }

                    // Floor: required for the negative-elevation half of a
                    // true 2:1 360-degree source. Circular all-sky sources do
                    // not normally contain this lower hemisphere.
                    if (_RoomFloor > 0.5 && ray.y < -0.0001)
                    {
                        float distance = -projectorPosition.y / ray.y;
                        float3 hit = projectorPosition + ray * distance;
                        if (distance > 0.0001 && distance < closestDistance
                            && abs(hit.x) <= halfWidth
                            && hit.z >= 0.0 && hit.z <= roomDepth)
                        {
                            closestDistance = distance;
                            roomHit = hit;
                            roomFace = 3.0;
                        }
                    }

                    if (roomFace < -0.5) return fixed4(0, 0, 0, 1);
                    direction = normalize(roomHit - _RoomViewerPosition.xyz);
                    domeAzimuth = atan2(direction.x, direction.z);
                    domePolarDegrees = degrees(acos(clamp(direction.y, -1.0, 1.0)));
                    domeRadius = domePolarDegrees / 90.0;
                }
                else if (_ProjectionRegionMode > 0.5)
                {
                    // Projector P sits near the rear rim and looks toward +Z.
                    // The fisheye ray is tilted upward, then intersected with
                    // the unit dome. Projector-image Y becomes physical height.
                    float aim = radians(_OffAxisProjection.z);
                    float aimCos = cos(aim);
                    float aimSin = sin(aim);
                    float3 opticalRay = float3(sinTheta * sin(azimuth),
                        sinTheta * cos(azimuth), cos(theta));
                    float3 pitchedRay = float3(opticalRay.x,
                        aimCos * opticalRay.y + aimSin * opticalRay.z,
                        -aimSin * opticalRay.y + aimCos * opticalRay.z);
                    float yaw = radians(_OffAxisHorizontal.y);
                    float yawCos = cos(yaw);
                    float yawSin = sin(yaw);
                    float3 ray = normalize(float3(
                        yawCos * pitchedRay.x + yawSin * pitchedRay.z,
                        pitchedRay.y,
                        -yawSin * pitchedRay.x + yawCos * pitchedRay.z));
                    float3 projectorPosition = float3(_OffAxisHorizontal.x,
                        _OffAxisProjection.y, -_OffAxisProjection.x);
                    float projected = dot(projectorPosition, ray);
                    float discriminant = projected * projected
                        - (dot(projectorPosition, projectorPosition) - 1.0);
                    if (discriminant < 0.0) return fixed4(0, 0, 0, 1);
                    float root = sqrt(discriminant);
                    // The projector can sit slightly outside the dome radius.
                    // In that case the near root is the unwanted near-side
                    // sphere surface; projection is intended for the opposite,
                    // inner-facing dome surface, so always use the far root.
                    float hitDistance = -projected + root;
                    if (hitDistance <= 0.0001) return fixed4(0, 0, 0, 1);
                    direction = normalize(projectorPosition + ray * hitDistance);
                    // Optionally retain a small area below the nominal horizon.
                    // -20 degrees matches the lower extent of a 220-degree
                    // zenith-centred projection.
                    float minimumHeight = sin(radians(_OffAxisProjection.w));
                    if (direction.y < minimumHeight) return fixed4(0, 0, 0, 1);
                    domeAzimuth = atan2(direction.x, direction.z);
                    domePolarDegrees = degrees(acos(clamp(direction.y, -1.0, 1.0)));
                    domeRadius = domePolarDegrees / 90.0;
                }

                float3 backgroundDirection = RotateDirection(direction, _Orientation);
                float2 backgroundUV = float2(frac(atan2(backgroundDirection.x, backgroundDirection.z) / (2.0 * PI) + 0.5),
                                             0.5 - asin(clamp(backgroundDirection.y, -1.0, 1.0)) / PI);
                if (_InputFlip.x > 0.5) backgroundUV.x = 1.0 - backgroundUV.x;
                if (_InputFlip.y > 0.5) backgroundUV.y = 1.0 - backgroundUV.y;
                float2 videoUV;
                float contentMask = 1.0;
                if (_ContentMode > 1.5)
                {
                    // The source is already an azimuthal/equidistant all-sky
                    // circle. Map the calibrated output circle directly into
                    // the largest true pixel circle in the source frame.
                    float sourceAspect = max(_SourceAspect, 0.0001);
                    float2 sourceRadius = sourceAspect >= 1.0
                        ? float2(0.5 / sourceAspect, 0.5)
                        : float2(0.5, 0.5 * sourceAspect);
                    float sourceRotation = radians(_CircularSource.w);
                    float sourceCos = cos(sourceRotation);
                    float sourceSin = sin(sourceRotation);
                    float2 domePoint = _ProjectionRegionMode > 0.5
                        ? float2(sin(domeAzimuth), cos(domeAzimuth)) * domeRadius
                        : lensPoint;
                    float2 sourcePoint = float2(
                        sourceCos * domePoint.x - sourceSin * domePoint.y,
                        sourceSin * domePoint.x + sourceCos * domePoint.y);
                    float sourceR2 = dot(sourcePoint, sourcePoint);
                    sourcePoint *= 1.0 + _CircularRadial.x * sourceR2
                        + _CircularRadial.y * sourceR2 * sourceR2;
                    videoUV = _CircularSource.xy
                        + sourcePoint * sourceRadius * _CircularSource.z;
                    contentMask = step(length(sourcePoint), 1.0)
                        * step(0.0, videoUV.x) * step(videoUV.x, 1.0)
                        * step(0.0, videoUV.y) * step(videoUV.y, 1.0);
                }
                else if (_ContentMode > 0.5)
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

                float3 grid = _ProjectionRegionMode > 1.5
                    ? RoomCalibrationGrid(roomHit, roomFace)
                    : CalibrationGrid(outputRadius, domeRadius,
                        domeAzimuth, domePolarDegrees);
                float gridAmount = max(grid.r, max(grid.g, grid.b));
                float3 color = videoColor;
                if (_GridMode > 1.5) color = float3(0.008, 0.012, 0.02) + grid;
                else if (_GridMode > 0.5) color = lerp(color, grid, saturate(gridAmount * _GridSettings.y));

                float mask = _ProjectionRegionMode > 1.5 ? 1.0
                    : _EdgeFade > 0.0001 ? 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, outputRadius) : 1.0;
                return fixed4(saturate(color) * mask * saturate(_ContentOpacity), 1.0);
            }
            ENDCG
        }
    }
}

