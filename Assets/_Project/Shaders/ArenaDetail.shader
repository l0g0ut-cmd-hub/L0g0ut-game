// Цельная арена из Meshy (сад святилища): у модели одна текстура 4K на всё — вблизи мыло. Поверх неё — настоящая фактура
// из бесшовных тайлов (ChatGPT), спроецированных по миру, без развёртки:
//  • горизонтальное — земля поляны и мох (по зелёности цвета Meshy), два масштаба против видимого повтора;
//  • вертикальное — дерево, бамбук (зелёное), камень (серое), красный лак (красное), проекция по X и Z;
// материал выбирается по цвету самой модели, яркость фактуры подгоняется под яркость Meshy — крупный рисунок и цвет
// модели сохраняются, добавляется детализация. Погода как у BladePath/Env: дождь темнит и даёт блеск, снег ложится сверху.
Shader "BladePath/ArenaDetail"
{
    Properties
    {
        _MainTex ("Base (Meshy)", 2D) = "white" {}
        _BumpMap ("Normal (Meshy)", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 0.8
        _Ground ("Ground", 2D) = "white" {}
        _Moss ("Moss", 2D) = "white" {}
        _Stone ("Stone", 2D) = "white" {}
        _Wood ("Wood", 2D) = "white" {}
        _Bamboo ("Bamboo", 2D) = "white" {}
        _Lacquer ("Lacquer", 2D) = "white" {}
        _GroundTile ("Ground m per tile", Float) = 2.2
        _WoodTile ("Wood m per tile", Float) = 1.8
        _BambooTile ("Bamboo m per tile", Float) = 0.7
        _StoneTile ("Stone m per tile", Float) = 1.4
        _DetailMix ("Detail Mix", Range(0,1)) = 0.72
        _Glossiness ("Smoothness", Range(0,1)) = 0.12
        _Metallic ("Metallic", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _Ground, _Moss, _Stone, _Wood, _Bamboo, _Lacquer;
        half _BumpScale, _DetailMix, _Glossiness, _Metallic;
        float _GroundTile, _WoodTile, _BambooTile, _StoneTile;
        half _BP_Snow, _BP_Wetness;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float3 worldNormal; INTERNAL_DATA
        };

        float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half3 base = tex2D(_MainTex, IN.uv_MainTex).rgb;
            half3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale; n = normalize(n);
            float3 wn = WorldNormalVector(IN, float3(0, 0, 1));
            float3 wp = IN.worldPos;

            // что это за материал — по цвету Meshy
            half mx = max(base.r, max(base.g, base.b)), mn = min(base.r, min(base.g, base.b));
            half sat = (mx - mn) / (mx + 0.001h);
            half red = saturate((base.r - max(base.g, base.b) * 1.25h) * 5.0h);
            half green = saturate((base.g - max(base.r, base.b) * 0.95h) * 7.0h);
            half gray = saturate(1.0h - sat * 3.5h) * (1.0h - green);

            // горизонтальное: земля / мох, два масштаба — повтор не читается
            float2 tp = wp.xz / _GroundTile;
            half3 g = tex2D(_Ground, tp).rgb * 0.62h + tex2D(_Ground, tp * 0.41 + 0.37).rgb * 0.38h;
            half3 m = tex2D(_Moss, tp * 1.35).rgb;
            half3 top = lerp(g, m, saturate(green * 1.6h));

            // вертикальное: проекции по X и Z
            float3 an = abs(wn);
            half wx = an.x / (an.x + an.z + 0.0001);
            float2 px = wp.zy, pz = wp.xy;
            half3 wood = lerp(tex2D(_Wood, pz / _WoodTile).rgb, tex2D(_Wood, px / _WoodTile).rgb, wx);
            half3 bam = lerp(tex2D(_Bamboo, pz / _BambooTile).rgb, tex2D(_Bamboo, px / _BambooTile).rgb, wx);
            half3 stone = lerp(tex2D(_Stone, pz / _StoneTile).rgb, tex2D(_Stone, px / _StoneTile).rgb, wx);
            half3 lac = lerp(tex2D(_Lacquer, pz / _WoodTile).rgb, tex2D(_Lacquer, px / _WoodTile).rgb, wx);
            half3 side = lerp(wood, stone, gray * 0.85h);
            side = lerp(side, bam, green);
            side = lerp(side, lac, red);
            // камень сверху (валуны, фонари) — камень, а не земля
            top = lerp(top, stone, gray * 0.7h * (1.0h - saturate((0.15 - wp.y) * 10.0)));

            half upK = smoothstep(0.55h, 0.85h, wn.y);
            half3 det = lerp(side, top, upK);

            // яркость фактуры — под яркость Meshy: его крупный рисунок (тени, грязь, переходы) сохраняется
            half lb = dot(base, half3(0.3h, 0.59h, 0.11h));
            half ld = dot(det, half3(0.3h, 0.59h, 0.11h)) + 0.02h;
            half3 detM = det * clamp(lb / ld, 0.55h, 1.7h);
            half3 alb = lerp(base, detM, _DetailMix);
            half smooth = _Glossiness, metal = _Metallic;

            if (_BP_Wetness > 0.001h)
            {
                half wet = _BP_Wetness * lerp(0.45h, 1.0h, saturate(wn.y));
                alb *= 1.0h - 0.32h * wet;
                smooth = lerp(smooth, max(smooth, 0.78h), wet);
            }
            if (_BP_Snow > 0.001h)
            {
                float2 p = wp.xz;
                float h = VNoise(p * 1.7) * 0.55 + VNoise(p * 5.3) * 0.3 + lb * 0.15;
                float amount = saturate(_BP_Snow / 0.85);
                float uK = smoothstep(0.42, 0.86, wn.y);
                float cover = smoothstep(1.0 - amount, 1.0 - amount + 0.28, h * 0.65 + uK * 0.35 + amount * 0.25) * uK;
                alb = lerp(alb, half3(0.78h, 0.81h, 0.87h), cover);
                n = normalize(lerp(n, half3(0, 0, 1), cover * 0.8h));
                smooth = lerp(smooth, 0.32h, cover);
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
