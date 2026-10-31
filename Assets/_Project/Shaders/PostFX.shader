// Пост-обработка для встроенного конвейера (без пакетов): свечение ярких источников (bloom: порог с мягким коленом,
// каскад уменьшения 4 отсчётами, обратный каскад «шатром» с добавлением) и цветокоррекция кадра
// (экспозиция, контраст, насыщенность, раздельное тонирование теней/светов — холодные тени луны, тёплый огонь).
Shader "Hidden/BladePath/PostFX"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BloomTex, _SourceTex;
    float4 _MainTex_TexelSize;
    half4 _Filter;          // x порог, y x−колено, z 2·колено, w 0.25/колено
    half _Intensity, _Exposure, _Contrast, _Saturation, _Delta;
    half _Vignette, _Aberr, _Grain, _Desat, _EdgeBlur;
    half3 _Shadows, _Highlights;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    half3 Box (float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float2(-d, d).xxyy;
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25h;
    }
    half3 Prefilter (half3 c)
    {
        half b = max(c.r, max(c.g, c.b));
        half soft = clamp(b - _Filter.y, 0, _Filter.z);
        soft = soft * soft * _Filter.w;
        half contrib = max(soft, b - _Filter.x) / max(b, 1e-4h);
        return c * contrib;
    }
    half3 Grade (half3 c)
    {
        c *= _Exposure;
        half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
        c = lerp(l.xxx, c, _Saturation);
        c = (c - 0.5h) * _Contrast + 0.5h;
        half t = saturate(l * 1.6h);
        c *= lerp(_Shadows, _Highlights, t);
        return saturate(c);
    }
    // объектив: виньетка, хроматическая аберрация (импульс на тяжёлых ударах), зерно плёнки, обесцвечивание (смерть)
    half3 SampleLens (float2 uv)
    {
        float2 d = uv - 0.5;
        half3 c;
        if (_Aberr > 0.0001)
        {
            float2 o = d * _Aberr;
            c = half3(tex2D(_MainTex, uv + o).r, tex2D(_MainTex, uv).g, tex2D(_MainTex, uv - o).b);
        }
        else c = tex2D(_MainTex, uv).rgb;
        // кинематографический фокус (добивание, гибель): края кадра мягко размыты, пара в центре резкая — 8 отсчётов в два кольца
        if (_EdgeBlur > 0.001)
        {
            float2 dd = d * float2(1.0, 0.75);
            half r = saturate(dot(dd, dd) * 5.0 - 0.12);
            if (r > 0.001)
            {
                float2 o1 = _MainTex_TexelSize.xy * (r * _EdgeBlur * 5.0), o2 = o1 * 2.2;
                half3 b = tex2D(_MainTex, uv + float2(o1.x, 0)).rgb + tex2D(_MainTex, uv - float2(o1.x, 0)).rgb
                        + tex2D(_MainTex, uv + float2(0, o1.y)).rgb + tex2D(_MainTex, uv - float2(0, o1.y)).rgb
                        + tex2D(_MainTex, uv + o2 * 0.7071).rgb + tex2D(_MainTex, uv - o2 * 0.7071).rgb
                        + tex2D(_MainTex, uv + float2(o2.x, -o2.y) * 0.7071).rgb + tex2D(_MainTex, uv + float2(-o2.x, o2.y) * 0.7071).rgb;
                c = lerp(c, b * 0.125h, saturate(r * 1.4h) * saturate(_EdgeBlur));
            }
        }
        return c;
    }
    half3 Lens (half3 c, float2 uv)
    {
        float2 d = (uv - 0.5) * float2(1.0, 0.8);
        half v = saturate(1.0 - dot(d, d) * 2.2);
        c *= lerp(1.0h, v * v * (3.0h - 2.0h * v), _Vignette);
        half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
        c = lerp(c, l.xxx * half3(1.05h, 0.92h, 0.9h), _Desat);
        float n = frac(sin(dot(uv * 431.7 + frac(_Time.y * 7.13), float2(12.9898, 78.233))) * 43758.5453);
        c += (n - 0.5) * _Grain;
        return saturate(c);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass // 0: порог + первое уменьшение
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Prefilter(Box(i.uv, 1)), 1); }
            ENDCG
        }
        Pass // 1: уменьшение
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Box(i.uv, 1), 1); }
            ENDCG
        }
        Pass // 2: увеличение с добавлением
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Box(i.uv, 0.5), 1); }
            ENDCG
        }
        Pass // 3: итог: кадр + свечение, цветокоррекция
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target
            {
                half3 c = SampleLens(i.uv);
                c += tex2D(_BloomTex, i.uv).rgb * _Intensity;
                return half4(Lens(Grade(c), i.uv), 1);
            }
            ENDCG
        }
        Pass // 4: только цветокоррекция (экономная графика)
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Lens(Grade(SampleLens(i.uv)), i.uv), 1); }
            ENDCG
        }
    }
    Fallback Off
}
