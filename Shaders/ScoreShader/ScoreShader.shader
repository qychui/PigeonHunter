Shader "Custom/ScoreShader"
{
    Properties
    {
        _MainTex("Number Atlas", 2D) = "white" {}
        _Rows("Rows", Float) = 1
        _Columns("Columns", Float) = 10
        _Index("Index (0 = first cell)", Float) = 0
        _PadLeft("Pad Left", Float) = 0
        _PadRight("Pad Right", Float) = 0
        _PadTop("Pad Top", Float) = 0
        _PadBottom("Pad Bottom", Float) = 0
        _BorderColor("Border Color", Color) = (1,1,1,0)
        _BorderWidth("Border Width", Range(0,0.5)) = 0.1
        _Color("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float _Rows;
            float _Columns;
            float _Index;
            float _PadLeft;
            float _PadRight;
            float _PadTop;
            float _PadBottom;
            fixed4 _BorderColor;
            float _BorderWidth;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float cols = max(_Columns, 1.0);
                float rows = max(_Rows, 1.0);

                float2 cellSize = float2(1.0 / cols, 1.0 / rows);

                float index = floor(_Index);
                index = clamp(index, 0.0, cols * rows - 1.0);

                float colIdx = fmod(index, cols);
                float rowIdx = floor(index / cols);

                float rowFromTop = (rows - 1.0) - rowIdx;

                float2 cellOffset = float2(colIdx * cellSize.x, rowFromTop * cellSize.y);

                float2 cellUV = frac(i.uv);

                float2 innerMin = float2(_PadLeft, _PadBottom);
                float2 innerMax = float2(1.0 - _PadRight, 1.0 - _PadTop);
                float2 innerSize = max(innerMax - innerMin, 0.0001);

                float2 innerUV = innerMin + cellUV * innerSize;

                float2 uv = innerUV * cellSize + cellOffset;

                fixed4 baseCol = tex2D(_MainTex, uv) * _Color;

                float2 normUV = saturate((innerUV - innerMin) / innerSize);
                float2 edgeDist2 = min(normUV, 1.0 - normUV);
                float distToEdge = min(edgeDist2.x, edgeDist2.y);

                float bw = max(_BorderWidth, 0.0001);
                float borderT = saturate(1.0 - distToEdge / bw);

                fixed4 borderCol = _BorderColor;
                float borderAlpha = borderCol.a * borderT;

                baseCol.rgb = lerp(baseCol.rgb, borderCol.rgb, borderAlpha);
                baseCol.a = max(baseCol.a, borderAlpha);

                return baseCol;
            }
            ENDCG
        }
    }

    FallBack "Unlit/Transparent"
}
