"""Static new-hand / actual Meshy shaft calibration. Source .blend files are never saved.

Run background Blender --factory-startup --python this.py. No Actions, retarget or clips.
Optimization uses an actual-shaft radial field; final evidence uses nearest mesh triangles
and Blender's evaluated Armature modifier, not a nominal cylinder or just socket distances.
"""
import bpy, json, math, hashlib, argparse, sys
import numpy as np
from pathlib import Path
from mathutils import Matrix, Vector, Euler
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / 'Art/PlayerV2'
OUT = ART / 'Calibration'
OUT.mkdir(parents=True, exist_ok=True)
HANDS = ART / 'DosaV2_Hands.blend'
BRUSH = ART / 'DosaBrushV2.blend'
FINGERS = ['Thumb', 'Index', 'Middle', 'Ring', 'Pinky']


def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def load():
    hashes = {'hands': digest(HANDS), 'brush': digest(BRUSH)}
    bpy.ops.wm.open_mainfile(filepath=str(HANDS))
    with bpy.data.libraries.load(str(BRUSH), link=False) as (source, target):
        target.objects = [name for name in source.objects if name.startswith('DosaBrushV2_') or name in ['GripSocket', 'TipSocket']]
    for obj in target.objects:
        if obj is not None and not obj.users_collection: bpy.context.scene.collection.objects.link(obj)
    rig = bpy.data.objects['DosaV2_Rig']; hand = bpy.data.objects['DosaV2_Hands']
    brush = bpy.data.objects['DosaBrushV2_Rig']; handle = bpy.data.objects['DosaBrushV2_Handle']; grip = bpy.data.objects['GripSocket']
    for p in rig.pose.bones: p.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    local = grip.matrix_world.inverted() @ handle.matrix_world
    handle.data.calc_loop_triangles()
    vertices = [local @ v.co for v in handle.data.vertices]
    print(json.dumps({'handle_in_grip_bounds': [[min(v[i] for v in vertices),max(v[i] for v in vertices)] for i in range(3)]}), flush=True)
    triangles = [tuple(t.vertices) for t in handle.data.loop_triangles]
    tree = BVHTree.FromPolygons(vertices, triangles, all_triangles=True)
    return hashes, rig, hand, brush, grip, tree


class Fit:
    def __init__(self, side, rig, hand, tree):
        self.side, self.rig, self.hand, self.tree = side, rig, hand, tree
        self.sign = 1 if side == 'Left' else -1
        bones = list(rig.data.bones); self.names = [b.name for b in bones]
        self.index = {name: i for i, name in enumerate(self.names)}
        self.rest = np.asarray([b.matrix_local for b in bones], dtype=np.float64)
        self.inverse = np.linalg.inv(self.rest)
        self.parent = [self.index[b.parent.name] if b.parent else -1 for b in bones]
        self.relative = np.asarray([self.inverse[self.parent[i]] @ m if self.parent[i] >= 0 else m for i, m in enumerate(self.rest)])
        self.fingers = [[self.index[side + 'Hand' + finger + str(j)] for j in range(1, 4)] for finger in FINGERS]
        transform = rig.matrix_world.inverted() @ hand.matrix_world
        original = np.asarray([transform @ v.co for v in hand.data.vertices], dtype=np.float64)
        self.ids = np.where(original[:, 0] * self.sign > .60)[0]
        row_map = {int(index): row for row,index in enumerate(self.ids)}
        hand.data.calc_loop_triangles()
        self.triangle_rows = np.asarray([[row_map[int(i)] for i in t.vertices] for t in hand.data.loop_triangles
            if all(int(i) in row_map for i in t.vertices)], dtype=np.int32)
        self.edge_rows = np.asarray([[row_map[int(i)] for i in e.vertices] for e in hand.data.edges
            if all(int(i) in row_map for i in e.vertices)], dtype=np.int32)
        self.vertices = np.concatenate([original[self.ids], np.ones((len(self.ids), 1))], axis=1)
        self.weights = np.zeros((len(self.ids), 4)); self.weight_ids = np.zeros((len(self.ids), 4), dtype=np.int32)
        names = [g.name for g in hand.vertex_groups]
        membership = np.zeros((len(self.ids), 5))
        for row, vertex_id in enumerate(self.ids):
            for column, group in enumerate(hand.data.vertices[int(vertex_id)].groups):
                if column >= 4: raise RuntimeError('Unexpected >4 skin weights')
                name = names[group.group]; self.weight_ids[row, column] = self.index[name]; self.weights[row, column] = group.weight
                for f, finger in enumerate(FINGERS):
                    if name.startswith(side + 'Hand' + finger): membership[row, f] += group.weight
        self.pools = []
        for f, indices in enumerate(self.fingers):
            base = self.rest[indices[0], :3, 3]
            end = np.asarray(bones[indices[-1]].tail_local)
            axis = end - base; t = (self.vertices[:, :3] - base) @ axis / (axis @ axis)
            self.pools.append(np.where((membership[:, f] > .45) & (t > .32))[0])
        self.grip_rest = np.asarray(rig.data.bones[side + 'BrushGrip'].matrix_local, dtype=np.float64)
        self.wrist = self.rest[self.index[side + 'Hand'], :3, 3].copy()
        self.ys = np.linspace(-.09, .09, 181); self.angles = np.linspace(-math.pi, math.pi, 513)
        field = []
        for y in self.ys:
            row = []
            for a in self.angles:
                hit = tree.ray_cast(Vector((0, y, 0)), Vector((math.cos(a), 0, math.sin(a))), .1)
                if hit[0] is None: raise RuntimeError(f'Actual shaft is not a closed radial cross-section at y={y}, angle={a}')
                row.append(math.sqrt(hit[0].x ** 2 + hit[0].z ** 2))
            field.append(row)
        self.field = np.asarray(field)
        self.lo = np.asarray([0, -65, -65, 0, 0] + [5, 5, 5] * 4 + [-.018, -.012, -.014])
        self.hi = np.asarray([85, 65, 65, 115, 110] + [100, 135, 115] * 4 + [.018, .012, .014])
        self.baseline = np.asarray([30, 15, 25, 28, 18] + [40, 58, 35] * 4 + [0, 0, 0], dtype=np.float64)
        self.calls = 0

    def grip_matrix(self, x):
        matrix = self.grip_rest.copy()
        matrix[:3, 3] = self.wrist + np.asarray([self.sign * (.077 + x[-3]), -x[-2], -.034 + x[-1]])
        return matrix

    def rotations(self, x):
        angles = [[(-x[0], x[1], x[2]), (-x[3], 0, 0), (-x[4], 0, 0)]]
        for f in range(4): angles.append([(-x[5 + f * 3 + j], 0, 0) for j in range(3)])
        return angles

    def deform(self, x):
        matrices = self.rest.copy()
        for indices, angles in zip(self.fingers, self.rotations(x)):
            for index, angle in zip(indices, angles):
                rotation = np.asarray(Euler(tuple(math.radians(v) for v in angle), 'XYZ').to_matrix().to_4x4())
                matrices[index] = matrices[self.parent[index]] @ self.relative[index] @ rotation
        skin = matrices @ self.inverse
        positioned = np.einsum('vwij,vj->vwi', skin[self.weight_ids], self.vertices)
        deformed = np.einsum('vw,vwi->vi', self.weights, positioned)[:, :3]
        grip = self.grip_matrix(x)
        return (deformed - grip[:3, 3]) @ grip[:3, :3], deformed

    def radial_distance(self, points):
        a = np.arctan2(points[:, 2], points[:, 0]); y = points[:, 1]
        fy = np.clip((y - self.ys[0]) / (self.ys[-1] - self.ys[0]) * 180, 0, 179.99999)
        fa = np.clip((a + math.pi) / math.tau * 512, 0, 511.99999)
        iy = fy.astype(int); ia = fa.astype(int); dy = fy - iy; da = fa - ia
        radius = (1 - dy) * ((1 - da) * self.field[iy, ia] + da * self.field[iy, ia + 1])
        radius += dy * ((1 - da) * self.field[iy + 1, ia] + da * self.field[iy + 1, ia + 1])
        return np.hypot(points[:, 0], points[:, 2]) - radius

    def cost(self, x, details=False):
        self.calls += 1
        points, _ = self.deform(x); distances = self.radial_distance(points) * 1000
        surface_points = np.concatenate([points, points[self.triangle_rows].mean(axis=1), points[self.edge_rows].mean(axis=1)], axis=0)
        surface_distances = self.radial_distance(surface_points) * 1000
        minimum = [float(np.min(distances[pool])) for pool in self.pools]
        contact_indices = [int(pool[np.argmin(distances[pool])]) for pool in self.pools]
        # Whole hand penetration is penalized, so a good fingertip number cannot hide a buried knuckle/palm.
        below = np.minimum(surface_distances - .12, 0)
        cost = float(np.mean(below ** 2) * 50 + np.min(surface_distances).clip(max=0) ** 2 * 500)
        for gap, weight in zip(minimum, [12, 10, 10, 4, 3]): cost += (gap - .35) ** 2 * weight
        normals = points[contact_indices][:, [0, 2]]
        normals /= np.maximum(np.linalg.norm(normals, axis=1)[:, None], 1e-8)
        opposite = [float(normals[0] @ normals[1]), float(normals[0] @ normals[2])]
        cost += sum(max(0, dot + .15) ** 2 * 20 for dot in opposite)
        cost += float(np.sum(((x[:17] - self.baseline[:17]) / 40) ** 2) * .005)
        return (cost, minimum, float(np.min(distances)), opposite) if details else cost

    def search(self, initial=None):
        best = self.baseline.copy() if initial is None else initial.copy(); score = self.cost(best)
        # Thumb opposition is a separate search because curling an abducted thumb cannot produce an opposing grasp.
        for y in [-50, -20, 20, 50]:
            for z in [-50, -20, 20, 50]:
                x = self.baseline.copy(); x[1:3] = y, z
                value = self.cost(x)
                if value < score: best, score = x, value
        for iteration, scale in enumerate([1, 1, .6, .4, .25, .15, .1, .06, .04, .025]):
            steps = np.asarray([12, 14, 14, 15, 15] + [12, 15, 12] * 4 + [.006, .004, .004]) * scale
            for sweep in range(8):
                changed = False
                for dimension in range(len(best)):
                    for direction in [-1, 1]:
                        x = best.copy(); x[dimension] = np.clip(x[dimension] + direction * steps[dimension], self.lo[dimension], self.hi[dimension])
                        value = self.cost(x)
                        if value < score - 1e-10: best, score, changed = x, value, True
                if not changed: break
            print(json.dumps({'side': self.side, 'iteration': iteration, 'calls': self.calls, 'result': self.cost(best, True)}), flush=True)
        return best

    def actual(self, x):
        for indices, angles in zip(self.fingers, self.rotations(x)):
            for index, angle in zip(indices, angles):
                pose = self.rig.pose.bones[self.names[index]]; pose.rotation_mode = 'XYZ'
                pose.rotation_euler = tuple(math.radians(v) for v in angle)
        bpy.context.view_layer.update()
        evaluated = self.hand.evaluated_get(bpy.context.evaluated_depsgraph_get()); mesh = evaluated.to_mesh()
        grip = Matrix(self.grip_matrix(x).tolist()); inverse = grip.inverted() @ self.rig.matrix_world.inverted() @ evaluated.matrix_world
        points = [inverse @ mesh.vertices[int(index)].co for index in self.ids]
        distances = []
        for point in points:
            nearest, normal, _, distance = self.tree.find_nearest(point)
            distances.append(-distance if self.inside(point) else distance)
        distances = np.asarray(distances)
        evidence = {}
        for finger, pool in zip(FINGERS, self.pools):
            index = int(pool[np.argmin(distances[pool])]); gap = float(distances[index])
            evidence[finger] = {'minimum_signed_surface_distance_m': gap,
                'maximum_penetration_m': float(max(0, -np.min(distances[pool]))),
                'contact_vertex_index': int(self.ids[index]), 'contact_in_grip_m': list(points[index]),
                'meets_primary_contact_tolerance': -.0005 <= gap <= .0015}
        # Mesh triangles, not only vertices: a thin shaft crossing a face cannot hide behind outside corners.
        local_to_row = {int(index): row for row, index in enumerate(self.ids)}
        self.hand.data.calc_loop_triangles()
        triangle_penetration = 0.0; triangle_samples = 0
        for triangle in self.hand.data.loop_triangles:
            if not all(int(index) in local_to_row for index in triangle.vertices): continue
            triangle_points = [points[local_to_row[int(index)]] for index in triangle.vertices]
            edge_length = max((triangle_points[i]-triangle_points[(i+1)%3]).length for i in range(3))
            if min(abs(distances[local_to_row[int(index)]]) for index in triangle.vertices) > edge_length+.001: continue
            subdivisions = max(2,min(48,math.ceil(edge_length/.0005)))
            for a in range(subdivisions+1):
              for b in range(subdivisions+1-a):
                bary=(a/subdivisions,b/subdivisions,1-(a+b)/subdivisions)
                point = sum((p * w for p, w in zip(triangle_points, bary)), Vector())
                nearest, normal, _, distance = self.tree.find_nearest(point)
                triangle_samples += 1
                if self.inside(point): triangle_penetration = max(triangle_penetration, distance)
        _, manual = self.deform(x)
        actual_rig = np.asarray([self.rig.matrix_world.inverted() @ evaluated.matrix_world @ mesh.vertices[int(index)].co for index in self.ids])
        deformation_error = float(np.max(np.linalg.norm(manual-actual_rig,axis=1)))
        evaluated.to_mesh_clear()
        return {'fingers': evidence, 'whole_hand_vertex_penetration_m': float(max(0,-min(distances))),
                'triangle_surface_penetration_m': triangle_penetration, 'triangle_surface_samples':triangle_samples,
                'triangle_sampling_edge_step_m':.0005, 'manual_vs_blender_skin_max_error_m': deformation_error,
                'primary_contacts_pass': all(evidence[name]['meets_primary_contact_tolerance'] for name in ['Thumb','Index','Middle'])
                    and max(0,-min(distances),triangle_penetration) <= .0005}

    def inside(self, point):
        # The gripped handle is verified star-shaped in every sampled cross-section. Ray from
        # its interior axis to the tested point gives the exact Meshy triangle boundary; this
        # does not depend on inconsistent source winding or a nearest edge's single-face normal.
        radial = Vector((point.x, 0, point.z)); radius = radial.length
        if radius < 1e-10: return True
        hit = self.tree.ray_cast(Vector((0,point.y,0)), radial/radius, .15)
        if hit[0] is None: return False
        actual_radius = math.hypot(hit[0].x,hit[0].z)
        return radius < actual_radius - 1e-7


def render(rig, hand, brush, grip, matrix, side):
    world = rig.matrix_world @ Matrix(matrix.tolist())
    brush_to_grip = brush.matrix_world.inverted() @ grip.matrix_world
    brush.matrix_world = world @ brush_to_grip.inverted()
    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH': obj.hide_render = obj not in [hand, bpy.data.objects['DosaBrushV2_Handle'], bpy.data.objects['DosaBrushV2_Bristles']]
    scene = bpy.context.scene; scene.render.engine = 'CYCLES'; scene.cycles.samples = 24; scene.cycles.use_denoising = True
    scene.render.resolution_x = 1100; scene.render.resolution_y = 1000; scene.render.resolution_percentage = 100
    scene.world.color = (.15,.15,.15); scene.view_settings.view_transform = 'AgX'
    center = world.translation.copy()
    for name, offset, energy in [('CalibrationKey',(.15,-.35,.45),80),('CalibrationFill',(-.25,.25,.15),45)]:
        light = bpy.data.objects.get(name)
        if light is None: light = bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA')); scene.collection.objects.link(light)
        light.data.energy = energy; light.data.shape = 'DISK'; light.data.size = .3; light.location = center+Vector(offset)
        light.rotation_euler = (center-light.location).to_track_quat('-Z','Y').to_euler()
    camera = bpy.data.objects.get('CalibrationCamera')
    if camera is None: camera=bpy.data.objects.new('CalibrationCamera',bpy.data.cameras.new('CalibrationCamera'));scene.collection.objects.link(camera)
    camera.data.type='ORTHO';camera.data.ortho_scale=.24;scene.camera=camera
    for name, offset in [('dorsal',(.20,-.30,.30)),('palm',(.18,-.25,-.22))]:
        if side=='Left': offset=(-offset[0],offset[1],offset[2])
        camera.location=center+Vector(offset);camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(OUT/(side+'-'+name+'.png'));bpy.ops.render.render(write_still=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--side', choices=['Right','Left','Both'],default='Right');parser.add_argument('--reuse',action='store_true');parser.add_argument('--refine',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    hashes, rig, hand, brush, grip, tree = load()
    report={'status':'STATIC_CALIBRATION_NOT_RIG_PASS','source_sha256':hashes,'production_actions_created':0,
            'signed_distance_method':'Actual evaluated skin point to nearest shaft triangle; inside classification is an exact shaft radial ray, avoiding nearest-edge single-normal sign ambiguity. Nearby triangle surfaces sampled at <=0.5mm edge spacing (48 subdivisions maximum).',
            'sides':{}}
    for side in (['Right','Left'] if args.side=='Both' else [args.side]):
        fit=Fit(side,rig,hand,tree)
        result_path=OUT/(side+'-parameters.json')
        if (args.reuse or args.refine) and result_path.exists():
            x=np.asarray(json.loads(result_path.read_text())['parameters'])
            if args.refine: x=fit.search(x)
        else: x=fit.search()
        result_path.write_text(json.dumps({'parameters':x.tolist(),'radial_objective':fit.cost(x,True)},indent=2),encoding='utf-8')
        actual=fit.actual(x)
        actual['finger_euler_xyz_degrees']={finger:[list(angle) for angle in angles] for finger,angles in zip(FINGERS,fit.rotations(x))}
        actual['grip_bone_head_rig_local_m']=fit.grip_matrix(x)[:3,3].tolist()
        actual['grip_bone_tail_rig_local_m']=(fit.grip_matrix(x)[:3,3]+fit.grip_matrix(x)[:3,1]*.035).tolist()
        actual['grip_matrix_rig_local']=fit.grip_matrix(x).tolist()
        actual['grip_matrix_hand_local']=(fit.inverse[fit.index[side+'Hand']] @ fit.grip_matrix(x)).tolist()
        actual['hand_grip_to_brush_grip_matrix']=np.eye(4).tolist()
        actual['grip_delta_from_original_m']=(fit.grip_matrix(x)[:3,3]-fit.grip_rest[:3,3]).tolist()
        report['sides'][side]=actual
        render(rig,hand,brush,grip,fit.grip_matrix(x),side)
    report['sources_unchanged']=hashes=={'hands':digest(HANDS),'brush':digest(BRUSH)}
    report['actions_in_derived_scene']=len(bpy.data.actions)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
    for side,result in report['sides'].items():
        bone=rig.data.edit_bones[side+'BrushGrip'];bone.head=result['grip_bone_head_rig_local_m'];bone.tail=result['grip_bone_tail_rig_local_m'];bone.align_roll(Vector((0,0,1)))
    bpy.ops.object.mode_set(mode='OBJECT');bpy.context.view_layer.update()
    (OUT/'hand-brush-contact-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'DosaV2_HandGrip_StaticCalibration.blend'))
    print(json.dumps(report),flush=True)


if __name__=='__main__': main()
