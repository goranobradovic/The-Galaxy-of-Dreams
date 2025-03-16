Shader "Custom/StarParticle" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Brightness ("Brightness", Range(0, 2)) = 1.0
        _CoreColor ("Core Color", Color) = (1, 1, 1, 1)
        _OuterColor ("Outer Color", Color) = (0, 0, 1, 1)
    }
    
    SubShader {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend One One
        Cull Off
        ZWrite Off
        
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f {
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Brightness;
            float4 _CoreColor;
            float4 _OuterColor;
            
            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            
            float4 frag (v2f i) : SV_Target {
                // Calculate distance from center
                float2 center = float2(0.5, 0.5);
                float dist = distance(i.uv, center);
                
                // Create soft particle effect
                float circle = 1.0 - smoothstep(0.0, 0.5, dist);
                
                // Color gradient from core to outer
                float4 gradientColor = lerp(_CoreColor, _OuterColor, dist * 2.0);
                
                // Apply brightness
                float4 finalColor = gradientColor * circle * _Brightness * i.color;
                
                return finalColor;
            }
            ENDCG
        }
    }
    FallBack "Particles/Additive"
}