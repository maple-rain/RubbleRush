using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 건물이 <b>기울어지며 구멍으로 넘어간다</b> [지시 2026-10-15].
    ///
    /// 기획자: "쏙하고 빠지는게 아니라 기울어지면서 떨어지는 느낌으로 하고싶어."
    ///
    /// <b>왜 기울어야 하는가.</b> 실측하면 6단계 구멍은 반지름 4.00m인데 건물 토대는
    /// 작은 것이 1.08m, 큰 것이 8.07m다. 즉 <b>큰 건물은 구멍보다 넓다.</b>
    /// 그런 것을 수직으로 내려보내면 바닥을 뚫고 지나가는 것처럼 보인다.
    /// 가장자리를 축으로 <b>넘어뜨리면</b> 구멍이 더 좁아도 말이 된다 — 나무가 쓰러져
    /// 틈으로 빠지는 그림이다.
    ///
    /// <b>물리가 아니다.</b> <see cref="CollapseMotion"/>과 같은 이유다 — Rigidbody를 달면
    /// 건물 60채가 전부 시뮬레이션 대상이 되고 결과가 결정적이지 않아 튜닝이 안 된다.
    /// 여기서는 <b>회전축 하나를 잡고 돌린 뒤 가라앉히는</b> 두 토막 트윈이다.
    ///
    /// 스스로 붙고 스스로 떨어진다 — <see cref="Begin"/>이 컴포넌트를 달고,
    /// 끝나면 <c>Destroy</c>한다. 부르는 쪽은 한 줄이면 된다.
    /// </summary>
    public sealed class ToppleFall : MonoBehaviour
    {
        /// <summary>넘어가는 중인 건물인가. 구멍이 같은 건물을 두 번 집지 않게 하는 데 쓴다.</summary>
        public static bool IsFalling(Destructible d) =>
            d != null && d.GetComponent<ToppleFall>() != null;

        Transform body;          // 실제로 기울일 것. 건물 자신이다
        Vector3 pivot;           // 넘어가는 축이 지나는 점 (구멍 반대쪽 밑동)
        Vector3 axis;            // 그 축의 방향
        Vector3 sinkTo;          // 다 넘어간 뒤 가라앉을 자리
        float tiltSeconds, sinkSeconds, tiltDegrees;
        float t;
        System.Action<Destructible> done;
        Destructible target;

        /// <summary>
        /// 넘어뜨리기 시작한다.
        /// </summary>
        /// <param name="target">넘어갈 건물</param>
        /// <param name="holeCenter">구멍의 월드 좌표. 이쪽으로 기운다</param>
        /// <param name="holeDepth">다 넘어간 뒤 얼마나 더 가라앉는가(m)</param>
        /// <param name="onDone">끝났을 때. 점수·소리는 부른 쪽이 맡는다</param>
        public static ToppleFall Begin(Destructible target, Vector3 holeCenter,
                                       float tiltSeconds, float sinkSeconds,
                                       float tiltDegrees, float holeDepth,
                                       System.Action<Destructible> onDone)
        {
            if (target == null || IsFalling(target)) return null;

            var f = target.gameObject.AddComponent<ToppleFall>();
            f.target = target;
            f.body = target.transform;
            f.tiltSeconds = Mathf.Max(0.05f, tiltSeconds);
            f.sinkSeconds = Mathf.Max(0.05f, sinkSeconds);
            f.tiltDegrees = tiltDegrees;
            f.done = onDone;

            // 구멍 쪽 수평 방향. 구멍 바로 위에 서 있으면 아무 쪽이나 고른다.
            Vector3 here = f.body.position;
            Vector3 toHole = new Vector3(holeCenter.x - here.x, 0f, holeCenter.z - here.z);
            if (toHole.sqrMagnitude < 1e-4f) toHole = Vector3.forward;
            toHole.Normalize();

            // <b>축은 구멍 반대쪽 밑동에 둔다.</b> 건물 한가운데를 축으로 돌리면
            // 밑동이 땅을 뚫고 올라와 떠 보인다. 먼 쪽 모서리를 밟고 넘어가야 쓰러지는 그림이다.
            target.FoundationFootprint(out Vector3 center, out float halfX, out float halfZ);
            float reach = Mathf.Abs(toHole.x) * halfX + Mathf.Abs(toHole.z) * halfZ;
            f.pivot = new Vector3(center.x - toHole.x * reach, here.y,
                                  center.z - toHole.z * reach);

            // 오른손 법칙: 구멍 쪽으로 기울려면 축은 그 방향의 왼쪽이다.
            f.axis = Vector3.Cross(Vector3.up, toHole).normalized;

            // 다 넘어가면 구멍 한가운데로 미끄러져 들어간다.
            f.sinkTo = new Vector3(holeCenter.x, here.y - Mathf.Max(0.5f, holeDepth), holeCenter.z);

            // 미끄러져 들어가는 거리는 <b>건물 높이</b>에서 잡는다. 낮은 건물이 깊이
            // 파고들면 과하고, 높은 건물이 얕게 들어가면 반쯤 걸친 채로 사라진다.
            float tall = target.Data != null ? target.Data.colliderSize.y : 3f;
            f.slideDepth = tall * 1.3f;
            return f;
        }

        void Update()
        {
            t += Time.deltaTime;

            if (t <= tiltSeconds)
            {
                // ① 기운다. 처음엔 천천히, 넘어가는 점을 지나면 급해진다 — 무게가 읽힌다.
                float k = t / tiltSeconds;
                float eased = k * k;
                Spin(eased * tiltDegrees);
                return;
            }

            // ② 다 기울었으면 구멍으로 빨려든다.
            Spin(tiltDegrees);

            // <b>가라앉기 시작하는 자리는 '다 기운 뒤'의 자리다</b>. 기울기 전에 잡으면
            // 회전으로 옮겨 간 만큼을 무시하게 되어 건물이 한 번 튄다.
            if (!sinking) { sinking = true; fallenFrom = body.position; fromScale = body.localScale; }

            float s = Mathf.Clamp01((t - tiltSeconds) / sinkSeconds);

            // <b>제 긴 축을 따라 비스듬히 박혀 들어간다</b> [지시 2026-10-15, 참고 그림].
            //
            // 수직으로 내리면 건물이 바닥을 뚫는 그림이 된다. 기울어진 채로 <b>자기
            // 아래쪽(기운 방향)</b>으로 미끄러지면 판자가 입으로 들어가듯 끝부터 박힌다 —
            // 기획자가 보여 준 그림이 그 모습이다.
            //
            // 동시에 구멍 한가운데로 조금씩 끌려간다. 안 그러면 비스듬히 선 채로
            // 제자리에서 꺼져 버린다.
            Vector3 along = -body.up;                    // 기운 만큼 비스듬한 '아래'
            Vector3 slid = fallenFrom + along * (slideDepth * s * s);
            Vector3 pulled = new Vector3(sinkTo.x, slid.y, sinkTo.z);
            body.position = Vector3.Lerp(slid, pulled, s * 0.6f);

            // <b>조금 줄어든다.</b> 건물이 구멍보다 넓어서 크기를 그대로 두면 가장자리가
            // 땅을 뚫는다. 다만 많이 줄이면 '먹힌다'가 아니라 '사라진다'로 보여 살짝만 줄인다.
            body.localScale = Vector3.Lerp(fromScale, fromScale * shrinkTo, s);

            if (s >= 1f) Finish();
        }

        /// <summary>
        /// 다 빨려들었을 때 남는 크기 비율. 1이면 그대로다.
        ///
        /// <b>0.80까지 올렸다</b> [고침 2026-10-15]. 구멍을 지름 8m로 키우고 나니
        /// 건물이 실제로 구멍 안에 들어가므로, 억지로 줄일 이유가 없어졌다.
        /// 많이 줄이면 '먹힌다'가 아니라 '사라진다'로 보인다.
        /// </summary>
        const float shrinkTo = 0.80f;

        /// <summary>제 축을 따라 미끄러져 들어가는 거리(m). 건물 높이에서 잡는다.</summary>
        float slideDepth = 6f;

        Vector3 fallenFrom, fromScale;
        bool sinking;
        float appliedDegrees;

        /// <summary>
        /// 축을 기준으로 <paramref name="degrees"/>까지 돌린다.
        ///
        /// 매번 처음 자세에서 다시 계산하지 않고 <b>지난번과의 차이만</b> 돌린다 —
        /// 누적 오차가 없고, 가라앉는 동안 위치를 따로 건드려도 자세가 안 꼬인다.
        /// </summary>
        void Spin(float degrees)
        {
            float delta = degrees - appliedDegrees;
            if (Mathf.Abs(delta) < 0.0001f) return;
            body.RotateAround(pivot, axis, delta);
            appliedDegrees = degrees;
        }

        void Finish()
        {
            var d = target;
            var cb = done;
            done = null;
            if (d != null)
            {
                // 풀로 돌아갈 수 있으니 <b>크기를 되돌려 놓고</b> 끈다.
                // 줄어든 채로 남으면 다음에 쓸 때 작은 건물이 선다.
                if (fromScale != Vector3.zero) d.transform.localScale = fromScale;
                d.gameObject.SetActive(false);
            }
            Destroy(this);
            cb?.Invoke(d);
        }
    }
}
