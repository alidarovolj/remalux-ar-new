Shader "Custom/WallPaintShader"
{
    Properties
    {
        _PaintColor ("Paint Color", Color) = (1,1,1,1)
        _MaskTex ("Mask Texture", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.8
        _Glossiness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Threshold ("Mask Threshold", Range(0.001, 0.5)) = 0.1
        _LuminancePreservation ("Luminance Preservation", Range(0, 1)) = 0.7
        _DepthOffset ("Depth Offset", Range(0, 1)) = 0.05
        [Toggle] _UseDepthTest ("Use Depth Test", Int) = 1
        [Toggle] _ShowMask ("Show Mask Only (Debug)", Int) = 0
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" }
        LOD 200
        
        // Use depth testing if enabled
        ZWrite Off
        ZTest [_UseDepthTest]
        Blend SrcAlpha OneMinusSrcAlpha
        
        // Grab the screen behind the object
        GrabPass { "_BackgroundTexture" }
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            #include "UnityCG.cginc"
            #include "UnityStandardUtils.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 grabPos : TEXCOORD1;
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD2;
                float3 worldPos : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
            };
            
            // Properties
            sampler2D _MaskTex;
            float4 _MaskTex_ST;
            float4 _PaintColor;
            float _Opacity;
            float _Glossiness;
            float _Metallic;
            float _Threshold;
            float _LuminancePreservation;
            float _DepthOffset;
            int _UseDepthTest;
            int _ShowMask;
            
            // Grabbed background texture
            sampler2D _BackgroundTexture;
            sampler2D _CameraDepthTexture;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MaskTex);
                
                // Compute grab position for background sampling
                o.grabPos = ComputeGrabScreenPos(o.pos);
                
                // Calculate world space values for lighting
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                
                // Calculate screen position for depth comparison
                o.screenPos = ComputeScreenPos(o.pos);
                
                return o;
            }
            
            half4 frag (v2f i) : SV_Target
            {
                // Sample the wall mask
                half4 maskColor = tex2D(_MaskTex, i.uv);
                half mask = maskColor.r; // Use red channel as mask
                
                // Apply threshold to mask
                half maskedArea = step(_Threshold, mask);
                
                // If showing mask only (debug mode)
                if (_ShowMask == 1)
                {
                    return half4(mask, mask, mask, 1.0);
                }
                
                // Get the background color from grab texture
                half4 backgroundColor = tex2Dproj(_BackgroundTexture, i.grabPos);
                
                // Extract luminance from background for lighting preservation
                half luminance = dot(backgroundColor.rgb, half3(0.2126, 0.7152, 0.0722));
                
                // Check depth if enabled (for occlusion by real objects)
                if (_UseDepthTest == 1)
                {
                    // Sample depth texture
                    float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
                    
                    // Calculate depth of current fragment
                    float fragDepth = i.screenPos.z;
                    
                    // Apply depth offset to avoid z-fighting and edge artifacts
                    float depthDiff = sceneDepth - fragDepth;
                    
                    // If something is in front of us in the real world, don't draw
                    if (depthDiff < _DepthOffset)
                    {
                        maskedArea = 0.0;
                    }
                }
                
                // Basic lighting calculation (based on view direction and normal)
                half3 worldViewDir = normalize(UnityWorldSpaceViewDir(i.worldPos));
                half3 worldNormal = normalize(i.worldNormal);
                
                // Simple specular reflection for gloss effect
                half3 halfDir = normalize(worldViewDir + normalize(_WorldSpaceLightPos0.xyz));
                half specular = pow(max(0, dot(worldNormal, halfDir)), _Glossiness * 64.0);
                
                // Mix original color with paint color based on luminance preservation
                half3 preservedLuminance = lerp(_PaintColor.rgb, _PaintColor.rgb * luminance, _LuminancePreservation);
                
                // Add specular highlight based on glossiness
                half3 finalColor = preservedLuminance + specular * _Glossiness * _Metallic;
                
                // Blend with background based on mask and opacity
                half alpha = maskedArea * _Opacity;
                return half4(lerp(backgroundColor.rgb, finalColor, alpha), 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
} 