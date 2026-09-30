using Demo.Core;
using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 파괴 화면에서 <b>팔만 화면에 들어오게</b> 로봇을 세운다 [지시: "타격 시 팔만 나와서 타격 연출"].
    ///
    /// <b>문제.</b> 파괴 화면의 카메라는 벽에 바짝 붙어 1인칭으로 선다. 로봇이 필드에 서 있던
    /// 자리에 그대로 있으면 화면 한가운데를 막거나(가까우면) 아예 안 보인다(멀면).
    /// 그래서 지금까지는 <see cref="PlayerVisibility"/>가 <b>통째로 숨겼다</b>. 숨기면
    /// 때리는 주체가 화면에서 사라져, 벽이 혼자 부서지는 것처럼 보인다.
    ///
    /// <b>방법.</b> 로봇을 카메라 바로 옆·뒤에 세운다. FPS의 손 모델과 같은 배치다 —
    /// 몸통과 머리는 카메라 뒤라 안 보이고, 주먹을 뻗는 동안만 <b>아래팔과 주먹이</b>
    /// 프레임을 가로지른다. 평소(Idle)에는 팔이 내려가 있어 화면에 없다.
    ///
    /// <b>뼈가 아니라 그려지는 덩어리로 조준한다</b> [지적 2026-09-30].
    /// 손목 뼈를 조준선에 올렸더니 <b>보이는 주먹이 화면 높이의 12% 위</b>로 빗나갔다 —
    /// 이 리그는 손 메시가 손목 뼈에서 위·앞으로 뻗어 있어서, 로컬로 (0.12, 0.10, 0.15)만큼
    /// 어긋난다. 건물 높이를 80cm 올려도 이 어긋남은 그대로였다. 로봇 안쪽 값이라
    /// 건물과 아무 상관이 없기 때문이다. 그래서 fistAtPeak은 <b>스킨 메시를 구워
    /// 손뼈 가중치 0.5 이상인 정점의 무게중심</b>을 잰 값이다.
    ///
    /// <b>높이를 주먹에 맞추는 이유.</b> 이 로봇은 데포르메라 <b>머리가 키의 절반</b>이고
    /// 어깨가 1.57m, 주먹이 뻗었을 때 1.76m다(3m 기준 실측). 카메라를 머리에 맞추면
    /// 주먹이 화면 0.9m 아래로 지나가 안 보인다. 그래서 <b>주먹 높이</b>를 카메라에 맞춘다.
    ///
    /// <b>본체가 아니라 Visual만 옮긴다.</b> 루트에는 CharacterController와 필드 좌표가
    /// 걸려 있어서, 그걸 옮기면 파괴가 끝나고 돌아왔을 때 엉뚱한 자리에 서 있게 된다.
    ///
    /// <b>전환 중에는 숨는다.</b> 카메라가 0.35초에 걸쳐 필드에서 벽으로 넘어가는데,
    /// 그동안 팔을 붙여 두면 로봇이 필드 자리에서 카메라까지 날아가는 것이 보인다.
    /// </summary>
    [DefaultExecutionOrder(100)]    // FollowCamera(LateUpdate)가 자리를 잡은 뒤에 붙는다
    public sealed class StrikeArmRig : MonoBehaviour
    {
        [SerializeField, Tooltip("옮길 대상. 비우면 자식에서 Animator를 찾는다")]
        Transform visual;

        [SerializeField,
         Tooltip("왼주먹이 최대로 뻗었을 때 <b>화면에 보이는 주먹 덩어리의 중심</b>" +
                 "(로봇 로컬, m). 비틀림 레이어를 켠 재생시점 0.36에서 잰 값이다. " +
                 "클립을 바꾸면 다시 재야 한다")]
        Vector3 fistAtPeak = new Vector3(0.004f, 2.001f, 1.393f);

        [SerializeField,
         Tooltip("오른주먹의 같은 값. <b>왼쪽 값의 x를 뒤집어 쓰면 안 된다</b> — " +
                 "VARCO 자동 리깅이 좌우 대칭이 아니라서 실측이 0.004 / 0.078로 다르다")]
        Vector3 fistAtPeakRight = new Vector3(0.078f, 2.015f, 1.402f);

        [SerializeField, Range(0f, 60f),
         Tooltip("몸을 옆으로 비트는 각도. 주먹은 조준선 위에 그대로 남고 어깨만 물러난다.\n" +
                 "<b>이것만으로는 좌우가 잘 안 갈린다</b> — 원본 펀치가 몸을 가로질러 치는 " +
                 "동작이라, 각도를 바꿔도 아래팔이 주먹 기준 같은 쪽에 남는다. aimSpread와 같이 쓴다")]
        float armSpread = 20f;

        [SerializeField, Range(0f, 1.5f),
         Tooltip("왼주먹은 왼쪽, 오른주먹은 오른쪽으로 이만큼(m) 갈라 친다.\n" +
                 "<b>0이 기본이다</b> [지시 2026-09-30 — 가운데를 쳐야 한다]. " +
                 "0이 아니면 주먹이 밀집부를 비껴 맞는다. 좌우 구분은 armRoll이 맡는다.\n" +
                 "보로노이 밀집부는 점이 아니라 <b>σ = 0.5 × 층높이</b>만큼 퍼진 영역이라 " +
                 "(FractureLayout.PlaceSeeds) 이 정도는 여전히 밀집 지역 안이다")]
        float aimSpread = 0f;

        [SerializeField, Range(-90f, 90f),
         Tooltip("<b>조준선을 축으로</b> 몸을 굴리는 각도. 왼팔은 좌하단, 오른팔은 " +
                 "우하단에서 들어오게 하는 값이다 [지시 2026-09-30 — 1인칭이니 " +
                 "각 모서리에서 가운데로 뻗어야 한다].\n" +
                 "<b>주먹의 화면 위치는 전혀 안 변한다.</b> 아래 " +
                 "SetPositionAndRotation이 주먹을 조준선 위에 못 박으므로, 이 회전은 " +
                 "결과적으로 주먹을 중심으로 한 회전이다 — 팔꿈치와 어깨만 화면에서 " +
                 "원을 그리며 내려온다.\n" +
                 "armSpread(Y축)로는 안 된다. 그건 팔을 좌우로만 벌리지 아래로 못 내린다")]
        float armRoll = 45f;

        [SerializeField, Range(0f, 1.5f),
         Tooltip("머리를 카메라 뒤로 이만큼(m) 더 물린다 [지시 3번 — 팔만 보이게].\n" +
                 "광선 위에서 물리므로 <b>주먹의 화면 위치는 바뀌지 않는다.</b> " +
                 "값이 클수록 주먹이 작게 보인다")]
        float headClearance = 0.30f;

        [SerializeField, Range(0.3f, 3f),
         Tooltip("주먹을 카메라에서 얼마나 앞에 둘 것인가(m). 작을수록 주먹이 크게 보인다.\n" +
                 "<b>조준은 이 값에 전혀 영향받지 않는다</b> — 조준선 위를 미끄러질 뿐이라 " +
                 "0.3에서 3까지 흔들어도 과녁차가 (0.000, 0.000)이다 [실측 2026-09-30].\n" +
                 "1.75에서 주먹 앞끝이 벽 4cm 앞에 선다 (층높이 1.9 기준). 더 키우면 벽을 " +
                 "파고들고, 줄이면 허공을 친다. armsOnlyMesh가 없으면 머리 보정이 걸려 " +
                 "이 값이 1 근처에서 잘린다")]
        float fistDistance = 1.75f;

        [SerializeField, Range(0.5f, 1f),
         Tooltip("전환이 이만큼 진행된 뒤부터 팔을 붙인다. 1에 가까울수록 늦게 나타난다")]
        float attachAt = 0.999f;

        [SerializeField,
         Tooltip("파괴 화면에서 갈아 끼울 <b>팔만 남긴 메시</b> (R_Arms.fbx).\n" +
                 "비워 두면 온몸을 그대로 쓰고 머리를 카메라 뒤로 미는 예전 방식이 된다.\n" +
                 "같은 GLB를 같은 설정으로 다시 구운 것이라 bindpose가 원본과 <b>완전히</b> " +
                 "같다 — 그래서 뼈 배열을 건드리지 않고 메시만 바꿔 끼울 수 있다")]
        Mesh armsOnlyMesh;

        [SerializeField, Range(0.01f, 0.3f),
         Tooltip("붙어 있는 동안 카메라 근평면을 이 값(m)까지 내린다.\n" +
                 "<b>팔뚝이 카메라에서 0.2m쯤에 온다.</b> 기본 근평면 0.3m로는 팔이 " +
                 "통째로 잘려 나가서, 거대한 주먹만 보이고 팔은 안 보였다 [실측 2026-09-30].\n" +
                 "화각은 그대로라 근평면 너머의 그림은 하나도 안 변한다. 값이 치르는 " +
                 "비용은 깊이 정밀도뿐이고, 이 게임의 먼 물체는 50m 안쪽이라 여유가 있다")]
        float nearClipWhileAttached = 0.05f;

        Renderer[] parts;
        FollowCamera view;
        InputRouter input;
        Transform cam;
        Transform head;          // 화면에 들어오면 안 되는 가장 앞쪽 부위
        Vector3 restPosition;
        Quaternion restRotation;
        bool attached;
        Camera lens;              // 근평면을 되돌리려면 원래 값을 들고 있어야 한다
        float restNearClip;
        SkinnedMeshRenderer skin;
        Mesh restMesh;
        bool restUpdateOffscreen;

        /// <summary>지금 팔 리그가 카메라에 붙어 있는가. <see cref="PlayerVisibility"/>가 읽는다.</summary>
        public bool Attached => attached;

        void Awake()
        {
            if (visual == null)
            {
                Animator a = GetComponentInChildren<Animator>(true);
                if (a != null) visual = a.transform;
            }
            if (visual != null)
            {
                restPosition = visual.localPosition;
                restRotation = visual.localRotation;
                parts = visual.GetComponentsInChildren<Renderer>(true);
                skin = visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
                foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Head") { head = t; break; }
            }
            input = GetComponent<InputRouter>();
            view = FindFirstObjectByType<FollowCamera>();
            if (view != null) cam = view.transform;
        }

        void LateUpdate()
        {
            if (visual == null) return;

            GameModeController mode = GameModeController.Instance;
            bool want = mode != null && mode.IsDemolition
                        && view != null && view.ModeBlend >= attachAt;

            if (!want)
            {
                if (!attached) return;
                Detach();
                return;
            }

            // 근평면은 <b>붙는 순간에만</b> 내린다. 매 프레임 쓰면 사람이 인스펙터에서
            // 고친 값을 덮어쓰고, 원래 값도 잃어버린다.
            if (!attached)
            {
                lens = cam != null ? cam.GetComponent<Camera>() : null;
                if (lens != null)
                {
                    restNearClip = lens.nearClipPlane;
                    lens.nearClipPlane = Mathf.Min(restNearClip, nearClipWhileAttached);
                }
                if (skin != null && armsOnlyMesh != null)
                {
                    restMesh = skin.sharedMesh;
                    skin.sharedMesh = armsOnlyMesh;

                    // <b>경계를 매 프레임 다시 잰다.</b> 기본값(false)이면 경계 상자를
                    // 루트 뼈에서 미리 구운 값으로 쓰는데, 리그를 카메라 옆으로 끌어다
                    // 놓으면 실제 정점과 어긋나 메시가 통째로 컬링된다 — 오류도 경고도
                    // 없이 화소 0개가 된다 [실측 2026-09-29]. 메시까지 바꾸면 더 어긋난다.
                    restUpdateOffscreen = skin.updateWhenOffscreen;
                    skin.updateWhenOffscreen = true;
                }
            }
            attached = true;

            // 카메라의 수평 기준축을 쓴다. 파괴 카메라는 원래 수평이지만, 흔들림(셰이크)이
            // 끼면 위아래로 기울 수 있어서 평평하게 눌러 둔다 — 안 그러면 팔이 같이 요동친다.
            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            bool right = input != null && input.StrikeRightArm;

            // 주먹이 <b>화면에서</b> 타격점을 덮도록 카메라->타격점 시선을 먼저 구한다.
            // aimSpread가 0이면 좌우 주먹 모두 밀집부 한가운데를 친다 [지시 2026-09-30].
            Vector3 right3 = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 aim = StrikePoint(mode) + right3 * (right ? aimSpread : -aimSpread);
            Vector3 ray = aim - cam.position;
            if (ray.sqrMagnitude < 0.0001f) ray = fwd;
            Vector3 fistWorld = cam.position + ray.normalized * fistDistance;

            // <b>몸을 비튼다.</b> 주먹 위치를 옮겨 팔을 벌리면 조준이 깨지므로, 대신 로봇을 돌린다.
            //
            // armSpread(Y축)는 팔을 좌우로만 벌린다. 1인칭에서 필요한 것은 팔이
            // <b>아래 모서리에서</b> 올라오는 그림이라, 조준선을 축으로 한 롤을 같이 준다 —
            // 주먹은 조준선 위에 그대로 남고 팔꿈치와 어깨만 화면에서 원을 그리며 내려온다.
            Quaternion facing = Quaternion.AngleAxis(right ? armRoll : -armRoll, ray.normalized)
                              * Quaternion.LookRotation(fwd, Vector3.up)
                              * Quaternion.Euler(0f, right ? armSpread : -armSpread, 0f);

            // 그 주먹 위치가 나오도록 로봇을 뒤로 물린다.
            Vector3 local = right ? fistAtPeakRight : fistAtPeak;
            visual.SetPositionAndRotation(fistWorld - facing * local, facing);

            // <b>머리가 화면에 들어오면 광선을 따라 더 물린다</b> [지시 3번].
            //
            // 로봇을 카메라→타격점 <b>광선 위에서</b> 밀고 당기면 주먹의 화면 위치가
            // 전혀 바뀌지 않는다 — 같은 광선 위의 점은 모두 같은 화소에 찍힌다.
            // 그래서 조준을 하나도 잃지 않고 머리만 카메라 뒤로 보낼 수 있다.
            //
            // 계산으로 미리 빼지 않고 <b>실제 머리뼈 위치를 보고</b> 고치는 이유는,
            // armSpread로 몸을 비틀면 머리가 같이 돌아 깊이가 달라지기 때문이다.
            // 팔만 그리는 동안에는 건너뛴다 — 안 그려지는 머리를 카메라 뒤로 밀면
            // 주먹까지 같이 끌려와서, 주먹이 화면을 덮는 원인이 된다.
            if (head != null && (skin == null || armsOnlyMesh == null))
            {
                Vector3 dir = ray.normalized;
                float depth = Vector3.Dot(head.position - cam.position, dir);
                float over = depth + headClearance;      // 0보다 크면 아직 덜 물렸다
                if (over > 0f)
                    visual.position -= dir * over;
            }
        }

        /// <summary>
        /// 균열이 몰리는 지점. 파편 씨앗의 <b>가우시안 뭉치 중심</b>과 같은 자리다
        /// (<c>FractureLayout.PlaceSeeds</c>) — 벽면 가로 한가운데, <b>부수고 있는 층</b>의 절반 높이.
        ///
        /// 층이 무너지면 위층이 내려와 그 자리를 채우므로 부수는 층은 언제나 맨 아래다.
        /// 그래서 높이는 층 높이의 절반으로 고정이다.
        ///
        /// 연출이 쓰던 <c>targetPoint</c>는 건물 <b>중심</b>이라 벽면과 상관이 없었다 —
        /// 그래서 주먹이 엉뚱한 곳을 지났다 [지적 2026-09-28].
        /// </summary>
        Vector3 StrikePoint(GameModeController mode)
        {
            Transform t = mode.Target.transform;
            Vector3 size = mode.Target.Data != null ? mode.Target.Data.colliderSize : Vector3.one * 3f;
            int floors = mode.Target.Data != null ? Mathf.Max(1, mode.Target.Data.floors) : 1;

            Vector3 normal = Destruction.StructureFace.Normal(t, mode.TargetFace);
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.back;
            normal.Normalize();

            return t.position
                 + normal * Destruction.StructureFace.Offset(size, mode.TargetFace)
                 + Vector3.up * (size.y / floors * 0.5f);
        }

        /// <summary>리그가 꺼질 때 제자리로 돌려놓는다. 안 그러면 카메라 옆에 박힌 채 남는다.</summary>
        void OnDisable()
        {
            if (visual == null || !attached) return;
            Detach();
        }

        /// <summary>
        /// 팔을 떼고 카메라 근평면도 되돌린다.
        /// <b>근평면을 안 되돌리면</b> 필드로 나가서도 깊이 정밀도를 계속 깎아 먹는다.
        /// </summary>
        void Detach()
        {
            attached = false;
            visual.localPosition = restPosition;
            visual.localRotation = restRotation;
            if (lens != null) { lens.nearClipPlane = restNearClip; lens = null; }
            if (skin != null && restMesh != null)
            {
                skin.sharedMesh = restMesh;
                skin.updateWhenOffscreen = restUpdateOffscreen;
                restMesh = null;
            }
        }
    }
}
