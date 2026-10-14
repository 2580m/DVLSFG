Shader "Hidden/DVScaling/TemporalCapture" {
    SubShader {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        sampler2D _CameraMotionVectorsTexture;
        float depth(v2f_img i):SV_Target {
            return SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,float2(i.uv.x,1-i.uv.y));
        }
        float2 motion(v2f_img i):SV_Target {
            float2 value=tex2D(_CameraMotionVectorsTexture,float2(i.uv.x,1-i.uv.y)).xy;
            return all(value==value) && all(abs(value)<=1) ? value : float2(0,0);
        }
        ENDCG
        Pass {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment depth
            ENDCG
        }
        Pass {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment motion
            ENDCG
        }
    }
}
