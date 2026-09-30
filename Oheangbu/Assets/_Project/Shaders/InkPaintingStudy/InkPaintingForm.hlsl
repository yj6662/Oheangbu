#ifndef OH_INK_PAINTING_FORM
#define OH_INK_PAINTING_FORM
// Optional world-XZ art direction, not a new camera effect or a silhouette test.
// RG = authored loaded ink / paper reserve, B = dry-bristle amount, A = override.
TEXTURE2D(_PaintedFormMap);
SAMPLER(sampler_PaintedFormMap);
struct IPFormData
{
    float load,reserve,dry,active;
    float2 flow;
};
IPFormData IPPrepareForm(float3 p,float4 geography)
{
    IPFormData result=(IPFormData)0;
    [branch] if(_PaintedFormEnabled>.5&&_CIEnabled>.5&&_CIDarkNear>.5&&_PaintedGeoEnabled>.5)
    {
        float relief=saturate(geography.a)*256;
        result.active=smoothstep(.001,.05,saturate(geography.r));
        float2 flow=geography.gb*2-1;
        float flowLength=length(flow);
        float2 direction=_PaintedFormDirection.xy;
        direction=dot(direction,direction)>.0001?normalize(direction):float2(.8,.6);
        result.flow=flowLength>.02?flow/max(.02,flowLength):direction;
        float side=smoothstep(-.65,.65,dot(result.flow,direction));
        float upper=CIProgress(relief,_PaintedFormRelief.xy);
        // A connected upper mass with a broad one-sided load, not bitmap islands.
        result.load=smoothstep(.20,.72,upper*lerp(.72,1,side));
        // Optional aspect contrast acts before the existing authored-map
        // override. Zero executes the original load expression unchanged.
        [branch] if(_PaintedFormAspectContrast>0)
        {
            float aspectFloor=lerp(.72,.32,saturate(_PaintedFormAspectContrast));
            result.load=smoothstep(.20,.72,upper*lerp(aspectFloor,1,side));
        }
        result.reserve=CIProgress(relief,float2(10,30))
            *(1-CIProgress(relief,_PaintedFormRelief.zw));
        result.dry=1;
        [branch] if(_PaintedFormAuthored>.5)
        {
            float2 uv=(p.xz-_PaintedFormMapRect.xy)/max(float2(1,1),_PaintedFormMapRect.zw);
            float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
            float4 art=SAMPLE_TEXTURE2D_LOD(_PaintedFormMap,sampler_PaintedFormMap,uv,0);
            float influence=saturate(art.a)*valid;
            result.load=lerp(result.load,saturate(art.r),influence);
            result.reserve=lerp(result.reserve,saturate(art.g),influence);
            result.dry=lerp(result.dry,saturate(art.b),influence);
        }
    }
    return result;
}
float IPFormReserveWeight(IPFormData form,float horizontalDistance)
{
    float2 range=float2(max(250,_PaintedFormPaperDistance.x),max(251,_PaintedFormPaperDistance.y));
    return form.active*form.reserve*CIProgress(horizontalDistance,range)
        *saturate(_PaintedFormPaper.x);
}
#endif
