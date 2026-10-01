using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 층이 무너지는 동안 조각 하나가 어떻게 움직이는가 [지시 8번, 2026-09-30].
    ///
    /// <b>고친 것 — 위층이 잔해를 추월해서 덮어버렸다</b> [실측 2026-09-30].
    ///
    /// 내려앉기는 9m/s <b>등속</b>이라 0.2초면 한 층을 다 내려왔는데, 낙하는 중력으로
    /// 0에서 가속하느라 같은 시각에 0.57m밖에 못 갔다. 위층이 1.2m 앞섰다 —
    /// 층 높이가 1.77m이니 <b>잔해가 제 높이의 2/3만큼 파묻힌다</b>.
    ///
    /// | t | 잔해 낙하 | 위층 내려앉음 |
    /// |---|---|---|
    /// | 32ms | 0.01m | <b>0.29m</b> |
    /// | 122ms | 0.21m | <b>1.10m</b> |
    /// | 201ms | 0.57m | <b>1.77m</b> (다 내려왔다) |
    /// | 514ms | 2.61m | 1.77m |
    ///
    /// 그래서 처음 0.45초 — 플레이어가 보는 시간 전부 — 동안 3D 조각이 한 톨도 안 보이고,
    /// 카메라 쪽으로 12mm 띄워 둔 균열선만 깊이 검사를 이기고 남아
    /// <b>평면 도형 한 장</b>으로 읽혔다. "2D 이미지"의 정체가 이것이다.
    ///
    /// 실제 붕괴는 반대다. 아래가 빠지고 위가 그 빈 자리로 <b>떨어진다</b> —
    /// 둘 다 중력이면 위가 아래를 추월할 수 없다. 그래서 내려앉기도 가속으로 바꿨다.
    ///
    /// <b>왜 따로 뺐나.</b> WallFracture가 이미 621줄이라 더 받을 수 없다 (CLAUDE.md 규칙 3).
    /// 그리고 낙하·내려앉기·구르기는 서로 물려 있어서 한 덩어리로 봐야 맞는다 —
    /// 이 파일의 존재 이유가 곧 <b>"위층이 아래층을 추월하면 안 된다"</b>는 한 문장이다.
    ///
    /// MonoBehaviour가 아닌 이유는 <see cref="Demo.Player.PunchRush"/>와 같다 —
    /// 수명주기도 코루틴도 필요 없고, 컴포넌트로 만들면 AddComponent 배선과
    /// 실행 순서 문제만 새로 생긴다.
    /// </summary>
    [System.Serializable]
    public sealed class FractureFall
    {
        [Tooltip("떨어져서 사라지기까지(초). 이 시간이 지나면 조각이 사라진다")]
        [SerializeField] float fallSeconds = 0.7f;

        [Tooltip("낙하 가속(m/s²). 실제 중력일 필요는 없다 — 9.8이면 한 층 떨어지는 데 " +
                 "0.6초라 타격 박자보다 한참 느리다")]
        [SerializeField] float fallGravity = 28f;

        [Tooltip("내려앉는 가속(m/s²) [제안·미승인 2026-09-30]. " +
                 "<b>속도가 아니라 가속이다.</b> 등속이던 시절 위층이 잔해를 추월해 덮어버려서 " +
                 "무너지는 장면이 통째로 안 보였다. " +
                 "fallGravity보다 <b>낮게</b> 두는 것이 핵심이다 — 같으면 위층과 잔해가 " +
                 "나란히 내려가 '건물이 통째로 가라앉는' 그림이 되고, 높으면 다시 추월한다. " +
                 "낮으면 잔해가 먼저 빠져나가고 위층이 뒤따라 내려앉는다")]
        [SerializeField] float settleGravity = 18f;

        [Tooltip("떨어지는 조각이 공중에서 구르는 속도(초당 회전수) [제안·미승인 2026-09-30]. " +
                 "0이면 안 구른다. 아래로 미끄러지기만 하면 눈이 조각 무리를 " +
                 "<b>판 한 장</b>으로 읽는다 — 이게 '2D 이미지'로 보이던 마지막 이유다. " +
                 "너무 크면 조각이 튀는 파편처럼 보여 무게가 사라진다")]
        [SerializeField, Range(0f, 3f)] float tumbleTurns = 0.85f;

        float[] y;      // 조각이 지금까지 내려앉은 거리(m)
        float[] v;      // 그 속도(m/s). 가속이라 상태가 하나 더 필요하다

        public float FallSeconds => fallSeconds;
        public float Gravity => fallGravity;

        /// <summary>구르기가 켜져 있는가. 꺼져 있으면 회전 계산을 통째로 건너뛴다.</summary>
        public bool Tumbles => tumbleTurns > 0f;

        public void Reset(int seedCount)
        {
            y = new float[seedCount];
            v = new float[seedCount];
        }

        /// <summary>그 조각이 지금까지 내려앉은 거리(m).</summary>
        public float SettleOf(int seed) => y == null ? 0f : y[seed];

        /// <summary>
        /// 아래층이 비어버린 조각을 <b>떨어뜨려</b> 한 층 아래에 다시 세운다.
        /// 사라지는 게 아니라 내려와서 다시 선다 — 1층을 부수면 2층이 1층이 되고,
        /// 그 자리에서 다시 부술 수 있다 [지시 2026-09-22].
        ///
        /// 등속이 아니라 가속인 이유는 이 클래스 주석에 적었다. 요약하면,
        /// 등속은 <b>잔해를 추월한다</b>.
        /// </summary>
        /// <returns>하나라도 움직였으면 true. 메시를 다시 써야 한다는 뜻이다</returns>
        public bool TickSettle(float dt, FracturePattern pattern, float floorHeight)
        {
            if (y == null) return false;

            bool moved = false;
            for (int s = 0; s < y.Length; s++)
            {
                float target = pattern.SettleTarget(s) * floorHeight;

                // 이미 닿아서 다시 서 있다. 나중에 그 아래가 또 무너지면 target이 커지고,
                // 그때 속도 0에서 다시 떨어지기 시작한다 — 실제로도 그렇다.
                if (y[s] >= target) { v[s] = 0f; continue; }

                v[s] += settleGravity * dt;
                y[s] += v[s] * dt;
                if (y[s] >= target) { y[s] = target; v[s] = 0f; }   // 착지
                moved = true;
            }
            return moved;
        }

        /// <summary>
        /// 떨어지는 조각이 <b>공중에서 구르는</b> 회전 [지시 8번, 2026-09-30].
        ///
        /// 기존 회전(crackTilt)은 Z축 하나라 화면 안에서 돌 뿐이고, 판이 판인 것은 그대로다.
        /// 축을 세 방향으로 열어야 옆면이 빛을 받으며 돌아가 <b>두께가 드러난다</b>.
        ///
        /// 축과 속도는 조각마다 고정이다. 프레임마다 다시 뽑으면 구르는 게 아니라 <b>떠는</b> 게 되고,
        /// 조각이 다 같으면 무리가 통째로 도는 한 판으로 보인다.
        /// </summary>
        public Quaternion TumbleOf(int seed, float fallElapsed)
        {
            if (tumbleTurns <= 0f || fallElapsed <= 0f) return Quaternion.identity;

            var axis = new Vector3(Hash(seed, 11), Hash(seed, 12), Hash(seed, 13));
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;

            float turns = tumbleTurns * (0.4f + Hash01(seed, 14) * 1.2f);
            return Quaternion.AngleAxis(turns * 360f * fallElapsed, axis.normalized);
        }

        /// <summary>
        /// −0.5~0.5. 이웃끼리 상관이 없어야 조각이 제각각 구른다 — 그래서 펄린이 아니라 해시다.
        /// WallFracture의 해시와 <b>일부러</b> 상수를 다르게 뒀다. 같으면 구르는 방향이
        /// 금 갈 때 밀린 방향과 붙어 버려서, 서로 다른 두 움직임이 한 패턴으로 읽힌다.
        /// </summary>
        static float Hash(int seed, int k)
        {
            int h = (seed * 668265263) ^ (k * 374761393);
            h = (h ^ (h >> 15)) * 1103515245;
            return ((h ^ (h >> 13)) & 0xFFFFFF) / (float)0xFFFFFF - 0.5f;
        }

        static float Hash01(int seed, int k) => Hash(seed, k) + 0.5f;
    }
}
