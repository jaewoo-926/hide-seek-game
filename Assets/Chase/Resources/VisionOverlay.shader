// 시야 밖 영역을 어둡게 곱하는 화면 오버레이. 값은 VisionOverlay.cs 가 넣어 준다.
Shader "Chase/VisionOverlay"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VisionOverlay"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // VisionOverlay.RayCount 와 같아야 한다
            #define RAY_COUNT 256

            float _VisionDist[RAY_COUNT];
            float4 _VisionOrigin;
            float4 _VisionOutside;
            float _VisionEdge;
            float _VisionBias;
            float _VisionBody;
            float _VisionYaw;
            float _VisionHalf;

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // 카메라와 상관없이 화면 전체를 덮는다
                o.positionCS = float4(v.positionOS.xy, 0.5, 1.0);
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
            #if UNITY_REVERSED_Z
                float depth = SampleSceneDepth(uv);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(uv));
            #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float2 d = world.xz - _VisionOrigin.xz;
                float dist = length(d);

                // 방향별 시야 거리표를 보간해서 이 픽셀 방향의 한계 거리를 구한다
                float turn = atan2(d.x, d.y) / TWO_PI;
                if (turn < 0.0) turn += 1.0;
                float f = turn * RAY_COUNT;
                int i0 = (int)floor(f) % RAY_COUNT;
                int i1 = (i0 + 1) % RAY_COUNT;
                float limit = lerp(_VisionDist[i0], _VisionDist[i1], frac(f));

                float radial = 1.0 - smoothstep(limit - _VisionEdge, limit + _VisionEdge, dist);

                // 부채꼴 밖(주변 원)은 바닥처럼 위를 보는 면만 밝힌다. 벽을 타고 올라가지 않게
                float delta = abs(frac(turn - _VisionYaw + 0.5) - 0.5);
                float inCone = 1.0 - smoothstep(_VisionHalf - 0.004, _VisionHalf + 0.004, delta);
                float3 normal = normalize(cross(ddy(world), ddx(world)));
                float upward = smoothstep(0.5, 0.8, abs(normal.y));

                // 내 캐릭터는 항상 보이게 (벽에 붙어도 벽까지 밝아지지 않도록 벽 거리로 자른다)
                float body = step(dist, min(_VisionBody, limit - _VisionBias));

                float visible = max(radial * lerp(upward, 1.0, inCone), body);
                return lerp(_VisionOutside, float4(1, 1, 1, 1), visible);
            }
            ENDHLSL
        }
    }
}
