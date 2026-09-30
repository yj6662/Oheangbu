"""Authored continuous granite escarpment and distance-based trail. Run in Blender."""
import bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Mountain285';MESH=OUT/'Meshes';MESH.mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
# Unity world coordinates. Control points describe an ascent, never a road cut.
nodes=np.array([[8,18,-4],[4,20,12],[-7,25,28],[-14,29,42],[-9,30,50],[-4,30.5,55],[1,32,64],[8,37,79],[13,39,90]],float)
def curve(t):
    i=min(len(nodes)-2,int(t));u=t-i
    a,b,c,d=nodes[max(0,i-1)],nodes[i],nodes[i+1],nodes[min(len(nodes)-1,i+2)]
    return .5*((2*b)+(-a+c)*u+(2*a-5*b+4*c-d)*u*u+(-a+3*b-3*c+d)*u*u*u)
dense=np.array([curve(t) for t in np.linspace(0,len(nodes)-1,3001)])
arc=np.r_[0,np.cumsum(np.linalg.norm(np.diff(dense,axis=0),axis=1))];length=float(arc[-1])
def at(s):return np.array([np.interp(s,arc,dense[:,i]) for i in range(3)])
# Fixed seed makes irregular natural shelves reproducible across rebuilds.
rng_steps=random.Random(287)
terraces=[]
for begin,end in [(17.,39.),(71.,88.)]:
    cuts=[begin]
    while cuts[-1]<end-.7:cuts.append(min(end,cuts[-1]+rng_steps.uniform(.56,1.24)))
    if cuts[-1]<end:cuts.append(end)
    for a,b in zip(cuts[:-1],cuts[1:]):
        ya,yb=at(a)[1],at(b)[1];rise=yb-ya
        jump=min(.205,max(.075,rise*rng_steps.uniform(.56,.85)))
        jump=min(jump,rise*.88)
        terraces.append(dict(a=a,b=b,ya=float(ya),yb=float(yb),jump=float(jump),skew=rng_steps.uniform(-.2,.2),bend=rng_steps.uniform(-.11,.11)))

def terrace(s):return next((t for t in terraces if t['a']<=s<t['b']),None)

def surface(s):
    p=at(s);t=terrace(s)
    if t:p[1]=t['ya']+(t['yb']-t['ya']-t['jump'])*(s-t['a'])/(t['b']-t['a'])
    return p

def side(s):
    t=at(min(length,s+.15))-at(max(0,s-.15));t[1]=0;t/=np.linalg.norm(t)
    return np.array([t[2],0,-t[0]])
def width(s):
    return 1.8+.22*math.sin(s*.13)**2+1.75*math.exp(-((s-(length-3))/4.5)**2)
bridge=(54.,59.)
samples=np.linspace(0,length,601)
profile={'points':[dict(zip(('x','y','z'),map(float,surface(s)))) for s in samples],
 'widths':[width(s) for s in samples],'distances':samples.tolist(),
 'types':[2 if bridge[0]<=s<=bridge[1] else 1 if 17<s<39 or 71<s<88 else 0 for s in samples],
 'knots':[dict(zip(('x','y','z'),map(float,p))) for p in nodes],
 'length':length,'rise':21.,'bridgeStart':bridge[0],'bridgeEnd':bridge[1]}
(OUT/'route.json').write_text(json.dumps(profile,separators=(',',':')))
(OUT/'natural-step-layout.json').write_text(json.dumps({'seed':287,'terraces':terraces,'riser_min':min(t['jump'] for t in terraces),'riser_max':max(t['jump'] for t in terraces)},indent=2))
def route_z(z):
    return np.array([np.interp(z,dense[:,2],dense[:,i]) for i in range(3)])
def summit(z):
    return np.interp(z,[-25,-10,5,20,34,48,64,80,98,114],[51,67,75,71,91,85,73,80,64,52])
def fissure(z,y):
    # Joint lengths terminate; avoid continuous horizontal stripes around a hill.
    q=z+.11*y
    planes=np.interp(q,[-60,-18,-15,7,10,35,37,61,66,97,100,140,170],
                         [1,0,3.1,.6,4.2,.3,3.3,0,4.7,.4,3.2,.5,2])
    seams=sum(a*math.exp(-((q-c)/w)**2)*max(.1,1-abs((y-cy)/span))
              for c,a,w,cy,span in [(8,1.7,.38,45,33),(37,2,.48,35,42),(65,2.4,.52,56,29),(99,1.8,.4,42,35)])
    flakes=0
    for cy,cz,extent,depth in [(35,5,9,1.4),(52,26,12,1.8),(46,72,10,1.7),(66,60,13,2)]:
        mask=max(0,1-abs((z-cz)/extent))
        dy=y-cy-.24*(z-cz)
        flakes+=depth*mask*max(0,1-abs(dy/7))
    return planes+seams-flakes

def weather(s):
    return .09*math.sin(s*1.73)+.055*math.sin(s*4.17+2)+.035*math.sin(s*8.71)

def facet(z,y,scale):
    # Piecewise planar fracture faces, not smoothed lumps or displacement waves.
    u=(z+.18*y)/scale;v=(y-.13*z)/scale;i=math.floor(u);j=math.floor(v);a=u-i;b=v-j
    def h(x,t):
        n=math.sin(x*127.1+t*311.7)*43758.5453
        return n-math.floor(n)
    if a+b<1:return h(i,j)*(1-a-b)+h(i+1,j)*a+h(i,j+1)*b
    return h(i+1,j+1)*(a+b-1)+h(i,j+1)*(1-a)+h(i+1,j)*(1-b)

def fracture(z,y):return 2.1*facet(z,y,7.3)+.7*facet(z+17,y,2.8)+.15*facet(z,y,1.05)

def cliff_x(z,y):
    # Independent geological mass: the high wall no longer extrudes the trail.
    spine=np.interp(z,[-45,-18,6,26,46,66,84,110,145],[19,11,5,17,8,22,13,26,34])
    gullies=sum(a*math.exp(-((z-c+.12*(y-40))/w)**2) for c,a,w in [(17,5,3.2),(57,6,4),(93,4,2.8)])
    return spine+.14*(y-35)+fissure(z,y)+gullies
def world_obj(name,vs,fs,smooth=True):
    me=bpy.data.meshes.new(name);me.from_pydata([(x,-z,y) for x,y,z in vs],[],fs);me.update()
    ob=bpy.data.objects.new(name,me);bpy.context.collection.objects.link(ob)
    for p in me.polygons:p.use_smooth=smooth
    return ob
stats=[]
def export(ob,name,target=None):
    clone=ob.copy();clone.data=ob.data.copy();bpy.context.collection.objects.link(clone);bpy.context.view_layer.objects.active=clone
    if target:
        clone.data.calc_loop_triangles();mod=clone.modifiers.new('Preserve_large_planes','DECIMATE');mod.ratio=min(1,target/len(clone.data.loop_triangles));bpy.ops.object.modifier_apply(modifier=mod.name)
    me=clone.data
    if not any(f.use_smooth for f in me.polygons):
        bm=bmesh.new();bm.from_mesh(me);bmesh.ops.split_edges(bm,edges=list(bm.edges));bm.to_mesh(me);bm.free();me.update()
    else:
        bm=bmesh.new();bm.from_mesh(me);bm.normal_update()
        sharp=[e for e in bm.edges if len(e.link_faces)==2 and e.calc_face_angle()>math.radians(38)]
        bmesh.ops.split_edges(bm,edges=sharp);bm.to_mesh(me);bm.free();me.update()
    me.calc_loop_triangles()
    data={'vertices':[dict(x=v.co.x,y=v.co.z,z=-v.co.y) for v in me.vertices],
          'normals':[dict(x=v.normal.x,y=v.normal.z,z=-v.normal.y) for v in me.vertices],
          'uv':[dict(x=v.co.x,y=-v.co.y) for v in me.vertices],
          'triangles':[i for f in me.loop_triangles for i in f.vertices]}
    (MESH/(name+'.json')).write_text(json.dumps(data,separators=(',',':')))
    stats.append(dict(name=name,triangles=len(me.loop_triangles)))
    bpy.data.objects.remove(clone,do_unlink=True)
def extended(s):
    if s<0:return at(0)+(at(.2)-at(0))/.2*s
    if s>length:return at(length)+(at(length)-at(length-.2))/.2*(s-length)
    # Geological surfaces must never inherit stair quantization.
    return at(s)
for part,(za,zb) in enumerate([(-20,35),(35,78),(78,length+20)]):
    cols=round((zb-za)/.22)+1;rows=241;vs=[];fs=[]
    for k in range(rows):
        v=k/(rows-1)
        for j in range(cols):
            s=za+(zb-za)*j/(cols-1);r=extended(s);z=r[2]
            y=r[1]-.45+v*(summit(z)-r[1]+.45)
            right=side(max(0,min(length,s)))
            # Worn foot has irregular pockets; a buried 45cm trail lip closes them.
            foot=r+right*(width(s)/2+.11+.07*math.sin(s*.72))
            rise=max(0,y-r[1]);blend=min(1,max(0,(rise-8)/27));blend=blend*blend*(3-2*blend)
            lower=foot[0]+.065*rise+.24*fissure(z,y)*min(1,rise/3)
            q=foot.copy();q[0]=(1-blend)*lower+blend*cliff_x(foot[2],y)
            q[0]+=fracture(z,y)*min(1,rise/1.8)
            q[1]=y;q[2]=foot[2]
            vs.append(q.tolist())
    for k in range(rows-1):
        for j in range(cols-1):
            a=k*cols+j;fs.extend([(a,a+1,a+cols),(a+1,a+cols+1,a+cols)])
    wall=world_obj('Granite_face_'+str(part),vs,fs)
    for level,target in enumerate([55000,17000,3500]):export(wall,wall.name+'_LOD'+str(level),target)
    # A deep outer face gives the trail shelf real thickness over the valley.
    vs=[];fs=[];rows=130
    for k in range(rows):
        v=k/(rows-1)
        for j in range(cols):
            s=za+(zb-za)*j/(cols-1);r=extended(s);z=r[2];y=-92+v*(r[1]+91.97)
            depth=r[1]-y
            # The outer face fans out beneath the stairs. A visible rock apron,
            # not a vertical extrusion hidden directly below the walking edge.
            apron=depth*(.86+.07*math.sin(s*.065))/(1+depth/50)+depth*.18
            joint=(.32*fissure(z,y)+.48*fracture(z,y))*min(1,depth/8)
            offset=-width(s)/2+.045-weather(s)*v**24-apron+joint
            if bridge[0]<s<bridge[1]:offset+=4*(math.sin(math.pi*(s-bridge[0])/5))**2*v**5
            q=r+side(max(0,min(length,s)))*offset;q[1]=y;vs.append(q.tolist())
    for k in range(rows-1):
        for j in range(cols-1):
            a=k*cols+j;fs.extend([(a,a+1,a+cols),(a+1,a+cols+1,a+cols)])
    ob=world_obj('Granite_foundation_'+str(part),vs,fs)
    for level,target in enumerate([30000,9000,2000]):export(ob,ob.name+'_LOD'+str(level),target)
# Closed shared shelf fronts: variable pitch/depth, sloping worn tops and
# skewed risers. The floor remains one continuous collision mesh.
for part,(sa,sb) in enumerate([(0,bridge[0]),(bridge[1],length)]):
    breaks={t['b']:t for t in terraces if sa<t['b']<sb}
    ss=sorted(set([float(s) for s in np.arange(sa,sb,.10)]+[sb]+list(breaks)))
    rows=[]
    for s in ss:
        if s in breaks:
            t=breaks[s];rows.append((s,t['yb']-t['jump'],t));rows.append((s,t['yb']-.025,t));rows.append((s+.035,t['yb'],t))
        elif not any(b<s<b+.04 for b in breaks):rows.append((s,float(surface(s)[1]),None))
    vs=[];fs=[];nc=17
    for s,y,edge in rows:
        p=at(s);p[1]=y;w=width(s);right=side(s)
        active=edge or terrace(s)
        for j in range(nc):
            t=j/(nc-1)*2-1
            lateral=t*w/2+max(0,t)**3*.45+min(0,t)**3*(weather(s)+.08)
            q=p+right*lateral;tangent=np.cross(right,np.array([0,1,0]))
            # Fronts are oblique and broken across the width, not ruler-straight.
            warp=.07*math.sin(s*.63+t*3)*t
            if active:
                target=active['skew']*t+active['bend']*math.sin(t*4)
                prev=next((b for b in terraces if abs(b['b']-active['a'])<.001),None)
                start=(prev['skew']*t+prev['bend']*math.sin(t*4)) if prev else 0
                alpha=1 if edge else (s-active['a'])/(active['b']-active['a'])
                warp=start*(1-alpha)+target*alpha
            q+=tangent*warp
            q[1]+=(.032*math.sin(s*1.31+t*4)+.015*math.sin(s*4.2-t*7))*abs(t)-.023*(1-t*t)
            vs.append(q.tolist())
    for i in range(len(rows)-1):
        for j in range(nc-1):
            a=i*nc+j;fs.extend([(a,a+nc,a+1),(a+1,a+nc,a+nc+1)])
    ob=world_obj('Carved_trail_'+str(part),vs,fs,False);export(ob,ob.name)
    # A low, intermittent bedrock shoulder, rooted into the cliff below the path.
    # It is not a uniform parapet. Larger pieces and plant pockets dress this base.
    vs=[];fs=[];cols=11;ss=np.linspace(sa,sb,round((sb-sa)/.27)+1)
    for s in ss:
        p=surface(s);right=side(s);w=width(s)
        end_fade=min(1,(s-sa)/1.3,(sb-s)/1.3)
        pocket=.13+.34*max(0,math.sin(s*.47+.8))**2+.13*max(0,math.sin(s*1.1))
        for j in range(cols):
            u=j/(cols-1);outward=-w/2+.07-u*(1.05+.3*math.sin(s*.31)**2)
            q=p+right*outward
            crown=math.sin(math.pi*u)**1.25
            q[1]+=-.10+pocket*crown*max(0,end_fade)-1.1*u**5+.06*facet(s,u*9,2)*crown
            vs.append(q.tolist())
    for i in range(len(ss)-1):
        for j in range(cols-1):
            a=i*cols+j;fs.extend([(a,a+1,a+cols),(a+1,a+cols+1,a+cols)])
    ob=world_obj('Trail_shoulder_'+str(part),vs,fs);export(ob,ob.name)
# Scanned boulder used at true near-ground scales, never enlarged into the mountain.
source=OUT/'Sources/boulder_01/boulder_01.fbx'
if source.exists():
    before=set(bpy.context.scene.objects);bpy.ops.import_scene.fbx(filepath=str(source));objects=[o for o in bpy.context.scene.objects if o not in before and o.type=='MESH']
    ob=max(objects,key=lambda o:len(o.data.polygons));bpy.context.view_layer.objects.active=ob
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    low=Vector(tuple(min(v.co[i] for v in ob.data.vertices) for i in range(3)));high=Vector(tuple(max(v.co[i] for v in ob.data.vertices) for i in range(3)))
    center=(low+high)/2;center.z=low.z
    for v in ob.data.vertices:v.co-=center
    for level,target in enumerate([18000,5000,700]):export(ob,'Scanned_boulder_LOD'+str(level),target)
    ob.hide_render=True;ob.hide_viewport=True
# Native lower mountain and connected distant ridges; protected hero corridor.
size=1025;xx,zz=np.meshgrid(np.linspace(-650,550,size),np.linspace(-350,650,size))
rx=np.interp(zz,dense[:,2],dense[:,0]);ry=np.interp(zz,dense[:,2],dense[:,1]);d=xx-rx
back=np.interp(d,[-700,-100,-20,0,15,35,65,100,170,280,800],[-110,-106,-100,-98,-75,-20,70,85,5,-100,-120])
back+=9*np.sin(zz*.019)+4*np.sin(zz*.048+xx*.011)
for az in [-8,28,69,102,170,235]:back-=9*np.exp(-((zz-az-(xx-45)*.28)/7)**2)
back=-110+(back+110)*np.exp(-((zz-42)/155)**4)
height=back
for index,(cx,cz,amp,wide) in enumerate([(-195,300,302,76),(-350,417,347,86),(-520,520,372,98)]):
    axis=cx+21*np.sin(zz*.007+cx)+9*np.sin(zz*.023+index)+4*np.sin(zz*.069+index*2)
    # Asymmetric granite escarpments: steep shoulders, narrow summits and
    # broad connected talus feet. Irregular saddles break the skyline.
    q=(xx-axis)/(wide*(1+.12*np.sin(zz*.047+index)+.055*np.sin(zz*.113)))
    q=np.abs(q)*np.where(q>0,1.12,.84)
    cross=np.interp(q,[0,.18,.38,.68,1.05,1.55,2.1],[1,.965,.85,.34,.15,.04,0])
    along=np.exp(-((zz-cz)/205)**4)*np.clip((zz-42)/100,0,1)
    knots=np.array([-20,38,65,92,116,149,174,207,241,282,316,355,393,428,470,518,570,650])+index*17
    crowns=np.array([.25,.36,.52,.79,.69,.94,.88,.66,.97,.83,.99,.76,.91,.65,.93,.80,.58,.3])
    crest=np.interp(zz,knots,crowns)
    crest+=.025*np.sin(zz*.167+index)+.013*np.sin(zz*.311+index*2)
    ridge=-130+amp*cross*along*crest
    for az,cut,w in [(127,22,4),(198,35,5),(268,28,4),(365,32,6),(443,30,5)]:
        gully=np.exp(-((zz-az-index*19-(xx-axis)*.32)/w)**2)
        ridge-=cut*gully*cross*along
    facets=(np.abs(((zz+xx*.29)/11)%2-1)-.5)*5
    # Multi-scale irregular buttresses break large flat silhouettes. These are
    # height variations in the rock, not a tiled surface texture or normal map.
    facets+=6*np.sin(zz*.131+xx*.081+index)*np.sin(xx*.147-zz*.042)
    facets+=3*np.sin(zz*.281+xx*.173)*np.sin(xx*.323-zz*.128)
    ridge+=facets*cross*along
    height=np.maximum(height,ridge)
# Conservative thermal settling in the lower slopes only, preserving crest and hero rock.
for _ in range(12):
    delta=np.zeros_like(height)
    for axis in [0,1]:
        diff=np.diff(height,axis=axis);flow=np.sign(diff)*np.maximum(np.abs(diff)-1.1,0)*.075
        if axis==0:delta[:-1]+=flow;delta[1:]-=flow
        else:delta[:,:-1]+=flow;delta[:,1:]-=flow
    mask=np.clip((20-height)/50,0,1);height+=delta*mask
height=np.clip(height,-139,240)
(OUT/'terrain.json').write_text(json.dumps({'heights':((height+140)/400).astype(float).ravel().tolist()},separators=(',',':')))
(OUT/'mesh-report.json').write_text(json.dumps(stats,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'GraniteTrail285.blend'))
print('Trail',length,'m; rise 21m; native terrain and',len(stats),'mesh assets exported.')
