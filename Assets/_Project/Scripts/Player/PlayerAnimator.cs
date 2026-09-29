using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 로봇 애니메이터를 굴린다 — 아래는 걷고, 위는 때릴 때만 [지시 2026-09-28].
    ///
    /// 컨트롤러가 두 층이고, <b>몸을 반으로 나눠 쓴다</b>.
    /// <list type="bullet">
    /// <item>0층 <b>Base</b>: <c>LowerBody</c> 마스크. 다리만. Idle·Walk·Sprint를 <c>Speed</c>로 섞는다</item>
    /// <item>1층 <b>UpperBody</b>: 상체 마스크. <b>가중치 1로 고정</b>. 평소 Idle, 때릴 때 Punch</item>
    /// </list>
    ///
    /// <b>상체가 걸음을 따라 흔들리지 않는 것이 의도다</b> [지시: "상체는 로봇이나 레고처럼"].
    /// 사람처럼 팔이 흔들리면 빠른 케이던스가 "허둥대는 사람"으로 읽히는데,
    /// 상체가 굳어 있으면 같은 속도가 "기계적"으로 읽힌다.
    ///
    /// (한때 1층 가중치를 때릴 때만 올렸다 내렸다. 걷기 팔 흔들림을 살리려던 것인데,
    /// 그 흔들림을 죽이는 것이 애초의 기획이었다. 되돌렸다 [2026-09-28].)
    ///
    /// <b>파라미터 둘을 내보낸다.</b> <c>Speed</c>는 Idle↔Walk를 섞고,
    /// <c>WalkRate</c>는 0층 상태의 <b>재생 배속</b>이다. 둘은 다른 일을 한다 —
    /// 섞기만으로는 발이 미끄러지고, 배속만으로는 멈춰 있을 때 제자리걸음이 남는다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField, Tooltip("속도를 읽어 올 곳. 비우면 부모에서 찾는다")]
        PlayerController player;

        [SerializeField, Range(0.05f, 0.9f),
         Tooltip("블렌드 트리에서 Walk가 놓인 위치. AC_Demolition의 Walk 문턱과 " +
                 "<b>반드시 같아야 한다</b>. Speed는 현재속도÷달리기속도이므로 " +
                 "걷기 1.2 / 달리기 6이면 0.2다")]
        float walkBlend = 0.2f;

        [SerializeField, Range(1f, 30f), Tooltip("걷기 블렌드가 따라오는 속도. 크면 뻣뻣하다")]
        float blendLerp = 10f;


        [Header("발 미끄러짐 — 값은 [제안·미승인]")]
        [SerializeField, Range(0.05f, 3f),
         Tooltip("Walk 클립이 1배속에서 실제로 나아가는 속도(m/s). 측정값 0.953 — " +
                 "디딘 발이 몸 기준으로 뒤로 가는 속도를 프레임마다 재서 평균낸 값이다 " +
                 "[실측 2026-09-28, 3m 로봇]. Demo/8b로 다시 자르면 다시 재야 한다")]
        float walkClipSpeed = 0.953f;

        [SerializeField, Range(0.5f, 12f),
         Tooltip("달리기 클립이 1배속에서 나아가는 속도(m/s). R_Sprint 측정값 4.244. " +
                 "클립을 R_Run(3.091)이나 R_Jog(1.279)로 바꾸면 이 값도 같이 바꿔야 한다")]
        float runClipSpeed = 4.244f;

        [SerializeField, Range(1f, 16f),
         Tooltip("걷기 재생 배속의 상한. 이걸 올리면 덜 미끄러지는 대신 다리가 빨라진다. " +
                 "1.68이면 사이클 0.67초 = 초당 1.5보")]
        float maxWalkRate = 1.68f;

        [SerializeField, Range(1f, 16f),
         Tooltip("달리기 재생 배속의 상한. <b>걷기와 따로 둔다</b> — 하나로 묶으면 걷기를 " +
                 "빠르게 하려고 올렸을 때 달리기 다리까지 같이 빨라진다. " +
                 "1.2면 사이클 0.42초 = 초당 2.4보로, 사람 달리기 케이던스다")]
        float maxRunRate = 1.2f;

        [SerializeField, Range(1f, 60f), Tooltip("배속이 따라오는 속도. 멈춤/출발에서 튀지 않게 한다")]
        float rateLerp = 20f;

        [Header("타격 — 골반 비틀림")]
        [SerializeField, Range(4f, 80f), Tooltip("주먹이 나갈 때 골반 비틀림이 올라오는 속도")]
        float twistRise = 40f;

        [SerializeField, Range(1f, 40f), Tooltip("주먹이 끝난 뒤 비틀림이 빠지는 속도")]
        float twistFall = 10f;

        [SerializeField, Range(0f, 1f),
         Tooltip("주먹이 최대로 뻗는 시점(재생 진행도 0~1). 실측 0.36 [지시 4번]")]
        float holdAt = 0.36f;

        [SerializeField, Range(0f, 0.5f), Tooltip("그 시점에서 늦출 구간의 폭(재생 진행도)")]
        float holdWidth = 0.10f;

        [SerializeField, Range(0.02f, 1f),
         Tooltip("늦출 때의 재생 배율. 0.12면 그 구간이 약 8배 길어져 '탁 멈췄다 빠진다'가 된다")]
        float holdRate = 0.12f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int PunchId = Animator.StringToHash("Punch");
        static readonly int WalkRateId = Animator.StringToHash("WalkRate");
        static readonly int RightArmId = Animator.StringToHash("RightArm");
        static readonly int PunchRateId = Animator.StringToHash("PunchRate");

        Animator anim;
        int upperLayer = -1;
        int twistLayer = -1;
        float blend;
        float walkRate = 1f;
        float twistWeight;

        /// <summary>지금 걷기/달리기 클립이 몇 배속으로 돌고 있는가. 1이면 원속도.</summary>
        public float WalkRate => walkRate;

        void Awake()
        {
            anim = GetComponent<Animator>();
            if (player == null) player = GetComponentInParent<PlayerController>();

            // 안 보여도 뼈를 계속 쓴다 [버그 2026-09-28].
            //
            // 임포트 기본값은 CullUpdateTransforms — "렌더러가 안 보이면 트랜스폼 쓰기를 건너뛴다"다.
            // 상태 머신은 계속 돌기 때문에 겉보기엔 멀쩡한데, PlayerVisibility가 파괴 화면에서
            // 렌더러를 끄는 순간 뼈가 <b>마지막 자세 그대로 얼어붙는다.</b> 하필 그 마지막 자세가
            // 방금 뻗은 주먹이라, 필드로 돌아와도 카메라가 따라붙을 때까지 주먹을 뻗은 채 미끄러진다.
            //
            // 실측: 렌더러를 끈 채 2252프레임 동안 normalizedTime은 18.5까지 갔는데
            // 발 위치 변화는 정확히 0.00000m였다. AlwaysAnimate로 바꾸니 움직였다.
            //
            // 비용은 스킨 메시 하나의 뼈 23개다. 이 씬에서 애니메이션이 붙은 것은 로봇뿐이라
            // 모바일 예산에서 잴 수 있는 값이 아니다.
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 이름으로 찾는다. 층 순서를 바꿔도 따라온다.
            for (int i = 0; i < anim.layerCount; i++)
            {
                if (anim.GetLayerName(i) == "UpperBody") upperLayer = i;
                else if (anim.GetLayerName(i) == "StrikeTwist") twistLayer = i;
            }
            if (twistLayer >= 0) anim.SetLayerWeight(twistLayer, 0f);

            // 상체는 늘 켜 둔다. 컨트롤러의 defaultWeight도 1이지만, 이전 버전이 0으로
            // 저장해 둔 컨트롤러를 만나도 같게 굴러가도록 여기서 한 번 더 못 박는다.
            if (upperLayer >= 0) anim.SetLayerWeight(upperLayer, 1f);
        }

        /// <summary>한 대 때렸다. <see cref="Destruction.DestructionFX"/>가 타격마다 부른다.</summary>
        /// <summary>
        /// 주먹이 나가는 동안에만 <b>골반 비틀림</b> 레이어를 켠다.
        ///
        /// <b>왜 따로 레이어인가.</b> 타격 클립은 골반을 최대 51도 비틀어 주먹을 싣는다
        /// (실측: Idle은 2도, Walk는 6도). 그런데 골반은 <b>하체 레이어 소유</b>여야 한다 —
        /// 달리기 클립의 골반이 95도 근처에 있고, 그것이 Armature의 -90도 방향 보정과
        /// 맞물려 상쇄되기 때문이다. 골반을 상체로 옮기면 그 상쇄가 깨져 <b>로봇이 옆으로 달린다.</b>
        ///
        /// 그래서 골반만 마스크한 레이어를 하나 더 두고, 때릴 때만 켠다. 이 레이어는
        /// 1층과 <c>syncedLayerIndex</c>로 묶여 있어 상태 머신을 복제할 필요가 없다.
        ///
        /// 비틀림이 빠지면 도달거리의 45%가 사라진다 — 실측으로 손끝이 1.17m가 아니라
        /// 0.66m까지만 나갔다.
        /// </summary>
        void DriveTwist()
        {
            if (twistLayer < 0 || upperLayer < 0) return;

            // 전이가 <b>시작될 때</b> 이미 올려야 주먹 정점(약 75ms)에 늦지 않는다.
            // 현재 상태만 보면 전이가 끝난 뒤에야 켜져서 한 박자 늦는다.
            AnimatorStateInfo cur = anim.GetCurrentAnimatorStateInfo(upperLayer);
            bool punching = cur.IsName("Punch") || cur.IsName("PunchRight");
            if (!punching && anim.IsInTransition(upperLayer))
            {
                AnimatorStateInfo next = anim.GetNextAnimatorStateInfo(upperLayer);
                punching = next.IsName("Punch") || next.IsName("PunchRight");
            }

            twistWeight = Mathf.MoveTowards(twistWeight, punching ? 1f : 0f,
                (punching ? twistRise : twistFall) * Time.deltaTime);
            anim.SetLayerWeight(twistLayer, twistWeight);

            // <b>정점에서 잠깐 멈춘다</b> [지시 4번].
            //
            // 클립을 다시 굽지 않고 재생 배율만 떨어뜨린다 — 타격 프레임을 늘려
            // "맞았다"를 읽을 시간을 주는 방식이고, 격투 게임의 히트 프리즈와 같은 원리다.
            // 히트스톱(Time.timeScale)과 다른 점은 <b>주먹만</b> 멈춘다는 것이다.
            // 화면 전체를 멈추면 초당 다섯 번 두드릴 때 조작이 끈적해진다.
            float t = cur.normalizedTime - Mathf.Floor(cur.normalizedTime);
            bool inHold = punching && !anim.IsInTransition(upperLayer)
                          && t >= holdAt && t < holdAt + holdWidth;
            anim.SetFloat(PunchRateId, inHold ? holdRate : 1f);
        }

        /// <param name="rightArm">true면 오른팔. 어느 팔인지는 입력이 정한다 —
        /// 키보드는 교대, 터치는 화면 좌우 중 누른 쪽 [지시 2026-09-28].</param>
        public void Punch(bool rightArm)
        {
            if (anim == null) return;
            // 방아쇠보다 <b>먼저</b> 팔을 정해 둔다. 순서가 바뀌면 전이 조건이
            // 낡은 값을 보고 갈라져서, 첫 타격이 반대 팔로 나간다.
            anim.SetBool(RightArmId, rightArm);
            anim.SetTrigger(PunchId);   // 상체 층은 늘 켜져 있으므로 방아쇠만 당기면 된다
        }

        void Update()
        {
            if (anim == null) return;

            float speed = player != null ? player.CurrentSpeed : 0f;
            float top = player != null && player.TopSpeed > 0.01f ? player.TopSpeed : 1f;

            // Speed는 <b>달리기 속도로 정규화</b>한 값이다. 트리의 문턱(0 / walkBlend / 1)이
            // 이 눈금 위에 놓여 있다.
            blend = Mathf.MoveTowards(blend, Mathf.Clamp01(speed / top),
                                      blendLerp * Time.deltaTime);
            anim.SetFloat(SpeedId, blend);

            // 블렌드는 "어떤 클립을 섞을지"만 정하지 "얼마나 빨리 돌릴지"는 안 정한다.
            // 그래서 Speed만으로는 발이 미끄러진다.
            //
            // 지금 섞여 있는 자리에서 트리가 <b>실제로 내는 속도</b>를 먼저 구하고,
            // 몸 속도를 그걸로 나눈 것이 배속이다. 걷기 구간 아래에서는 Walk 클립만
            // 의미가 있고, 그 위로는 Walk와 달리기 사이를 섞으므로 속도도 같이 섞인다.
            //
            // 배속은 0층 상태에만 건다. anim.speed를 쓰면 상체 주먹까지 같이 빨라져
            // 84ms에 맞춰 둔 타격 타이밍이 어긋난다.
            //
            // 하한이 1인 이유: 0층 상태에는 Idle도 같이 들어 있어서, 멈춰 있을 때
            // 배속이 1 아래로 내려가면 대기 동작이 느려터진다.
            float toRun = blend <= walkBlend
                ? 0f
                : (blend - walkBlend) / Mathf.Max(0.01f, 1f - walkBlend);
            float clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, toRun);

            // 상한도 같은 자리에서 섞는다. 걷기와 달리기는 원하는 다리 속도가 달라서
            // 상한 하나로 묶으면 한쪽을 맞추는 순간 다른 쪽이 어긋난다.
            float cap = Mathf.Lerp(maxWalkRate, maxRunRate, toRun);
            float wanted = Mathf.Clamp(speed / Mathf.Max(0.01f, clipSpeed), 1f, cap);
            walkRate = Mathf.MoveTowards(walkRate, wanted, rateLerp * Time.deltaTime);
            anim.SetFloat(WalkRateId, walkRate);

            DriveTwist();

        }
    }
}
