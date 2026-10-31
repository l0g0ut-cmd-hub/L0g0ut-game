// Листва и трава с ветром: PBR как у Standard (альфа-срез, тусклое самосвечение под цвет листа — чтобы ночью не уходило в чёрное),
// вершины качаются в мировых координатах (работает и со статическим батчингом): трава — сильнее к верхушке (uv.y),
// карточки крон — лёгкий трепет по месту. На порыве (глобально _BP_Wind из WindGusts) всё гнётся по ветру сильнее.
Shader "BladePath/Foliage"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (A = cutout)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.45
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Sway ("Sway (0 карточка кроны … 1 трава)", Range(0,1)) = 0
        _Flutter ("Flutter amount", Float) = 0.035
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Standard alphatest:_Cutoff vertex:vert addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color, _EmissionColor;
        half _Glossiness, _Sway, _Flutter;
        float _BP_Wind;          // 0…1 — сила порыва (WindGusts)
        float _BP_Wetness, _BP_Snow;
        float _BP_Dew;           // рассвет (DayNight): роса на траве искрится
        float4 _BP_WindDir;      // xz — направление ветра

        struct Input { float2 uv_MainTex; float3 worldNormal; float3 worldPos; };
        float DewHash (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

        void vert (inout appdata_full v)
        {
            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float t = _Time.y;
            float gust = saturate(_BP_Wind);
            float2 wd = dot(_BP_WindDir.xz, _BP_WindDir.xz) > 0.01 ? normalize(_BP_WindDir.xz) : float2(1, 0);
            // трава: верхушка гнётся, низ стоит; крона: всё качается понемногу, с разной фазой по месту
            float h = lerp(1.0, saturate(v.texcoord.y), _Sway);
            float ph = wp.x * 0.7 + wp.z * 0.55;
            float idle = sin(t * 1.6 + ph) * 0.6 + sin(t * 2.9 + ph * 1.7) * 0.4;
            float amp = lerp(_Flutter, 0.05, _Sway) * (1.0 + 2.5 * gust);
            float lean = gust * lerp(0.05, 0.16, _Sway);   // на порыве — гнётся по ветру
            wp.xz += (wd * (lean + amp * idle * 0.6) + float2(-wd.y, wd.x) * amp * idle * 0.4) * h;
            wp.y += sin(t * 3.7 + ph * 2.3) * amp * 0.25 * h * (1.0 - _Sway);
            v.vertex = mul(unity_WorldToObject, float4(wp, 1.0));
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            fixed4 c = t * _Color;
            // дождь: листья темнее и блестят; снег: присыпка сверху (у травы — на верхушках)
            float wet = saturate(_BP_Wetness);
            float snow = saturate(_BP_Snow) * saturate(IN.worldNormal.y * 0.5 + 0.6) * lerp(1.0, IN.uv_MainTex.y, _Sway);
            o.Albedo = lerp(c.rgb * (1.0 - 0.28 * wet), fixed3(0.78, 0.8, 0.84), snow * 0.55);
            o.Metallic = 0;
            o.Smoothness = lerp(_Glossiness, 0.3, wet);   // выше — отражение неба на мокрых листьях читалось розово-белыми пятнами
            o.Emission = t.rgb * _EmissionColor.rgb;   // как у Standard с _EmissionMap = альбедо
            // роса на рассвете: редкие капли на травинках вспыхивают и гаснут (мерцают по своей фазе), трава чуть влажно блестит
            if (_BP_Dew > 0.01 && _Sway > 0.5)
            {
                float3 cell = floor(IN.worldPos * 70.0);
                float h = DewHash(cell.xz + cell.y * 17.31);
                float tw = 0.5 + 0.5 * sin(_Time.y * 2.6 + h * 80.0);
                float dew = _BP_Dew * (1.0 - saturate(_BP_Snow * 2.0));
                o.Emission += step(0.992, h) * tw * tw * dew * fixed3(1.0, 0.95, 0.86) * 1.1;
                o.Smoothness = max(o.Smoothness, 0.32 * dew);
            }
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/Diffuse"
}
