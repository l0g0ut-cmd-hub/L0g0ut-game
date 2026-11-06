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
    half _Vignette, _Aberr, _Grain, _Desat, _EdgeBlur, _Speed;
    half3 _Shadows, _Highlights;
    half _BP_Snow;          // глобально из Weather: в снег по краям кадра нарастает иней
    half _BP_Wetness;       // глобально из Weather: 1 под дождём — на «объективе» капли
    sampler2D _DofTex, _DofTex2;          // уменьшенные (¼ и ⅛) копии кадра — размытый дальний план
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    float4 _Dof;                           // x — с какой глубины (м) начинается размытие, y — на сколько метров нарастает, z — сила
    float Hash21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
    float VNoise (float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y); }

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
    // капли дождя на «объективе»: редкие, каждая — маленькая линза (картинка за ней перевёрнута и сжата), садится
    // быстро и медленно высыхает; два слоя — крупные редкие и мелкие чаще. Возвращает смещение uv, в mask — сила капли.
    float2 RainDrops (float2 uv, float scale, float speed, float density, float seed, inout half mask)
    {
        float aspect = _MainTex_TexelSize.z * _MainTex_TexelSize.y;
        float2 p = uv * float2(aspect, 1.0) * scale;
        float2 id = floor(p), f = frac(p) - 0.5;
        float r = Hash21(id + seed);
        if (Hash21(id + seed + 7.7) > density) return 0;
        float ph = frac(_Time.y * speed + r);
        half a = smoothstep(0.0, 0.03, ph) * (1.0 - smoothstep(0.35, 1.0, ph));
        float2 c = (float2(Hash21(id + seed + 3.1), Hash21(id + seed + 5.3)) - 0.5) * 0.55;
        float rad = 0.1 + 0.14 * Hash21(id + seed + 9.4);
        float2 q = (f - c) / rad;
        q.y *= 0.85 + 0.15 * r;                 // чуть вытянута вниз
        float d = length(q);
        if (d >= 1.0) return 0;
        half edge = smoothstep(1.0, 0.8, d) * a;
        mask = max(mask, edge);
        return -q * rad * 1.8 * edge / (float2(aspect, 1.0) * scale);
    }

    // объектив: виньетка, хроматическая аберрация (импульс на тяжёлых ударах), зерно плёнки, обесцвечивание (смерть)
    half3 SampleLens (float2 uv)
    {
        float2 d = uv - 0.5;
        half3 c;
        half drop = 0;
        if (_BP_Wetness > 0.01)
        {
            float2 off = RainDrops(uv, 8.0, 0.07, 0.1, 1.7, drop) + RainDrops(uv, 15.0, 0.11, 0.1, 13.3, drop);
            uv += off * saturate(_BP_Wetness);
            drop *= saturate(_BP_Wetness);
        }
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
        // в капле картинка светлее в середине и темнеет к краю (полное отражение у кромки)
        if (drop > 0.001) c *= lerp(1.0h, 1.08h - 0.12h * (1.0h - drop), drop);
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
        // линии скорости (как в аниме/манге; идея — MirzaBeig/Anime-Speed-Lines, Unlicense): тонкие светлые лучи от краёв
        // к центру на рывке, выхвате и добивании; какие лучи горят — меняется 16 раз в секунду, середина кадра чистая
        if (_Speed > 0.001)
        {
            float2 cd = uv - 0.5; cd.x *= _ScreenParams.x / _ScreenParams.y;
            float rr = length(cd);
            float a = atan2(cd.y, cd.x) * 0.15915494 * 150.0;
            float id = floor(a);
            float hsh = frac(sin(id * 12.9898 + floor(_Time.y * 16.0) * 3.17) * 43758.5453);
            float beam = 1.0 - abs(frac(a) - 0.5) * 2.0;
            half ln = step(0.6, hsh) * smoothstep(0.45, 1.0, beam) * saturate((rr - 0.26 - hsh * 0.12) / 0.3);
            c = lerp(c, half3(0.94h, 0.92h, 0.88h), ln * _Speed * 0.6h);
        }
        // иней на «объективе» в снегопад: морозные разводы растут от углов и краёв, холодные и светлые
        if (_BP_Snow > 0.01)
        {
            float2 e = abs(uv - 0.5) * 2.0;
            float edge = max(e.x * 0.92, e.y) + 0.25 * e.x * e.y;
            float cr = VNoise(uv * float2(38.0, 22.0)) * 0.6 + VNoise(uv * float2(130.0, 75.0)) * 0.4;   // кристаллы
            half frost = saturate((edge - 0.84 + cr * 0.3) / 0.18) * saturate(_BP_Snow * 1.2);
            c = lerp(c, half3(0.78h, 0.85h, 0.95h) * (0.75h + 0.35h * cr), frost * 0.3h);
        }
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
                // глубина резкости: бойцы резкие, храм, лес и небо за ними мягко расплываются (как в объективе)
                if (_Dof.z > 0.001)
                {
                    float z = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
                    half k = saturate((z - _Dof.x) / _Dof.y) * _Dof.z;
                    if (z > 70.0) k *= 0.6h;   // небо — мягче (звёзды и луна не тают совсем)
                    half3 b = lerp(tex2D(_DofTex, i.uv).rgb, tex2D(_DofTex2, i.uv).rgb, k);
                    c = lerp(c, b, k);
                }
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
