// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Debug/Render Points"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            ZWrite On
            Cull Off
            
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc

#include "GaussianSplatting.hlsl"

float4x4 _SplatCutouts;

struct v2f
{
    half3 color : TEXCOORD0;
    float4 vertex : SV_POSITION;
    float isCut : TEXCOORD1;
};

float _SplatSize;
bool _DisplayIndex;
int _SplatCount;

// If this is set to true, then DebugPoints also get cut - set it to false, so you have the set it to false if you want to see everything.
bool _DebugRenderPointsCutout = false;

bool IsPointInCutout(float3 pos)
{
    if(_DebugRenderPointsCutout)
        return true;
    
    // Transform the point to the cutout's local space
    float3 localPos = mul(_SplatCutouts, float4(pos, 1.0)).xyz;
    
    // Check if the point is inside the unit cube in the cutout's local space
    return all(abs(localPos) <= 1.0);
}

v2f vert (uint vtxID : SV_VertexID, uint instID : SV_InstanceID)
{
    v2f o;
    uint splatIndex = instID;

    SplatData splat = LoadSplatData(splatIndex);

    float3 centerWorldPos = splat.pos;
    centerWorldPos = mul(unity_ObjectToWorld, float4(centerWorldPos,1)).xyz;

    float4 centerClipPos = mul(UNITY_MATRIX_VP, float4(centerWorldPos, 1));
    
    o.vertex = centerClipPos;

    uint idx = vtxID;
    float2 quadPos = float2(idx&1, (idx>>1)&1) * 2.0 - 1.0;
    o.vertex.xy += (quadPos * _SplatSize / _ScreenParams.xy) * o.vertex.w;

    o.color.rgb = saturate(splat.sh.col);
    if (_DisplayIndex)
    {
        o.color.r = frac((float)splatIndex / (float)_SplatCount * 100);
        o.color.g = frac((float)splatIndex / (float)_SplatCount * 10);
        o.color.b = (float)splatIndex / (float)_SplatCount;
    }

    // Check if the point should be cut out
    o.isCut = IsPointInCutout(splat.pos) ? 0.0 : 1.0;

    return o;
}

half4 frag (v2f i) : SV_Target
{
    // Discard the fragment if it's cut out
    //if (i.isCut > 0.5)
      //  discard;

    return half4(i.color.rgb, 1);
}
ENDCG
        }
    }
}