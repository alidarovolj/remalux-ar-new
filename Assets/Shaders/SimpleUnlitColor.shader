Shader "Custom/SimpleUnlitColor" {
    Properties {
        _Color ("Color", Color) = (1,1,1,1)
    }
    
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            
            #include "UnityCG.cginc"
            
            struct appdata {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct v2f {
                float4 vertex : SV_POSITION;
                UNITY_FOG_COORDS(0)
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            fixed4 _Color;
            
            v2f vert (appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o,o.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target {
                fixed4 col = _Color;
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    
    // Fallback for old devices and WebGL
    FallBack "Mobile/Unlit (Supports Lightmap)"
} 