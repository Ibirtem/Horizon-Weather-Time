Shader "Hidden/Horizon/DepthOnly"
{
    Properties
    {
        _MainTex ("", 2D) = "white" {}
        _Cutoff ("", Float) = 0.5
        _Color ("", Color) = (1,1,1,1)
    }
    
    CGINCLUDE
    #include "UnityCG.cginc"

    #define HORIZON_TRANSPARENT_IGNORE_ALPHA
    #define HORIZON_TRANSPARENT_THRESHOLD 0.02

    struct appdata
    {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float depth : TEXCOORD0;
        float2 uv : TEXCOORD1;
    };

    sampler2D _MainTex;
    float4 _MainTex_ST;
    float _Cutoff;
    fixed4 _Color;

    v2f depthVert(appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        float z = -UnityObjectToViewPos(v.vertex).z;
        o.depth = z / _ProjectionParams.z; 
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        return o;
    }

    float4 depthFrag(v2f i) : SV_Target
    {
        return float4(i.depth, 0, 0, 1);
    }

    float4 depthFragCutout(v2f i) : SV_Target
    {
        fixed4 col = tex2D(_MainTex, i.uv) * _Color;
        clip(col.a - _Cutoff);
        return float4(i.depth, 0, 0, 1);
    }

    float4 depthFragAlpha(v2f i) : SV_Target
    {
#if !defined(HORIZON_TRANSPARENT_IGNORE_ALPHA)
        fixed4 col = tex2D(_MainTex, i.uv) * _Color;
        clip(col.a - HORIZON_TRANSPARENT_THRESHOLD); 
#endif
        return float4(i.depth, 0, 0, 1);
    }
    ENDCG

    // --- Opaque ---
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite On
            CGPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            ENDCG
        }
    }

    // --- Transparent Cutout ---
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" }
        Pass
        {
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFragCutout
            ENDCG
        }
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFragAlpha
            ENDCG
        }
    }
    
    SubShader
    {
        Tags { "RenderType"="TreeOpaque" }
        Pass { ZWrite On CGPROGRAM #pragma vertex depthVert #pragma fragment depthFrag ENDCG }
    }
    
    SubShader
    {
        Tags { "RenderType"="TreeBillboard" }
        Pass { ZWrite On Cull Off CGPROGRAM #pragma vertex depthVert #pragma fragment depthFragCutout ENDCG }
    }
    
    SubShader
    {
        Tags { "RenderType"="Grass" }
        Pass { ZWrite On Cull Off CGPROGRAM #pragma vertex depthVert #pragma fragment depthFragCutout ENDCG }
    }
}