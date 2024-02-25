Shader "KH2/Kingdom Shader (Pre-Sliced)"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Scrolling ("Scrolling", Vector) = (0, 0, 0, 0)
        // blend mode
        _SrcBlend ("SrcBlend", Int) = 1
        _DstBlend ("DstBlend", Int) = 0
        _ZWrite ("ZWrite", Int) = 1
        
        [Header(Baked Lighting)]
        _MetaAlbedoFactor ("Albedo Factor", Range(0, 4)) = 1
        _MetaEmissionFactor ("Emission Factor", Range(0, 8)) = 0
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
                float2 lmapUV : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Scrolling;

            v2f vert (const appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // get alpha from UV2.x, because .dae files don't support vertex color alpha or 3d UVs
                o.color = fixed4(pow(v.color.rgb, 2.2), v.uv2.x);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + _Scrolling.xy * _Time.y + _Scrolling.zw * _SinTime.w;
                o.lmapUV = v.uv2 * unity_LightmapST.xy + unity_LightmapST.zw;
                return o;
            }

            fixed4 frag (const v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
                //return half4(DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lmapUV)), 1.0);
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
                return UnityMetaFragment(o);
            }
            ENDCG
        }
    }
}
