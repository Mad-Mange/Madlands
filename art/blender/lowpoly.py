"""
Low-poly modelling helpers for the Madlands art scripts. Runs inside Blender (bpy).

Conventions (see art/README.md):
- Meters, +Z up, models face -Y (Blender's front view). Exported for Unity as +Y up, facing +Z.
- Flat shading, no textures: every part has a named material with a plain color. Unity tints some
  materials at runtime by name (the gear regions of characters), so material names are part of the
  contract with the client.
- Rigged parts follow exactly one bone (rigid skinning), the classic low-poly look. Cloth that has to
  bend with the body (robe skirts, cloaks) passes a function instead of a bone name: it gives each
  vertex its bone weights.
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

AXES = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}

# Level of detail the builders are working at: 1.0 for LOD0, less for the coarser levels. Counts of
# sides, rows and segments go through res(); parts smaller than MIN_PART meters are left out.
DETAIL = 1.0
MIN_PART = 0.0
LODS = ((1.0, 0.0), (0.55, 0.012), (0.3, 0.03))


def res(n, minimum=3):
    """A count of sides or rows at the current level of detail."""
    return max(minimum, int(round(n * DETAIL)))


def set_lod(level):
    """Builds at level of detail `level` (0, 1, 2) from now on."""
    global DETAIL, MIN_PART
    DETAIL, MIN_PART = LODS[level]


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_color(value):
    """'#a07040' -> sRGB tuple 0..1."""
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = 30
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)


def material(name, color, metallic=0.0, roughness=0.85, emission=None):
    """A flat-colored material. `color` is sRGB ('#rrggbb' or a tuple)."""
    existing = bpy.data.materials.get(name)
    if existing is not None:
        return existing
    if isinstance(color, str):
        color = hex_color(color)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    linear = tuple(srgb_to_linear(c) for c in color) + (1.0,)
    bsdf.inputs["Base Color"].default_value = linear
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = linear
        bsdf.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = linear
    return mat


class MeshBuilder:
    """
    Collects primitive parts into one mesh. Each part has a material and optionally a bone: a bone
    name, or a function from a vertex position to {bone: weight} (weights are normalized).
    """

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.deform = self.bm.verts.layers.deform.verify()
        self.materials = []
        self.groups = []

    def _material_index(self, mat):
        if mat not in self.materials:
            self.materials.append(mat)
        return self.materials.index(mat)

    def _group_index(self, bone):
        if bone not in self.groups:
            self.groups.append(bone)
        return self.groups.index(bone)

    def add(self, verts, faces, mat, bone=None, matrix=None, closed=False, double_sided=False, smooth=False):
        """
        Adds raw geometry: `faces` index into `verts` (counter-clockwise seen from outside). `closed`
        shapes get their normals pointed outward; `double_sided` adds a back side (cloth, roofs).
        `smooth` shades the part smoothly (organic shapes, cloth) instead of flat.
        """
        return self.add_faces(verts, {mat: faces}, bone, matrix=matrix, closed=closed, double_sided=double_sided, smooth=smooth)

    def add_faces(self, verts, groups, bone=None, matrix=None, closed=False, double_sided=False, smooth=False,
                  inside=None, facing_in=False):
        """
        Like add, with faces in several materials sharing the vertices ({material: faces}), so smooth
        shading runs on across material borders. Faces point away from `inside(p)` when given.
        Parts smaller than MIN_PART are left out (the coarser levels of detail).
        """
        if matrix is not None:
            verts = [matrix @ Vector(v) for v in verts]
        if MIN_PART > 0 and len(verts) > 1:
            lo = Vector(map(min, *[Vector(v) for v in verts]))
            hi = Vector(map(max, *[Vector(v) for v in verts]))
            if (hi - lo).length < MIN_PART:
                return []
        if inside is not None:
            groups = {m: [_away(verts, f, inside(_center(verts, f)), facing_in) for f in faces] for m, faces in groups.items()}
        if double_sided:
            self.add_faces(verts, {m: [tuple(reversed(f)) for f in faces] for m, faces in groups.items()}, bone, smooth=smooth)
        bm_verts = [self.bm.verts.new(Vector(v)) for v in verts]
        if callable(bone):
            for v in bm_verts:
                weights = {b: w for b, w in bone(v.co).items() if w > 1e-4}
                total = sum(weights.values())
                for b, w in weights.items():
                    v[self.deform][self._group_index(b)] = w / total
        elif bone is not None:
            group = self._group_index(bone)
            for v in bm_verts:
                v[self.deform][group] = 1.0
        for mat, faces in groups.items():
            index = self._material_index(mat)
            for face in faces:
                try:
                    f = self.bm.faces.new([bm_verts[i] for i in face])
                except ValueError:
                    continue
                f.material_index = index
                f.smooth = smooth
        if closed:
            self._fix_normals(bm_verts)
        return bm_verts

    def box(self, center, size, mat, bone=None, top=(1.0, 1.0), rotation=None, bottom_offset=(0.0, 0.0)):
        """A box; `top` scales the +Z face (tapers), `bottom_offset` shifts the -Z face in XY."""
        sx, sy, sz = (s * 0.5 for s in size)
        tx, ty = top
        bx, by = bottom_offset
        verts = [
            (-sx + bx, -sy + by, -sz), (sx + bx, -sy + by, -sz), (sx + bx, sy + by, -sz), (-sx + bx, sy + by, -sz),
            (-sx * tx, -sy * ty, sz), (sx * tx, -sy * ty, sz), (sx * tx, sy * ty, sz), (-sx * tx, sy * ty, sz),
        ]
        faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        return self.add(verts, faces, mat, bone, _placement(center, rotation))

    def frustum(self, a, b, ra, rb, mat, bone=None, sides=6, squash=(1.0, 1.0), cap_a=True, cap_b=True, twist=0.0, smooth=False):
        """A tube from point a (radius ra) to point b (radius rb); `squash` scales the cross-section."""
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, -1, 0))
        u = axis.cross(ref).normalized()
        v = axis.cross(u).normalized()
        verts = []
        for end, radius in ((a, ra), (b, rb)):
            for i in range(sides):
                t = 2 * math.pi * i / sides + twist
                verts.append(end + u * (math.cos(t) * radius * squash[0]) + v * (math.sin(t) * radius * squash[1]))
        faces = []
        for i in range(sides):
            j = (i + 1) % sides
            faces.append((i, j, sides + j, sides + i))
        if cap_a:
            faces.append(tuple(reversed(range(sides))))
        if cap_b:
            faces.append(tuple(range(sides, 2 * sides)))
        created = self.add(verts, faces, mat, bone, smooth=smooth)
        self._fix_normals(created)
        return created

    def sphere(self, center, radius, mat, bone=None, segments=8, rings=5, rotation=None, smooth=False):
        """A low-poly UV sphere; `radius` may be a 3-vector (an ellipsoid)."""
        if not isinstance(radius, (tuple, list)):
            radius = (radius, radius, radius)
        verts = [(0, 0, -radius[2])]
        for r in range(1, rings):
            phi = math.pi * r / rings - math.pi / 2
            for s in range(segments):
                t = 2 * math.pi * s / segments
                verts.append((math.cos(phi) * math.cos(t) * radius[0], math.cos(phi) * math.sin(t) * radius[1], math.sin(phi) * radius[2]))
        verts.append((0, 0, radius[2]))
        top = len(verts) - 1
        faces = []
        for s in range(segments):
            faces.append((0, 1 + (s + 1) % segments, 1 + s))
        for r in range(rings - 2):
            for s in range(segments):
                i0 = 1 + r * segments + s
                i1 = 1 + r * segments + (s + 1) % segments
                faces.append((i0, i1, i1 + segments, i0 + segments))
        last = 1 + (rings - 2) * segments
        for s in range(segments):
            faces.append((last + s, last + (s + 1) % segments, top))
        return self.add(verts, faces, mat, bone, _placement(center, rotation), smooth=smooth)

    def loft(self, rings, mat, bone=None, cap_a=True, cap_b=True, smooth=False):
        """A closed tube through cross-sections (lists of points with the same count): torsos, sleeves."""
        n = len(rings[0])
        verts = [p for r in rings for p in r]
        faces = []
        for k in range(len(rings) - 1):
            for i in range(n):
                j = (i + 1) % n
                faces.append((k * n + i, k * n + j, (k + 1) * n + j, (k + 1) * n + i))
        if cap_a:
            faces.append(tuple(reversed(range(n))))
        if cap_b:
            faces.append(tuple(range((len(rings) - 1) * n, len(rings) * n)))
        created = self.add(verts, faces, mat, bone, smooth=smooth)
        self._fix_normals(created)
        return created

    def tube(self, sections, mat, bone=None, sides=6, twist=0.0, cap_a=True, cap_b=True, smooth=False):
        """
        A tube along a path of (point, radius) sections: limbs with muscles and cuffs. `radius` may be
        (r_u, r_v): for a limb along X, u is front-back (Y) and v is up-down (Z); along -Z, u is X and v is Y.
        """
        points = [Vector(p) for p, _ in sections]
        rings = []
        for k, (_, radius) in enumerate(sections):
            a, b = points[max(k - 1, 0)], points[min(k + 1, len(points) - 1)]
            axis = (b - a).normalized()
            ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, -1, 0))
            u = axis.cross(ref).normalized()
            v = axis.cross(u).normalized()
            ru, rv = radius if isinstance(radius, (tuple, list)) else (radius, radius)
            rings.append([points[k] + u * (math.cos(t) * ru) + v * (math.sin(t) * rv)
                          for t in (2 * math.pi * i / sides + twist for i in range(sides))])
        return self.loft(rings, mat, bone, cap_a, cap_b, smooth=smooth)

    def hull(self, points, mat, bone=None, center=(0, 0, 0), rotation=None):
        """The convex hull of `points`: chunky heads, rocks, crystals."""
        placement = _placement(center, rotation)
        bm = bmesh.new()
        for p in points:
            bm.verts.new(placement @ Vector(p))
        result = bmesh.ops.convex_hull(bm, input=bm.verts)
        for geom in result.get("geom_interior", []) + result.get("geom_unused", []):
            if isinstance(geom, bmesh.types.BMVert) and geom.is_valid:
                bm.verts.remove(geom)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        verts = [v.co.copy() for v in bm.verts]
        index = {v: i for i, v in enumerate(bm.verts)}
        faces = [tuple(index[v] for v in f.verts) for f in bm.faces]
        bm.free()
        return self.add(verts, faces, mat, bone)

    def grid(self, rows, mat, bone=None, inside=None, closed=False, facing_in=False, smooth=False):
        """
        A surface through rows of points (cloth, plates). A row of one point is a pole: a fan of
        triangles to the next row. `closed` joins each row's last point to its first. Faces point away
        from `inside(p)`, a point inside the shape near p (toward it with `facing_in`: linings).
        """
        flat, index = [], []
        for row in rows:
            index.append(list(range(len(flat), len(flat) + len(row))))
            flat.extend(Vector(p) for p in row)
        faces = []
        for k in range(len(rows) - 1):
            a, b = index[k], index[k + 1]
            if len(a) == 1 or len(b) == 1:
                pole, ring = (a[0], b) if len(a) == 1 else (b[0], a)
                for i in range(len(ring) - (0 if closed else 1)):
                    faces.append((pole, ring[i], ring[(i + 1) % len(ring)]))
                continue
            for i in range(len(a) - (0 if closed else 1)):
                j = (i + 1) % len(a)
                faces.append((a[i], a[j], b[j], b[i]))
        if inside is not None:
            faces = [_away(flat, f, inside(_center(flat, f)), facing_in) for f in faces]
        return self.add(flat, faces, mat, bone, smooth=smooth)

    def shell(self, rows, thickness, mat, inner_mat, inside, bone=None, closed=False, rim_mat=None, smooth=False):
        """
        A grid with a thickness: the surface through `rows`, a lining `thickness` closer to `inside(p)`
        and a rim between the two along the open borders (hood openings, hems, plate edges). Thickness 0
        gives cloth: the lining's faces lie on the surface's, facing the other way. Cloth that bends
        between bones needs that; with a thickness, shearing tips the surface through its lining.
        """
        def inward(p):
            p = Vector(p)
            return p + (Vector(inside(p)) - p).normalized() * thickness

        if thickness <= 0:
            self.grid(rows, mat, bone, inside, closed, smooth=smooth)
            self.grid(rows, inner_mat, bone, inside, closed, facing_in=True, smooth=smooth)
            return rows
        inner = [[inward(p) for p in row] for row in rows]
        if callable(bone):
            # The lining moves exactly like the surface over it, or the two cross when the body bends.
            surface = {_key(q): Vector(p) for row, inner_row in zip(rows, inner) for p, q in zip(row, inner_row)}
            weigh = bone
            bone = lambda p: weigh(surface.get(_key(p), p))
        self.grid(rows, mat, bone, inside, closed, smooth=smooth)
        self.grid(inner, inner_mat, bone, inside, closed, facing_in=True, smooth=smooth)
        rim_mat = rim_mat or mat
        borders = []  # (outer edge, inner edge, a point on the shell's side of the edge)
        for k, neighbour in ((0, 1), (len(rows) - 1, len(rows) - 2)):
            if len(rows[k]) > 1:
                count = len(rows[k]) - (0 if closed else 1)
                for i in range(count):
                    j = (i + 1) % len(rows[k])
                    ref = rows[neighbour][min(i, len(rows[neighbour]) - 1)]
                    borders.append(((rows[k][i], rows[k][j]), (inner[k][i], inner[k][j]), ref))
        if not closed:
            for i, neighbour in ((0, 1), (-1, -2)):
                for k in range(len(rows) - 1):
                    if len(rows[k]) == 1 or len(rows[k + 1]) == 1:
                        continue
                    borders.append(((rows[k][i], rows[k + 1][i]), (inner[k][i], inner[k + 1][i]), rows[k][neighbour]))
        for (a, b), (ia, ib), ref in borders:
            verts = [Vector(a), Vector(b), ib, ia]
            self.add(verts, [_away(verts, (0, 1, 2, 3), Vector(ref), False)], rim_mat, bone)
        return inner

    def _fix_normals(self, verts):
        faces = {f for v in verts for f in v.link_faces}
        bmesh.ops.recalc_face_normals(self.bm, faces=list(faces))

    def finish(self, collection=None, sharp_angle=None):
        """
        Builds the Blender object (materials, vertex groups) and frees the builder. With `sharp_angle`
        (degrees), smooth parts get hard edges where their faces meet at a sharper angle (plate armor).
        """
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        if sharp_angle is not None:
            mesh.set_sharp_from_angle(angle=math.radians(sharp_angle))
        for mat in self.materials:
            mesh.materials.append(mat)
        obj = bpy.data.objects.new(self.name, mesh)
        for group in self.groups:
            obj.vertex_groups.new(name=group)
        (collection or bpy.context.scene.collection).objects.link(obj)
        return obj


def ring(z, width, depth, sides=8, front=1.0, back=1.0, y=0.0, square=0.0):
    """
    A horizontal cross-section at height z for MeshBuilder.loft: `width` along X by `depth` along Y,
    the front half (-Y) stretched by `front` and the back by `back`. `square` (0..1) squares it off.
    """
    e = 2.0 / (2.0 + 3.0 * square)
    points = []
    for i in range(sides):
        t = 2 * math.pi * (i + 0.5) / sides
        c, s = math.cos(t), math.sin(t)
        px = math.copysign(abs(c) ** e, c) * width / 2
        py = math.copysign(abs(s) ** e, s) * depth / 2
        points.append((px, y + py * (back if py > 0 else front), z))
    return points


def mirrored(points):
    """The points and their mirror images in X (for symmetric hulls)."""
    out = []
    for x, y, z in points:
        out.append((x, y, z))
        if abs(x) > 1e-6:
            out.append((-x, y, z))
    return out


def _key(p):
    return tuple(round(c, 4) for c in p)


def _center(points, face):
    return sum((Vector(points[i]) for i in face), Vector()) / len(face)


def _away(points, face, ref, toward=False):
    """`face` wound so its normal points away from `ref` (toward it if `toward`)."""
    p = [Vector(points[i]) for i in face]
    normal = (p[1] - p[0]).cross(p[2] - p[0])
    if len(p) > 3:
        normal += (p[2] - p[0]).cross(p[3] - p[0])
    outward = normal.dot(_center(points, face) - Vector(ref)) >= 0
    return face if outward != toward else tuple(reversed(face))


def _placement(center, rotation):
    m = Matrix.Translation(Vector(center))
    if rotation is not None:
        m = m @ euler_matrix(rotation)
    return m


def euler_matrix(rotation):
    """Rotation in degrees about the X, then Y, then Z axis."""
    rx, ry, rz = (math.radians(a) for a in rotation)
    return (Matrix.Rotation(rz, 4, "Z") @ Matrix.Rotation(ry, 4, "Y") @ Matrix.Rotation(rx, 4, "X"))


# ---------------------------------------------------------------- armatures


def armature(name, bones, collection=None):
    """`bones`: list of (name, head, tail, parent or None). Returns the armature object."""
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    (collection or bpy.context.scene.collection).objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    for bone_name, head, tail, parent in bones:
        eb = data.edit_bones.new(bone_name)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        eb.roll = 0.0
        if parent is not None:
            eb.parent = data.edit_bones[parent]
            eb.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def bind(mesh_obj, arm_obj):
    """Parents a mesh with vertex groups (named after bones) to an armature."""
    mesh_obj.parent = arm_obj
    modifier = mesh_obj.modifiers.new("Armature", "ARMATURE")
    modifier.object = arm_obj


def attach(obj, arm_obj, bone):
    """Parents a rigid object (no skinning) to a bone, keeping its world placement."""
    world = obj.matrix_world.copy()
    obj.parent = arm_obj
    obj.parent_type = "BONE"
    obj.parent_bone = bone
    obj.matrix_world = world


# ---------------------------------------------------------------- animation


def rest_rotation(rotations):
    """[(axis, degrees), ...] applied in order, about the armature's rest axes."""
    q = Quaternion()
    for axis, degrees in rotations:
        q = Quaternion(AXES[axis], math.radians(degrees)) @ q
    return q


def mirror(rotations):
    """The same rotation for the other side (mirrored in X): Y and Z angles flip."""
    return [(axis, degrees if axis == "X" else -degrees) for axis, degrees in rotations]


def keyframe_action(arm_obj, name, keys, loop=False):
    """
    One action from `keys`: [(frame, {bone: [(axis, degrees), ...], "@root": (x, y, z)}), ...].
    Rotations are about the armature's rest axes (X = model's left, -Y = forward, Z = up) and
    relative to the rest pose; "@root" moves the first bone (the hips) in armature space.
    Every bone is keyed on every key, so clips never inherit values from each other.
    """
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    if arm_obj.animation_data is None:
        arm_obj.animation_data_create()
    arm_obj.animation_data.action = action
    bones = arm_obj.pose.bones
    root = arm_obj.data.bones[0]
    previous = {}
    for frame, pose in keys:
        for pb in bones:
            pb.rotation_mode = "QUATERNION"
            rest = pb.bone.matrix_local.to_quaternion()
            q = rest.inverted() @ rest_rotation(pose.get(pb.name, [])) @ rest
            prev = previous.get(pb.name)
            if prev is not None and prev.dot(q) < 0:
                q.negate()
            previous[pb.name] = q
            pb.rotation_quaternion = q
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=pb.name)
            if pb.bone == root:
                offset = Vector(pose.get("@root", (0, 0, 0)))
                pb.location = root.matrix_local.to_3x3().inverted() @ offset
                pb.keyframe_insert("location", frame=frame, group=pb.name)
            else:
                pb.location = (0, 0, 0)
                pb.keyframe_insert("location", frame=frame, group=pb.name)
    action.frame_range = (keys[0][0], keys[-1][0])
    action.use_frame_range = True
    action.use_cyclic = loop
    return action


def sample_action(arm_obj, name, frames, pose_at, loop=False):
    """`pose_at(t)` for t in [0, 1] -> pose dict; sampled on `frames` + 1 keys."""
    keys = [(i, pose_at(i / frames)) for i in range(frames + 1)]
    return keyframe_action(arm_obj, name, keys, loop)


# ---------------------------------------------------------------- export


def export_fbx(path, objects, animated):
    """Writes the given objects (with their children) as one FBX for Unity."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        for o in [obj] + list(obj.children_recursive):
            o.select_set(True)
    root = objects[0]
    if root.animation_data is not None:
        root.animation_data.action = None
    if root.type == "ARMATURE":
        # Unassigning an action keeps its last pose. Unity takes the exported pose as the humanoid
        # T-pose of an animation-only file, so it must be the rest pose.
        for pb in root.pose.bones:
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)
            pb.scale = (1, 1, 1)
        bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,  # Unity bakes the axis conversion (ArtImporter), the same for every model
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        use_triangles=True,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        armature_nodetype="NULL",
        use_armature_deform_only=False,
        bake_anim=animated,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=animated,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
        use_metadata=False,
    )
