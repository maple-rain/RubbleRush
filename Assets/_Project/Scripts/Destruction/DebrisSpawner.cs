using Demo.Collection;
using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴된 대상 자리에 잔해를 뿌린다.
    ///
    /// 산란 반경이 곧 다음 판단이다 — 좁게 떨어지면 작은 고리로 즉시 회수되고,
    /// 넓게 흩어지면 큰 고리가 필요하다. 파괴 선택이 수집 시간까지 결정하는 지점이
    /// 이 한 줄에 들어 있다 (GAME_DESIGN §4.1).
    /// </summary>
    public sealed class DebrisSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("잔해로 쓸 프리팹. Debris 컴포넌트가 붙어 있어야 한다")]
        GameObject debrisPrefab;

        [SerializeField, Range(0f, 0.5f), Tooltip(
            "조각이 바닥면에 얼마나 잠기는가. 한 변 대비 비율. " +
            "0이면 정확히 얹힌다. 박혀 보여야 하는 것은 조각이 아니라 토대였다 [정정 2026-10-12]")]
        float sinkShare = 0f;

        public GameObject DebrisPrefab { get => debrisPrefab; set => debrisPrefab = value; }

        /// <summary>구역 시작 전에 풀을 채운다. 첫 파괴에서 수십 개가 한꺼번에 생기는 것을 피한다.</summary>
        public void Prewarm(int count)
        {
            if (debrisPrefab != null && PoolManager.Instance != null)
                PoolManager.Instance.Prewarm(debrisPrefab, count);
        }

        /// <summary>대상이 잔해가 된 순간 한 번 부른다.</summary>
        public void SpawnFor(StructureData data, Vector3 center)
        {
            if (data == null || debrisPrefab == null || PoolManager.Instance == null) return;

            int budget = RemainingBudget();

            for (int i = 0; i < data.debrisCount; i++)
            {
                if (budget-- <= 0)
                {
                    // 동시 잔해 상한(CLAUDE.md 성능 예산)에 걸렸다. 조용히 넘기면
                    // "부쉈는데 잔해가 없다"로만 보이므로 한 번은 알린다.
                    Debug.LogWarning($"[DebrisSpawner] 동시 잔해 상한 도달 — '{data.id}'의 잔해 " +
                                     $"{data.debrisCount - i}개를 생략했다.");
                    break;
                }

                Vector3 pos = FindFreeSpot(center, data);
                pos.y = RestHeight(pos, data);
                Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject go = PoolManager.Instance.Get(debrisPrefab, pos, rot);
                if (go == null) continue;

                // 크기는 Debris.Init이 정한다 — 여기서 덮어쓰면 baseScale이 어긋난다.
                // 구멍에 들어갈지 말지가 화면에서 읽혀야 "지금은 못 넣는다"가 짜증이 아니라 판단이 된다.
                var d = go.GetComponent<Debris>();
                if (d != null) d.Init(data.debrisSizeClass, data.debrisRadius);
                else Debug.LogError("[DebrisSpawner] 잔해 프리팹에 Debris 컴포넌트가 없다.", debrisPrefab);
            }
        }

        /// <summary>
        /// 그 자리에서 조각의 <b>중심이 놓일 높이</b> [지시 2026-10-12].
        ///
        /// <b>예전에는 0.25m 고정이었다.</b> 그런데 조각 한 변은 종류마다 0.16m에서 1.17m까지
        /// <b>7.3배</b> 차이가 난다 — 실측(2026-10-12)으로 <c>t1_weak</c>은 바닥이 0.17m
        /// 공중에 떠 있었고 <c>t5_solid</c>는 0.34m가 땅에 묻혔다. 절대 미터값 하나를
        /// 7배 벌어지는 것에 물린 자리다 (<see cref="MeshFit"/>·<c>FractureProxy</c>와 같은 함정).
        ///
        /// <b>토대 위에 놓인다.</b> 잔해는 제가 나온 건물 자리에 떨어지는데, 그 자리에는
        /// 방금 토대가 섰다. 지면 높이로 놓으면 토대 속에 파묻힌다 [지적 2026-10-12].
        /// </summary>
        float RestHeight(Vector3 at, StructureData data)
        {
            float side = Mathf.Max(0.01f, data.debrisRadius);
            bool onTop = Hazard.ObstacleField.OnFoundation(at);

            // <b>토대 위에서는 안 묻는다</b> [지시 2026-10-12]. 땅에서는 박혀 있는 꼴이 맞지만,
            // 토대 위에서 묻으면 0.4m 상자 속으로 들어가 <b>가려진다</b> —
            // 제일 작은 조각은 한 변이 0.16m라 통째로 사라진다.
            float sink = onTop ? 0f : sinkShare;
            float floor = onTop ? Destructible.FoundationHeight : 0f;
            return floor + side * (0.5f - sink);
        }

        /// <summary>
        /// 다른 잔해와 겹치지 않는 자리를 찾는다 [지시 2026-09-22].
        ///
        /// 기각 표본추출(rejection sampling)이다 — 무작위로 찍어보고 겹치면 다시 찍는다.
        /// 시도 횟수를 제한해서, 자리가 정말 없으면 마지막 후보를 그냥 쓴다.
        /// 완벽한 배치보다 <b>끝나는 것</b>이 중요하다 — 파괴 한 번에 수십 번 도는 코드다.
        /// </summary>
        Vector3 FindFreeSpot(Vector3 center, StructureData data)
        {
            const int MaxTries = 12;
            float mine = data.debrisRadius;
            Vector3 candidate = center;

            for (int attempt = 0; attempt < MaxTries; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * data.debrisScatterRadius;
                candidate = center + new Vector3(offset.x, 0f, offset.y);

                var flat = new Vector2(candidate.x, candidate.z);
                bool clash = false;

                var active = Debris.Active;
                for (int i = 0; i < active.Count; i++)
                {
                    Debris other = active[i];
                    if (other == null || other.IsFalling) continue;

                    float need = mine + other.SizeRadius;
                    if ((other.FlatPosition - flat).sqrMagnitude < need * need) { clash = true; break; }
                }

                if (!clash) return candidate;
            }

            return candidate;
        }

        /// <summary>
        /// 더 뿌릴 수 있는 개수. <b>0 이하면 상한이 없다</b> [지시 2026-10-13].
        ///
        /// 상한을 두던 이유는 모바일 드로우콜 예산이었는데, 기획자가 상한을 빼기로 했다 —
        /// 잘리면 "부쉈는데 잔해가 안 나온다"가 되고, 그게 이 게임에서 제일 보여야 할 것을 지운다.
        /// <b>CLAUDE.md 성능 예산표의 '동시 잔해 500'은 이 결정으로 낡았다.</b>
        /// </summary>
        static int RemainingBudget()
        {
            GameConfig cfg = GameConfigRef.Current;
            int cap = cfg != null ? cfg.debrisMaxActive : 0;
            return cap > 0 ? cap - Debris.Active.Count : int.MaxValue;
        }
    }
}
