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

        [SerializeField, Tooltip("잔해가 놓이는 높이(m). 바닥에 반쯤 박히지 않게 띄운다")]
        float groundOffset = 0.25f;

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
                candidate = center + new Vector3(offset.x, groundOffset, offset.y);

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

        static int RemainingBudget()
        {
            GameConfig cfg = GameConfigRef.Current;
            int cap = cfg != null ? cfg.debrisMaxActive : 80;
            return cap - Debris.Active.Count;
        }
    }
}
