Shader "Custom/WallMaskShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaskTex ("Mask Texture", 2D) = "white" {}
        _Color ("Paint Color", Color) = (1,1,1,1)
        _Opacity ("Opacity", Range(0, 1)) = 0.7
        _Threshold ("Mask Threshold", Range(0.001, 0.5)) = 0.03
        _SmoothFactor ("Smoothing Factor", Range(0, 0.1)) = 0.01
        _EdgeEnhance ("Edge Enhancement", Range(0, 2)) = 1.2
        _DepthOffset ("Depth Offset", Range(0, 1)) = 0.2
        [Toggle] _UseDepthTest ("Use Depth Test", Int) = 1
        [Toggle] _DebugMode ("Debug Mode", Int) = 0
        [KeywordEnum(Mask, Edges, FinalBlend)] _DebugView ("Debug View Mode", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100
        
        ZWrite Off
        ZTest [_UseDepthTest]
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _DEBUGVIEW_MASK _DEBUGVIEW_EDGES _DEBUGVIEW_FINALBLEND
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            sampler2D _MaskTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_ST;
            float4 _Color;
            float _Opacity;
            float _Threshold;
            float _SmoothFactor;
            float _EdgeEnhance;
            float _DepthOffset;
            int _DebugMode;
            int _UseDepthTest;
            float _DebugView;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the base texture (camera or scene)
                fixed4 baseColor = tex2D(_MainTex, i.uv);
                
                // Sample the mask (where the walls are)
                fixed4 maskColor = tex2D(_MaskTex, i.uv);
                
                // Combine all channels for better sensitivity, prioritizing red channel more (DeepLabV3 output format)
                fixed mask = max(maskColor.r * 1.5, max(maskColor.g, maskColor.b));
                
                // Sharpen the mask with adjusted thresholds to prevent bleeding
                fixed sharpMask = step(_Threshold, mask);
                
                // Apply smooth thresholding for edges
                fixed smoothMask = smoothstep(_Threshold - _SmoothFactor, _Threshold + _SmoothFactor, mask);
                
                // Edge detection with improved sensitivity
                float edgeFactor = 1.0;
                float edgeMask = 0.0;
                
                // Sample neighboring pixels for edge detection with adaptive texel size
                float2 texelSize = float2(1.0 / _ScreenParams.x, 1.0 / _ScreenParams.y) * 2.0;
                float2 offsets[8] = {
                    float2(texelSize.x, 0),
                    float2(-texelSize.x, 0),
                    float2(0, texelSize.y),
                    float2(0, -texelSize.y),
                    float2(texelSize.x, texelSize.y),
                    float2(-texelSize.x, texelSize.y),
                    float2(texelSize.x, -texelSize.y),
                    float2(-texelSize.x, -texelSize.y)
                };
                
                int edgeCount = 0;
                float neighborSum = 0;
                float maxNeighborMask = 0;
                
                for (int j = 0; j < 8; j++) {
                    fixed4 neighborColor = tex2D(_MaskTex, i.uv + offsets[j]);
                    fixed neighborMask = max(neighborColor.r * 1.5, max(neighborColor.g, neighborColor.b));
                    
                    neighborSum += neighborMask;
                    maxNeighborMask = max(maxNeighborMask, neighborMask);
                    
                    // Only count as edge if there's a significant difference
                    if (neighborMask < _Threshold * 0.8 && mask > _Threshold * 1.2) {
                        edgeCount++;
                    }
                }
                
                // Calculate edge mask - more edges = brighter
                edgeMask = float(edgeCount) / 8.0;
                
                // Apply more conservative edge enhancement (avoid over-brightening)
                if (edgeCount > 0 && mask > _Threshold) {
                    edgeFactor = 1.0 + (_EdgeEnhance - 1.0) * (edgeCount / 8.0);
                }
                
                // Reduce mask "stickiness" by checking if surrounding area has ANY mask detection
                // This prevents isolated floating pixels
                float surroundFactor = neighborSum / 8.0;
                if (surroundFactor < _Threshold * 0.5) {
                    smoothMask *= surroundFactor / (_Threshold * 0.5); // Fade out isolated pixels
                }
                
                // Apply the color where the mask is present
                fixed4 paintedColor = _Color * edgeFactor;
                
                // Reduce opacity for potentially floating areas (no surrounding mask support)
                paintedColor.a = smoothMask * _Opacity * saturate((neighborSum / 4.0) + 0.5);
                
                // Debug mode visualization options
                if (_DebugMode == 1) {
                    #if defined(_DEBUGVIEW_MASK)
                        // Show raw mask
                        return fixed4(mask, mask, mask, 1.0);
                    #elif defined(_DEBUGVIEW_EDGES)
                        // Show edge detection
                        return fixed4(edgeMask, edgeMask, 0, 1.0);
                    #else
                        // Show blended result
                        return lerp(baseColor, paintedColor, paintedColor.a * 0.5) + fixed4(0, 0, paintedColor.a * 0.5, 0);
                    #endif
                }
                
                // Blend between original and painted color
                fixed4 finalColor = lerp(baseColor, paintedColor, paintedColor.a);
                finalColor.a = 1.0; // Ensure the result is fully opaque
                
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
} 