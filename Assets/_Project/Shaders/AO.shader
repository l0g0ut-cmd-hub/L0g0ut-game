// Запечённое «затенение у основания» (AO): плоские полосы и пятна на земле у стен, платформы храма, фонарей, жаровен,
// столбов и стволов, умножающие цвет пола (Blend DstColor Zero). Без SSAO на телефоне предметы «висели» над землёй —
// мягкая тень в углу стыка сажает их на место. Туман ведёт к белому (умножение на 1 — без изменений), поэтому вдали не темнеет.
Shader "BladePath/AO"
{
    Properties
    {
        _MainTex ("Mask (white = нет тени)", 2D) = "white" {}
        _Strength ("Strength", Range(0,1)) = 0.6
        _Frame2 ("Next frame: xy = UV offset, z = blend", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry+20" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        Pass
        {
            Blend DstColor Zero
            ZWrite Off
            Cull Off
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half _Strength;
            float4 _Frame2;   // плавный переход к следующему кадру листа (тень идущего монаха); у остальных наклеек z = 0

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD2; UNITY_FOG_COORDS(1) };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.uv2 = v.texcoord.xy * _MainTex_ST.xy + _Frame2.xy;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed m = lerp(tex2D(_MainTex, i.uv).r, tex2D(_MainTex, i.uv2).r, _Frame2.z);
                fixed4 c = fixed4(lerp(1.0, m, _Strength).xxx, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(1, 1, 1, 1));
                return c;
            }
            ENDCG
        }
    }
}
