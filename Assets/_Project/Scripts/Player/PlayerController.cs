using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 철거 로봇의 이동. 무한궤도 차량이므로 진행 방향으로 차체가 돌아간다.
    ///
    /// 조준이 없다 (GAME_DESIGN §3). 조이스틱 하나로 이동만 하고, 차체 방향은 이동 방향을 따라간다.
    /// 입력과 견인량은 밖에서 넣어준다 — InputRouter와 CargoTow가 붙기 전에도 이 스크립트는 돈다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField, Tooltip("차체가 이동 방향으로 도는 속도(도/초)")]
        float turnSpeedDegrees = 720f;

        [SerializeField, Tooltip("접지 유지용 하강 가속. 낙하 연출용이 아니다")]
        float groundStick = 20f;

        [Header("연결 — 같은 오브젝트에 있으면 Awake가 알아서 잡는다")]
        [SerializeField] InputRouter input;
        [SerializeField] Collection.CargoTow cargo;

        CharacterController cc;
        Core.FollowCamera view;
        float verticalVelocity;
        float stunRemaining;
        bool warnedPlaceholder;

        /// <summary>조이스틱/WASD 입력. InputRouter가 매 프레임 넣는다. 크기 0~1.</summary>
        public Vector2 MoveInput { get; set; }

        /// <summary>현재 끌고 있는 잔해 총 단위. CargoTow가 바뀔 때마다 넣는다.</summary>
        public int TowLoadUnits { get; set; }

        /// <summary>정지 중인가. 붕괴에 깔리거나 폭발에 맞으면 GameLoop가 건다.</summary>
        public bool IsStunned => stunRemaining > 0f;

        /// <summary>이번 프레임의 실제 이동 속도(m/s). HUD와 카메라 줌이 읽는다.</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>달리기 토글이 켜져 있는가 [지시 2026-09-28]. PC는 왼쪽 Shift.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// 달릴 때의 속도(m/s). <see cref="PlayerAnimator"/>가 블렌드를 정규화할 때 쓴다 —
        /// 애니메이터가 GameConfig를 따로 읽으면 두 곳이 어긋날 수 있다.
        /// </summary>
        public float TopSpeed { get; private set; }

        void Awake()
        {
            // CLAUDE.md 규칙 5 — Update에서 GetComponent 금지. 여기서 잡아둔다.
            cc = GetComponent<CharacterController>();
            if (input == null) input = GetComponent<InputRouter>();
            if (cargo == null) cargo = GetComponent<Collection.CargoTow>();
            view = FindFirstObjectByType<Core.FollowCamera>();
        }

        /// <summary>정지를 건다. 이미 걸려 있으면 긴 쪽으로 덮어쓴다 — 누적되면 너무 가혹하다.</summary>
        public void ApplyStun(float seconds)
        {
            if (seconds > stunRemaining) stunRemaining = seconds;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // 붙어 있으면 스스로 읽는다. 없으면 밖에서 넣어준 값을 그대로 쓴다 —
            // 테스트에서 컴포넌트 없이 이 스크립트만 돌릴 수 있어야 한다.
            if (input != null) MoveInput = input.Move;
            if (cargo != null) TowLoadUnits = cargo.TotalUnits;

            if (stunRemaining > 0f)
            {
                stunRemaining -= dt;
                CurrentSpeed = 0f;
                ApplyGravityOnly(dt);
                return;
            }

            // 파괴 화면에서는 움직이지 않는다 [지시 2026-09-22].
            // 화면이 층에 바짝 붙어 있어서, 이동이 살아 있으면 벽을 뚫고 나가거나
            // 돌아왔을 때 엉뚱한 자리에 서 있게 된다.
            GameModeController mode = GameModeController.Instance;
            if (mode != null && !mode.IsField)
            {
                CurrentSpeed = 0f;
                MoveInput = Vector2.zero;   // 안 지우면 복귀 첫 프레임에 쌓인 입력으로 튄다
                ApplyGravityOnly(dt);
                return;
            }

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;

            TopSpeed = cfg.moveSpeedBase;
            IsRunning = input != null && input.RunActive;

            // 입력을 <b>화면 기준</b>으로 돌린다 [지시 2026-09-28].
            //
            // 카메라가 yaw 45도로 고정된 쿼터뷰인데 입력을 월드 그대로 쓰면,
            // W를 눌렀을 때 로봇이 화면 위가 아니라 <b>오른쪽 위 대각선</b>으로 간다.
            // 쿼터뷰에서 늘 나오는 문제이고, 화면에서 보이는 방향과 손이 어긋나면
            // 플레이어는 그걸 "조작이 이상하다"로 느낀다.
            Vector3 dir = Quaternion.Euler(0f, view != null ? view.FieldYaw : 0f, 0f)
                        * new Vector3(MoveInput.x, 0f, MoveInput.y);
            float magnitude = Mathf.Min(dir.magnitude, 1f);

            CurrentSpeed = magnitude > 0.01f
                ? ComputeMoveSpeed(cfg, TowLoadUnits, IsRunning) : 0f;

            if (magnitude > 0.01f)
            {
                dir /= dir.magnitude;
                FaceDirection(dir, dt);
            }

            verticalVelocity -= groundStick * dt;
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;

            Vector3 motion = dir * (CurrentSpeed * dt);
            motion.y = verticalVelocity * dt;
            cc.Move(motion);
        }

        void ApplyGravityOnly(float dt)
        {
            verticalVelocity -= groundStick * dt;
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;
            cc.Move(new Vector3(0f, verticalVelocity * dt, 0f));
        }

        void FaceDirection(Vector3 dir, float dt)
        {
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, turnSpeedDegrees * dt);
        }

        // ──────────────────────────────────────────────────────────────────────
        // TODO(기획자) — 견인량이 속도를 깎는 곡선
        //
        // 이 게임의 핵심 딜레마가 이 함수 하나에 들어간다 (GAME_DESIGN §1):
        //   "크게 갈 것인가, 빠르게 갈 것인가."
        // 많이 끌수록 느려지고, 느릴수록 붕괴에서 못 빠져나온다. 곡선의 모양이
        // 플레이어가 "한 번에 몰아서 나를지, 조금씩 자주 나를지"를 결정한다.
        //
        // GameConfig가 가진 재료: moveSpeedBase, speedPenaltyPerUnit, moveSpeedMin
        // (값은 아직 비어 있다. 곡선의 모양과 값은 둘 다 기획자 몫이다.)
        //
        // 후보 세 가지:
        //   ① 선형 + 하한   max(min, base − penalty × load)
        //      1단위의 무게가 처음과 끝이 같다. TECH_SPEC 초안이 쓴 형태이고 제일 읽기 쉽다.
        //      대신 하한에 닿는 순간부터 더 실어도 공짜라서, 후반에 무한정 쌓는 게 최적이 된다.
        //
        //   ② 체감(遞減)     base − penalty × sqrt(load) 류
        //      많이 끌수록 추가 1단위가 덜 아프다 → 대량 수집을 장려한다.
        //      "몰아서 부수고 한 번에 크게 둘러싸기"를 고급 플레이로 만들고 싶으면 이쪽.
        //
        //   ③ 체증(遞增)     base − penalty × load² 류
        //      많이 끌수록 추가 1단위가 더 아프다 → 적정선에서 끊고 나르게 만든다.
        //      왕복이 늘어 지루해질 위험이 있지만, 구멍을 여러 개 둔 설계와는 잘 맞는다.
        //
        // 어느 쪽이든 5~8줄이면 끝난다. 정하고 나면 balance_sim.py의 spd()도 같은 모양으로 맞춰야
        // D16 시뮬레이션이 실제 게임과 같은 것을 재게 된다.
        // ──────────────────────────────────────────────────────────────────────
        float ComputeMoveSpeed(GameConfig cfg, int towLoad, bool running)
        {
            if (!warnedPlaceholder)
            {
                warnedPlaceholder = true;
                Debug.LogWarning("[PlayerController] 견인 감속 곡선이 아직 비어 있다 — " +
                                 "지금은 얼마를 끌어도 속도가 안 줄어든다. " +
                                 "ComputeMoveSpeed()의 TODO 참조.", this);
            }

            // 걷기 속도가 아직 안 채워졌으면 달리기 속도를 그대로 쓴다.
            // 0을 돌려주면 걷기 토글이 "못 움직임"이 되어 버그로 보인다 —
            // Validate()가 미입력을 따로 잡아 주므로 여기서는 굴러가게만 한다.
            if (!running && cfg.walkSpeedBase > 0f) return cfg.walkSpeedBase;
            return cfg.moveSpeedBase;
        }
    }
}
