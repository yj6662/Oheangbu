// #297 candidate-only screen atmosphere, a copy of Reworld292/RealmFog293 (protected, untouched) with a light-source exemption.
// Distance fog + valley mist that pools in low ground + a slight realm air tint from the authored realm pigments.
// The sky (no depth) is never changed. Light sources keep out of the fog and the valley mist: marker = normals alpha < -0.5
// (point-loaded; InkLightSource / CodexInkLantern / InkBeacon297 write -1) OR an HDR, chromatic scene colour (_EmissiveMark).
// The feature needs the Normal requirement to read the marker. Defaults = no exemption (_LightMarkerFog 0 renders as
// RealmFog293). All values are TEST (SPEC-WORLD-FINISH-297 attraction).
// #308 (TEST, every addition renders the #297 output at its default):
//  * _SkyFogFollow (SPEC-REGION-SKY-308): the far fog colour moves toward the regional sky horizon (_OhSkyFog, linear global
//    from WorldLookDriver) over the realm-air near..far band, so distant ridges meet the sky; near fog keeps the realm tint.
//  * far air of the 권역 담채 (SPEC-EVENT-WASH-308 §3): a surface inside an active zone (radius x _OhEventWashAir.x) moves the fog
//    and valley-mist colour toward the zone's meaning hue at the fog's own value by _OhEventWashAir.y x weight (LDR; achromatic
//    서사 zones do not tint the air). Off while _OhEventWashCount is 0. Light-source markers stay out of the air as before.
//  * look "PBZ-Ink" (D308-78, PLAN_look308.md; global _OhLook308 0 or unset = the output above, bit for bit): the air gets the
//    look's value and tint, starts nearer / thicker by the look's multipliers and brightens toward the sun (in-scatter, LDR);
//    the hanji fibre shows in the air; drifting motes (ash, snow, rain, spores, dust) in three world-anchored shells around
//    the eye, in front of the fog and over the sky, hidden behind nearer geometry. Motes are pigment: never brighter than
//    their own colour, no emission.
//  * 운해 band (D308-118, SPEC-FIELD-FIRST-HOUR-308 §5.1, TEST; _CloudBand308.z 0 = the output above, bit for bit): paper mist
//    in a world-height band (Gaussian around centre y, half-width w) broken into banks by a STATIC world-XZ noise - no time,
//    no wind term (DECISIONS #151 iii). Marched in 10 steps over the part of the ray inside the band, from a start distance on
//    (the near ground stays clear), and over the sky up to a far cap, so a bank reads against the horizon and peaks above the
//    band stand out of it. Pigment only (LDR, never brighter than its colour); light-source markers stay out as for the fog.
Shader "Oheangbu/Finish297/RealmFog297"
{
 Properties
 {
  _FogColor("Mist colour",Color)=(.72,.76,.73,1)
  _FogRange("Start/end metres",Vector)=(170,1250,0,0)
  _Realm293("Realm pigments (world XZ)",2D)="gray"{}
  _RealmFog293("Realm air: near/far metres, fog tint, mist tint",Vector)=(250,1400,.3,.15)
  _Mist293("Valley mist: base height, scale height, density per metre, start metres",Vector)=(95,38,.0015,70)
  _MistColor293("Valley mist colour (a = maximum)",Color)=(.82,.84,.84,.85)
  _SkyFogFollow("Far fog follows the sky horizon (_OhSkyFog; 0 = #297)",Range(0,1))=0
  [Header(Light sources)]
  _LightMarkerFog("Fog and mist removed from marked light sources",Range(0,1))=0
  _EmissiveMark("HDR emissive detection amount (0 = normals marker only)",Range(0,1))=0
  _EmissiveLo("Emissive detection: HDR peak where exemption starts",Range(.5,4))=1.1
  _EmissiveHi("Emissive detection: HDR peak of full exemption",Range(.5,8))=2
  _EmissiveChroma("Emissive detection: minimum chroma (0 = any colour)",Range(0,1))=.2
  [Header(Near hills 302)]
  _NearHillFog("Fog and mist thinned over the near-hill band (0 = off)",Range(0,1))=0
  _NearHill("Near hill band (m): in start, in full, out start, out end (same as InkWash297)",Vector)=(70,220,700,1600)
  [Header(Cloud band 308)]
  _CloudBand308("운해 band: centre y, half-width m, density per metre (0 = off), start metres",Vector)=(290,40,0,250)
  _CloudShape308("운해 shape: bank noise scale m, coverage 0-1, edge softness, sky far cap m",Vector)=(700,.5,.25,5000)
  _CloudColor308("운해 colour (a = maximum)",Color)=(.93,.92,.88,.9)
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline"} ZWrite Off ZTest Always Cull Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
   TEXTURE2D(_Realm293);SAMPLER(sampler_Realm293);
   TEXTURE2D_X(_CameraNormalsTexture);
   CBUFFER_START(UnityPerMaterial)
   float4 _FogColor,_FogRange,_RealmFog293,_Mist293,_MistColor293;
   float _LightMarkerFog,_EmissiveMark,_EmissiveLo,_EmissiveHi,_EmissiveChroma;
   float _NearHillFog;float4 _NearHill;
   float _SkyFogFollow;
   float4 _CloudBand308,_CloudShape308,_CloudColor308;
   CBUFFER_END
   // #308 globals: regional sky fog (WorldLookDriver) and the 권역 담채 zones (EventWashDriver308; arrays always 8 long)
   float4 _OhSkyFog;
   float _OhEventWashCount;float4 _OhEventWashZone[8];float4 _OhEventWashColour[8];float4 _OhEventWashBand[8];float4 _OhEventWashParams;float4 _OhEventWashAir;
   // #308 look globals (LookDriver308): Fog=(colour value x, fog amount x, fog start x, mist density x); Scatter=rgb x amount, a=power;
   // Mote=(density, size px@1080p, fall m/s, wind m/s); MoteB=(streak, near shell m, far shell m); MoteColour=rgb, a=opacity
   float _OhLook308;float4 _OhLook308Fog,_OhLook308FogTint,_OhLook308Scatter,_OhLook308Mote,_OhLook308MoteB,_OhLook308MoteColour,_OhLook308Paper;
   float Hash21_308(float2 p){p=frac(p*float2(123.34,345.45));p+=dot(p,p+34.345);return frac(p.x*p.y);}
   float Noise308(float2 p){float2 i=floor(p),f=frac(p);float2 u=f*f*(3-2*f);return lerp(lerp(Hash21_308(i),Hash21_308(i+float2(1,0)),u.x),lerp(Hash21_308(i+float2(0,1)),Hash21_308(i+1),u.x),u.y);}
   float Hash31_308(float3 p){p=frac(p*float3(.1031,.1030,.0973));p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
   // the same fibre as InkWash297 step 6 (screen space, 1080p-relative)
   float Fibre308(float2 px){float2 fp=px/max(_BlitTexture_TexelSize.w/1080.0,1e-3);return Noise308(fp*float2(.06,.9))*.55+Noise308(fp*float2(.9,.07)+17)*.25+Hash21_308(floor(fp*.5))*.2;}
   // three shells around the eye; a mote is a cell of a world-space grid (it parallaxes and does not swim with the camera's
   // rotation), drawn where the view ray crosses the shell and only when the scene surface is farther than the shell
   float3 Motes308(float3 colour,float2 uv,float dist)
   {
    float amount=saturate(_OhLook308)*_OhLook308MoteColour.a;
    if(amount<=0||_OhLook308Mote.x<=0)return colour;
    float3 eye=_WorldSpaceCameraPos;
    float3 dir=normalize(ComputeWorldSpacePosition(uv,.5,UNITY_MATRIX_I_VP)-eye);
    float cover=0;
    [unroll]for(int layer=0;layer<3;layer++)
    {
     float shell=lerp(_OhLook308MoteB.y,_OhLook308MoteB.z,layer*.5);
     float cell=max(shell*.12,.01);
     float3 p=eye+dir*shell;
     p.y+=_Time.y*_OhLook308Mote.z;p.xz+=_Time.y*_OhLook308Mote.w*float2(.8,.6);
     float3 g=p/cell;float3 id=floor(g);float3 f=frac(g)-.5;
     float on=step(shell,dist)*step(Hash31_308(id+layer*17.3),_OhLook308Mote.x);
     float3 q=f-(float3(Hash31_308(id+1.7),Hash31_308(id+5.1),Hash31_308(id+9.4))-.5)*.5;
     q.y/=max(_OhLook308MoteB.x,1);
     float r=max(_OhLook308Mote.y,.1)*.0081;
     cover+=on*(1-smoothstep(r*.4,r,length(q)))*(1-layer*.25);
    }
    return lerp(colour,_OhLook308MoteColour.rgb,saturate(cover)*amount);
   }
   // 운해 bank cover at a world XZ: two octaves of the static value noise, thresholded by the coverage (no _Time)
   float CloudCover308(float2 xz)
   {
    float2 q=xz/max(_CloudShape308.x,1);
    float n=Noise308(q)*.65+Noise308(q*2.3+31.7)*.35;
    float soft=max(_CloudShape308.z,.01);
    return smoothstep(1-_CloudShape308.y-soft,1-_CloudShape308.y+soft,n);
   }
   // opacity of the band along eye + dir * [s0, s1] (dir normalised): 10 steps over the slab centre +- 2.2 half-widths
   float CloudBand308(float3 eye,float3 dir,float s0,float s1)
   {
    if(_CloudBand308.z<=0||s1<=s0)return 0;
    float w=max(_CloudBand308.y,1),lo=_CloudBand308.x-2.2*w,hi=_CloudBand308.x+2.2*w;
    if(abs(dir.y)>1e-4)
    {
     float ta=(lo-eye.y)/dir.y,tb=(hi-eye.y)/dir.y;
     s0=max(s0,min(ta,tb));s1=min(s1,max(ta,tb));
    }
    else if(eye.y<lo||eye.y>hi)return 0;
    if(s1<=s0)return 0;
    float ds=(s1-s0)/10,optical=0;
    [unroll]for(int k=0;k<10;k++)
    {
     float3 p=eye+dir*(s0+ds*(k+.5));float u=(p.y-_CloudBand308.x)/w;
     optical+=exp(-u*u)*CloudCover308(p.xz);
    }
    return (1-exp(-optical*ds*_CloudBand308.z))*_CloudColor308.a;
   }
   float3 Cloud308(float3 colour,float3 dir,float d,float keep)
   {
    if(_CloudBand308.z<=0)return colour;
    float c=CloudBand308(_WorldSpaceCameraPos,dir,_CloudBand308.w,d)*keep;
    // a gentle fade-in over the first start..2 x start metres so a bank never begins as an edge on the ground
    float s=_CloudBand308.w;c*=smoothstep(s,2*s,d);
    return lerp(colour,_CloudColor308.rgb,c);
   }
   float3 Chroma293(float3 c){return clamp(c/max(.05,dot(c,float3(.2126,.7152,.0722))),.6,1.5);}
   float Ramp01(float x,float a,float b){float t=saturate((x-a)/max(b-a,1e-4));return t*t*(3-2*t);}
   half4 Frag(Varyings i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 source=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,i.texcoord);
    float raw=SampleSceneDepth(i.texcoord);
    #if UNITY_REVERSED_Z
    if(raw<.000001)return half4(Motes308(Cloud308(source.rgb,normalize(ComputeWorldSpacePosition(i.texcoord,.5,UNITY_MATRIX_I_VP)-_WorldSpaceCameraPos),_CloudShape308.w,1),i.texcoord,1e6),source.a);
    #else
    if(raw>.999999)return half4(Motes308(Cloud308(source.rgb,normalize(ComputeWorldSpacePosition(i.texcoord,.5,UNITY_MATRIX_I_VP)-_WorldSpaceCameraPos),_CloudShape308.w,1),i.texcoord,1e6),source.a);
    raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
    #endif
    float3 world=ComputeWorldSpacePosition(i.texcoord,raw,UNITY_MATRIX_I_VP);
    float3 eye=_WorldSpaceCameraPos;float3 ray=world-eye;float d=length(ray);
    // #308 look: fog starts nearer / thicker (fogStart = _FogRange.x and fog unchanged at look 0)
    float look=saturate(_OhLook308);float fogStart=_FogRange.x*lerp(1,_OhLook308Fog.z,look);
    float t=saturate((d-fogStart)/max(1,_FogRange.y-fogStart));
    float fog=saturate(smoothstep(0,1,t)*lerp(1,_OhLook308Fog.y,look));
    // realm air: the camera's own realm near, the seen ground's realm far (blurred mips, no hard borders)
    float2 extent=float2(4000,6000);
    float3 near=SAMPLE_TEXTURE2D_LOD(_Realm293,sampler_Realm293,saturate(eye.xz/extent),5).rgb;
    float3 far=SAMPLE_TEXTURE2D_LOD(_Realm293,sampler_Realm293,saturate(world.xz/extent),4).rgb;
    float3 tint=Chroma293(lerp(near,far,smoothstep(_RealmFog293.x,_RealmFog293.y,d)));
    float3 fogColour=_FogColor.rgb*lerp(float3(1,1,1),tint,_RealmFog293.z);
    float3 mistColour=_MistColor293.rgb*lerp(float3(1,1,1),tint,_RealmFog293.w);
    // #308 far fog follows the sky horizon (near fog keeps the realm tint)
    // (alpha 0 = no regional sky driver wrote it: keep the material fog instead of pulling toward black)
    if(_SkyFogFollow>0&&_OhSkyFog.a>.5)fogColour=lerp(fogColour,_OhSkyFog.rgb,_SkyFogFollow*smoothstep(_RealmFog293.x,_RealmFog293.y,d));
    // #308 far air over active 권역 담채 zones: hue move at the fog's own value (LDR)
    if(_OhEventWashCount>.5&&_OhEventWashAir.y>0)
    {
     int airCount=(int)min(_OhEventWashCount,8.0);float airFeather=saturate(_OhEventWashParams.x);
     float airSum=0,airChroma=0;float3 airTint=0;
     [loop]for(int z=0;z<airCount;z++)
     {
      float4 zone=_OhEventWashZone[z];float r=max(zone.z*max(_OhEventWashAir.x,1e-3),1e-3);
      float w=max(0,(1-smoothstep(r*(1-airFeather),r,distance(world.xz,zone.xy)))*zone.w);
      airSum+=w;airChroma+=w*_OhEventWashBand[z].w;airTint+=w*_OhEventWashBand[z].w*_OhEventWashColour[z].rgb;
     }
     if(airChroma>1e-4)
     {
      float3 hue=airTint/airChroma;float amount=_OhEventWashAir.y*saturate(airChroma);
      fogColour=lerp(fogColour,saturate(hue/max(dot(hue,float3(.2126,.7152,.0722)),1e-3)*dot(fogColour,float3(.2126,.7152,.0722))),amount);
      mistColour=lerp(mistColour,saturate(hue/max(dot(hue,float3(.2126,.7152,.0722)),1e-3)*dot(mistColour,float3(.2126,.7152,.0722))),amount);
     }
    }
    // #308 look: the air takes the look's value and tint and brightens toward the sun (in-scatter; LDR)
    if(look>0)
    {
     float3 lookTint=lerp(float3(1,1,1),_OhLook308FogTint.rgb*_OhLook308Fog.x,look);
     float sunAmount=pow(saturate(dot(ray/max(d,.001),_MainLightPosition.xyz)),max(_OhLook308Scatter.a,1))*look;
     fogColour=fogColour*lookTint+_OhLook308Scatter.rgb*sunAmount;
     mistColour=mistColour*lookTint+_OhLook308Scatter.rgb*sunAmount*.6;
    }
    // valley mist: density k*exp(-(y-base)/H) integrated analytically from the start distance to the surface
    float H=max(1,_Mist293.y);float s0=min(_Mist293.w,d);float dy=ray.y/max(d,.001);float y0=eye.y-_Mist293.x;
    float a=exp(-clamp((y0+dy*s0)/H,-20,20)),b=exp(-clamp((y0+dy*d)/H,-20,20));
    float optical=abs(dy)>.0005?(a-b)*H/dy:a*(d-s0);
    float mist=(1-exp(-max(0,optical)*_Mist293.z*lerp(1,_OhLook308Fog.w,look)))*_MistColor293.a;
    // light sources stay out of the air: normals marker (point load, no half-marked fringe) or HDR chromatic colour
    float marker=step(LOAD_TEXTURE2D_X(_CameraNormalsTexture,int2(i.texcoord*_BlitTexture_TexelSize.zw)).w,-.5);
    float peak=max(source.r,max(source.g,source.b)),low=min(source.r,min(source.g,source.b));
    marker=max(marker,_EmissiveMark*Ramp01(peak,_EmissiveLo,_EmissiveHi)*Ramp01((peak-low)/max(peak,1e-3),_EmissiveChroma*.5,_EmissiveChroma));
    float keep=1-_LightMarkerFog*marker;fog*=keep;mist*=keep;
    // #302 near mountains stay 농묵: less air over the near band (far ranges keep the full fade to 담묵)
    float nearHill=smoothstep(_NearHill.x,_NearHill.y,d)*(1-smoothstep(_NearHill.z,_NearHill.w,d));
    fog*=1-_NearHillFog*nearHill;mist*=1-_NearHillFog*nearHill;
    float3 colour=lerp(source.rgb,fogColour,fog);
    colour=lerp(colour,mistColour,mist);
    colour=Cloud308(colour,ray/max(d,.001),d,keep);
    // #308 look: hanji fibre in the air (fog = bare paper), then the drifting motes in front of it
    if(look>0)colour*=1+(Fibre308(i.positionCS.xy)-.5)*.06*_OhLook308Paper.x*look*saturate(fog+mist);
    return half4(Motes308(colour,i.texcoord,d),source.a);
   }
   ENDHLSL
  }
 }
}
