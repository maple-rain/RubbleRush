using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 발이 <b>실제로 땅에 닿는 순간</b>을 집어낸다 [고침 2026-10-14].
    ///
    /// <b>무엇이 틀렸었나.</b> 발소리를 <b>걸은 거리</b>로 냈다 — 2.2m마다 한 번.
    /// 그런데 다리는 애니메이션 주기로 움직인다. 둘은 서로를 모르므로
    /// 소리와 발이 계속 어긋났다. 기획자 지적: <b>"발을 딛는 타이밍과 소리 재생의
    /// 타이밍이 안 맞는다."</b> 거리로 내면 보폭이 일정해 보인다는 것이 그때의 이유였는데,
    /// 보폭이 일정한 것과 <b>발이 닿을 때 소리가 나는 것</b>은 다른 문제였다.
    ///
    /// <b>애니메이션 이벤트를 안 쓰는 이유.</b> Walk은 FBX에서 임포트된 클립이라
    /// 이벤트를 심으려면 ModelImporter를 거쳐야 하고, 클립을 다시 자를 때마다
    /// (<c>Demo/8b</c>) 날아간다. 상태의 <b>위상</b>을 읽으면 클립이 바뀌어도 따라온다.
    ///
    /// <b>위상은 실측값이다</b> [2026-10-14]. 임시 복제본에 클립을 48등분으로 샘플링해
    /// 발가락 뼈가 제일 낮은 지점을 찾았다:
    ///
    /// | 클립 | 주기 | 오른발 | 왼발 |
    /// |---|---|---|---|
    /// | Walk | 1.125초 | 0.292 | 0.792 |
    /// | Run | 0.625초 | 0.375 | 0.896 |
    ///
    /// 블렌드 트리는 자식 클립을 <b>같은 정규화 시간</b>으로 돌리므로, 걷기와 달리기
    /// 사이에서는 위상도 그 사이에서 섞인다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="LocomotionRate"/>와 같다.
    /// </summary>
    [System.Serializable]
    public sealed class FootPlant
    {
        [Header("발이 닿는 위상 — 실측 2026-10-14")]
        [SerializeField, Range(0f, 1f), Tooltip("Walk에서 오른발이 닿는 위상")]
        float walkRight = 0.292f;

        [SerializeField, Range(0f, 1f), Tooltip("Walk에서 왼발이 닿는 위상")]
        float walkLeft = 0.792f;

        [SerializeField, Range(0f, 1f), Tooltip("Run에서 오른발이 닿는 위상")]
        float runRight = 0.375f;

        [SerializeField, Range(0f, 1f), Tooltip("Run에서 왼발이 닿는 위상")]
        float runLeft = 0.896f;

        [SerializeField, Range(0.05f, 0.9f),
         Tooltip("블렌드 트리에서 Walk가 놓인 자리. AC_Demolition은 0.373이다.\n" +
                 "이 위로는 Run 쪽 위상에 가까워진다")]
        float walkBlend = 0.373f;

        [SerializeField, Range(0f, 0.5f),
         Tooltip("이보다 느리면 발소리를 내지 않는다. 제자리 대기에서 울리지 않게 하는 문턱")]
        float minBlend = 0.1f;

        float lastPhase = -1f;

        /// <summary>실측·디버그용. 지금까지 집어낸 착지 횟수.</summary>
        public int Plants { get; private set; }

        /// <summary>
        /// 한 프레임 보고, <b>이번 프레임에 발이 닿은 횟수</b>를 돌려준다.
        /// 보통 0이고 가끔 1이다. 배속이 아주 높으면 2가 될 수도 있다.
        /// </summary>
        /// <param name="anim">로봇의 Animator</param>
        /// <param name="layer">이동이 들어 있는 층. 보통 0</param>
        /// <param name="blend">지금 블렌드 위치(0=정지, 1=최고속). <see cref="LocomotionRate.Blend"/></param>
        public int Tick(Animator anim, int layer, float blend)
        {
            if (anim == null || blend < minBlend)
            {
                // 멈추면 위상을 버린다. 다시 걸을 때 <b>직전 위상에서 이어지면</b>
                // 멈춘 동안 지나간 착지가 한꺼번에 터진다.
                lastPhase = -1f;
                return 0;
            }

            // 전환 중에는 위상이 두 상태 사이에서 튄다 — 그 틈에 울리면 박자가 깨진다.
            if (anim.IsInTransition(layer)) return 0;

            AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(layer);
            float phase = st.normalizedTime - Mathf.Floor(st.normalizedTime);

            if (lastPhase < 0f)                     // 막 걷기 시작했다 — 기준만 잡는다
            {
                lastPhase = phase;
                return 0;
            }

            // 걷기 구간 위로는 Run 쪽 위상에 가까워진다. 블렌드 트리가 두 클립을 같은
            // 정규화 시간으로 돌리므로, 발이 닿는 자리도 그 사이에서 섞인다.
            float toRun = blend <= walkBlend
                ? 0f
                : (blend - walkBlend) / Mathf.Max(0.01f, 1f - walkBlend);

            int hits = 0;
            if (Crossed(lastPhase, phase, Mathf.Lerp(walkRight, runRight, toRun))) hits++;
            if (Crossed(lastPhase, phase, Mathf.Lerp(walkLeft, runLeft, toRun))) hits++;

            lastPhase = phase;
            Plants += hits;
            return hits;
        }

        /// <summary>
        /// 위상이 <paramref name="at"/>을 지나갔는가.
        ///
        /// 위상은 1에서 0으로 <b>되감긴다.</b> 그 프레임에서 <c>from &gt; to</c>가 되므로
        /// 단순 비교로는 한 주기에 한 번씩 착지를 놓친다 — 되감긴 경우를 따로 본다.
        /// </summary>
        static bool Crossed(float from, float to, float at)
        {
            return to >= from ? from < at && at <= to     // 평범하게 흘렀다
                              : at > from || at <= to;    // 1 -> 0으로 되감겼다
        }
    }
}
