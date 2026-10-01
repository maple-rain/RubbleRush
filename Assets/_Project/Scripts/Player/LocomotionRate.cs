using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 걷기·달리기 클립을 <b>발이 안 미끄러지게</b> 돌리는 배속 계산 [분리 2026-10-01].
    ///
    /// <b>블렌드와 배속은 다른 일이다.</b> 블렌드(Speed)는 "어떤 클립을 섞을지"만 정하지
    /// "얼마나 빨리 돌릴지"는 안 정한다. 그래서 Speed만으로는 발이 미끄러진다.
    ///
    /// 지금 섞여 있는 자리에서 트리가 <b>실제로 내는 속도</b>를 먼저 구하고,
    /// 몸 속도를 그걸로 나눈 것이 배속이다.
    ///
    /// <b>왜 떼어냈나.</b> <see cref="PlayerAnimator"/>가 334줄이었다 (CLAUDE.md 규칙 3).
    /// 이동 배속은 타격·자세와 엮이지 않는 자족적인 덩어리라 경계가 깨끗하다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="PunchRush"/>·<see cref="PostureFix"/>와 같다.
    /// </summary>
    [System.Serializable]
    public sealed class LocomotionRate
    {
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

        float blend;
        float rate = 1f;

        /// <summary>0=정지, 1=최고속. 블렌드 트리가 읽는 값이고 자세 보정도 이걸 본다.</summary>
        public float Blend => blend;

        /// <summary>0층 상태에 거는 재생 배속. 디버그 표시가 읽는다.</summary>
        public float Rate => rate;

        /// <summary>
        /// 한 프레임 진행시킨다.
        ///
        /// 배속은 <b>0층 상태에만</b> 건다 — anim.speed를 쓰면 상체 주먹까지 같이 빨라져
        /// 84ms에 맞춰 둔 타격 타이밍이 어긋난다. 그 거는 일은 부르는 쪽이 한다.
        /// </summary>
        /// <param name="speed">몸이 실제로 내는 속도(m/s)</param>
        /// <param name="top">달리기 최고 속도(m/s). 블렌드의 눈금이다</param>
        public void Tick(float speed, float top, float dt)
        {
            if (top < 0.01f) top = 1f;

            // Speed는 <b>달리기 속도로 정규화</b>한 값이다. 트리의 문턱(0 / walkBlend / 1)이
            // 이 눈금 위에 놓여 있다.
            blend = Mathf.MoveTowards(blend, Mathf.Clamp01(speed / top), blendLerp * dt);

            // 걷기 구간 아래에서는 Walk 클립만 의미가 있고, 그 위로는 Walk와 달리기 사이를
            // 섞으므로 속도도 같이 섞인다.
            float toRun = blend <= walkBlend
                ? 0f
                : (blend - walkBlend) / Mathf.Max(0.01f, 1f - walkBlend);
            float clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, toRun);

            // 상한도 같은 자리에서 섞는다. 걷기와 달리기는 원하는 다리 속도가 달라서
            // 상한 하나로 묶으면 한쪽을 맞추는 순간 다른 쪽이 어긋난다.
            //
            // 하한이 1인 이유: 0층 상태에는 Idle도 같이 들어 있어서, 멈춰 있을 때
            // 배속이 1 아래로 내려가면 대기 동작이 느려터진다.
            float cap = Mathf.Lerp(maxWalkRate, maxRunRate, toRun);
            float wanted = Mathf.Clamp(speed / Mathf.Max(0.01f, clipSpeed), 1f, cap);
            rate = Mathf.MoveTowards(rate, wanted, rateLerp * dt);
        }
    }
}
