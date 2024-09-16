Shader "Unlit/DepthBasedTransparency"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _DepthTex ("Depth Texture", 2D) = "white" {}
        _FarPlane ("Far Plane", Float) = 1000
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        CGPROGRAM
        #pragma surface surf Lambert alpha

        sampler2D _MainTex;
        sampler2D _DepthTex;
        float _FarPlane;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex);
            float depth = tex2D(_DepthTex, IN.uv_MainTex).r;

            o.Albedo = c.rgb;

            // Convert depth to linear depth
            float linearDepth = 1.0 / (depth * _FarPlane);

            // If depth is very close to the far plane, consider it background
            o.Alpha = linearDepth < 0.99 ? 1 : 0;
        }
        ENDCG
    }
}
