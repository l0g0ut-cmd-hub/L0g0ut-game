// Персонажи: тот же PBR, что Standard (альбедо, нормали, металл/гладкость, эмиссия — имена свойств как у Standard,
// поэтому вспышки попаданий и ярость из DuelManager работают без изменений) + контровой свет по силуэту (rim):
// тёмный боец не сливается с ночным фоном. Встроенный конвейер, мобильный target 3.0.
Shader "BladePath/Character"
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
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _RimColor ("Rim Color", Color) = (0.3,0.37,0.52,1)
        _RimPower ("Rim Power", Range(0.5,8)) = 3.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        #pragma shader_feature_local _NORMALMAP
        #pragma shader_feature_local _METALLICGLOSSMAP
        #include "UnityStandardUtils.cginc"

        sampler2D _MainTex, _BumpMap, _MetallicGlossMap, _EmissionMap;
        fixed4 _Color, _EmissionColor, _RimColor;
        half _BumpScale, _Metallic, _Glossiness, _GlossMapScale, _RimPower;
        half _BP_Wetness;   // глобально из Weather: дождь 0…1

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
        #ifdef _NORMALMAP
            o.Normal = UnpackScaleNormal(tex2D(_BumpMap, IN.uv_MainTex), _BumpScale);
        #else
            o.Normal = half3(0, 0, 1);
        #endif
        #ifdef _METALLICGLOSSMAP
            fixed4 mg = tex2D(_MetallicGlossMap, IN.uv_MainTex);
            o.Metallic = mg.r;
            o.Smoothness = mg.a * _GlossMapScale;
        #else
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        #endif
            // мокрый под дождём: верх (плечи, шлем, руки) мокнет сильнее; ткань темнеет, всё блестит
            if (_BP_Wetness > 0.001h)
            {
                half top = saturate(WorldNormalVector(IN, o.Normal).y * 0.5h + 0.5h);
                half w = _BP_Wetness * lerp(0.5h, 1.0h, top);
                o.Albedo *= lerp(1.0h, 0.7h, w * (1.0h - o.Metallic));
                o.Smoothness = lerp(o.Smoothness, max(o.Smoothness, 0.8h), w * 0.85h);
            }
            // контровой свет: сильнее у краёв силуэта (вид почти по касательной к поверхности)
            half rim = 1.0h - saturate(dot(normalize(IN.viewDir), o.Normal));
            rim = pow(rim, _RimPower);
            o.Emission = tex2D(_EmissionMap, IN.uv_MainTex).rgb * _EmissionColor.rgb + _RimColor.rgb * rim;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
