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
         Tooltip("왼주먹이 최대로 뻗었을 때의 위치(로봇 로컬, m). 비틀림 레이어까지 켠 " +
                 "상태에서 잰 값이다 (재생시점 0.36). 클립을 바꾸면 다시 재야 한다")]
        Vector3 fistAtPeak = new Vector3(-0.114f, 1.902f, 1.246f);

        [SerializeField,
         Tooltip("오른주먹의 같은 값. <b>왼쪽 값의 x를 뒤집어 쓰면 안 된다</b> — " +
                 "VARCO 자동 리깅이 좌우 대칭이 아니라서 실측이 −0.114 / +0.188로 다르다")]
        Vector3 fistAtPeakRight = new Vector3(0.188f, 1.902f, 1.246f);

        [SerializeField, Range(0f, 60f),
         Tooltip("몸을 옆으로 비트는 각도. 주먹은 조준선 위에 그대로 남고 어깨만 물러난다.\n" +
                 "<b>이것만으로는 좌우가 잘 안 갈린다</b> — 원본 펀치가 몸을 가로질러 치는 " +
                 "동작이라, 각도를 바꿔도 아래팔이 주먹 기준 같은 쪽에 남는다. aimSpread와 같이 쓴다")]
        float armSpread = 20f;

        [SerializeField, Range(0f, 1.5f),
         Tooltip("왼주먹은 왼쪽, 오른주먹은 오른쪽으로 이만큼(m) 갈라 친다. " +
                 "좌우가 화면에서 확실히 갈리게 하는 주된 수단이다.\n" +
                 "보로노이 밀집부는 점이 아니라 <b>σ = 0.5 × 층높이</b>만큼 퍼진 영역이라 " +
                 "(FractureLayout.PlaceSeeds) 이 정도는 여전히 밀집 지역 안이다")]
        float aimSpread = 0.4f;

        [SerializeField, Range(0f, 1.5f),
         Tooltip("머리를 카메라 뒤로 이만큼(m) 더 물린다 [지시 3번 — 팔만 보이게].\n" +
                 "광선 위에서 물리므로 <b>주먹의 화면 위치는 바뀌지 않는다.</b> " +
                 "값이 클수록 주먹이 작게 보인다")]
        float headClearance = 0.30f;

        [SerializeField, Range(0.3f, 3f),
         Tooltip("주먹을 카메라에서 얼마나 앞에 둘 것인가(m). 작을수록 주먹이 크게 보인다.\n" +
                 "실제로 벽에 닿게 하려면 로봇이 카메라 앞을 막으므로, 시선 위에만 올려 둔다")]
        float fistDistance = 0.9f;

        [SerializeField, Range(0.5f, 1f),
         Tooltip("전환이 이만큼 진행된 뒤부터 팔을 붙인다. 1에 가까울수록 늦게 나타난다")]
        float attachAt = 0.999f;

        Renderer[] parts;
        FollowCamera view;
        InputRouter input;
        Transform cam;
        Transform head;          // 화면에 들어오면 안 되는 가장 앞쪽 부위
        Vector3 restPosition;
        Quaternion restRotation;
        bool attached;

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
                attached = false;
                visual.localPosition = restPosition;
                visual.localRotation = restRotation;
                return;
            }

            attached = true;

            // 카메라의 수평 기준축을 쓴다. 파괴 카메라는 원래 수평이지만, 흔들림(셰이크)이
            // 끼면 위아래로 기울 수 있어서 평평하게 눌러 둔다 — 안 그러면 팔이 같이 요동친다.
            Vector3 fwd = cam.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            bool right = input != null && input.StrikeRightArm;

            // <b>몸을 옆으로 비튼다.</b> 주먹 위치를 옮겨 팔을 벌리면 조준이 깨지므로,
            // 대신 로봇 자체를 돌린다 — 주먹은 아래에서 조준선 위에 다시 놓이고,
            // 몸과 팔만 바깥으로 물러나 팔이 비스듬히 들어온다.
            // 왼팔은 화면 왼쪽에서, 오른팔은 오른쪽에서 들어와야 번갈아 치는 것으로 읽힌다.
            // 몸을 <b>주먹 반대쪽으로</b> 돌리면 어깨가 그쪽으로 물러난다 [실측으로 부호 확인].
            Quaternion facing = Quaternion.LookRotation(fwd, Vector3.up)
                              * Quaternion.Euler(0f, right ? armSpread : -armSpread, 0f);

            // 주먹이 <b>화면에서</b> 타격점을 덮도록 카메라→타격점 시선 위에 올린다.
            // 좌우 주먹은 밀집부 안에서 조금 갈라 친다 — 그래야 화면에서 번갈아 치는 것이 읽힌다.
            Vector3 right3 = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 aim = StrikePoint(mode) + right3 * (right ? aimSpread : -aimSpread);
            Vector3 ray = aim - cam.position;
            if (ray.sqrMagnitude < 0.0001f) ray = fwd;
            Vector3 fistWorld = cam.position + ray.normalized * fistDistance;

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
            if (head != null)
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
            attached = false;
            visual.localPosition = restPosition;
            visual.localRotation = restRotation;
        }
    }
}
