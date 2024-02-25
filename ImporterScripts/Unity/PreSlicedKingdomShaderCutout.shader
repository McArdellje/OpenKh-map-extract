Shader "KH2/Kingdom Shader (Pre-Sliced Cutout)"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scrolling ("Scrolling", Vector) = (0, 0, 0, 0)
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        // blend mode
        _SrcBlend ("SrcBlend", Int) = 1
        _DstBlend ("DstBlend", Int) = 0
        _ZWrite ("ZWrite", Int) = 1
        
        [Header(Baked Lighting)]
        _MetaAlbedoFactor ("Albedo Factor", Range(0, 4)) = 1
        _MetaEmissionFactor ("Emission Factor", Float) = 0
    }
    SubShader
    {
        Pass
        {
            blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Scrolling;
            fixed _Threshold;

            v2f vert (const appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // get alpha from UV2.x, because .dae files don't support vertex color alpha or 3d UVs
                o.color = fixed4(pow(v.color.rgb, 2.2), v.uv2.x);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scrolling.xy * _Time.y + _Scrolling.zw * _SinTime.w;
                return o;
            }

            fixed4 frag (const v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                clip(c.a - _Threshold);
                return c;
            }
            ENDCG
        }
        
        Pass
        {
            Name "META"
            Tags { "LightMode" = "Meta" }
            Cull Off
            
                        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Scrolling;

            float _MetaAlbedoFactor;
            float _MetaEmissionFactor;
            float _Threshold;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityMetaVertexPosition(v.vertex, v.uv1.xy, v.uv2.xy, unity_LightmapST, unity_DynamicLightmapST);;
                o.color = fixed4(pow(v.color.rgb, 2.2), 1.0);
                o.uv = TRANSFORM_TEX(v.uv0, _MainTex);
                return o;
            }

            float4 frag (const v2f i) : SV_Target
            {
                UnityMetaInput o;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, o);
                float4 rawCol = tex2D(_MainTex, i.uv);
                float4 col = rawCol * i.color;
                o.Albedo = rawCol * _MetaAlbedoFactor;
                o.Emission = col * _MetaEmissionFactor;
                if (col.a < _Threshold) o.Emission = 0.0;
                return UnityMetaFragment(o);
            }
            ENDCG
        }
    }
}
