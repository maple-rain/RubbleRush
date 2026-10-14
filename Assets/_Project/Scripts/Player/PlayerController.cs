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

        [SerializeField, Range(0.5f, 6f), Tooltip("발소리 한 번 사이에 걷는 거리(m) [제안·미승인].\n" +
                 "3m 로봇의 보폭에서 잡았다. 작으면 종종거리고, 크면 미끄러져 보인다")]
        float stepDistance = 2.2f;

        [Header("연결 — 같은 오브젝트에 있으면 Awake가 알아서 잡는다")]
        [SerializeField] InputRouter input;
        [SerializeField] Collection.CargoTow cargo;

        CharacterController cc;
        Core.FollowCamera view;

        /// <summary>씬에 박혀 있던 stepOffset. 11번 Platform에서 되돌릴 기준값이다.</summary>
        float baseStepOffset;
        float verticalVelocity;
        float stunRemaining;

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
            baseStepOffset = cc.stepOffset;
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

            TopSpeed = cfg.moveSpeedBase * Core.Upgrades.Mul(Core.Upgrades.MoveSpeed);
            IsRunning = input != null && input.RunActive;

            // 11번 Platform이면 토대(높이 0.40)에 올라설 수 있어야 한다.
            // 씬 값 0.30으로는 실측에서 완전히 막혔다 — 넘으려면 0.40보다 커야 한다.
            // 규칙이 바뀔 수 있으므로 매 프레임 보지만, 같으면 쓰지 않는다.
            float wantStep = cfg.foundationRule == GameConfig.FoundationRule.Platform
                           ? cfg.platformStepOffset : baseStepOffset;
            if (!Mathf.Approximately(cc.stepOffset, wantStep)) cc.stepOffset = wantStep;

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

            // 끌던 잔해가 건물에 걸려 있으면 느려진다 [14번, 2026-10-01].
            // ComputeMoveSpeed 안에 넣지 않는다 — 저쪽은 '얼마나 많이 끄는가'의 축이고
            // 이쪽은 '지금 걸려 있는가'의 축이다. 기획자의 감속 곡선 TODO와 섞이면 안 된다.
            // DebrisSnag가 Slow가 아니면 배율이 1이라 아무 일도 안 일어난다.
            if (cargo != null) CurrentSpeed *= cargo.SpeedScale;

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

            Footsteps(dt);
        }

        /// <summary>
        /// 발소리 [지시 2026-10-14].
        ///
        /// <b>시간이 아니라 걸은 거리로 낸다.</b> 초로 내면 걷기와 달리기에서 보폭이 달라져
        /// 발이 미끄러져 보인다. 애니메이션 이벤트로 심는 길도 있지만 클립마다 다시 심어야 하고,
        /// 업그레이드로 속도가 바뀌면 또 어긋난다.
        ///
        /// 음원은 2026-10-08부터 폴더에 있었는데 <see cref="Demo.Data.Sfx"/> 목록에 없어서
        /// 한 번도 울리지 않고 있었다.
        /// </summary>
        void Footsteps(float dt)
        {
            if (!cc.isGrounded || CurrentSpeed < 0.2f) { stepLeft = stepDistance * 0.4f; return; }

            stepLeft -= CurrentSpeed * dt;
            if (stepLeft > 0f) return;

            stepLeft = stepDistance;
            Utils.GameAudio.Play(Data.Sfx.Footstep, transform.position);
        }

        float stepLeft;

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

        /// <summary>
        /// 이동 속도. <b>견인량은 속도를 깎지 않는다</b> [확정 2026-10-13].
        ///
        /// 오래 비어 있던 자리다. 원래는 "크게 갈 것인가, 빠르게 갈 것인가"를 만드는
        /// 감속 곡선을 넣을 예정이었고, 선형·체감·체증 세 안을 올렸다.
        /// 기획자가 <b>감속을 하지 않기로</b> 정했다 — 얼마를 끌든 같은 속도다.
        /// 그래서 <c>speedPenaltyPerUnit</c>과 <c>moveSpeedMin</c>은 쓰이지 않는다.
        ///
        /// 속도의 차이는 대신 <b>업그레이드</b>가 만든다 — 구멍 단계가 오를 때 고르는
        /// "더 빠르게" 카드다 (<see cref="Core.Upgrades"/>).
        /// </summary>
        float ComputeMoveSpeed(GameConfig cfg, int towLoad, bool running)
        {
            // 걷기 속도가 아직 안 채워졌으면 달리기 속도를 그대로 쓴다.
            // 0을 돌려주면 걷기 토글이 "못 움직임"이 되어 버그로 보인다 —
            // Validate()가 미입력을 따로 잡아 주므로 여기서는 굴러가게만 한다.
            float baseSpeed = (!running && cfg.walkSpeedBase > 0f)
                            ? cfg.walkSpeedBase : cfg.moveSpeedBase;
            return baseSpeed * Core.Upgrades.Mul(Core.Upgrades.MoveSpeed);
        }
    }
}
