// Gradient skybox: zenith / horizon / ground colors and a soft sun disc. Colors are driven from code
// so the sky can burn toward dusk over the run.
Shader "Skyfall/Sky"
{
    Properties
    {
        _TopColor("Sky Top", Color) = (0.28, 0.38, 0.52, 1)
        _HorizonColor("Sky Horizon", Color) = (0.72, 0.64, 0.55, 1)
        _GroundHorizonColor("Ground Horizon", Color) = (0.5, 0.45, 0.4, 1)
        _GroundBottomColor("Ground Bottom", Color) = (0.12, 0.1, 0.09, 1)
        _SunColor("Sun Color", Color) = (1, 0.9, 0.78, 1)
        _SunDir("Sun Direction (towards sun)", Vector) = (0.4, 0.6, 0.5, 0)
        _SunSize("Sun Size", Float) = 0.9994
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _GroundHorizonColor;
                half4 _GroundBottomColor;
                half4 _SunColor;
                float4 _SunDir;
                float _SunSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half3 c;
                // Same falloff as Godot's ProceduralSkyMaterial (sky_curve 0.15, ground_curve 0.02).
                float a = asin(saturate(abs(d.y))) / (PI * 0.5);
                if (d.y >= 0)
                    c = lerp(_HorizonColor.rgb, _TopColor.rgb, 1.0 - pow(1.0 - a, 1.0 / 0.15));
                else
                    c = lerp(_GroundHorizonColor.rgb, _GroundBottomColor.rgb, 1.0 - pow(1.0 - a, 1.0 / 0.02));
                float sd = dot(d, normalize(_SunDir.xyz));
                half sun = smoothstep(_SunSize, _SunSize + 0.0004, sd);
                half glow = pow(saturate(sd), 64.0) * 0.35;
                c += _SunColor.rgb * (sun * 6.0 + glow);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
