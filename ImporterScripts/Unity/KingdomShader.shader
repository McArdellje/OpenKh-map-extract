Shader "KH2/Kingdom Shader"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Scrolling ("Scrolling", Vector) = (0, 0, 0, 0)
        _TextureRegion ("Texture Region", Vector) = (0, 1, 0, 1)
        _TextureWrapModeU ("Texture Wrap Mode U", Int) = 0
        _TextureWrapModeV ("Texture Wrap Mode V", Int) = 0
        // blend mode
        _SrcBlend ("SrcBlend", Int) = 1
        _DstBlend ("DstBlend", Int) = 0
        _ZWrite ("ZWrite", Int) = 1
    }
    CGINCLUDE
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float4 _TextureRegion;
        float4 _Scrolling;
        int _TextureWrapModeU;
        int _TextureWrapModeV;
        
        int _SrcBlend;
        int _DstBlend;

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
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
        };
        
        static float apply_texture_wrap(const float v, const int mode, float2 region, int is_y) {
            if (mode == 0) { // repeat
                return v;
            }
            if (mode == 1) { // clamp
                return clamp(v, 0, 1);
            }
            if (mode == 2) { // region clamp
                return clamp(v, region.x, region.y);
            }
            if (mode == 3) { // region repeat
                const float region_size = region.y - region.x;
                float mod = (v - region.x) % region_size;
                if (mod < 0)
                    mod += region.y - region.x;
                const float uv_out = mod + region.x;
                /*
                float derivative;
                if (is_y == 1) {
                    derivative = ddy(uv_out);
                } else {
                    derivative = ddx(uv_out);
                }
                if (derivative >= region_size * 0.95) {
                    return float2(uv_out, 0);
                } else {
                    return float2(uv_out, derivative);
                }
                */
                return uv_out;
            }
            return -1;
        }

        static fixed4 bilinear(fixed2 subpixel_pos, const float4 s1, const float4 s2, const float4 s3, const float4 s4) {
            const fixed4 a = lerp(s1, s2, subpixel_pos.x);
            const fixed4 b = lerp(s3, s4, subpixel_pos.x);
            return lerp(a, b, subpixel_pos.y);
        }

        static fixed4 sample_tex(float2 uv, const float2 dx, const float2 dy) {
            uv = float2(
                apply_texture_wrap(uv.x, _TextureWrapModeU, _TextureRegion.xy, 0),
                // 1 - apply_texture_wrap(1 - uv.y, _TextureWrapModeV, _TextureRegion.zw, 1)
                apply_texture_wrap(uv.y, _TextureWrapModeV, _TextureRegion.zw, 1)
            );
            return tex2D(_MainTex, uv, dx, dy);
            //return float4(uv, 0, 1);
        }

        static v2f vert_internal(const appdata v)
        {
            v2f o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            // get alpha from UV2.x, because .dae files don't support vertex color alpha or 3d UVs
            o.color = float4(v.color.rgb, v.uv2.x);
            o.uv = v.uv;
            o.uv += _Scrolling.xy * _Time.y + _Scrolling.zw * _SinTime.w;
            return o;
        }
    ENDCG
    SubShader
    {
        Pass
        {
            blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag

            v2f vert (const appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                // get alpha from UV2.x, because .dae files don't support vertex color alpha or 3d UVs
                o.color = float4(pow(v.color.rgb, 2.2), v.uv2.x);
                o.uv = v.uv;
                o.uv += _Scrolling.xy * _Time.y + _Scrolling.zw * _SinTime.w;
                return o;
            }

            fixed4 frag (const v2f i) : SV_Target
            {
                const float2 subpixel_pos = frac(i.uv * _MainTex_TexelSize.zw + .5);
                const float2 pixel_uv = floor(i.uv * _MainTex_TexelSize.zw + .5) * _MainTex_TexelSize.xy;
                const float2 dx = ddx(i.uv);
                const float2 dy = ddy(i.uv);
                const fixed4 colour = bilinear(
                    subpixel_pos,
                    sample_tex(pixel_uv + _MainTex_TexelSize.xy * float2(-.5, -.5), dx, dy),
                    sample_tex(pixel_uv + _MainTex_TexelSize.xy * float2(.5, -.5),  dx, dy),
                    sample_tex(pixel_uv + _MainTex_TexelSize.xy * float2(-.5, .5),  dx, dy),
                    sample_tex(pixel_uv + _MainTex_TexelSize.xy * float2(.5, .5),   dx, dy)
                );
                if (_SrcBlend == 1 && _DstBlend == 0)
                {
                    // clip on alpha
                    clip(colour.a * i.color.a - .25);
                }
                return colour * i.color;
            }
            ENDCG
        }
    }
}
