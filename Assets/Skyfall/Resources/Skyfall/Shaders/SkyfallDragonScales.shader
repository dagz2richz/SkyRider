// Procedural overlapping dragon scales (triplanar in object space), with a lighter belly, darker scale rims,
// a faint sheen and rim light. Ported from the Godot SCALE_SHADER.
Shader "Skyfall/DragonScales"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.1, 0.4, 0.95, 1)
        _EdgeColor("Edge Color", Color) = (0.02, 0.07, 0.28, 1)
        _BellyColor("Belly Color", Color) = (0.6, 0.8, 1.0, 1)
        _SheenColor("Sheen Color", Color) = (0.5, 0.85, 1.0, 1)
        _ScaleDensity("Scale Density", Float) = 6.0
        _BellyAmount("Belly Amount", Float) = 1.0
        _MetalVal("Metallic", Float) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _EdgeColor;
            half4 _BellyColor;
            half4 _SheenColor;
            float _ScaleDensity;
            float _BellyAmount;
            float _MetalVal;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SkyfallCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 objPos     : TEXCOORD2;
                float3 objN       : TEXCOORD3;
                half fogFactor    : TEXCOORD4;
            };

            Varyings vert(SkyAttributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4x4 m = GetObjectToWorldMatrix();
                float3 sc = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));
                o.objPos = v.positionOS.xyz * sc;
                o.objN = v.normalOS;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float hash21(float2 q)
            {
                return frac(sin(dot(q, float2(127.1, 311.7))) * 43758.5453);
            }

            // Overlapping shingle scales. Returns distance to scale center (0..1, 1 = rim); id = per-scale random.
            float scales(float2 q, out float id)
            {
                q *= _ScaleDensity;
                float j = floor(q.y);
                float best = 10.0;
                id = 0.0;
                [unroll] for (int k = 1; k >= 0; k--)
                {
                    float r = j + (float)k;
                    float o = fmod(abs(r), 2.0) * 0.5;
                    float cx = floor(q.x - o) + 0.5 + o;
                    [unroll] for (int n = -1; n <= 1; n++)
                    {
                        float2 c = float2(cx + (float)n, r);
                        float2 dv = q - c;
                        float d = length(dv * float2(1.0, 1.3));
                        if (d < 0.66 && best > 5.0)
                        {
                            best = d;
                            id = hash21(c);
                        }
                    }
                }
                if (best > 5.0) best = 0.66;
                return best / 0.66;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 on = normalize(i.objN);
                float3 w = pow(abs(on), 4.0);
                w /= (w.x + w.y + w.z);
                float idx, idy, idz;
                float dx = scales(float2(i.objPos.z, -i.objPos.y), idx);
                float dy = scales(float2(i.objPos.x, i.objPos.z), idy);
                float dz = scales(float2(i.objPos.x, -i.objPos.y), idz);
                float dd = dx * w.x + dy * w.y + dz * w.z;
                float id = idx * w.x + idy * w.y + idz * w.z;
                float rim = smoothstep(0.62, 0.98, dd);
                float3 col = _BaseColor.rgb * (0.82 + 0.36 * id);
                col *= lerp(1.15, 0.85, dd);
                col = lerp(col, _EdgeColor.rgb, rim);
                float belly = smoothstep(-0.15, -0.65, on.y) * _BellyAmount;
                col = lerp(col, _BellyColor.rgb * lerp(1.0, 0.7, rim), belly * 0.85);

                float3 n = normalize(i.normalWS);
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = col;
                s.alpha = 1;
                s.metallic = _MetalVal;
                s.smoothness = 1.0 - lerp(0.28, 0.75, rim);
                s.normalTS = half3(0, 0, 1);
                s.occlusion = 1;
                s.emission = _SheenColor.rgb * 0.04 * (1.0 - rim);

                half4 c = UniversalFragmentPBR(d, s);
                half rimLight = pow(1.0 - saturate(dot(n, d.viewDirectionWS)), 3.0) * 0.35;
                c.rgb += col * rimLight * 0.6;
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "SkyfallCommon.hlsl"

            float4 vert(SkyAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return SkyShadowPositionCS(v);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "SkyfallCommon.hlsl"

            float4 vert(SkyAttributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                return TransformObjectToHClip(v.positionOS.xyz);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "SkyfallCommon.hlsl"

            struct V2F { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            V2F vert(SkyAttributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                V2F o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(V2F i) : SV_Target { return half4(normalize(i.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
