Shader "Hidden/Reprojection"
{
    HLSLINCLUDE
    
    #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/StdLib.hlsl"
    #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Colors.hlsl"

    #define SAMPLE_TEX2D(name, uv) SAMPLE_TEXTURE2D(name, sampler##name, uv)
    #define SAMPLE_DEPTH(name, uv) SAMPLE_DEPTH_TEXTURE(name, sampler##name, uv).x

    TEXTURE2D_SAMPLER2D(_MainTex, sampler_MainTex);
    TEXTURE2D_SAMPLER2D(_CameraDepthTexture, sampler_CameraDepthTexture);
    TEXTURE2D_SAMPLER2D(_CameraMotionVectorsTexture, sampler_CameraMotionVectorsTexture);

    TEXTURE2D_SAMPLER2D(_PreviousColorTexture, sampler_PreviousColorTexture);
    TEXTURE2D_SAMPLER2D(_PreviousMotionDepthTexture, sampler_PreviousMotionDepthTexture);
    TEXTURE2D_SAMPLER2D(_MotionVectorHistory, sampler_MotionVectorHistory);

    // Camera Matrices

    // Previous camera matrices
    float3 _PreviousCameraPosition;
    float4x4 _PreviousInvViewMatrix;
    float4x4 _PreviousInvProjectionMatrix;
    float4x4 _PreviousProjectionMatrix;
    float4x4 _PreviousProjectionViewMatrix;

    // Current camera matrices
    float3 _CameraPosition;
    float4x4 _InvViewMatrix;
    float4x4 _ProjectionMatrix;
    float4x4 _InvProjectionMatrix;
    float4x4 _ProjectionViewMatrix;

    struct FragmentOutput
    {
        half4 previousColor : SV_Target0;
        half4 previousMotionDepth : SV_Target1;
    };

    struct FragmentOutputReprojected
    {
        half4 reprojectedColor : SV_Target0;
        half4 reprojectedMotionDepth : SV_Target1;
    };

    // Helper functions

    // Converts a raw depth value to a linear 0-1 depth value.
    float LinearizeDepth(float z)
    {
        // A little bit complicated to take the current projection mode
        // (perspective/orthographic) into account.
        float isOrtho = unity_OrthoParams.w;
        float isPers = 1.0 - unity_OrthoParams.w;
        z *= _ZBufferParams.x;
        return (1.0 - isOrtho * z) / (isPers * z + _ZBufferParams.y);
    }
    
    // Calculates direction vector from camera through screen position [0-1] (not NDC) to cameras far plane
    float3 ScreenPosToWorldPosDirectionVector(float2 uv, float4x4 invViewMatrix, float4x4 cameraInvProjectionMatrix)
    {
        float4 worldSpace = mul(invViewMatrix, mul(cameraInvProjectionMatrix, float4(uv * 2.0f - 1, 1, 1)));
        return (worldSpace.xyz / worldSpace.w);
    }

    // Calculates direction vector from camera through screen position (not NDC) by given depth value
    float3 ScreenPosToWorldPosDirectionVectorByDepth(float2 uv, float depth, float4x4 invViewMatrix, float4x4 cameraInvProjectionMatrix)
    {
        // Lineare raw depth value
        float z = LinearizeDepth(depth);
        float4 worldSpace = mul(invViewMatrix, mul(cameraInvProjectionMatrix, float4(uv * 2.0f - 1, 1, 1)));
        return (worldSpace.xyz / worldSpace.w) * z;
    }

    // Calculates screen position (not NDC) by a given world position and projection-view matrix
    float2 WorldPosToScreenPos(float3 worldCoords, float4x4 projectionViewMatrix)
    {
        float4 clipSpacePosition = mul(projectionViewMatrix, float4(worldCoords, 1));
        float2 uv = clipSpacePosition.xy /= clipSpacePosition.w;
        return uv * 0.5f + 0.5f;
    }

    // Fragment shaders

    // Fragment shader pass that returns color, motion vector and depth textures
    // Is used as a snapshot for access in future shader passes
    // Previous color contains full-rendered texture as half4
    // Previous motion-depth contains motion vectors and depth values in half4.
    // Components: xy - Motion Vector, z - Depth Value, w - not utilized (0)
    FragmentOutput Initialize(VaryingsDefault i)
    {
        half4 c = SAMPLE_TEX2D(_MainTex, i.texcoord);
        half2 m = SAMPLE_TEX2D(_CameraMotionVectorsTexture, i.texcoord);
        half d = SAMPLE_DEPTH(_CameraDepthTexture, i.texcoord);

        FragmentOutput o;
        o.previousColor = half4(c.rgb, 1);
        o.previousMotionDepth = half4(m, d, 0);
        return o;
    }

    half4 Timewarp(VaryingsDefault i) : SV_Target
    {
        return SAMPLE_TEX2D(_PreviousMotionDepthTexture, i.texcoord);
    }

    half4 Display(VaryingsDefault i) : SV_Target
    {
        return SAMPLE_TEX2D(_MainTex, i.texcoord);
    }

    /** 
        Fragment shader pass for positional timewarp (backward evaluation)
        Reprojects a target color texture to a new camera/view plane, by ray marching through the z-buffer of the target texture
        to find the closest possible projected position. The projected position is then used to calculate screen position from the target / previous camera
        to get a screen position mapping between the target / previous camera and current camera.
        This method is called "backward evaluation" because it starts at the current camera to find a possible reprojection to the 
        previous / target camera.
    */
    // half4 Timewarp(VaryingsDefault i) : SV_Target
    // {   
    //     int iter = 32;  // iteration steps
    //     //float power = 1.0f;
    //     float occlusionThreshold = 0.02f; // threshold value that determines the maximum distance between two positions to be considered occluded
// 
    //     float distanceToMarchedPoint = 0;   // describes the distance between previous camera position and the current marched position
    //     float distanceToTracedPoint = 0;    // the distance of the ray between previous camera position, through the marched point, to the nearest hit in the previous depth texture
    //     float distanceDelta = 0;            // the difference between the two distances above
// 
    //     // calculates a normalized direction vector for the current cameras uv
    //     float3 cameraVector = normalize(ScreenPosToWorldPosDirectionVector(i.texcoord, _InvViewMatrix, _InvProjectionMatrix));
    //     // calculates the position of the marched ray
    //     float3 marchedPosition = _CameraPosition + cameraVector;
// 
    //     for (int i = 0; i < iter; i++) {
    //         // calculates the screen position for the previous camera, from the current marched position
    //         float2 screenPosPreviousCamera = WorldPosToScreenPos(marchedPosition, _PreviousProjectionViewMatrix);
    //         // calculates the world space position of the ray from the previous camera position, through the current marched position, to the nearest "hit" in the depth buffer
    //         float depth = SAMPLE_TEX2D(_PreviousMotionDepthTexture, screenPosPreviousCamera).z;
    //         float3 tracedDepthWorldPositionDirection = ScreenPosToWorldPosDirectionVectorByDepth(screenPosPreviousCamera, depth, _PreviousInvViewMatrix, _PreviousInvProjectionMatrix);
    //         
    //         // calculate the distance between the previous camera position and the marched ray
    //         distanceToMarchedPoint = distance(_PreviousCameraPosition, marchedPosition);
    //         //distanceToTracedPoint = length(tracedDepthWorldPositionDirection);
// 
    //         // calculate the distance between the previous camera position and the world position evaluated by the depth buffer
    //         distanceToTracedPoint = distance(_PreviousCameraPosition, _PreviousCameraPosition + tracedDepthWorldPositionDirection);
// 
    //         // calculate the delta of the two distances
    //         // goal is to minimize the delta
    //         distanceDelta = distanceToTracedPoint - distanceToMarchedPoint;
// 
    //         // use the distance delta to add to the marched ray position
    //         // the distance is clamped to the maximum step size and multiplied by the step size factor parameters
    //         marchedPosition += (cameraVector * clamp(distanceDelta * _StepSizeFactor, -_MaximumStepSize, _MaximumStepSize));
    //     }
// 
    //     // evaluate if the final distance delta is higher than occlusion threshold parameter
    //     float occlusionFactor = step(occlusionThreshold, abs(distanceDelta));
// 
    //     // if its occluded, wind back the marched position along the camera vector direction by a little bit
    //     // this is done to fill in occlusion holes by capturing colors along the epipolar lines
    //     marchedPosition -= cameraVector * occlusionFactor * 0.3f;
// 
    //     // get the screen position of the current marched position for the previous camera
    //     float2 texcoord = WorldPosToScreenPos(marchedPosition, _PreviousProjectionViewMatrix);
    //     
    //     half4 color = SAMPLE_TEX2D(_PreviousColorTexture, texcoord.xy);
    //     half4 filledColor = lerp(half4(1,0,0,1), color, _FillDepthOcclusion);
// 
    //     return lerp(color, filledColor, occlusionFactor);
    //     // finally sample the previous color texture 
    //     //return half4(texcoord, 0, 1);
    //     //return SAMPLE_TEX2D(_PreviousColorTexture, texcoord);
    // }

    ENDHLSL

    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertDefault
            #pragma fragment Initialize
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertDefault
            #pragma fragment Timewarp
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex VertDefault
            #pragma fragment Display
            ENDHLSL
        }
    }
}
