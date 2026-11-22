Shader "BladePath/TacticsUnit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : TEXCOORD1;
            };
            Varyings vert(appdata_base v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            fixed4 frag(Varyings i) : SV_Target
            {
                half sun = saturate(dot(normalize(i.normal), half3(-0.45h, 0.72h, -0.53h)));
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb;
                half peak = max(albedo.r, max(albedo.g, albedo.b));
                albedo *= 1.0h + 0.55h * (1.0h - smoothstep(0.04h, 0.42h, peak));
                return fixed4(albedo * _Color.rgb * (0.72h + 0.38h * sun), 1);
            }
            ENDCG
        }
    }
}
