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
    /// 파티클과 소리는 아직 없다. 재료는 있다 —
    /// <c>Assets/ThirdParty/Cartoon FX Remaster/</c>, <c>Casual Game Sounds U6</c>,
    /// VARCO Sound. 붙일 때는 CLAUDE.md 규칙 6대로 <b>반드시 풀링</b>해야 한다.
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

        Core.FollowCamera cam;
        Player.PlayerAnimator robot;
        Coroutine freeze;
        float restoreScale = 1f;      // 히트스톱이 끝나면 되돌릴 배율
        bool frozen;

        void Awake()
        {
            cam = FindFirstObjectByType<Core.FollowCamera>();
            robot = FindFirstObjectByType<Player.PlayerAnimator>();
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
        }

        public void OnStrike(float progress01, Vector3 point)
        {
            Shake(Mathf.Lerp(strikeShake, strikeShakeMax, Mathf.Clamp01(progress01)),
                  strikeShakeSeconds);
        }

        /// <summary>
        /// 층 하나가 통째로 쏟아졌다. 이 게임에서 가장 자주 오는 <b>큰 박자</b>다 —
        /// 금이 번지다 한 번에 터지는 순간이라, 여기가 비면 "쾅"이 그림으로만 남는다.
        /// </summary>
        public void OnFloorCollapse(Vector3 point)
        {
            Shake(collapseShake, collapseShakeSeconds);
            HitStop(collapseHitStop);
        }

        /// <summary>대상이 완전히 부서진 순간.</summary>
        public void OnBroken(Vector3 point)
        {
            Shake(brokenShake, brokenShakeSeconds);
            HitStop(brokenHitStop);
        }

        void Shake(float magnitude, float seconds)
        {
            if (cam == null) cam = FindFirstObjectByType<Core.FollowCamera>();
            if (cam != null) cam.Shake(magnitude, seconds);
        }

        /// <summary>
        /// 화면을 아주 잠깐 멈춘다. 때린 것이 <b>단단했다</b>는 신호다 —
        /// 부드럽게 이어지면 손이 허공을 지난 것처럼 느껴진다.
        /// </summary>
        void HitStop(float scale)
        {
            GameConfig cfg = Core.GameConfigRef.Current;
            float seconds = (cfg != null ? cfg.hitStopSeconds : 0f) * scale;
            if (seconds <= 0f || !isActiveAndEnabled) return;

            // 되돌릴 배율은 <b>멈추기 전에</b> 기억한다.
            //
            // 코루틴 안에서 읽으면 겹칠 때 터진다 — 두 번째 히트스톱이 첫 코루틴을 끊는
            // 시점에는 이미 배율이 0이라, 그걸 "되돌릴 값"으로 삼으면 게임이 영영 멈춘다.
            // 이미 멈춰 있는 동안 들어온 호출은 <b>시간만 늘린다</b>.
            if (!frozen)
            {
                if (Time.timeScale <= 0f) return;   // 남이 멈춰 둔 것이다 — 건드리지 않는다
                restoreScale = Time.timeScale;
                frozen = true;
            }

            if (freeze != null) StopCoroutine(freeze);
            freeze = StartCoroutine(Freeze(seconds));
        }

        /// <summary>
        /// 대기는 반드시 <b>실제 시간</b>으로 잰다. 배율이 0인 동안에는 게임 시간이
        /// 한 톨도 안 흐르므로, 보통 대기를 쓰면 영원히 안 깨어난다.
        /// </summary>
        IEnumerator Freeze(float seconds)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);
            Restore();
        }

        void Restore()
        {
            if (!frozen) return;
            Time.timeScale = restoreScale;
            frozen = false;
            freeze = null;
        }

        /// <summary>연출 도중에 꺼져도 시간이 멈춘 채로 남지 않게 한다.</summary>
        void OnDisable()
        {
            if (freeze != null) StopCoroutine(freeze);
            freeze = null;
            Restore();
        }
    }
}
