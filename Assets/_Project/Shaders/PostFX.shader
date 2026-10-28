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
                half3 c = tex2D(_MainTex, i.uv).rgb;
                c += tex2D(_BloomTex, i.uv).rgb * _Intensity;
                return half4(Grade(c), 1);
            }
            ENDCG
        }
        Pass // 4: только цветокоррекция (экономная графика)
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (v2f i) : SV_Target { return half4(Grade(tex2D(_MainTex, i.uv).rgb), 1); }
            ENDCG
        }
    }
    Fallback Off
}
