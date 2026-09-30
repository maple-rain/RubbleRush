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
            ApplyFrameRate(config);

            string missing = config.Validate();
            if (missing != null) Debug.LogError($"[GameConfigRef] {missing}", config);
        }

        /// <summary>
        /// 프레임을 못 박는다 [지시 2026-09-30].
        ///
        /// <b>모바일에서 Unity의 기본 targetFrameRate는 30이다.</b> 그대로 두면
        /// "60 이하로 떨어지지 않는 선"을 확인할 수가 없다 — 애초에 60이 안 나온다.
        ///
        /// vSync를 같이 끄는 이유: <b>vSync가 켜져 있으면 targetFrameRate는 무시된다.</b>
        /// QualitySettings에 이미 0이 들어 있지만 품질 레벨을 바꾸면 따라 바뀌므로,
        /// 여기서 못 박아 의존 관계를 코드에 드러낸다.
        /// </summary>
        static void ApplyFrameRate(GameConfig cfg)
        {
            if (cfg.targetFrameRate <= 0) return;      // 0 = 플랫폼 기본에 맡긴다
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = cfg.targetFrameRate;
        }

        void OnDestroy()
        {
            if (s_current == config) s_current = null;
        }
    }
}
