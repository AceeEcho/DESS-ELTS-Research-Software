// Synthetic target presentation. Surface markings stay fixed to the sphere,
// while the highlight follows the viewing angle; neither affects hit geometry.
Shader "ELTS/Development Target"
{
    Properties
    {
        _BaseColor ("Target color", Color) = (1, 0.52, 0.09, 1)
        _MarkColor ("Surface marking color", Color) = (0.28, 0.095, 0.025, 1)
        _MarkStrength ("Surface marking strength", Range(0,1)) = 0.65
        _MarkWidth ("Surface marking width (normal units)", Range(0.005,0.08)) = 0.018
        _LightDirection ("Key light direction (world space)", Vector) = (-0.45,0.7,-0.8,0)
        _Ambient ("Ambient fill", Range(0,1)) = 0.22
        _Diffuse ("Key light strength", Range(0,2)) = 0.8
        _Specular ("Highlight strength", Range(0,1)) = 0.55
        _Shininess ("Highlight focus", Range(4,128)) = 40
        _Rim ("Edge fill", Range(0,0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "TargetSurface"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MarkColor;
                float4 _LightDirection;
                float _MarkStrength, _MarkWidth, _Ambient, _Diffuse;
                float _Specular, _Shininess, _Rim;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 normalOS : TEXCOORD2;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalOS = input.normalOS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 localNormal = normalize(input.normalOS);
                float3 light = normalize(_LightDirection.xyz);
                float3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // A latitude band and two meridians give the eye fixed references
                // that wrap around the sphere. Derivatives soften subpixel seams.
                float latitude = abs(localNormal.y - 0.22);
                float meridian = min(abs(localNormal.x), abs(localNormal.z));
                float distanceToMark = min(latitude, meridian);
                float aa = max(fwidth(distanceToMark), 0.001);
                float marking = 1 - smoothstep(_MarkWidth-aa, _MarkWidth+aa, distanceToMark);
                float3 baseColor = lerp(_BaseColor.rgb, _MarkColor.rgb, marking*_MarkStrength);

                // World-fixed illumination reveals curvature without needing a
                // scene Light. Both administrator and participant cameras receive
                // the same light direction, with their own specular viewpoint.
                float diffuse = saturate(dot(normal,light));
                float3 halfway = SafeNormalize(light+view);
                float specular = pow(saturate(dot(normal,halfway)),_Shininess)*_Specular*diffuse;
                float rim = pow(1-saturate(dot(normal,view)),3)*_Rim;
                float3 color = baseColor*(_Ambient+_Diffuse*diffuse) + specular*float3(1,0.92,0.78) + rim*baseColor;
                return half4(color,1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
