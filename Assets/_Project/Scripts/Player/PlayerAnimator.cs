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

        [Header("이동 — 블렌드와 발 미끄러짐")]
        [SerializeField, Tooltip("걷기/달리기 배속 계산. 자세한 설명은 LocomotionRate.cs에 있다")]
        LocomotionRate locomotion = new LocomotionRate();

        [SerializeField, Tooltip("발이 닿는 순간을 집어낸다. 자세한 설명은 FootPlant.cs에 있다")]
        FootPlant foot = new FootPlant();

        [SerializeField, Range(0.5f, 6f),
         Tooltip("발밑 둥근 그림자의 지름(m). 실시간 그림자를 껐으므로 이것이 대신한다")]
        float blobShadowSize = 2.4f;

        [SerializeField, Range(0.3f, 3f),
         Tooltip("주먹 바람의 크기 배율 [제안·미승인 2026-10-16]. 눈으로 보고 고를 값이다")]
        float punchWindSize = 1f;

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

        [Header("타격 — 러쉬 [지시 2026-09-30, 오라 러쉬]")]
        [SerializeField, Tooltip("연타 감지와 배속. 자세한 설명은 PunchRush.cs에 있다")]
        PunchRush rush = new PunchRush();

        [Header("자세 — 클립에 구워진 쏠림 되돌리기 [지시 10번, 2026-10-01]")]
        [SerializeField, Tooltip("실측한 상수 쏠림을 뺀다. 자세한 설명과 수치 근거는 PostureFix.cs에 있다")]
        PostureFix posture = new PostureFix();

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int PunchId = Animator.StringToHash("Punch");
        static readonly int WalkRateId = Animator.StringToHash("WalkRate");
        static readonly int RightArmId = Animator.StringToHash("RightArm");
        static readonly int PunchRateId = Animator.StringToHash("PunchRate");

        Animator anim;
        StrikeArmRig armRig;       // 붙어 있는 동안에는 자세 보정을 건너뛴다
        int upperLayer = -1;
        int twistLayer = -1;
        bool lastRight;            // 마지막에 어느 팔로 쳤나. 마무리가 반대팔로 나가게 한다
        int peakCount;             // 주먹이 정점에 닿은 횟수. 연출이 '다음 정점'을 기다리는 데 쓴다
        bool wasAtPeak;

        /// <summary>지금 연타 중인가. 0이면 단발, 1이면 러쉬. 연출이 읽는다.</summary>
        public float Rush01 => rush.Level01;

        /// <summary>
        /// 주먹이 정점에 닿은 <b>횟수</b>. 연출은 이 수가 늘기를 기다린다.
        ///
        /// <b>고정 지연으로는 안 된다.</b> 방아쇠 소비(최대 1프레임)와 전이(0.015초)의
        /// 지터가 정점까지의 53ms와 같은 자릿수라, 같은 코드로 재도 뻗음이 97%와 21%로
        /// 갈렸고 층 붕괴는 32%에서 터졌다 [실측 2026-09-30].
        ///
        /// <b>"지금 정점인가"로도 안 된다.</b> 새 탭이 들어왔을 때 이전 펀치가 아직 정점
        /// 구간에 있으면 즉시 참이라, 새 주먹이 아니라 옛 주먹을 보고 쏘게 된다.
        /// 횟수를 세면 언제나 <b>다음</b> 정점이다.
        /// </summary>
        public int StrikePeakCount => peakCount;

        bool AtStrikePeak
        {
            get
            {
                if (anim == null || upperLayer < 0) return false;
                if (anim.IsInTransition(upperLayer)) return false;
                AnimatorStateInfo cur = anim.GetCurrentAnimatorStateInfo(upperLayer);
                if (!cur.IsName("Punch") && !cur.IsName("PunchRight")) return false;
                float t = cur.normalizedTime - Mathf.Floor(cur.normalizedTime);
                return t >= holdAt && t < holdAt + holdWidth;
            }
        }

        /// <summary>지금 펀치가 몇 배로 돌고 있는가. 연출이 타격 시점을 앞당기는 데 쓴다.</summary>
        public float PunchSpeedScale => rush.SpeedScale;
        float twistWeight;

        /// <summary>지금 걷기/달리기 클립이 몇 배속으로 돌고 있는가. 1이면 원속도.</summary>
        public float WalkRate => locomotion.Rate;

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

            // 발가락 뼈는 여기서 한 번만 찾는다. 발소리가 이 뼈의 높이로 난다 —
            // FootPlant.cs 참조.
            foot.Bind(transform);

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

            // 발밑 둥근 그림자 [지시 2026-10-15: "실시간 그림자 끄자"].
            //
            // <b>맨 끝에서, 감싸서 부른다.</b> 전에는 Awake 한가운데에서 불렀는데
            // 거기서 예외가 나자 <b>그 아래의 레이어 찾기가 통째로 안 돌았다</b> —
            // upperLayer가 -1로 남아 타격 자세가 어긋났다. 곁다리 연출 하나가
            // 로봇의 움직임을 망가뜨릴 수는 없다.
            try
            {
                Utils.BlobShadow.Ensure(player != null ? player.transform : transform, blobShadowSize);

                // 주먹 바람도 여기서 만든다 [지시 2026-10-16]. 같은 try 안에 둔 이유는
                // 같다 — 연출을 만들다 던진 예외가 <b>Awake의 나머지를 통째로 건너뛰게</b>
                // 하면 안 된다. 전에 그림자가 그 사고를 냈고 상체 층이 죽었다.
                // 애니메이터를 넘긴다 — 휴머노이드면 뼈 이름을 안 보고 팔꿈치를 집는다.
                wind = Utils.PunchWind.Ensure(anim, punchWindSize);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PlayerAnimator] 발밑 그림자나 주먹 바람을 못 만들었다 — "
                               + "움직임은 그대로다: " + e.Message);
            }

            // 뼈를 한 번만 찾아 둔다 (규칙 5).
            posture.Bind(transform);
            armRig = GetComponentInParent<StrikeArmRig>();
        }

        /// <summary>
        /// 자세 보정은 <b>애니메이터가 뼈를 쓴 뒤에</b> 들어가야 한다 — 그래서 LateUpdate다.
        /// Update(이 클래스의 나머지)에서 하면 같은 프레임에 덮어써져 아무 일도 안 일어난다.
        ///
        /// 파괴 화면에서는 건너뛴다. <see cref="StrikeArmRig"/>이 미리 잰 고정 오프셋으로
        /// 주먹을 과녁에 박아 두는데(어긋남 0.0005), Hips를 돌리면 그 조준이 통째로 틀어진다.
        /// </summary>
        void LateUpdate()
        {
            if (!posture.Ready) return;
            if (armRig != null && armRig.Attached) return;
            posture.Apply(player != null ? player.transform : transform, locomotion.Blend);
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
            // <b>러쉬 중에는 감속을 뺀다</b> [지시 2026-09-30].
            //
            // 감속은 단발에서 한 방을 읽게 하는 장치인데, 연타에서는 그것이
            // 헛치는 원인이 된다 — 늦어진 주먹 위로 다음 입력이 들어와 회수되기 때문이다.
            // 러쉬에서 필요한 것은 "더 세게"가 아니라 <b>제때 도착</b>이다.
            rush.Decay();

            // 정점 진입을 센다. 여기서 세는 이유는 DriveTwist가 매 프레임 한 번만
            // 불리기 때문이다 — 프로퍼티 안에서 세면 읽는 쪽 수만큼 중복으로 센다.
            bool nowPeak = AtStrikePeak;
            if (nowPeak && !wasAtPeak) peakCount++;
            wasAtPeak = nowPeak;

            float t = cur.normalizedTime - Mathf.Floor(cur.normalizedTime);
            bool inHold = punching && !anim.IsInTransition(upperLayer)
                          && t >= holdAt && t < holdAt + holdWidth
                          && rush.Level01 < 0.5f;
            anim.SetFloat(PunchRateId, inHold ? holdRate : PunchSpeedScale);
        }

        /// <summary>
        /// <b>러쉬를 끝맺는 한 방</b> [지적 2026-09-30].
        ///
        /// 러쉬 마무리 정지가 주먹이 정점의 20%밖에 안 뻗은 상태에서 터지고 있었다.
        /// 마무리를 Rush01 감쇠 타이머로 쐈기 때문인데, 어느 탭이 마지막인지는
        /// <b>다음 탭이 안 와야</b> 알 수 있어서 타이머로는 원리적으로 못 맞춘다.
        ///
        /// 그래서 맞추는 대신 <b>따로 지른다</b>. 러쉬를 즉시 해제해 정상 속도로 내보내므로
        /// 주먹이 끝까지 뻗고, 연출은 그 정점(impactDelay 뒤)에서 멈춘다.
        /// 반대팔로 나가서 연타의 마지막 타를 이어받는 그림이 된다.
        ///
        /// 게이지는 <see cref="Destruction.DemolitionSession"/>이 올리므로
        /// 이 한 방은 <b>순수 연출</b>이고 진행에 영향이 없다.
        /// </summary>
        public void PunchFinisher()
        {
            if (anim == null) return;
            rush.Clear();                // 정상 속도로 — 러쉬 배속이면 정점을 지나쳐 버린다
            lastRight = !lastRight;
            anim.SetBool(RightArmId, lastRight);
            anim.SetTrigger(PunchId);
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

            // <b>실제 시간</b>으로 잰다. 히트스톱이 걸린 동안 게임 시간은 안 흐르는데
            // 플레이어의 손은 계속 움직이므로, 게임 시간으로 재면 간격이 0으로 보인다.
            lastRight = rightArm;
            rush.Note();
        }

        /// <summary>
        /// 주먹 쪽 바람 [지시 2026-10-16]. 애니메이션과 따로 두는 이유는
        /// <see cref="PunchFinisher"/>처럼 <b>바람 없이 애니메이션만</b> 내보내야 하는
        /// 자리가 있을 수 있어서다.
        ///
        /// 못 만들어도 움직임은 그대로다 — 연출이 본동작을 막아서는 안 된다
        /// (<see cref="Utils.BlobShadow"/>에서 한 번 당한 적이 있다).
        /// </summary>
        public void PunchWind(bool rightArm)
        {
            if (wind == null) return;
            try { wind.Burst(rightArm); }
            catch (System.Exception e)
            {
                wind = null;        // 한 번 실패하면 매 타격마다 던지게 두지 않는다
                Debug.LogWarning("[PlayerAnimator] 주먹 바람을 못 냈다 — 끄고 간다: " + e.Message);
            }
        }

        /// <summary>
        /// 걸려 있는 주먹을 거둔다 [고침 2026-10-16].
        ///
        /// <c>SetTrigger</c>로 건 방아쇠는 <b>소비될 때까지 남아 있다.</b> 파괴 화면이
        /// 끝나는 순간에 걸린 것이 그대로 남으면 필드로 돌아간 뒤에 터져서,
        /// 로봇이 허공에 주먹을 지른다. <c>ResetTrigger</c>가 그 대기를 지운다.
        /// </summary>
        public void CancelPunch()
        {
            if (anim == null) return;
            anim.ResetTrigger(PunchId);
        }

        Utils.PunchWind wind;

        void Update()
        {
            if (anim == null) return;

            float speed = player != null ? player.CurrentSpeed : 0f;
            float top = player != null ? player.TopSpeed : 1f;

            locomotion.Tick(speed, top, Time.deltaTime);
            anim.SetFloat(SpeedId, locomotion.Blend);

            // 배속은 <b>0층 상태에만</b> 건다. anim.speed를 쓰면 상체 주먹까지 같이 빨라져
            // 84ms에 맞춰 둔 타격 타이밍이 어긋난다.
            anim.SetFloat(WalkRateId, locomotion.Rate);

            // 발소리는 <b>여기서</b> 난다 [고침 2026-10-14]. 발이 닿는 순간을 아는 것은
            // 애니메이션뿐이다 — 걸은 거리로 내면 다리와 계속 어긋난다.
            Transform body = player != null ? player.transform : transform;
            if (foot.Tick(body, locomotion.Blend, Time.deltaTime) > 0)
                Utils.GameAudio.Play(Data.Sfx.Footstep, body.position);

            DriveTwist();

        }
    }
}
