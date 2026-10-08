using System.Collections;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 연출 — 두드림·층 붕괴·완파의 세 박자에 <b>셰이크와 히트스톱</b>을 붙인다
    /// (TECH_SPEC §3, GAME_DESIGN §4.4).
    ///
    /// 이 클래스에는 오래 "아직 구현되지 않았다 (D8)"고 적혀 있었고, 그 이유로
    /// <b>"두드리는 순간이라는 이벤트 자체가 없다"</b>를 들고 있었다. 파괴가 연속 게이지라
    /// 히트스톱을 걸 지점이 없다는 뜻이었다. <b>그 조건은 해소됐다</b> [2026-09-28] —
    /// 지금 파괴는 <see cref="DemolitionSession.Tap"/>이라는 이산 사건이고, 층은
    /// 한 번에 쏟아진다. 연출이 붙을 자리가 세 군데로 또렷해졌다.
    ///
    /// <b>박자를 셋으로 나눈 이유.</b> 모든 타격에 같은 연출을 걸면 박자가 없다.
    /// 초당 다섯 번 두드리는데 매번 히트스톱 0.08초를 걸면 <b>시간의 4할이 멈춰</b>
    /// 조작이 끈적해진다. 그래서 잦은 것은 약하게, 드문 것은 세게 준다.
    ///
    /// | 박자 | 빈도 | 셰이크 | 히트스톱 |
    /// |---|---|---|---|
    /// | 두드림 | 초당 5회 | 약함. 진행도만큼 커진다 | 없음 |
    /// | 층 붕괴 | 층마다 1회 | 강함 | 있음 |
    /// | 완파 | 건물마다 1회 | 가장 강함 | 가장 길다 |
    ///
    /// <b>소리는 붙었다</b> [2026-10-08]. 네 박자가 그대로 <see cref="Data.Sfx"/>의 네 이름이 된다 —
    /// 어떤 음원을 울릴지는 <see cref="Data.SoundBank"/> 에셋이 정하고, 재생은
    /// <see cref="Utils.GameAudio"/>가 풀에서 한다 (CLAUDE.md 규칙 6).
    /// 파티클은 아직 없다. 재료는 <c>Assets/ThirdParty/Cartoon FX Remaster/</c>에 있다.
    /// </summary>
    public sealed class DestructionFX : MonoBehaviour
    {
        [Header("셰이크 — 모든 값이 [제안·미승인]")]
        [SerializeField, Range(0f, 0.5f),
         Tooltip("한 번 두드릴 때 흔들리는 세기(m). 진행도 0에서의 값이다")]
        float strikeShake = 0.035f;

        [SerializeField, Range(0f, 0.5f),
         Tooltip("진행도 1에서의 두드림 세기(m). 층이 끝나갈수록 손맛이 무거워진다 — " +
                 "같은 동작을 반복하는데 반응이 커지면 '버티던 것이 무너지고 있다'로 읽힌다")]
        float strikeShakeMax = 0.09f;

        [SerializeField, Range(0.02f, 0.5f), Tooltip("두드림 셰이크가 잦아드는 시간(초)")]
        float strikeShakeSeconds = 0.10f;

        [SerializeField, Range(0f, 1f), Tooltip("층 하나가 통째로 쏟아질 때의 세기(m)")]
        float collapseShake = 0.30f;

        [SerializeField, Range(0.05f, 1.5f), Tooltip("층 붕괴 셰이크가 잦아드는 시간(초)")]
        float collapseShakeSeconds = 0.45f;

        [SerializeField, Range(0f, 1.5f), Tooltip("건물이 완전히 부서질 때의 세기(m)")]
        float brokenShake = 0.55f;

        [SerializeField, Range(0.05f, 2f), Tooltip("완파 셰이크가 잦아드는 시간(초)")]
        float brokenShakeSeconds = 0.7f;

        [Header("히트스톱")]
        [SerializeField, Range(0f, 4f),
         Tooltip("층 붕괴에서 멈추는 시간. GameConfig.hitStopSeconds에 곱한다. " +
                 "설정에 0.08이 들어 있으므로 1이면 0.08초다")]
        float collapseHitStop = 1f;

        [SerializeField, Range(0f, 6f),
         Tooltip("완파에서 멈추는 시간. GameConfig.hitStopSeconds에 곱한다. " +
                 "마지막 한 방이라 가장 길게 잡는다")]
        float brokenHitStop = 2.2f;

        [SerializeField, Range(0f, 2f),
         Tooltip("<b>보통 두드림</b>에서 멈추는 시간. GameConfig.hitStopSeconds에 곱한다.\n" +
                 "여기는 반드시 짧아야 한다. 탭이 초당 5번이라 간격이 200ms인데, " +
                 "층 붕괴와 같은 1(=80ms)을 걸면 <b>간격의 40%가 정지</b>라 " +
                 "타격감이 아니라 렉으로 읽힌다.\n" +
                 "0.5 = 40ms = 60fps에서 2.4프레임. 격투게임 약공격 히트스톱이 " +
                 "보통 2~4프레임이다")]
        float strikeHitStop = 0.5f;

        [SerializeField, Range(0.05f, 0.6f),
         Tooltip("연출이 <b>주먹이 다음 정점에 닿기를</b> 이만큼(초)까지 기다린다.\n" +
                 "고정 지연으로 쏘면 프레임 지터에 걸려 팔이 안 펴진 채 멈춘다 — " +
                 "실측으로 같은 코드가 97%와 21%로 갈렸고, 층 붕괴는 32%에서 터졌다 " +
                 "[2026-09-30]. 그래서 보고 쏜다.\n" +
                 "이 시간을 넘기면 그냥 쏜다 — 어떤 이유로든 정점이 안 오면 " +
                 "연출이 통째로 빠지는 것보다 늦게라도 나오는 편이 낫다")]
        float peakTimeout = 0.30f;

        [SerializeField, Range(1f, 6f),
         Tooltip("러쉬가 최대일 때 <b>마지막 한 방</b>의 정지를 몇 배로 키우는가 " +
                 "[지시 2026-09-30, 오라 러쉬].\n" +
                 "연타 중에는 '마지막 입력만' 규칙이 정지를 삼키고 마지막 탭에서만 터진다. " +
                 "그 한 방이 단발과 같으면 러쉬가 조용히 끝나 버린다 — 러쉬는 예열이고 " +
                 "마무리가 타격이다.\n" +
                 "3이면 0.5 x 3 x 0.08 = 120ms. 층 붕괴(80ms)보다 크고 완파(176ms)보다 작다. " +
                 "[제안·미승인]")]
        float rushFinishHitStop = 3f;

        [SerializeField, Range(0.1f, 1f),
         Tooltip("Rush01이 이 값을 넘는 동안에는 <b>정지를 걸지 않는다</b>. 흔들기만 남는다.\n" +
                 "러쉬 중에 멈추면 애니메이션도 같이 멈춰서 다음 주먹이 못 나간다 — " +
                 "실측으로 8연타 중 정점에 닿은 것이 2번뿐이었다. 오라 러쉬는 " +
                 "<b>안 멈추는 구간</b>이 있어야 잔상이 된다. [제안·미승인]")]
        float rushSuppressAbove = 0.5f;

        [SerializeField, Range(0f, 0.6f),
         Tooltip("러쉬가 끝날 때의 셰이크 세기(m). <b>층 붕괴(0.30)를 쓰면 안 된다</b> — " +
                 "콤보 마무리에 '한 층이 통째로 무너졌다' 세기를 쓰는 꼴이라 " +
                 "일반 타격(0.035~0.09)의 8배까지 튄다 [지적 2026-09-30].\n" +
                 "0.15면 한 방 최대치의 약 1.7배다. [제안·미승인]")]
        float rushFinishShake = 0.15f;

        [SerializeField, Range(0.05f, 1f), Tooltip("러쉬 마무리 셰이크가 잦아드는 시간(초)")]
        float rushFinishShakeSeconds = 0.25f;

        Core.FollowCamera cam;
        Player.PlayerAnimator robot;
        HitStop stop;             // 시간을 멈추는 일은 이쪽이 전담한다
        Coroutine pendingImpact;  // 대기 중인 타격은 하나뿐이다 — 새 입력이 이긴다
        float rushPeak;           // 이번 러쉬에서 도달한 최대치
        bool rushArmed;           // 마무리를 쏠 준비가 됐는가

        void Awake()
        {
            cam = FindFirstObjectByType<Core.FollowCamera>();
            robot = FindFirstObjectByType<Player.PlayerAnimator>();

            // DemolitionSession이 이 컴포넌트를 런타임에 붙이므로, 짝도 여기서 챙긴다.
            // Awake에서의 AddComponent는 규칙 5(Update 금지)에 걸리지 않는다.
            stop = GetComponent<HitStop>();
            if (stop == null) stop = gameObject.AddComponent<HitStop>();
        }

        /// <summary>파괴가 진행되는 동안 매 프레임. 지금은 쓰지 않는다 — 연출은 전부 사건 단위다.</summary>
        public void OnProgress(float progress01) { }

        /// <summary>
        /// 한 번 두드렸다. 초당 다섯 번 오므로 <b>가볍게</b> 해야 한다 —
        /// 여기에 히트스톱을 걸면 조작이 끈적해진다.
        /// </summary>
        /// <summary>
        /// 로봇이 주먹을 뻗는다. <b>모든 탭에서</b> 불러야 한다 [지시 9번, 2026-09-29].
        ///
        /// 예전에는 <see cref="OnStrike"/> 안에 있었는데, 층이 무너지는 탭은
        /// <see cref="OnFloorCollapse"/> 쪽으로 갈라지고 <b>마지막 탭은 둘 다 아니라서</b>
        /// (층이 터지면서 동시에 완파) 주먹이 아예 안 나갔다. 건물이 굳어 있다가
        /// 저 혼자 무너지는 것처럼 보인 원인이다.
        ///
        /// 주먹은 <b>플레이어의 행동</b>이지 연출 박자가 아니다. 그래서 박자 선택과 분리한다.
        /// </summary>
        /// <param name="rightArm">오른팔로 치는가. <see cref="Player.InputRouter"/>가
        /// 누르는 순간에 정한다 — 키보드는 교대, 터치는 화면 좌우 [지시 2026-09-28].</param>
        public void OnPunch(bool rightArm)
        {
            if (robot == null) robot = FindFirstObjectByType<Player.PlayerAnimator>();
            if (robot != null) robot.Punch(rightArm);
            Utils.GameAudio.Play(Data.Sfx.Punch, transform.position);
        }

        public void OnStrike(float progress01, Vector3 point)
        {
            Impact(Mathf.Lerp(strikeShake, strikeShakeMax, Mathf.Clamp01(progress01)),
                   strikeShakeSeconds, strikeHitStop);

            // 셰이크와 같은 곡선을 탄다 — 건물이 깎여 갈수록 소리도 커진다.
            // 박자를 셋으로 나눈 이유(위 표)가 소리에도 그대로 적용된다.
            Utils.GameAudio.Play(Data.Sfx.Strike, point,
                                 Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(progress01)));
        }

        /// <summary>
        /// 층 하나가 통째로 쏟아졌다. 이 게임에서 가장 자주 오는 <b>큰 박자</b>다 —
        /// 금이 번지다 한 번에 터지는 순간이라, 여기가 비면 "쾅"이 그림으로만 남는다.
        /// </summary>
        public void OnFloorCollapse(Vector3 point)
        {
            Impact(collapseShake, collapseShakeSeconds, collapseHitStop);
            Utils.GameAudio.Play(Data.Sfx.FloorCollapse, point);
        }

        /// <summary>대상이 완전히 부서진 순간.</summary>
        public void OnBroken(Vector3 point)
        {
            Impact(brokenShake, brokenShakeSeconds, brokenHitStop);
            Utils.GameAudio.Play(Data.Sfx.Broken, point);
        }

        /// <summary>
        /// 타격 한 박자. <b>주먹이 닿는 순간까지 미뤘다가</b> 흔들고 멈춘다.
        ///
        /// 기다리는 이유는 <see cref="peakTimeout"/>에 적어 뒀다 — 연출이 주먹보다
        /// 먼저 터지고 있었다. 흔들림을 키우거나 히트스톱을 얹기 전에 이것부터
        /// 맞춰야 한다. 어긋난 자리에 크게 얹으면 더 어색해질 뿐이다.
        ///
        /// <b>대기 중인 타격은 하나뿐이다</b> [지시 2026-09-30]. 손이 떨어지기 전에
        /// 다음 펀치가 나오면 앞의 것은 버리고 <b>마지막 입력</b>만 남긴다.
        ///
        /// tapsPerSecond(5)는 게이지 계산에만 쓰이지 입력을 막지 않는다. 연타하면
        /// 프레임마다도 들어오는데, 탭마다 띄우면 정지 구간이 겹겹이 쌓여 화면이
        /// 계속 끊긴다 — 한 번이 33.5ms인데 60ms 간격이면 절반 이상이 정지다.
        ///
        /// 버려진 탭도 <b>게이지는 이미 올렸다</b>. 여기서 버리는 것은 연출뿐이라
        /// 진행이 손해 보지 않는다. 팔의 회수는 애니메이터가 맡는다 —
        /// Punch -> Punch 전이가 0.040초 고정이라 새 입력이 뻗은 팔을 곧장 당긴다.
        /// </summary>
        void Impact(float magnitude, float shakeSeconds, float hitStopScale)
        {
            if (!isActiveAndEnabled)
            {
                Shake(magnitude, shakeSeconds);
                if (stop != null) stop.Hold(hitStopScale);
                return;
            }
            if (pendingImpact != null) StopCoroutine(pendingImpact);
            pendingImpact = StartCoroutine(ImpactAfter(magnitude, shakeSeconds, hitStopScale));
        }

        /// <summary>
        /// <b>주먹이 다음 정점에 닿을 때</b> 흔들고 멈춘다.
        ///
        /// 정점 <b>횟수</b>가 늘기를 기다리는 것이 핵심이다. 지금 정점인지만 보면,
        /// 새 탭이 들어왔을 때 이전 펀치가 아직 정점 구간에 있어 즉시 터진다 —
        /// 새 주먹이 아니라 옛 주먹을 보고 쏘는 꼴이 된다.
        /// </summary>
        IEnumerator ImpactAfter(float magnitude, float shakeSeconds, float hitStopScale)
        {
            int seen = robot != null ? robot.StrikePeakCount : 0;
            float deadline = Time.unscaledTime + peakTimeout;
            while (Time.unscaledTime < deadline
                   && robot != null && robot.StrikePeakCount == seen)
                yield return null;

            pendingImpact = null;

            // 흔들기는 <b>러쉬 중에도 매 타격</b> 울린다 — 그게 연속 타격으로 읽힌다.
            // 정지만 뺀다. 멈추면 애니메이션도 멈춰서 다음 주먹이 못 나가기 때문이다.
            Shake(magnitude, shakeSeconds);
            float rush = robot != null ? robot.Rush01 : 0f;
            if (rush <= rushSuppressAbove && stop != null) stop.Hold(hitStopScale);
        }

        /// <summary>
        /// <b>러쉬가 끝나는 순간을 잡는다</b> [지시 2026-09-30, 오라 러쉬].
        ///
        /// 러쉬는 예열이고 마무리가 타격이다. 연타하는 동안에는 정지를 빼 두었으므로
        /// (<see cref="rushSuppressAbove"/>) 여기서 한 번 크게 갚는다.
        ///
        /// 취소 규칙에 맡기지 않고 따로 잡는 이유: 러쉬에서는 지연이 30ms까지 줄어
        /// 탭 간격(60ms)보다 짧아지므로, 각 타격이 다음 탭 전에 이미 터져 버린다.
        /// 취소는 <b>대기 중인 것</b>만 막는다.
        /// </summary>
        void Update()
        {
            if (robot == null) return;
            float r = robot.Rush01;

            if (r > rushSuppressAbove)
            {
                rushPeak = Mathf.Max(rushPeak, r);
                rushArmed = true;
                return;
            }
            if (!rushArmed) return;

            rushArmed = false;
            float peak = rushPeak;
            rushPeak = 0f;

            // <b>마무리는 따로 지른다.</b> 타이머로 잡은 이 순간에는 마지막 연타의 팔이
            // 이미 내려와 있다 (실측 정점의 20%). 한 방을 정상 속도로 더 내고
            // 그 정점에서 멈추면 팔이 끝까지 뻗은 그림이 나온다.
            if (robot != null) robot.PunchFinisher();
            Impact(rushFinishShake, rushFinishShakeSeconds,
                   strikeHitStop * rushFinishHitStop * peak);
        }

        void Shake(float magnitude, float seconds)
        {
            if (cam == null) cam = FindFirstObjectByType<Core.FollowCamera>();
            if (cam != null) cam.Shake(magnitude, seconds);
        }

        /// <summary>연출 도중에 꺼져도 대기 중인 타격이 남지 않게 한다.</summary>
        void OnDisable()
        {
            if (pendingImpact != null) StopCoroutine(pendingImpact);
            pendingImpact = null;
            rushArmed = false;
            rushPeak = 0f;
        }
    }
}
