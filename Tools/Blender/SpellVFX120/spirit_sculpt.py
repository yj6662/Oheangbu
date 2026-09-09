"""VFX spirit sculpt, applied in the separate Blender MCP lab only."""
import bpy,math,json
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/SpellVFX120/Blender'
U2B=Matrix(((1,0,0),(0,0,-1),(0,1,0)));B2U=U2B.inverted()
INK=(.045,.038,.026,1);PALE=(.80,.75,.63,1);COAT=(.66,.565,.385,1)

class Geometry:
    def __init__(self):self.v=[];self.f=[];self.c=[]
    def point(self,p,c=COAT):self.v.append(U2B@Vector(p));self.c.append(c);return len(self.v)-1
    def tri(self,a,b,c):self.f.append((a,b,c))
    def quad(self,a,b,c,d):self.f.extend([(a,b,c),(a,c,d)])
    def ellipsoid(self,center,scale,sides=12,rows=8,color=COAT):
        center=Vector(center);scale=Vector(scale);top=self.point(center+Vector((0,scale.y,0)),color)
        first=len(self.v)
        for r in range(1,rows):
            p=r*math.pi/rows
            for s in range(sides):
                a=s*math.tau/sides;q=Vector((math.sin(p)*math.cos(a)*scale.x,math.cos(p)*scale.y,math.sin(p)*math.sin(a)*scale.z))
                self.point(center+q,color)
        bottom=self.point(center-Vector((0,scale.y,0)),color)
        for s in range(sides):
            n=(s+1)%sides;self.tri(top,first+n,first+s)
            self.tri(bottom,first+(rows-2)*sides+s,first+(rows-2)*sides+n)
        for r in range(rows-2):
            for s in range(sides):
                n=(s+1)%sides;a=first+r*sides+s;b=first+r*sides+n;c=first+(r+1)*sides+n;d=first+(r+1)*sides+s
                self.quad(a,b,c,d)
    def tube(self,points,radii,sides=8,color=COAT,aspect=(1,1)):
        points=[Vector(p) for p in points];first=len(self.v);previous=None;right=None
        for i,(p,r) in enumerate(zip(points,radii)):
            tangent=(points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
            if previous is None:
                ref=Vector((0,1,0)) if abs(tangent.y)<.9 else Vector((1,0,0));right=ref.cross(tangent).normalized()
            else:right=previous.rotation_difference(tangent)@right;right=(right-tangent*right.dot(tangent)).normalized()
            up=tangent.cross(right).normalized();previous=tangent
            for j in range(sides):
                a=j*math.tau/sides;self.point(p+right*(math.cos(a)*r*aspect[0])+up*(math.sin(a)*r*aspect[1]),color)
        for i in range(len(points)-1):
            for j in range(sides):a=first+i*sides+j;b=first+i*sides+(j+1)%sides;c=first+(i+1)*sides+(j+1)%sides;d=first+(i+1)*sides+j;self.quad(a,b,c,d)
        a=self.point(points[0],color);z=self.point(points[-1],color)
        for j in range(sides):self.tri(a,first+(j+1)%sides,first+j);self.tri(z,first+(len(points)-1)*sides+j,first+(len(points)-1)*sides+(j+1)%sides)
    def object(self,name):
        me=bpy.data.meshes.new(name);me.from_pydata(self.v,[],self.f);me.update();ob=bpy.data.objects.new(name,me);bpy.context.scene.collection.objects.link(ob)
        for p in me.polygons:p.use_smooth=True
        col=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
        for d,c in zip(col.data,self.c):d.color=c
        me.color_attributes.active_color=col
        uv=me.uv_layers.new(name='UVMap')
        for p in me.polygons:
            for li in p.loop_indices:
                co=B2U@me.vertices[me.loops[li].vertex_index].co;uv.data[li].uv=(co.x+.5,co.z+.5)
        return ob

def apply_modifier(obj,modifier):
    for o in bpy.context.view_layer.objects:o.select_set(False)
    obj.hide_set(False);obj.select_set(True);bpy.context.view_layer.objects.active=obj
    with bpy.context.temp_override(selected_objects=[obj],selected_editable_objects=[obj],active_object=obj,object=obj):bpy.ops.object.modifier_apply(modifier=modifier.name)

def pigment_material():
    m=bpy.data.materials.get('Spirit_Ink_VertexPigment') or bpy.data.materials.new('Spirit_Ink_VertexPigment');m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Roughness'].default_value=.88
    attribute=m.node_tree.nodes.new('ShaderNodeVertexColor');attribute.layer_name='Color';m.node_tree.links.new(attribute.outputs['Color'],bs.inputs['Base Color'])
    return m

def merge_geometry(base,extra,name):
    verts=[v.co.copy() for v in base.data.vertices];faces=[tuple(p.vertices) for p in base.data.polygons]
    colors=[tuple(x.color) for x in base.data.color_attributes['Color'].data];off=len(verts)
    verts.extend(extra.v);faces.extend(tuple(off+i for i in f) for f in extra.f);colors.extend(extra.c)
    g=Geometry();g.v=verts;g.f=faces;g.c=colors;result=g.object(name);result.data.materials.append(pigment_material())
    base.hide_render=True;base.hide_set(True)
    return result

def normalize(obj):
    lo=Vector(tuple(min(v.co[k] for v in obj.data.vertices) for k in range(3)));hi=Vector(tuple(max(v.co[k] for v in obj.data.vertices) for k in range(3)))
    mid=(lo+hi)/2;size=max(hi-lo)
    for v in obj.data.vertices:v.co=(v.co-mid)/size
    obj.data.update();return {'normalization_scale':1/size,'normalization_offset_blender':list(-mid/size)}

def build_tiger(revision='B2'):
    assert 'SpellVFX120' in bpy.data.filepath
    g=Geometry()
    g.ellipsoid((0,.09,-.06),(.205,.245,.405),16,10)
    g.ellipsoid((0,.11,.255),(.20,.25,.19),14,9)
    g.ellipsoid((0,.235,.455),(.225,.205,.165),16,10)
    g.ellipsoid((0,.115,.575),(.14,.079,.065),12,8)
    for side in [-1,1]:
        g.ellipsoid((side*.164,.393,.425),(.068,.082,.047),10,7)
        # Bent shoulder/haunch chains are joined before remesh, avoiding the source's frame-flip seams.
        x=side*.132
        g.tube([(x,.03,.215),(x,-.10,.215),(x,-.23,.27),(x,-.305,.29)],[.072,.067,.050,.045],10)
        g.ellipsoid((x,-.317,.337),(.078,.043,.115),10,7)
        g.ellipsoid((x,.00,-.27),(.12,.17,.17),12,8)
        g.tube([(x,-.10,-.28),(x,-.19,-.365),(x,-.29,-.30),(x,-.316,-.235)],[.076,.068,.050,.044],10)
        g.ellipsoid((x,-.32,-.21),(.072,.043,.111),10,7)
    # A low arcing tail followed by an inward hook; silhouette differs from the source's straight antenna.
    tail=[];radii=[]
    for i in range(22):
        t=i/21;tail.append((.05+.16*math.sin(t*math.pi),.075+.34*math.sin(t*math.pi*.72),-.405-.33*t+.065*math.sin(t*math.pi*1.5)));radii.append(.034*(1-.45*t))
    g.tube(tail,radii,9)
    base=g.object('Tiger_Sculpt_Base')
    rem=base.modifiers.new('Join shoulders haunches and skull','REMESH');rem.mode='VOXEL';rem.voxel_size=.020;rem.use_smooth_shade=True;apply_modifier(base,rem)
    smooth=base.modifiers.new('Settle joint volumes','SMOOTH');smooth.factor=.34;smooth.iterations=3;apply_modifier(base,smooth)
    tri_count=sum(len(p.vertices)-2 for p in base.data.polygons)
    if tri_count>2200:
        dec=base.modifiers.new('VFX body triangle budget','DECIMATE');dec.ratio=2200/tri_count;apply_modifier(base,dec)
    # New point colors after topology changes. A pale underside and narrow irregular flank stripes.
    for c in list(base.data.color_attributes):base.data.color_attributes.remove(c)
    col=base.data.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
    for v,cd in zip(base.data.vertices,col.data):
        u=B2U@v.co
        pigment=COAT
        if u.y<-.11 and abs(u.x)<.18:pigment=(.73,.67,.53,1)
        sidebody=u.z<.36 and u.y>-.13
        stripe=math.sin(u.z*34+abs(u.x)*7.3+u.y*2.8)
        if sidebody and stripe>.44:pigment=INK
        if u.z<-.40 and math.sin((u.y+u.z)*48)>.30:pigment=INK
        cd.color=pigment
    base.data.color_attributes.active_color=col
    detail=Geometry()
    for side in [-1,1]:
        detail.ellipsoid((side*.095,.118,.604),(.083,.061,.044),10,6,PALE)
        detail.ellipsoid((side*.120,.260,.579),(.055,.031,.022),10,6,(.68,.61,.45,1))
        detail.ellipsoid((side*.118,.259,.600),(.024,.021,.009),8,6,(.39,.25,.065,1))
        detail.ellipsoid((side*.117,.259,.609),(.013,.014,.005),8,5,INK)
        detail.ellipsoid((side*.162,.398,.469),(.042,.052,.012),9,6,(.17,.12,.07,1))
        # Brows taper at both ends and turn down near the inner corner.
        pts=[];rs=[]
        for i in range(9):
            t=i/8;pts.append((side*(.052+.144*t),.292+.035*math.sin(t*math.pi*.8),.572-.027*t));rs.append(.007+.009*math.sin(t*math.pi))
        detail.tube(pts,rs,5,INK,aspect=(1,.65))
        # Two cheek stripes and three thin curved whiskers per side.
        for j in range(2):
            pts=[(side*(.14+.055*t),.195-j*.027-.035*t,.567-.025*t) for t in [0,.25,.5,.75,1]]
            detail.tube(pts,[.009,.011,.010,.007,.0015],5,INK)
        for j in range(3):
            pts=[(side*(.085+.18*t),.125+j*.018+.03*t*(j-1),.642-.035*t*t) for t in [0,.25,.5,.75,1]]
            detail.tube(pts,[.0035,.0032,.0027,.0018,.0006],4,(.80,.755,.64,1))
        for j in [-1,0,1]:
            detail.ellipsoid((side*(.095+j*.020),.125+.015*(j%2),.648),(.0035,.004,.002),5,3,INK)
        # Short ink toe divisions help separate paws from stumps without rigging or claws.
        for rear in [False,True]:
            z=.411 if not rear else -.128
            for toe in [-1,0,1]:
                x=side*.132+toe*.032
                detail.tube([(x,-.30,z),(x,-.324,z+.012)],[.0035,.0025],4,INK)
    detail.ellipsoid((0,.149,.650),(.048,.025,.021),10,7,(.14,.085,.04,1))
    detail.ellipsoid((0,.158,.660),(.044,.014,.012),9,6,INK)
    detail.tube([(0,.13,.659),(0,.103,.659)],[.0038,.0038],5,INK)
    for side in [-1,1]:detail.tube([(0,.103,.660),(side*.032,.087,.650),(side*.057,.097,.648)],[.0038,.004,.001],5,INK)
    # Project forehead strokes onto the actual sculpt, fixing B1's buried forehead bands.
    from mathutils.bvhtree import BVHTree
    surface=BVHTree.FromPolygons([v.co for v in base.data.vertices],[list(p.vertices) for p in base.data.polygons])
    def on_forehead(x,y):
        hit=surface.ray_cast(U2B@Vector((x,y,1.0)),U2B@Vector((0,0,-1)),2.0)
        return tuple(B2U@hit[0]+Vector((0,0,.003))) if hit[0] is not None else (x,y,.58)
    for j in range(3):
        y=.327+j*.027;z=.564-j*.017
        pts=[on_forehead(-.085,y-.010),on_forehead(-.044,y+.006),on_forehead(0,y+.01),on_forehead(.044,y+.006),on_forehead(.085,y-.010)]
        detail.tube(pts,[.001,.007,.009,.007,.001],5,INK)
    # Two broad slanted temple marks meet the narrow brows, leaving the eyes readable.
    for side in [-1,1]:
        pts=[on_forehead(side*x,y) for x,y in [(.065,.305),(.13,.322),(.19,.303)]]
        detail.tube(pts,[.002,.018,.001],5,INK)
    result=merge_geometry(base,detail,'Beast_FolkTiger_'+revision);result['role']='VFX folk-painting tiger spirit; not a production creature rig'
    info=normalize(result);info.update(name=result.name,vertices=len(result.data.vertices),triangles=sum(len(p.vertices)-2 for p in result.data.polygons),materials=len(result.data.materials),skeleton_bones=0,source='UnitySource_Beast; rebuilt joined volumes after visible seams and toy-like head diagnosis',changes=['Fused shoulders/haunches/paws to remove broken joint seams','Broader forehead, round inner ears, pale cheeks and muzzle','Iris and slit pupils, tapered brows, cheek marks, whiskers','Dark flank/tail striping as exported vertex color; no extra texture dependency','Lower curved tail and four separated padded feet'])
    (OUT/('beast_sculpt_stats_'+revision+'.json')).write_text(json.dumps(info,indent=2),encoding='utf-8')
    for o in bpy.context.scene.objects:
        if o.type=='MESH':o.hide_render=o!=result
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SpellVFX120_MeshLab.blend'))
    print(json.dumps(info))
    return result

def stone_block(g,center,scale,bevel=.06,color=(.47,.405,.29,1)):
    center=Vector(center);sx,sy,sz=scale;bevel=min(bevel,sx*.45,sy*.45,sz*.45)
    outline=[(-sx+bevel,-sz),(sx-bevel,-sz),(sx,-sz+bevel),(sx,sz-bevel),(sx-bevel,sz),(-sx+bevel,sz),(-sx,sz-bevel),(-sx,-sz+bevel)]
    first=len(g.v)
    for i,y in enumerate([-sy,-sy+bevel,sy-bevel,sy]):
        factor=.86 if i in [0,3] else 1
        for j,(x,z) in enumerate(outline):
            variation=.90+.13*math.sin(j*2.71+i*1.13+center.x*8)
            c=tuple(k*variation for k in color[:3])+(1,)
            g.point(center+Vector((x*factor,y,z*factor)),c)
    for row in range(3):
        for j in range(8):g.quad(first+row*8+j,first+row*8+(j+1)%8,first+(row+1)*8+(j+1)%8,first+(row+1)*8+j)
    a=g.point(center+Vector((0,-sy,0)),color);b=g.point(center+Vector((0,sy,0)),color)
    for j in range(8):g.tri(a,first+j,first+(j+1)%8);g.tri(b,first+24+(j+1)%8,first+24+j)

def build_guardian():
    assert 'SpellVFX120' in bpy.data.filepath
    g=Geometry();stone=(.47,.405,.29,1);deep=(.10,.078,.052,1);pale=(.69,.62,.47,1)
    # Broad layered chest and a waist distinct from the tiger's horizontal body.
    stone_block(g,(0,.64,0),(.34,.34,.23),.08,stone)
    stone_block(g,(0,.30,.015),(.28,.14,.20),.055,(.40,.345,.24,1))
    for side in [-1,1]:
        stone_block(g,(side*.40,.81,.005),(.17,.16,.205),.07,(.51,.44,.31,1))
        stone_block(g,(side*.50,.59,.035),(.12,.20,.15),.05,stone)
        stone_block(g,(side*.60,.35,.14),(.19,.19,.18),.06,(.42,.37,.27,1))
        # Four worn knuckle ridges are stone anatomy, with no fingers/rig dependency.
        for j in range(4):stone_block(g,(side*(.475+j*.08),.32,.308),(.035,.11,.036),.018,(.52,.455,.32,1))
        stone_block(g,(side*.18,.10,.03),(.12,.18,.155),.05,stone)
        stone_block(g,(side*.18,-.07,.145),(.16,.075,.245),.045,(.43,.375,.26,1))
    # Long pillar face with a restrained broad cap: a stone jangseung-like spirit,
    # intentionally distinct from a horned western rock golem or the animal family.
    stone_block(g,(0,1.19,.018),(.205,.275,.175),.035,(.53,.465,.34,1))
    stone_block(g,(0,1.49,.005),(.27,.066,.21),.035,(.40,.355,.255,1))
    stone_block(g,(0,1.567,-.02),(.18,.025,.145),.021,(.47,.41,.29,1))
    for side in [-1,1]:
        g.ellipsoid((side*.10,1.282,.198),(.052,.041,.023),10,7,pale)
        g.ellipsoid((side*.10,1.280,.218),(.023,.026,.010),8,6,deep)
        # Carved roof-like eyebrow ridge; uneven tips prevent a modern robot visor.
        pts=[(side*.025,1.319,.211),(side*.07,1.352,.217),(side*.145,1.338,.209),(side*.188,1.305,.199)]
        g.tube(pts,[.013,.019,.018,.005],5,deep)
        # Moustache/incised cheek curve follows the long nose, matching the face language.
        pts=[(side*.022,1.095,.229),(side*.075,1.080,.225),(side*.147,1.109,.211),(side*.179,1.124,.205)]
        g.tube(pts,[.010,.012,.008,.002],5,deep)
    # A long wedge nose, with a blunt bottom and two recesses.
    stone_block(g,(0,1.183,.215),(.042,.113,.060),.016,(.59,.515,.365,1))
    for side in [-1,1]:g.ellipsoid((side*.030,1.089,.265),(.012,.009,.008),6,4,deep)
    stone_block(g,(0,1.015,.199),(.151,.041,.012),.009,deep)
    for i in range(6):stone_block(g,(-.121+i*.048,1.015,.215),(.018,.023,.012),.004,pale)
    # Torso fissures and a belt are contour markings rather than fake readable text.
    for side in [-1,1]:
        g.tube([(side*.015,.87,.232),(side*.09,.765,.242),(side*.16,.725,.24),(side*.13,.645,.241)],[.006,.008,.006,.002],4,deep)
    g.tube([(-.25,.345,.21),(-.08,.365,.215),(.08,.356,.215),(.25,.345,.21)],[.008,.010,.010,.008],5,deep)
    ob=g.object('StoneGuardian_Jangseung_B1');ob.data.materials.append(pigment_material())
    # Body blocks keep planar structure; faces of the inserted eyes remain smooth.
    import bmesh
    bm=bmesh.new();bm.from_mesh(ob.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(ob.data);bm.free()
    info=normalize(ob);info.update(name=ob.name,vertices=len(ob.data.vertices),triangles=sum(len(p.vertices)-2 for p in ob.data.polygons),materials=1,skeleton_bones=0,source='New Blender geometry informed by inspected Korean tal/guardian face motifs; not a scanned historical artifact',changes=['Upright two-footed stone guardian, broad layered shoulders and heavy fists','Long pillar face, raised curved eyebrows, blunt long nose and incised mouth','Short broad cap, pale eyes and individual worn stone teeth','Separate family silhouette; no shared tiger torso or tail'])
    (OUT/'stone_guardian_stats.json').write_text(json.dumps(info,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'SpellVFX120_MeshLab.blend'));print(json.dumps(info));return ob
