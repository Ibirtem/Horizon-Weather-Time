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

    struct appdata
    {
        float4 vertex : POSITION;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float depth : TEXCOORD0;
    };

    v2f depthVert(appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        float z = -UnityObjectToViewPos(v.vertex).z;
        o.depth = z / _ProjectionParams.z; 
        return o;
    }

    float4 depthFrag(v2f i) : SV_Target
    {
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
            #pragma fragment depthFrag
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
        Pass { ZWrite On Cull Off CGPROGRAM #pragma vertex depthVert #pragma fragment depthFrag ENDCG }
    }
    
    SubShader
    {
        Tags { "RenderType"="Grass" }
        Pass { ZWrite On Cull Off CGPROGRAM #pragma vertex depthVert #pragma fragment depthFrag ENDCG }
    }
}