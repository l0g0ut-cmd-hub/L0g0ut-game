// Окружение храма: тот же PBR, что Standard (альбедо, нормали, металл/гладкость с картой или числами, те же имена свойств —
// материалы переключаются без потерь, Weather по-прежнему крутит _Glossiness/_GlossMapScale/_Color), плюс погода:
//  • снег (_BP_Snow 0…0,85, копится ~25 с): ложится на всё, что смотрит вверх, — крыши, колпаки стен, верх фонарей, камень,
//    гравий, лесную подстилку; сперва пятнами (мировой шум), потом сплошь; крутые скаты держат меньше, стены — ничего.
//    Наклон берётся по геометрической нормали (карта нормалей давала «крапинки»), рельеф под снегом сглаживается;
//  • дождь (_BP_Wetness): пористое темнеет, всё блестит — верх сильнее, стены слабее (огни отражаются в мокром дереве и камне).
Shader "BladePath/Env"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1
        _SnowCatch ("Snow Catch", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature _METALLICGLOSSMAP

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap;
        fixed4 _Color;
        half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _SnowCatch;
        half _BP_Snow, _BP_Wetness;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
        };

        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            half3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale; n = normalize(n);
        #ifdef _METALLICGLOSSMAP
            fixed4 ms = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            half metal = ms.r, smooth = ms.a * _GlossMapScale;
        #else
            half metal = _Metallic, smooth = _Glossiness;
        #endif
            half3 alb = c.rgb;

            // наклон по геометрической нормали (до карты нормалей)
            float up = WorldNormalVector(IN, float3(0, 0, 1)).y;

            if (_BP_Wetness > 0.001h)
            {
                half wet = _BP_Wetness * lerp(0.45h, 1.0h, saturate(up));
                alb *= 1.0h - 0.32h * wet;
                smooth = lerp(smooth, max(smooth, 0.82h), wet);
            }

            if (_BP_Snow > 0.001h)
            {
                float2 p = IN.worldPos.xz;
                float h = VNoise(p * 1.7) * 0.55 + VNoise(p * 5.3) * 0.3 + Luminance(c.rgb) * 0.15;   // пятна: крупные заносы + мелкая рябь
                float amount = saturate(_BP_Snow / 0.85) * _SnowCatch;
                float upK = smoothstep(0.42, 0.86, up);
                float cover = smoothstep(1.0 - amount, 1.0 - amount + 0.28, h * 0.65 + upK * 0.35 + amount * 0.25) * upK;
                half3 snowCol = half3(0.78h, 0.81h, 0.87h) * (0.92h + 0.08h * VNoise(p * 31.0));
                alb = lerp(alb, snowCol, cover);
                n = normalize(lerp(n, half3(0, 0, 1), cover * 0.8h));
                smooth = lerp(smooth, 0.32h, cover);
                metal = lerp(metal, 0.0h, cover);
            }

            o.Albedo = alb;
            o.Normal = n;
            o.Metallic = metal;
            o.Smoothness = smooth;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
