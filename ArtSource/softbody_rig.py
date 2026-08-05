# =============================================================================
#  SPELLY ZOMBIE — SOFT-BODY RIG BUILDER  (Blender 4.x / 5.x)
#
#  Select your MESH in Object Mode, press Run Script. Done.
#
#  It builds the rig the game expects:
#     Root          — rigid handle at the pivot, NOT a deform bone
#      └ D_*        — the jiggle bones the game slumps / wobbles / drifts
#  ...then paints smooth weights and caps them at 4 per vertex (Unity's max).
#
#  Works on ANY shape, not just spheres: it measures the mesh and adapts.
#  A round-ish shape gets bones radiating from the centre; a long shape
#  (plank, pin) gets them spaced along its length so it can bend.
#
#  Re-runnable: running it again on the same mesh cleanly replaces the rig.
# =============================================================================

import bpy
from mathutils import Vector

# ----------------------------- knobs ----------------------------------------
DEFORM_BONES = 6         # how many jiggle bones (radial mode caps at 6)
REACH        = 0.65      # how far they reach, as a fraction of the half-size
SPREAD       = 0.55      # radial: how far OUT each bone sits from the centre.
                         # Bones must live INSIDE the region of skin they own —
                         # all heads stacked at the middle makes every bone
                         # swing its patch in a wide arc instead of nudging it
                         # locally, and the blob pivots instead of jiggling.
STUB         = 0.30      # radial: how much further the tail reaches past the head
ROOT_LEN     = 0.08      # visual length of the rigid Root handle (metres)
MODE         = 'AUTO'    # 'AUTO' | 'RADIAL' | 'CHAIN'
SET_ORIGIN   = True      # move the pivot to the shape's centre
SHADE_SMOOTH = True      # smooth shading (a faceted blob reads as a rock)
ELONGATED_AT = 2.5       # length:width ratio that counts as "long" in AUTO
# -----------------------------------------------------------------------------


def build():
    obj = bpy.context.active_object
    if obj is None or obj.type != 'MESH':
        raise RuntimeError("Select your MESH object in Object Mode first, then run.")

    if bpy.context.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')

    notes = []

    def solo(o):
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o

    # --- 1. unhook any rig this mesh already carries -------------------------
    if obj.parent is not None and obj.parent.type == 'ARMATURE':
        notes.append("unparented from '%s' (delete it if you don't want it)"
                     % obj.parent.name)
        solo(obj)
        bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
    for m in list(obj.modifiers):
        if m.type == 'ARMATURE':
            obj.modifiers.remove(m)
            notes.append("removed an old Armature modifier")

    # --- 2. clean the transforms (the FBX-scale killer) ---------------------
    solo(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if SET_ORIGIN:
        bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')
    if SHADE_SMOOTH:
        bpy.ops.object.shade_smooth()

    # --- 3. measure the shape (local space, origin now at its centre) -------
    bb = [Vector(c) for c in obj.bound_box]
    lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    centre = (lo + hi) * 0.5
    half = (hi - lo) * 0.5
    half = Vector((max(half.x, 1e-4), max(half.y, 1e-4), max(half.z, 1e-4)))

    dims = [half.x, half.y, half.z]
    mode = MODE
    if mode == 'AUTO':
        mode = 'CHAIN' if max(dims) / min(dims) > ELONGATED_AT else 'RADIAL'

    root_len = max(ROOT_LEN, min(dims) * 0.25)

    # --- 4. build the armature ---------------------------------------------
    arm_data = bpy.data.armatures.new(obj.name + "_Rig")
    arm_obj = bpy.data.objects.new(obj.name + "_Rig", arm_data)
    bpy.context.collection.objects.link(arm_obj)
    arm_obj.matrix_world = obj.matrix_world.copy()   # share the mesh's frame

    solo(arm_obj)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    root = eb.new("Root")
    root.head = centre
    root.tail = centre + Vector((0.0, 0.0, root_len))
    root.use_deform = False          # ← the whole rig hinges on this

    made = []
    if mode == 'RADIAL':
        spec = [("D_Up", Vector((0, 0, 1))), ("D_Dn", Vector((0, 0, -1))),
                ("D_Xp", Vector((1, 0, 0))), ("D_Xn", Vector((-1, 0, 0))),
                ("D_Yp", Vector((0, 1, 0))), ("D_Yn", Vector((0, -1, 0)))]
        for name, d in spec[:min(DEFORM_BONES, 6)]:
            extent = max(abs(d.x) * half.x + abs(d.y) * half.y + abs(d.z) * half.z, 1e-3)
            # the bone SITS in the patch of skin it drives, pointing outward —
            # heads spread through the volume, never stacked at the centre
            b = eb.new(name)
            b.head = centre + d * (extent * SPREAD)
            b.tail = centre + d * (extent * min(SPREAD + STUB, 0.95))
            b.parent = root
            b.use_connect = False
            made.append(b.name)
    else:  # CHAIN — spaced along the longest axis so the shape can BEND
        axis = dims.index(max(dims))
        a = Vector((0, 0, 0))
        a[axis] = 1.0
        span = half[axis] * 2.0 * REACH
        n = max(2, DEFORM_BONES)
        step = span / n
        for i in range(n):
            t = (i + 0.5) / n - 0.5
            head = centre + a * (t * span)
            b = eb.new("D_%02d" % (i + 1))
            b.head = head
            b.tail = head + a * (step * 0.8)
            b.parent = root
            b.use_connect = False
            made.append(b.name)

    bpy.ops.object.mode_set(mode='OBJECT')

    # --- 5. weights ---------------------------------------------------------
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    arm_obj.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    weighting = "automatic (bone heat)"
    try:
        bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    except RuntimeError:
        bpy.ops.object.parent_set(type='ARMATURE_ENVELOPE')
        weighting = "ENVELOPE fallback — bone heat refused this mesh"
        notes.append("heat weighting failed: check for loose/doubled verts "
                     "(Edit Mode > M > By Distance) and re-run")

    # Unity reads at most 4 influences per vertex — match it here so what you
    # see in Blender is what you get in game.
    solo(obj)
    try:
        bpy.ops.object.vertex_group_limit_total(limit=4)
    except RuntimeError:
        pass

    # --- 6. report ----------------------------------------------------------
    print("\n" + "=" * 60)
    print("SPELLY ZOMBIE soft-body rig built")
    print("  mesh      : %s" % obj.name)
    print("  armature  : %s" % arm_obj.name)
    print("  layout    : %s" % mode)
    print("  size (m)  : %.2f x %.2f x %.2f" % (half.x * 2, half.y * 2, half.z * 2))
    print("  Root      : rigid handle, deform OFF")
    print("  deform    : %s" % ", ".join(made))
    print("  weights   : %s" % weighting)
    for n in notes:
        print("  note      : %s" % n)
    print("-" * 60)
    print("TEST: select the armature, Ctrl+Tab into Pose Mode, move a D_ bone.")
    print("EXPORT: select mesh + armature -> File > Export > FBX")
    print("        Limit to: Selected Objects  |  Armature: uncheck Add Leaf Bones")
    print("        Apply Scalings: FBX All")
    print("UNITY : Rig tab -> Generic. Prefab into Resources/Custom/FX_StateBlob")
    print("=" * 60 + "\n")

    return arm_obj


build()
