using Demo.Collection;
using Demo.Data;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 고정 각도 쿼터뷰. 플레이어를 부드럽게 따라가고, 견인량이 많을수록 살짝 줌아웃한다.
    ///
    /// 거리를 고정하는 것은 <b>생성 메시의 결함과 텍스처 해상도 한계가 근접 샷에서만 보이기 때문</b>이다
    /// (GAME_DESIGN §8). 카메라를 가까이 붙이는 순간 VARCO 출력의 약점이 드러난다.
    ///
    /// 셰이크는 로컬 오프셋으로만 처리해 추종 로직과 분리한다 — 섞으면 흔들 때마다 목표점이 흔들린다.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] CargoTow cargo;

        [SerializeField, Tooltip("내려다보는 각도(도). 쿼터뷰")]
        float pitch = 50f;

        [SerializeField, Tooltip("수평 회전(도). 고정이다 — 플레이어가 돌려도 따라 돌지 않는다")]
        float yaw = 45f;

        /// <summary>
        /// 필드 카메라의 수평 각도(도). <see cref="Player.PlayerController"/>가
        /// <b>입력을 화면 기준으로 돌릴 때</b> 읽는다 [지시 2026-09-28].
        ///
        /// 카메라 트랜스폼을 직접 읽지 않고 이 값을 내보내는 이유는 파괴 화면 때문이다 —
        /// 전환 중에는 카메라가 벽 쪽으로 돌아가 있어서, 그 순간의 트랜스폼을 기준으로 삼으면
        /// 필드로 돌아오는 0.35초 동안 <b>조작 방향이 휘어 버린다.</b> 필드 각도는 고정이다.
        /// </summary>
        public float FieldYaw => yaw;

        /// <summary>
        /// 필드(0)와 파괴 화면(1) 사이 어디쯤인가. 전환 중에는 그 사이 값이다.
        ///
        /// <see cref="Player.StrikeArmRig"/>가 읽는다 — 팔 리그는 <b>전환이 끝난 뒤에만</b>
        /// 붙어야 한다. 전환 중에 붙이면 로봇이 필드 위치에서 카메라까지 날아가는 것이 보인다.
        /// </summary>
        public float ModeBlend => modeBlend;

        [SerializeField, Tooltip("추종 감쇠. 클수록 뻣뻣하다")]
        float followLerp = 6f;

        [Header("파괴 화면 — 1인칭")]
        [SerializeField, Tooltip("1인칭일 때 눈높이(m). 대상이 작을 때만 쓰인다")]
        float eyeHeight = 1.5f;

        [SerializeField, Tooltip("아무리 작은 대상이어도 이보다 가까이 가지 않는다(m)")]
        float minStandoff = 1.0f;

        [SerializeField, Tooltip("아무리 큰 대상이어도 이보다 멀리 물러나지 않는다(m). 면에서 잰 거리다. " +
                                 "<b>주먹 때문에 있다.</b> 위 계산은 층 높이에 비례해 늘어나는데, 주먹은 " +
                                 "StrikeArmRig의 fistDistance로 카메라에서 <b>고정 거리</b>에 매달려 있다. " +
                                 "그래서 층을 높이면 카메라만 물러나고 주먹과 벽 사이가 그만큼 벌어진다. " +
                                 "여기서 끊으면 층이 높아져도 주먹은 벽에서 멀어지지 않는다 " +
                                 "[지시 2026-09-30]. 층 띠가 프레임을 넘치면 그만큼 잘린다 — " +
                                 "층 집중 연출에서는 의도된 그림이다")]
        float maxStandoff = 1.95f;

        [SerializeField, Tooltip("대상 크기 대비 여유. 1이면 화면에 꽉 차고, 클수록 여백이 생긴다")]
        float framingMargin = 1.25f;

        [SerializeField, Tooltip("이 높이(m)를 넘는 대상부터 여백을 더 준다. 층 집중이 꺼졌을 때만 쓰인다")]
        float tallFromHeight = 3f;

        [SerializeField, Tooltip("기준 높이를 1m 넘을 때마다 더해지는 여백. [제안·미승인]")]
        float tallMarginPerMeter = 0.07f;

        [Header("층 집중 — 부수는 층만 크게 본다 [지시 2026-09-22]")]
        [SerializeField, Tooltip("끄면 건물 전체를 화면에 담는다. 켜면 부수고 있는 층만 본다")]
        bool focusOnFloor = true;

        [SerializeField, Tooltip("화면에 담을 층 수. 1.6이면 부수는 층과 그 위층의 아랫부분까지.\n" +
                                 "1.0에 가까울수록 위층의 낙하가 갑작스럽고, 2를 넘으면 예고가 되어버린다")]
        float floorsInView = 1.2f;

        [SerializeField, Tooltip("시선 높이를 층 높이의 몇 배에 둘 것인가. 0.5면 층 한가운데")]
        float aimFloorFactor = 0.75f;

        [SerializeField, Tooltip("가로/세로 비. 층 띠는 가로로 길어서 이 값이 실제 거리를 정한다")]
        float focusWidthRatio = 1.78f;

        [SerializeField, Tooltip("모드 전환에 걸리는 시간(초)")]
        float modeBlendSeconds = 0.35f;

        Camera cam;

        Vector3 shakeOffset;
        float shakeMagnitude;
        float shakeDecay;

        /// <summary>0 = 필드 쿼터뷰, 1 = 파괴 화면 1인칭.</summary>
        float modeBlend;

        public Transform Target { get => target; set => target = value; }

        void Awake()
        {
            // 규칙 5 — 참조는 Awake에 캐시한다. LateUpdate에서 매 프레임 GetComponent 하지 않는다.
            cam = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            if (target == null) return;

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;

            float zoom = 0f, lookBack = 0f;
            if (cargo != null)
            {
                // ① 예전 식 — 무게가 늘면 조금 물러난다. 그대로 둔다
                zoom = Mathf.Min(cargo.TotalUnits * cfg.cameraZoomPerUnit, cfg.cameraZoomMax);

                // ② 짐을 담는 식 [2026-10-08]. <b>단위 수가 아니라 실제로 뻗은 길이로 몬다</b> —
                // 짐 뒤끝 ÷ 단위가 종류별로 2.50~0.54라 단위 수로는 작은 종류가 통째로 밖이었다.
                float back = cargo.LoadBack;
                lookBack = Mathf.Min(back * cfg.cameraLookBackShare, cfg.cameraLookBackMax);
                float fit = cfg.cameraFitPerMeter * (back - lookBack) - cfg.cameraDistance;
                zoom = Mathf.Min(Mathf.Max(zoom, fit), cfg.cameraZoomMax);
            }

            float distance = cfg.cameraDistance + zoom;

            // 필드 — 고정 각도 쿼터뷰
            Quaternion fieldRot = Quaternion.Euler(pitch, yaw, 0f);

            // <b>짐을 끌 때는 플레이어가 아니라 플레이어와 짐 사이를 본다</b> [2026-10-08].
            //
            // 쿼터뷰 각도가 고정이라 <b>뒤쪽 시야가 진행 방향에 따라 달라진다</b> — 카메라를
            // 향해 달리면 짐이 화면 아래로 빠진다. 실측(최악 각도 36방향 중):
            // 짐을 다 담는 데 필요한 카메라 거리는 <b>짐 뒤끝의 2.07배</b>로 거의 정확히 비례하고,
            // 일반 건물 12조각이면 26.5m가 필요하다 — 지금 8m의 세 배라 전부 작아진다.
            // 시선을 1m 뒤로 주면 거리 2.07m를 아낀다. 둘을 같이 쓰는 쪽이 싸다.
            //
            // 이 식이 없던 동안에도 짐은 이미 화면 밖이었다 — 해바라기 배열에서도
            // 견고 32조각은 최악 각도에서 13%만 보였다. 주머니가 만든 결함이 아니다.
            Vector3 focus = target.position - target.forward * lookBack;
            Vector3 fieldPos = focus - fieldRot * Vector3.forward * distance;

            // 파괴 화면 — 1인칭 [확정 2026-09-22].
            // 타격 동작은 보이지 않는다. 벽이 화면을 채우고, 두드릴 때마다 그 벽이 무너져 갈 뿐이다.
            GameModeController mode = GameModeController.Instance;
            bool demolition = mode != null && mode.IsDemolition && mode.Target != null;

            float blendStep = modeBlendSeconds > 0f ? Time.deltaTime / modeBlendSeconds : 1f;
            modeBlend = Mathf.MoveTowards(modeBlend, demolition ? 1f : 0f, blendStep);

            Vector3 desiredPos = fieldPos;
            Quaternion desiredRot = fieldRot;

            if (modeBlend > 0f && mode != null && mode.Target != null)
            {
                Vector3 wall = mode.Target.transform.position;

                // 대상이 화면에 <b>통째로</b> 들어오도록 물러난다.
                // 거리를 고정하면 큰 건물은 벽 텍스처 한 장으로만 보이고,
                // 윤곽이 안 보이니 마지막 조각이 사라질 때 "갑자기 없어졌다"가 된다.
                Vector3 size = mode.Target.Data != null
                    ? mode.Target.Data.colliderSize : Vector3.one * 3f;

                // 어느 면을 부수는가는 상호작용 순간에 이미 정해져 있다 [지시 2026-09-23].
                // 조각 판과 <b>같은 값</b>을 읽어야 한다 — 카메라만 따로 플레이어 방향을 보면
                // 판은 면에 붙어 있는데 카메라는 비스듬히 서서 파사드가 찌그러진다.
                int face = mode.TargetFace;
                float faceW = Destruction.StructureFace.Width(size, face);

                float tanHalf = cam != null
                    ? Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) : 0.577f;
                float aspect = cam != null ? cam.aspect : 1.778f;

                // 화면에 담을 상자를 먼저 정한다. 건물 전체냐, 부수고 있는 층이냐.
                float boxH, boxW, aimY, margin;

                if (focusOnFloor && mode.Target.Data != null && mode.Target.Data.floors > 1)
                {
                    // 층 집중 [지시 2026-09-22] —
                    // "1층에 카메라를 집중하고, 열심히 부쉈는데 위층이 떨어졌다는 느낌을 준다."
                    //
                    // 카메라를 <b>바닥 띠에 고정</b>하기만 하면 된다. 층이 무너지면 위층이
                    // 내려와 그 자리를 채우므로, 부수는 층은 언제나 로컬 y 0~층높이다.
                    // 카메라가 따라 올라갈 필요가 없고, 오히려 <b>움직이지 않기 때문에</b>
                    // 위층이 프레임 안으로 떨어지는 것이 사건이 된다.
                    float floorH = size.y / Mathf.Max(1, mode.Target.Data.floors);
                    boxH = floorH * Mathf.Max(0.2f, floorsInView);
                    boxW = Mathf.Min(faceW, boxH * Mathf.Max(0.5f, focusWidthRatio));
                    aimY = Mathf.Max(0.4f, floorH * aimFloorFactor);
                    margin = framingMargin;
                }
                else
                {
                    // 건물 전체. 높을수록 여백을 더 준다 — 여백이 같으면 높은 건물일수록
                    // 화면 점유율이 커져서, 무너지는 게 아니라 판때기가 녹는 것처럼 보인다.
                    boxH = size.y;
                    boxW = faceW;
                    aimY = Mathf.Max(eyeHeight, size.y * 0.5f);
                    margin = framingMargin
                           + Mathf.Max(0f, size.y - tallFromHeight) * tallMarginPerMeter;
                }

                float needH = (boxH * 0.5f) / Mathf.Max(0.01f, tanHalf);
                float needW = (boxW * 0.5f) / Mathf.Max(0.01f, tanHalf * aspect);

                // 면까지의 거리를 더하는 것이 핵심이다.
                // 조각 판은 건물 <b>면</b>에 서는데 대상의 Transform은 <b>중심</b>에 있다.
                // 중심 기준으로만 물러나면 실제 판까지의 거리가 그 절반 두께만큼 짧아진다.
                // 옆면을 부술 때는 더할 값이 z가 아니라 x의 절반이다 — 그래서 면을 물어본다.
                //
                // <b>위로도 막는다</b> [지시 2026-09-30]. 위 계산은 층 높이에 선형으로
                // 비례하므로 건물을 높이면 카메라가 같이 물러난다. 그런데 주먹은
                // 카메라에서 고정 거리에 있어서, 층을 높일수록 주먹과 벽 사이만 벌어졌다.
                float standoff = Mathf.Clamp(Mathf.Max(needH, needW) * margin,
                                             minStandoff, Mathf.Max(minStandoff, maxStandoff))
                                 + Destruction.StructureFace.Offset(size, face);

                Vector3 eyeAim = wall + Vector3.up * aimY;

                // 플레이어가 선 자리가 아니라 <b>면의 정면</b>에 선다. 비스듬히 서면
                // 파사드가 사다리꼴로 찌그러지고, 입구가 한쪽으로 쏠려 보인다.
                Vector3 away = Destruction.StructureFace.Normal(mode.Target.transform, face);
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                away.Normalize();

                Vector3 eyePos = wall + away * standoff + Vector3.up * aimY;
                Quaternion eyeRot = Quaternion.LookRotation(eyeAim - eyePos, Vector3.up);

                // 블렌드가 0이면 필드, 1이면 1인칭. 전환 중에는 그 사이를 지난다.
                desiredPos = Vector3.Lerp(fieldPos, eyePos, modeBlend);
                desiredRot = Quaternion.Slerp(fieldRot, eyeRot, modeBlend);
            }

            // 전환 중에는 추종 감쇠를 끈다 — 두 보간이 겹치면 카메라가 늘어진다.
            float lerp = modeBlend > 0f && modeBlend < 1f
                ? 1f
                : 1f - Mathf.Exp(-followLerp * Time.deltaTime);

            transform.position = Vector3.Lerp(transform.position, desiredPos, lerp) + shakeOffset;
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, lerp);

            TickShake();
        }

        /// <summary>파괴·붕괴·폭발이 부른다. 진행도에 비례해 세게 흔들면 된다.</summary>
        public void Shake(float magnitude, float duration)
        {
            if (magnitude <= shakeMagnitude) return;
            shakeMagnitude = magnitude;
            shakeDecay = duration > 0f ? magnitude / duration : magnitude;
        }

        void TickShake()
        {
            if (shakeMagnitude <= 0f)
            {
                shakeOffset = Vector3.zero;
                return;
            }

            // <b>히트스톱 중에는 한 장면을 붙든다</b> [지시 2026-09-30].
            //
            // 아래 감쇠가 Time.deltaTime을 쓰므로 timeScale이 0이면 세기가 안 줄어든다.
            // 그런데 방향은 매 프레임 다시 뽑히니, 세상이 멈춘 채 카메라만 최대 세기로
            // 요동친다 — 실측으로 정지 113ms 동안 변위가 프레임마다 0.068m씩 바뀌었다.
            // 정지가 주는 "박혔다"가 그 난동에 다 지워진다.
            if (Time.timeScale <= 0f) return;

            shakeOffset = Random.insideUnitSphere * shakeMagnitude;
            shakeOffset.y *= 0.5f;   // 세로로 덜 흔든다. 수직 흔들림이 더 빨리 멀미가 난다

            shakeMagnitude = Mathf.Max(0f, shakeMagnitude - shakeDecay * Time.deltaTime);
        }
    }
}
