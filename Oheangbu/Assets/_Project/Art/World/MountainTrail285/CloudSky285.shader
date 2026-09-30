Shader "Oheangbu/Prototype/CloudSky285" {
 Properties { _Tex("CC0 pure sky HDR",2D)="white"{} _Exposure("Exposure",Float)=.7 _Rotation("Rotation",Float)=0 }
 SubShader { Tags {"Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox"} Cull Off ZWrite Off
 Pass { HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_Tex);SAMPLER(sampler_Tex);
 CBUFFER_START(UnityPerMaterial)
 float _Exposure,_Rotation;
 CBUFFER_END
 struct A {float4 position:POSITION;};
 struct V {float4 position:SV_POSITION;float3 direction:TEXCOORD0;};
 V Vert(A i){V o;o.position=TransformObjectToHClip(i.position.xyz);o.direction=i.position.xyz;return o;}
 half4 Frag(V i):SV_Target {float3 d=normalize(i.direction);float2 uv=float2(atan2(d.z,d.x)/6.2831853+.5+_Rotation/360,1-acos(clamp(d.y,-1,1))/3.14159265);half3 c=SAMPLE_TEXTURE2D(_Tex,sampler_Tex,uv).rgb;half grey=dot(c,half3(.299,.587,.114));c=lerp(grey.xxx,c,.12)*_Exposure;c=lerp(c,half3(.75,.79,.8),.45);c=lerp(unity_FogColor.rgb,c,smoothstep(-.08,.42,d.y));return half4(c,1);}
 ENDHLSL }
 } }
