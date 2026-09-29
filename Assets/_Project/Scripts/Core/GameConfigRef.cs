using Demo.Data;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// GameConfig 전역 접근점. [Bootstrap]에 하나만 붙인다.
    ///
    /// 모든 시스템이 config를 SerializeField로 들고 있으면 프리팹 열 개를 일일이 물려야 하는데,
    /// 씬 와이어링은 사람이 손으로 하는 작업이다 (CLAUDE.md 규칙 1). 한 군데만 물리게 한다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameConfigRef : MonoBehaviour
    {
        [SerializeField] GameConfig config;

        static GameConfig s_current;

        /// <summary>어디서든 읽는다. Bootstrap이 깨어나기 전에 부르면 null이다.</summary>
        public static GameConfig Current => s_current;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[GameConfigRef] GameConfig 에셋이 물려 있지 않다. " +
                               "[Bootstrap] 오브젝트의 인스펙터에서 연결할 것.", this);
                return;
            }

            s_current = config;

            string missing = config.Validate();
            if (missing != null) Debug.LogError($"[GameConfigRef] {missing}", config);
        }

        void OnDestroy()
        {
            if (s_current == config) s_current = null;
        }
    }
}
