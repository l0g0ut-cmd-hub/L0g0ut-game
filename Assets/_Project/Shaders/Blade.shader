// Клинок катаны: PBR как Standard (альбедо с хамоном, металл/гладкость) + кровь по маске (tools/texgen/katana_blood.py).
// _Blood 0…1 — сколько крови: заливаются места, где маска ниже, поэтому кровь растёт от острия к хабаки потёками вдоль клинка,
// у кромки раньше, чем у обуха. Свежая — глубокий красный с мокрым бликом, у края потёка толще и темнее (мениск);
// _BloodDry 0…1 — подсыхает: бурая, матовая. Под дождём сталь в каплях — гладкость выше.
Shader "BladePath/Blade"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.9
        _Glossiness ("Smoothness", Range(0,1)) = 0.8
        _GlossMapScale ("Smoothness Scale", Range(0,1)) = 1
        _BloodMask ("Blood Mask (R)", 2D) = "white" {}
        _Blood ("Blood", Range(0,1)) = 0
        _BloodDry ("Blood Dry", Range(0,1)) = 0
        _EmissionColor ("Emission", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature_local _METALLICGLOSSMAP

        sampler2D _MainTex, _MetallicGlossMap, _BloodMask;
        fixed4 _Color, _EmissionColor;
        half _Metallic, _Glossiness, _GlossMapScale, _Blood, _BloodDry;
        half _BP_Wetness;

        struct Input { float2 uv_MainTex; };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
        #ifdef _METALLICGLOSSMAP
            fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            half metal = mg.r, smooth = mg.a * _GlossMapScale;
        #else
            half metal = _Metallic, smooth = _Glossiness;
        #endif
            // под дождём — капли на стали
            smooth = lerp(smooth, max(smooth, 0.93h), _BP_Wetness * 0.6h);

            half m = tex2D(_BloodMask, IN.uv_MainTex).r;
            half lvl = _Blood * 1.04h - m;                        // > 0 — под кровью
            half t = saturate(lvl / 0.06h) * step(0.004h, _Blood);
            half rim = saturate(1.0h - abs(lvl - 0.025h) / 0.025h) * t;   // мениск у края потёка
            half3 wetCol = half3(0.30h, 0.012h, 0.012h), dryCol = half3(0.11h, 0.02h, 0.015h);
            half3 bc = lerp(wetCol, dryCol, _BloodDry) * (1.0h - rim * 0.4h);
            o.Albedo = lerp(c.rgb, bc, t * 0.96h);
            o.Metallic = lerp(metal, 0.04h, t);
            o.Smoothness = lerp(smooth, lerp(0.9h, 0.38h, _BloodDry), t);
            o.Emission = _EmissionColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Standard"
}
