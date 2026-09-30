"""Base.glb에서 팔만 남긴 FBX를 굽는다 (Blender 헤드리스).

[지시 2026-09-30] 파괴 화면은 1인칭이라 팔만 보여야 한다. 몸통과 머리를 카메라 뒤로
밀어내는 방식(headClearance)은 주먹 거리의 상한을 만들어서, 주먹이 화면 폭의 25%를
덮는 것을 못 고친다. 아예 안 그리면 그 상한이 사라진다.

<b>뼈는 23개를 그대로 둔다.</b> AC_Demolition.controller, 아바타, 마스크 3종
(UpperBody/LowerBody/StrikeTwist), 클립이 전부 뼈를 경로 이름으로 부른다.
뼈를 줄이면 그 전부가 깨진다. 지우는 것은 정점뿐이다.

원본을 건드리지 않는다 — R_Demolition.fbx는 필드 뷰에서 계속 온몸으로 쓴다.
"""
import bpy
import bmesh
import os
import sys

SRC = sys.argv[sys.argv.index("--") + 1]
DST = sys.argv[sys.argv.index("--") + 2]

# 남길 뼈. 어깨까지 넣는 이유는 위팔이 어깨에서 잘리면 공중에 뜬 팔이 되기 때문이다.
# 어깨 자체는 카메라 뒤라 화면에 안 나온다.
KEEP = {"LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand",
        "RightShoulder", "RightArm", "RightForeArm", "RightHand"}

# 정점 하나가 남으려면 위 뼈들에 실린 가중치 합이 이 값 이상이어야 한다.
# 0.5 = "절반 이상 팔이다". 더 낮추면 몸통 쪽 정점이 딸려 오고,
# 더 높이면 어깨 이음매가 뜯긴다.
THRESHOLD = 0.5


def report(mesh_obj, gidx):
    """문턱값을 바꾸면 몇 개가 남는지 미리 보여 준다. 숫자를 보고 고르기 위함이다."""
    for t in (0.3, 0.5, 0.7, 0.9):
        n = 0
        for v in mesh_obj.data.vertices:
            w = sum(g.weight for g in v.groups if g.group in gidx)
            if w >= t:
                n += 1
        print("   문턱 %.1f -> 정점 %d / %d (%.1f%%)"
              % (t, n, len(mesh_obj.data.vertices),
                 100.0 * n / len(mesh_obj.data.vertices)))


def main():
    print("블렌더 " + bpy.app.version_string)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, "Base.glb"))

    # 스킨 안 걸린 메시(VARCO가 끼워 넣는 Icosphere)를 버린다
    for o in list(bpy.data.objects):
        if o.type == "MESH" and not any(m.type == "ARMATURE" for m in o.modifiers):
            print("버림: " + o.name)
            bpy.data.objects.remove(o, do_unlink=True)

    mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH")
    gidx = {g.index for g in mesh_obj.vertex_groups if g.name in KEEP}
    missing = KEEP - {g.name for g in mesh_obj.vertex_groups}
    if missing:
        raise SystemExit("그룹이 없다: %s" % sorted(missing))

    print("깎기 전 정점 %d  면 %d" % (len(mesh_obj.data.vertices), len(mesh_obj.data.polygons)))
    report(mesh_obj, gidx)

    # 팔이 아닌 정점을 고른다
    doomed = []
    for v in mesh_obj.data.vertices:
        w = sum(g.weight for g in v.groups if g.group in gidx)
        if w < THRESHOLD:
            doomed.append(v.index)

    bm = bmesh.new()
    bm.from_mesh(mesh_obj.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in doomed], context="VERTS")
    bm.to_mesh(mesh_obj.data)
    bm.free()
    mesh_obj.data.update()

    print("깎은 뒤 정점 %d  면 %d  (문턱 %.1f)"
          % (len(mesh_obj.data.vertices), len(mesh_obj.data.polygons), THRESHOLD))

    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    print("뼈 %d개 — 그대로 둔다" % len(arm.data.bones))

    os.makedirs(DST, exist_ok=True)
    dst = os.path.join(DST, "R_Arms.fbx")
    # 설정은 glb2fbx.py와 한 글자도 다르지 않아야 한다.
    # 하나라도 어긋나면 뼈대가 미묘하게 달라져 유니티에서 아바타가 안 물린다.
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="STRIP",
        embed_textures=False,
    )
    print("[ok] %s -> %.2fMB" % (dst, os.path.getsize(dst) / 1048576.0))


main()
