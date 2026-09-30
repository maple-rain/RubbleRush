"""VARCO GLB -> Unity용 FBX 변환 (Blender 헤드리스).

VARCO의 FBX 내보내기는 애니메이션을 빠뜨린다 (takes=0). GLB에는 들어 있으므로
GLB로 받아 Blender를 거쳐 FBX로 다시 굽는다. CLAUDE.md의
"VARCO 3D 생성 -> Blender(bpy) 정리 -> FBX" 파이프라인이 바로 이 단계다.

기본 모델까지 같이 변환하는 이유: 기본만 VARCO FBX를 쓰면 뼈 이름이나 축이
Blender를 거친 클립과 미묘하게 달라져, 유니티에서 공유 아바타가 안 물린다.
"""
import bpy
import os
import sys

SRC = sys.argv[sys.argv.index("--") + 1]
DST = sys.argv[sys.argv.index("--") + 2]

PAIRS = [
    ("Base.glb", "R_Demolition.fbx"),
    ("Idle.glb", "R_Demolition@Idle.fbx"),
    ("Walk.glb", "R_Demolition@Walk.fbx"),
    ("Punch.glb", "R_Demolition@Punch.fbx"),
    ("Push.glb", "R_Demolition@Push.fbx"),
    ("Drag.glb", "R_Demolition@Drag.fbx"),
    ("Landing.glb", "R_Demolition@Landing.fbx"),
    ("Smash.glb", "R_Demolition@Smash.fbx"),
    ("Run.glb", "R_Demolition@Run.fbx"),
    ("Jog.glb", "R_Demolition@Jog.fbx"),
    ("Sprint.glb", "R_Demolition@Sprint.fbx"),
]

# 세 번째 인자부터는 변환할 이름만 골라내는 필터다. 비우면 전부 변환한다.
# 이미 유니티에서 구간을 잘라 둔 클립을 다시 구우면 firstFrame/lastFrame이
# 새 테이크 길이를 벗어날 수 있어서, 한 개만 추가할 때는 반드시 골라 써야 한다.
ONLY = [a for a in sys.argv[sys.argv.index("--") + 3:]] if "--" in sys.argv else []


def wipe():
    """새 파일로 시작한다. 이전 임포트가 남으면 뼈가 두 벌이 된다."""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def drop_unskinned():
    """스킨이 안 걸린 메시를 버린다.

    VARCO 리깅 결과에는 진짜 캐릭터 말고 커다란 Icosphere가 하나 더 들어 있다.
    캐릭터를 통째로 감싸는 크기라 유니티에서 그것만 보이고 로봇은 그 안에 숨는다.
    뼈에 묶인 메시만 남기면 이름과 무관하게 걸러진다.
    """
    victims = []
    for o in list(bpy.data.objects):
        if o.type != "MESH":
            continue
        skinned = any(m.type == "ARMATURE" for m in o.modifiers)
        if not skinned:
            victims.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
    return victims


def convert(src_name, dst_name):
    src = os.path.join(SRC, src_name)
    dst = os.path.join(DST, dst_name)
    if not os.path.exists(src):
        print("[skip] 없음: " + src)
        return

    wipe()
    bpy.ops.import_scene.gltf(filepath=src)
    dropped = drop_unskinned()

    actions = len(bpy.data.actions)
    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    bones = len(armatures[0].data.bones) if armatures else 0

    kwargs = dict(
        filepath=dst,
        use_selection=False,
        apply_unit_scale=True,
        bake_space_transform=False,
        object_types={"ARMATURE", "MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        # 유니티는 잎 뼈를 쓰지 않는다. 남기면 "_end" 뼈가 줄줄이 생겨 아바타 매핑이 지저분해진다.
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        armature_nodetype="NULL",
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=False,
        # 한 파일에 액션이 여럿이면 유니티에서 클립이 여러 개로 갈린다. 하나만 굽는다.
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        # 텍스처를 FBX에 넣지 않는다. 넣으면 유니티가 매번 .fbm으로 꺼내고,
        # 그 폴더 변경이 재임포트를 불러 무한 루프가 된다. 텍스처는 따로 에셋으로 둔다.
        path_mode="STRIP",
        embed_textures=False,
    )

    try:
        bpy.ops.export_scene.fbx(**kwargs)
    except TypeError as e:
        # 블렌더 버전마다 인자가 조금씩 다르다. 모르는 것만 떨구고 다시 시도한다.
        print("[warn] 인자 불일치, 축소해서 재시도: " + str(e))
        for k in ("apply_unit_scale", "armature_nodetype", "bake_anim_force_startend_keying",
                  "bake_anim_step", "bake_anim_simplify_factor", "mesh_smooth_type"):
            kwargs.pop(k, None)
        bpy.ops.export_scene.fbx(**kwargs)

    size = os.path.getsize(dst) / 1048576.0
    print("[ok] %-26s 액션=%d 뼈=%d 버림=%s -> %.2fMB" % (src_name, actions, bones, dropped, size))


def main():
    print("블렌더 " + bpy.app.version_string)
    os.makedirs(DST, exist_ok=True)
    if ONLY:
        print("필터: " + ", ".join(ONLY))
    for src_name, dst_name in PAIRS:
        if ONLY and os.path.splitext(src_name)[0] not in ONLY:
            continue
        try:
            convert(src_name, dst_name)
        except Exception as e:
            print("[FAIL] %s: %s" % (src_name, e))
    print("변환 종료")


main()
