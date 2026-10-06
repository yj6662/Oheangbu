// #308 walked-land function shared by the paper (Oheangbu/UI/TwiceFoldedHanji) and the brush strips (UI/MapStroke308):
// SPEC-MAP-OVERHAUL-308 §6.2 "the same function". Both read the same _FogTex (125 x 188 point-sampled cells, alpha 1 =
// not walked), so terrain, strips and the walked edge can never disagree. The body is the paper shader's KnownPaper of
// #304 QA / #307, moved here unchanged (only the helper names differ): MapFog308_Known, the SOFT edge a map without a #308
// bundle draws. A map with a bundle draws the CRISP edge of MapFog308_Edge instead (paper _MAP308 branch + strips).
// The including shader must NOT declare _FogTex, _FogTex_TexelSize, _FogSoft or _FogNoise itself.
#ifndef MAP_FOG_308_INCLUDED
#define MAP_FOG_308_INCLUDED

sampler2D _FogTex;
float4 _FogTex_TexelSize;
float _FogSoft,_FogNoise;

float MapFog308_Hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float MapFog308_Noise(float2 p)
{
    float2 i=floor(p);float2 f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(MapFog308_Hash(i),MapFog308_Hash(i+float2(1,0)),f.x),lerp(MapFog308_Hash(i+float2(0,1)),MapFog308_Hash(i+float2(1,1)),f.x),f.y);
}
// #308 CRISP walked edge (SPEC-MAP-OVERHAUL-308 §4, the ink rim of the direction sheet). Only the _MAP308 branch of the paper
// and the brush strips call it; a map without a bundle keeps MapFog308_Known below, untouched.
// #308 map fix 2 (M2: ruler-straight sides, cross blots). The edge is an ink WASH line, not a cut along the 32 m cells:
//   cover = bilinear of the four LATTICE CORNERS of the discovery cell, where a corner is 1 only when all four cells round
//           it are walked. So cover is exactly 0 on every unwalked cell and on its border (#214 by construction).
//   depth = 1 - sqrt(2 (1 - bs)), bs = quadratic B-spline of the SAME nine cells: the depth in cells behind a straight border
//           (0 on the border, 1 one cell in, = cover there), but ROUND at the corners of the cell staircase.
//   land  = min(depth, KCOVER x cover): <= 0 on every unwalked cell (cover is exactly 0 there), whatever the spline says.
//   theta = FROM + SPAN x (.5 + .5 x wave) cells, wave = two sine waves of fixed amplitude (48 m and 34 m long, 27 deg off the
//           grid) whose phase is pushed by a slow value noise (86 m lattice). theta is never under FLOOR screen px, so the
//           1 px anti-aliasing of the caller cannot reach an unwalked pixel.
//   near  = land - theta: the field of map fix 2, line for line. The edge lies 3.8 .. 19.8 m INSIDE the walked cells.
// #308 map 3 (D308-18: "2번으로 가고, 반듯하지 않게" - the BROKEN dry-brush rim, and an edge that is not evenly scalloped):
//   field = near                                  (MAPFOG308_E_DEEP 0, the default: the contour of map fix 2, 9 texture reads)
//         = min(near, dmix - theta - sw)          (MAPFOG308_E_DEEP 1, an option: BITES - 25 texture reads, the fog is read two cells deep)
//     de   = depth behind the border of the ERODED land (a cell is eroded land when it and its eight neighbours are walked),
//            linearised: equal to `depth` over the first walked cell of open land, 1 .. 2 in the second, 0 in corridors.
//     dmix = de + BO x max(depth - de, 0): >= depth everywhere; where there is no eroded land behind (corridors two cells wide,
//            rings round a hole, necks) the plain depth counts BO times, so a bite cannot close a corridor.
//     sw   = the extra inset, cells: SG (< 0: no bite, `near` rules) + SWMAX x a bite noise + BITE x a slow sweep noise. The
//            bite noise is read at the pixel's FOOT on the border (c - grad(bs) x depth / |grad|, the analytic gradient of the
//            B-spline, no ddx): it does not change along the inward normal, so the contour cannot fold into islands there.
//            One cell deep the B-spline is flat and has no foot: the bite fades to 0 over the last 1 / FK cells before it
//            (without the fade the field creases there and the caller draws a rim line INSIDE the land: 1,570 px in 80 discs).
//            sw is x saturate(CK x cover): exactly 0 on every unwalked cell, so there field = near, the map fix 2 field.
//     #214: field <= near on every pixel and field = near on every unwalked cell: no bound of map fix 2 moves. The constant
//            rule gains FROM + SPAN + SG + SWMAX + BITE < 1 (a cell whose eight neighbours are walked always shows) and SG < 0.
//   .y    = rim gate: 1 - .08 x weight, weight = the rim value s ramped over RIM0 .. RIM2 (0 .. 1, CONTINUOUS), s = RS x two
//           sine waves (phases pushed by the edge's two slow noises, crossed) + (1 - RS) x a 25 m value noise: runs and gaps of
//           unequal length, no period. Deep inside (field + GATE0 >= 1) the gate is 1: no rim. RIMBREAK 0 = weight 1 = a
//           whole rim. The paper turns .y into ink with MapFog308_Rim below (ink ramp x a rim WIDTH that follows the weight:
//           thick where the brush is pressed, thin toward the end of a run). The rim is x the walked value in the caller, so
//           no value of .y can put ink on an unwalked pixel.
// p (_M308Edge / _S308Edge, the bundle's fogEdge.crispEdge) is NOT read: the numbers below replace it, so a notation value
// cannot move the edge onto unwalked land. Everything here must stay `float` (sin of ~1300 rad).
// Texture reads: 25 (DEEP 1) or 9 (DEEP 0). Value noise: 5 (3 at DEEP 0). sin: 4. sqrt: 4 (1). No ddx / ddy but pxPerCell's.
// The caller turns the field into screen px: d = field / length(float2(ddx(field), ddy(field))).
#define MAPFOG308_E_FROM     .12                    // cells: the edge is at least 3.8 m inside a walked cell
#define MAPFOG308_E_SPAN     .50                    // cells: ... and at most 19.8 m without a bite (FROM + SPAN = .62 < 1)
#define MAPFOG308_E_KCOVER   1.5                    // slope of the lattice-corner clamp
#define MAPFOG308_E_FLOOR    1.5                    // screen px: least distance of the edge from an unwalked cell (#214 guard, >= 1.2135 needed)
#define MAPFOG308_E_D1       float2(3.742,1.907)    // wave 1: 4.2 rad per cell along 27 deg  = 47.9 m long
#define MAPFOG308_E_D2       float2(-2.679,5.257)   // wave 2: 5.9 rad per cell along 117 deg = 34.1 m long
#define MAPFOG308_E_HALF1    .7480                  // cells: half a wave of wave 1 (pi / 4.2), for the zoom-out fade
#define MAPFOG308_E_HALF2    .5325                  // cells: half a wave of wave 2 (pi / 5.9)
#define MAPFOG308_E_PHASE    3.5                    // radians of phase push per unit of noise
#define MAPFOG308_E_PK       .37                    // phase noise lattice cells per fog cell (86 m)
#define MAPFOG308_E_DEEP     0                      // 0 = the contour of map fix 2 (9 reads; stage default), 1 = bites (25 reads, two cells deep; OPTION:
                                                    // it missed three of its targets - README 3 - and is kept for the user's eye)
#define MAPFOG308_E_SK       1.0                    // bite noise lattice cells per fog cell (32 m lattice: bites 40 - 130 m long)
#define MAPFOG308_E_S0       .35                    // bite: the noise band S0 .. S1 maps to 0 .. SWMAX
#define MAPFOG308_E_S1       .90
#define MAPFOG308_E_SG       -.05                   // cells: extra inset on calm borders (< 0: `near` rules there)
#define MAPFOG308_E_SWMAX    .26                    // cells: the deepest bite
#define MAPFOG308_E_BITE     .10                    // cells: the slow sweep on top (FROM + SPAN + SG + SWMAX + BITE = .93 < 1)
#define MAPFOG308_E_BK       .25                    // sweep noise lattice cells per fog cell (128 m)
#define MAPFOG308_E_B0       .45                    // sweep: the noise band B0 .. B1 maps to 0 .. BITE
#define MAPFOG308_E_B1       .85
#define MAPFOG308_E_BO       1.70                   // where there is no eroded land behind, the plain depth counts BO times
#define MAPFOG308_E_HALF3    .94                    // cells: the extra inset fades out when zoomed far out (cells under ~4 px)
#define MAPFOG308_E_FK       2.5                    // the bite fades out over the last 1 / FK cells before one cell deep (no foot there)
#define MAPFOG308_E_FE       .02                    // keeps the foot finite where the B-spline goes flat (one cell deep)
#define MAPFOG308_E_CK       4.0                    // the extra inset is x saturate(CK x cover): exactly 0 on every unwalked cell
#define MAPFOG308_E_RK       1.3                    // rim noise lattice cells per fog cell (25 m)
#define MAPFOG308_E_RS       .30                    // share of the two rim waves in the rim value
#define MAPFOG308_E_RD1      float2(3.1,-2.9)       // rim wave 1: 4.24 rad per cell along -43 deg = 47 m long
#define MAPFOG308_E_RD2      float2(2.3,1.7)        // rim wave 2: 2.86 rad per cell along 36 deg  = 70 m long
#define MAPFOG308_E_RPH      3.5                    // radians of phase push of the rim waves
#define MAPFOG308_E_RF0      1.3                    // px per fog cell: at RF0 and under the rim's break pattern is folded away (a whole rim) ...
#define MAPFOG308_E_RF1      3.9                    // ... at RF1 and over it is all there. 1.3 / 3.9 (= RK / 3 RK, the stage default) keeps the pattern at every zoom
                                                    // the game has: on the whole-world page (4 px per cell) the rim all but disappears. OPTION 5.0 / 10.0: a whole
                                                    // thin rim on the whole-world page, the broken rim from the spread page's own zoom (12 px per cell) inward
#define MAPFOG308_E_RIM0     .26                    // ink: none under RIM0 ...
#define MAPFOG308_E_RIM1     .60                    // ... full above RIM1 (the taper of a run end)
#define MAPFOG308_E_RIM2     .80                    // pressure: full width at RIM2
#define MAPFOG308_E_WMIN     .25                    // rim width at no pressure, x the notation's rim width (_M308Rim.y)
#define MAPFOG308_E_WMAX     1.25                   // rim width at full pressure (3 px notation -> 3.75 px: under 4 px at 1080p)
#define MAPFOG308_E_GATE0    .60                    // no rim where field + GATE0 >= 1 (deep inside the walked land)
#define MAPFOG308_E_RIMBREAK 1.0                    // 1 = the broken dry-brush rim (D308-18), 0 = a whole ink rim
float2 MapFog308_Edge(float2 uv,float4 p)
{
    float2 ts=_FogTex_TexelSize.xy;
    float2 c=uv*_FogTex_TexelSize.zw;
    // #308 map fix (M1): ONE floor gives the cell AND the fraction inside it, and the cells are read at their texel
    // CENTRES (b). Before, the fraction was frac(c) and the taps were tex2D(uv + k * texel): the point sampler picks its
    // texel on its own (fixed point, off by a sub-texel bias), so a pixel centre lying on a cell border could take the
    // fraction of one cell and the taps of its neighbour - the corners were read one cell off along that pixel column: a
    // 1 px hairline every 32 m whenever the window put pixel centres on the borders (fixed north, 1080p: 28 px per cell).
    // With one floor either side of a border gives the same cover (k10 of a cell = k00 of the next), so it is continuous.
    float2 cell=floor(c);
    float2 f=c-cell;
    float2 b=(cell+.5)*ts;
    float w00=1-tex2D(_FogTex,b+float2(-ts.x,-ts.y)).a;
    float w10=1-tex2D(_FogTex,b+float2(0,-ts.y)).a;
    float w20=1-tex2D(_FogTex,b+float2(ts.x,-ts.y)).a;
    float w01=1-tex2D(_FogTex,b+float2(-ts.x,0)).a;
    float w11=1-tex2D(_FogTex,b).a;
    float w21=1-tex2D(_FogTex,b+float2(ts.x,0)).a;
    float w02=1-tex2D(_FogTex,b+float2(-ts.x,ts.y)).a;
    float w12=1-tex2D(_FogTex,b+float2(0,ts.y)).a;
    float w22=1-tex2D(_FogTex,b+float2(ts.x,ts.y)).a;
    float k00=min(min(w00,w10),min(w01,w11));
    float k10=min(min(w10,w20),min(w11,w21));
    float k01=min(min(w01,w11),min(w02,w12));
    float k11=min(min(w11,w21),min(w12,w22));
    float cover=lerp(lerp(k00,k10,f.x),lerp(k01,k11,f.x),f.y);
    float2 a0=.5*(1-f)*(1-f);
    float2 a2=.5*f*f;
    float2 a1=1-a0-a2;
    float bs=(w00*a0.x+w10*a1.x+w20*a2.x)*a0.y+(w01*a0.x+w11*a1.x+w21*a2.x)*a1.y+(w02*a0.x+w12*a1.x+w22*a2.x)*a2.y;
    float depth=1-sqrt(max(2*(1-bs),0));
    float land=min(depth,MAPFOG308_E_KCOVER*cover);
    float pxPerCell=1/max(length(float2(length(ddx(c)),length(ddy(c))))*.7071,1e-6);
    float n1=MapFog308_Noise(c*MAPFOG308_E_PK+5.2);
    float n2=MapFog308_Noise(c*MAPFOG308_E_PK+31.7);
    float wave1=sin(dot(c,MAPFOG308_E_D1)+MAPFOG308_E_PHASE*n1)*saturate(pxPerCell*MAPFOG308_E_HALF1*.5-.5);
    float wave2=sin(dot(c,MAPFOG308_E_D2)+MAPFOG308_E_PHASE*n2)*saturate(pxPerCell*MAPFOG308_E_HALF2*.5-.5);
    #if defined(_MAP308) && !defined(_MINI_HUD)
    // #308 map 4 (the legend swatch's DOUBLED line). The swatch is a fog of ONE row drawn by the paper's sheet variant (_MAP308 without
    // _MINI_HUD) - the only code that ever sees such a fog, so only that variant compiles this block: the minimap variant and the strips
    // (UI/MapStroke308 has neither keyword) keep the bytes they had. The two edge waves push a 2-D contour sideways; on a strip they
    // change only ACROSS the border, flatten the field there, and the caller's first-order distance (field / |gradient|) put a second
    // ink line 5 - 6 px inside the edge. On a one-row fog they are off: the swatch's edge is straight, FROM + SPAN / 2 cells in. On the
    // world's fog (188 rows) oneRow is exactly 1.0 and wave x 1.0 is the same float: the sheet's field is the strips' field, bit for bit.
    float oneRow=saturate(_FogTex_TexelSize.w-1);
    wave1*=oneRow;wave2*=oneRow;
    #endif
    float theta=MAPFOG308_E_FROM+MAPFOG308_E_SPAN*(.5+.25*(wave1+wave2));
    theta=max(theta,MAPFOG308_E_FLOOR/pxPerCell);
    float field=land-theta;
    // review 2026-10-05: a fog texture of ONE row is the legend swatch (WorldMapPresenter LegendSwatchMaterial304, 128 x 1). Its
    // rim would be whatever the rim value is at that one fixed place (no rim at all for 29 % of the hashes tried): there the
    // rim is whole and nothing is bitten. rows is exactly 1 on the world's fog (188 rows), so nothing else changes.
    float rows=saturate(_FogTex_TexelSize.w-1);
    #if MAPFOG308_E_DEEP
    // the ring of sixteen cells round the nine (v<x><y>, x and y 0 .. 4; the nine are w<x-1><y-1>)
    float v00=1-tex2D(_FogTex,b+float2(-2*ts.x,-2*ts.y)).a;
    float v10=1-tex2D(_FogTex,b+float2(-ts.x,-2*ts.y)).a;
    float v20=1-tex2D(_FogTex,b+float2(0,-2*ts.y)).a;
    float v30=1-tex2D(_FogTex,b+float2(ts.x,-2*ts.y)).a;
    float v40=1-tex2D(_FogTex,b+float2(2*ts.x,-2*ts.y)).a;
    float v01=1-tex2D(_FogTex,b+float2(-2*ts.x,-ts.y)).a;
    float v41=1-tex2D(_FogTex,b+float2(2*ts.x,-ts.y)).a;
    float v02=1-tex2D(_FogTex,b+float2(-2*ts.x,0)).a;
    float v42=1-tex2D(_FogTex,b+float2(2*ts.x,0)).a;
    float v03=1-tex2D(_FogTex,b+float2(-2*ts.x,ts.y)).a;
    float v43=1-tex2D(_FogTex,b+float2(2*ts.x,ts.y)).a;
    float v04=1-tex2D(_FogTex,b+float2(-2*ts.x,2*ts.y)).a;
    float v14=1-tex2D(_FogTex,b+float2(-ts.x,2*ts.y)).a;
    float v24=1-tex2D(_FogTex,b+float2(0,2*ts.y)).a;
    float v34=1-tex2D(_FogTex,b+float2(ts.x,2*ts.y)).a;
    float v44=1-tex2D(_FogTex,b+float2(2*ts.x,2*ts.y)).a;
    // eroded land of the nine cells: e<y>.xyz = min of the 3 x 3 cells round (x, y) - rows first (h), then columns
    float3 h0=float3(min(min(v00,v10),v20),min(min(v10,v20),v30),min(min(v20,v30),v40));
    float3 h1=float3(min(min(v01,w00),w10),min(min(w00,w10),w20),min(min(w10,w20),v41));
    float3 h2=float3(min(min(v02,w01),w11),min(min(w01,w11),w21),min(min(w11,w21),v42));
    float3 h3=float3(min(min(v03,w02),w12),min(min(w02,w12),w22),min(min(w12,w22),v43));
    float3 h4=float3(min(min(v04,v14),v24),min(min(v14,v24),v34),min(min(v24,v34),v44));
    float3 e0=min(min(h0,h1),h2);
    float3 e1=min(min(h1,h2),h3);
    float3 e2=min(min(h2,h3),h4);
    float3 ax=float3(a0.x,a1.x,a2.x);
    float bsE=dot(e0,ax)*a0.y+dot(e1,ax)*a1.y+dot(e2,ax)*a2.y;
    float de=sqrt(2*min(bsE,.5))+1-sqrt(max(2*(1-max(bsE,.5)),0));
    // the pixel's foot on the border: the analytic gradient of bs (d0, d1, d2 = the derivatives of a0, a1, a2)
    float2 d0=f-1;
    float2 d1=1-2*f;
    float2 d2=f;
    float2 g=float2((w00*d0.x+w10*d1.x+w20*d2.x)*a0.y+(w01*d0.x+w11*d1.x+w21*d2.x)*a1.y+(w02*d0.x+w12*d1.x+w22*d2.x)*a2.y,
                    (w00*a0.x+w10*a1.x+w20*a2.x)*d0.y+(w01*a0.x+w11*a1.x+w21*a2.x)*d1.y+(w02*a0.x+w12*a1.x+w22*a2.x)*d2.y);
    float2 foot=c-g*(max(depth,0)/(length(g)+MAPFOG308_E_FE));
    float bite=smoothstep(MAPFOG308_E_S0,MAPFOG308_E_S1,MapFog308_Noise(foot*MAPFOG308_E_SK+47.3))*saturate((1-depth)*MAPFOG308_E_FK);
    float sweep=smoothstep(MAPFOG308_E_B0,MAPFOG308_E_B1,MapFog308_Noise(c*MAPFOG308_E_BK+73.9));
    float sw=(MAPFOG308_E_SG+MAPFOG308_E_SWMAX*bite+MAPFOG308_E_BITE*sweep)*rows*saturate(pxPerCell*MAPFOG308_E_HALF3*.5-.5)*saturate(MAPFOG308_E_CK*cover);
    float dmix=de+MAPFOG308_E_BO*max(depth-de,0);
    field=min(field,dmix-theta-sw);
    #endif
    float fr=saturate((pxPerCell-MAPFOG308_E_RF0)/(MAPFOG308_E_RF1-MAPFOG308_E_RF0))*rows;
    float rs=.5+.25*(sin(dot(c,MAPFOG308_E_RD1)+MAPFOG308_E_RPH*n2)+sin(dot(c,MAPFOG308_E_RD2)+MAPFOG308_E_RPH*n1));
    float s=lerp(1,lerp(MapFog308_Noise(c*MAPFOG308_E_RK+23.1),rs,MAPFOG308_E_RS),fr);
    float weight=saturate((s-MAPFOG308_E_RIM0)/(MAPFOG308_E_RIM2-MAPFOG308_E_RIM0));
    float gate=max(saturate(field+MAPFOG308_E_GATE0),1-.08*lerp(1,weight,MAPFOG308_E_RIMBREAK));
    return float2(field,gate);
}
// #308 map 3: the paper's ink rim from the gate (.y of MapFog308_Edge), the paper's own edge distance (screen px) and the
// notation's rim width (_M308Rim.y, screen px). The paper multiplies it by its walked value: rim = crisp x MapFog308_Rim(...).
// ink = the gate's weight ramped to full at RIM1 (a run tapers out), width = rimPx x WMIN .. WMAX by the pressure.
float MapFog308_Rim(float y,float edgePx,float rimPx)
{
    float w=saturate((1-y)*12.5);
    float press=w*w*(3-2*w);
    float ink=saturate(w*(MAPFOG308_E_RIM2-MAPFOG308_E_RIM0)/(MAPFOG308_E_RIM1-MAPFOG308_E_RIM0));
    return ink*saturate(rimPx*lerp(MAPFOG308_E_WMIN,MAPFOG308_E_WMAX,press)-edgePx+.5);
}
// 1 = walked, 0 = not walked. uv = world uv (x / world width, z / world depth).
float MapFog308_Known(float2 uv)
{
    // Feather inward at discovered boundaries without revealing an unknown cell.
    float2 cell=frac(uv*_FogTex_TexelSize.zw);
    float here=1-tex2D(_FogTex,uv).a;
    float known=here;
    float left=tex2D(_FogTex,uv-float2(_FogTex_TexelSize.x,0)).a;
    float right=tex2D(_FogTex,uv+float2(_FogTex_TexelSize.x,0)).a;
    float bottom=tex2D(_FogTex,uv-float2(0,_FogTex_TexelSize.y)).a;
    float top=tex2D(_FogTex,uv+float2(0,_FogTex_TexelSize.y)).a;
    known*=lerp(1,smoothstep(0,.36,cell.x),left);
    known*=lerp(1,smoothstep(0,.36,1-cell.x),right);
    known*=lerp(1,smoothstep(0,.36,cell.y),bottom);
    known*=lerp(1,smoothstep(0,.36,1-cell.y),top);
    // #304 QA (_FogSoft): the 32 m cells read as square steps on the 740 px print. A bilinear field of the walked
    // cells (the fog texture is point sampled, so the four texel centres are read by hand) rounds the corners and a
    // world-anchored value noise wobbles the edge like the mockup's map_fog. Still inward only: `here` keeps every
    // unknown cell at 0, so no road or place ink leaks out of the walked land.
    float2 t=uv*_FogTex_TexelSize.zw-.5;
    float2 f=frac(t);
    float2 b=(floor(t)+.5)*_FogTex_TexelSize.xy;
    float a00=tex2D(_FogTex,b).a;
    float a10=tex2D(_FogTex,b+float2(_FogTex_TexelSize.x,0)).a;
    float a01=tex2D(_FogTex,b+float2(0,_FogTex_TexelSize.y)).a;
    float a11=tex2D(_FogTex,b+_FogTex_TexelSize.xy).a;
    float field=1-lerp(lerp(a00,a10,f.x),lerp(a01,a11,f.x),f.y);
    field+=(MapFog308_Noise(uv*_FogTex_TexelSize.zw*2.5)-.5)*_FogNoise;
    float soft=here*smoothstep(.5,.95,field);
    return lerp(known,soft,saturate(_FogSoft));
}

#endif
