Shader "PigeonHunter/UI/Retro TV Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}

        [Header(Base)]
        _Color("Overlay Tint", Color) = (0.72, 0.9, 0.82, 1)
        _Opacity("Overall Opacity", Range(0, 1)) = 0.55
        _Brightness("Brightness", Range(0, 2)) = 1
        _Contrast("Contrast", Range(0, 2)) = 1.1

        [Header(Static Noise)]
        _NoiseStrength("Static Strength", Range(0, 1)) = 0.22
        _NoiseScale("Static Scale", Range(8, 900)) = 260
        _NoiseSpeed("Static Speed", Range(0, 80)) = 28
        _SnowAmount("White Snow Amount", Range(0, 1)) = 0.18
        _SnowThreshold("White Snow Threshold", Range(0, 1)) = 0.88

        [Header(Scanlines)]
        _ScanlineStrength("Scanline Strength", Range(0, 1)) = 0.32
        _ScanlineCount("Scanline Count", Range(20, 900)) = 260
        _ScanlineSpeed("Scanline Drift Speed", Range(-10, 10)) = 0.7
        _ScanlineSharpness("Scanline Sharpness", Range(0.2, 8)) = 3

        [Header(Rolling Bands)]
        _BandStrength("Rolling Band Strength", Range(0, 1)) = 0.18
        _BandCount("Rolling Band Count", Range(1, 24)) = 7
        _BandSpeed("Rolling Band Speed", Range(-10, 10)) = 1.2
        _BandSharpness("Rolling Band Sharpness", Range(0.2, 12)) = 5

        [Header(Vignette)]
        _VignetteStrength("Vignette Strength", Range(0, 1)) = 0.45
        _VignetteRadius("Vignette Radius", Range(0.1, 1.4)) = 0.72
        _VignetteSoftness("Vignette Softness", Range(0.01, 1)) = 0.34

        [Header(CRT Shape)]
        _CornerRounding("Corner Rounding", Range(0, 0.5)) = 0.08
        _CornerSoftness("Corner Softness", Range(0.001, 0.2)) = 0.04
        _EdgeGlow("Edge Glow", Range(0, 1)) = 0.18
        _EdgeGlowWidth("Edge Glow Width", Range(0.001, 0.25)) = 0.055

        [Header(Flicker)]
        _FlickerStrength("Flicker Strength", Range(0, 1)) = 0.12
        _FlickerSpeed("Flicker Speed", Range(0, 80)) = 13
        _FlashChance("Flash Chance", Range(0, 1)) = 0.06
        _FlashStrength("Flash Strength", Range(0, 3)) = 1.2
        _FlashSpeed("Flash Check Speed", Range(0.1, 40)) = 5
        _FlashWidth("Flash Width", Range(0.001, 0.25)) = 0.055

        [Header(Color Aging)]
        _ColorBleed("Color Bleed", Range(0, 1)) = 0.18
        _GreenBias("Green Bias", Range(0, 1)) = 0.18
        _ChromaticJitter("Chromatic Jitter", Range(0, 1)) = 0.08
        _HorizontalJitter("Horizontal Jitter", Range(0, 1)) = 0.04

        [Header(Phosphor Glow)]
        _PhosphorGlowStrength("Glow Strength", Range(0, 3)) = 0.35
        _PhosphorGlowRadius("Glow Radius", Range(0, 0.08)) = 0.012
        _PhosphorGlowThreshold("Glow Threshold", Range(0, 1)) = 0.35
        _PhosphorGlowTint("Glow Tint", Color) = (0.75, 1.0, 0.86, 1)
        _PhosphorGlowAlpha("Glow Alpha", Range(0, 1)) = 0.28
        _EmissionIntensity("Bloom Emission Intensity", Range(0, 10)) = 1.0

        [Header(UI Mask)]
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;

            fixed4 _Color;
            float _Opacity;
            float _Brightness;
            float _Contrast;

            float _NoiseStrength;
            float _NoiseScale;
            float _NoiseSpeed;
            float _SnowAmount;
            float _SnowThreshold;

            float _ScanlineStrength;
            float _ScanlineCount;
            float _ScanlineSpeed;
            float _ScanlineSharpness;

            float _BandStrength;
            float _BandCount;
            float _BandSpeed;
            float _BandSharpness;

            float _VignetteStrength;
            float _VignetteRadius;
            float _VignetteSoftness;

            float _CornerRounding;
            float _CornerSoftness;
            float _EdgeGlow;
            float _EdgeGlowWidth;

            float _FlickerStrength;
            float _FlickerSpeed;
            float _FlashChance;
            float _FlashStrength;
            float _FlashSpeed;
            float _FlashWidth;

            float _ColorBleed;
            float _GreenBias;
            float _ChromaticJitter;
            float _HorizontalJitter;

            float _PhosphorGlowStrength;
            float _PhosphorGlowRadius;
            float _PhosphorGlowThreshold;
            fixed4 _PhosphorGlowTint;
            float _PhosphorGlowAlpha;
            float _EmissionIntensity;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float RoundedRectMask(float2 uv, float radius, float softness)
            {
                float2 p = abs(uv - 0.5) * 2.0;
                float2 q = p - (1.0 - radius * 2.0);
                float outside = length(max(q, 0.0)) - radius * 2.0;
                return 1.0 - smoothstep(0.0, max(softness, 0.0001), outside);
            }

            float3 ExtractGlow(float4 col)
            {
                float brightness = max(max(col.r, col.g), col.b) * col.a;
                float glowMask = smoothstep(_PhosphorGlowThreshold, 1.0, brightness);
                float saturation = max(max(col.r, col.g), col.b) - min(min(col.r, col.g), col.b);
                glowMask *= lerp(0.65, 1.25, saturate(saturation * 2.0));
                return col.rgb * glowMask;
            }

            float3 SamplePhosphorGlow(float2 uv, float radius)
            {
                if (_PhosphorGlowStrength <= 0.0 || radius <= 0.0)
                {
                    return 0.0;
                }

                float2 r = float2(radius, 0.0);
                float2 u = float2(0.0, radius);
                float2 d1 = float2(radius * 0.7071, radius * 0.7071);
                float2 d2 = float2(radius * 0.7071, -radius * 0.7071);

                float3 glow = ExtractGlow(tex2D(_MainTex, uv)) * 0.22;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv + r))) * 0.12;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv - r))) * 0.12;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv + u))) * 0.12;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv - u))) * 0.12;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv + d1))) * 0.075;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv - d1))) * 0.075;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv + d2))) * 0.075;
                glow += ExtractGlow(tex2D(_MainTex, saturate(uv - d2))) * 0.075;

                return glow * _PhosphorGlowStrength * _PhosphorGlowTint.rgb;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.worldPosition = v.vertex;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = saturate(i.uv);
                float time = _Time.y;
                fixed4 source = tex2D(_MainTex, uv) + _TextureSampleAdd;

                float jitterSeed = floor(time * 24.0);
                float lineNoise = Hash12(float2(floor(uv.y * 180.0), jitterSeed));
                float horizontalJitter = (lineNoise - 0.5) * _HorizontalJitter * 0.025;
                float2 noiseUv = uv + float2(horizontalJitter, 0.0);

                float noiseFrame = floor(time * max(_NoiseSpeed, 0.01));
                float staticNoise = Hash12(floor(noiseUv * _NoiseScale) + noiseFrame);
                float fineNoise = Hash12(noiseUv * (_NoiseScale * 2.73) + noiseFrame * 1.37);
                float noise = lerp(staticNoise, fineNoise, 0.35);

                float snow = step(_SnowThreshold, noise) * _SnowAmount;
                float scanPhase = uv.y * _ScanlineCount + time * _ScanlineSpeed;
                float scan = pow(0.5 + 0.5 * sin(scanPhase * 6.2831853), _ScanlineSharpness);
                float scanDark = 1.0 - scan * _ScanlineStrength;

                float bandPhase = uv.y * _BandCount + time * _BandSpeed;
                float band = pow(0.5 + 0.5 * sin(bandPhase * 6.2831853), _BandSharpness);

                float flicker = (Hash12(float2(floor(time * _FlickerSpeed), 19.17)) - 0.5) * _FlickerStrength;

                float flashSlot = floor(time * _FlashSpeed);
                float flashRandom = Hash12(float2(flashSlot, 71.43));
                float flashAge = frac(time * _FlashSpeed);
                float flashMask = step(1.0 - _FlashChance, flashRandom) * (1.0 - smoothstep(0.0, _FlashWidth, flashAge));

                float2 centered = uv - 0.5;
                float vignetteDistance = length(centered);
                float vignette = smoothstep(_VignetteRadius, max(_VignetteRadius - _VignetteSoftness, 0.0001), vignetteDistance);

                float cornerMask = RoundedRectMask(uv, _CornerRounding, _CornerSoftness);
                float innerMask = RoundedRectMask(uv, _CornerRounding + _EdgeGlowWidth, _CornerSoftness + _EdgeGlowWidth);
                float edge = saturate(cornerMask - innerMask);

                float chromaA = Hash12(float2(floor((uv.x + time * 0.12) * 48.0), floor(uv.y * 32.0)));
                float chromaB = Hash12(float2(floor((uv.y - time * 0.09) * 64.0), floor(uv.x * 19.0)));
                float colorShift = (chromaA - chromaB) * _ChromaticJitter;

                float3 agedColor = _Color.rgb;
                agedColor.r += colorShift * 0.8 + _ColorBleed * 0.08;
                agedColor.g += _GreenBias * 0.18;
                agedColor.b -= _GreenBias * 0.12 + colorShift * 0.6;
                agedColor = saturate(agedColor);
                float3 phosphorGlow = SamplePhosphorGlow(uv, _PhosphorGlowRadius);

                float staticValue = (noise - 0.5) * 2.0 * _NoiseStrength;
                float signal = _Brightness + staticValue + snow + flicker + band * _BandStrength + flashMask * _FlashStrength;
                signal *= scanDark;
                signal = (signal - 0.5) * _Contrast + 0.5;
                signal *= lerp(1.0 - _VignetteStrength, 1.0, vignette);

                float edgeGlow = edge * _EdgeGlow;
                float3 baseRgb = saturate(agedColor * signal + edgeGlow);
                float3 hdrGlow = phosphorGlow * _EmissionIntensity;
                float3 rgb = baseRgb + hdrGlow;

                float alpha = _Opacity;
                alpha += _NoiseStrength * 0.16;
                alpha += snow * 0.35;
                alpha += band * _BandStrength * 0.15;
                alpha += flashMask * saturate(_FlashStrength) * 0.35;
                alpha += max(max(phosphorGlow.r, phosphorGlow.g), phosphorGlow.b) * _PhosphorGlowAlpha;
                alpha += edgeGlow;
                alpha *= source.a;
                alpha *= cornerMask;
                alpha *= i.color.a;

                return fixed4(rgb, saturate(alpha));
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
