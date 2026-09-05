// Upgrade NOTE: commented out 'float4x4 _CameraToWorld', a built-in variable
// Upgrade NOTE: replaced '_CameraToWorld' with 'unity_CameraToWorld'

Shader "Hidden/CylindricalFog"
{
    Properties
    {
        _MainTex  ("", 2D)          = "white" {}
        _FogColor ("Fog Color", Color) = (0.5, 0.5, 0.5, 1)
        _FogStart ("Fog Start", Float) = 0
        _FogEnd   ("Fog End",   Float) = 200
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4   _FogColor;
            float    _FogStart;
            float    _FogEnd;
            float3   _FrustumBottomLeft;
            float3   _FrustumTopLeft;
            float3   _FrustumTopRight;
            float3   _FrustumBottomRight;
            float4x4 _CylFogCameraToWorld;
            float3   _FogOrigin;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);
                float  depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);

                float3 bottomRay = lerp(_FrustumBottomLeft, _FrustumBottomRight, i.uv.x);
                float3 topRay = lerp(_FrustumTopLeft, _FrustumTopRight, i.uv.x);
                float3 viewPos = lerp(bottomRay, topRay, i.uv.y) * LinearEyeDepth(depth);
                float dist = length(viewPos);

                float t = saturate((dist - _FogStart) / max(_FogEnd - _FogStart, 0.001));
                return float4(lerp(col.rgb, _FogColor.rgb, t), col.a);
            }
            ENDCG
        }

    }
}
