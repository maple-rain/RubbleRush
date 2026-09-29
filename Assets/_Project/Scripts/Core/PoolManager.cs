using System.Collections.Generic;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>인스턴스가 어느 풀에서 나왔는지 기억한다. 반납할 때 프리팹을 다시 찾지 않기 위해서다.</summary>
    public sealed class PooledItem : MonoBehaviour
    {
        public GameObject SourcePrefab;
    }

    /// <summary>
    /// 잔해·파티클·오디오 풀. 모든 런타임 생성이 여기를 거친다 (CLAUDE.md 규칙 6).
    ///
    /// 모바일 GC 스파이크의 주범이 프레임마다 Instantiate/Destroy를 부르는 것이다.
    /// 파괴 한 번에 잔해가 수십 개 나오는 게임이라 특히 그렇다.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PoolManager : MonoBehaviour
    {
        public static PoolManager Instance { get; private set; }

        readonly Dictionary<GameObject, Stack<GameObject>> pools =
            new Dictionary<GameObject, Stack<GameObject>>();

        Transform parkingLot;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PoolManager] 인스턴스가 둘이다. 나중 것을 끈다.", this);
                enabled = false;
                return;
            }

            Instance = this;
            parkingLot = new GameObject("[Pooled]").transform;
            parkingLot.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>미리 만들어 둔다. 첫 파괴에서 수십 개가 한꺼번에 생기는 것을 피한다.</summary>
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null) return;
            Stack<GameObject> pool = GetPool(prefab);

            for (int i = 0; i < count; i++)
            {
                GameObject go = CreateNew(prefab);
                go.SetActive(false);
                pool.Push(go);
            }
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;

            Stack<GameObject> pool = GetPool(prefab);
            GameObject go = pool.Count > 0 ? pool.Pop() : CreateNew(prefab);

            Transform t = go.transform;
            t.SetParent(null, false);
            t.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            return go;
        }

        public void Release(GameObject instance)
        {
            if (instance == null) return;

            var tag = instance.GetComponent<PooledItem>();
            if (tag == null || tag.SourcePrefab == null)
            {
                // 풀에서 나온 게 아니다. 파괴해도 되지만 왜 여기 왔는지 알려주는 편이 낫다.
                Debug.LogWarning($"[PoolManager] 풀 소속이 아닌 '{instance.name}' 반납 시도. 파괴한다.", instance);
                Destroy(instance);
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(parkingLot, false);
            GetPool(tag.SourcePrefab).Push(instance);
        }

        Stack<GameObject> GetPool(GameObject prefab)
        {
            if (!pools.TryGetValue(prefab, out Stack<GameObject> pool))
            {
                pool = new Stack<GameObject>(32);
                pools[prefab] = pool;
            }
            return pool;
        }

        GameObject CreateNew(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, parkingLot);
            go.AddComponent<PooledItem>().SourcePrefab = prefab;
            return go;
        }
    }
}
