using Demo.Data;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 구역 점수와 등급 [구현 2026-10-13].
    ///
    /// <b>값은 전부 <see cref="GameConfig"/>에 있다.</b> 이 파일에는 더하는 순서만 있다 —
    /// 수치는 기획자 몫이고 <c>Tools/balance/score_rules.csv</c> · <c>grades.csv</c>가 기록처다.
    /// 항목을 0으로 두면 그 항목은 점수에 안 들어간다.
    ///
    /// <b>왜 할당량 안팎을 나누는가.</b> 할당량은 통과 조건이고, 그 위는 "더 했다"다.
    /// 둘을 같은 단가로 주면 할당량을 채운 뒤 그만두는 것과 끝까지 나르는 것이
    /// 같은 기울기가 된다 — 넘긴 쪽에 배율을 따로 둔 이유다.
    ///
    /// <b>구멍은 '자란 단계'를 센다.</b> 시작이 1단계라 그대로 세면 아무것도 안 해도
    /// 한 칸을 먼저 받는다. 0부터 세서 <b>키운 만큼만</b> 준다.
    ///
    /// <b>안 세는 것</b>: 층 붕괴 수 · 내려놓고 버린 잔해. 둘 다 세는 코드가 아직 없다
    /// (score_rules.csv에 칸은 있다).
    /// </summary>
    public static class Score
    {
        /// <summary>구역 점수. 음수는 나오지 않는다.</summary>
        public static int Of(GameConfig cfg, int collected, int quota,
                             float timeLeft, int structuresBroken, int holeLevel)
        {
            if (cfg == null) return 0;

            int inQuota = Mathf.Min(collected, Mathf.Max(0, quota));
            int over = Mathf.Max(0, collected - Mathf.Max(0, quota));

            float total = inQuota * cfg.pointsPerDebrisUnit
                        + over * cfg.pointsPerDebrisUnit * Mathf.Max(0f, cfg.overQuotaMultiplier)
                        + Mathf.Max(0f, timeLeft) * cfg.pointsPerSecondLeft
                        + Mathf.Max(0, structuresBroken) * cfg.pointsPerStructure
                        + Mathf.Max(0, holeLevel) * cfg.holeLevelBonus;

            if (quota > 0 && collected >= quota) total += cfg.quotaBonus;

            return Mathf.Max(0, Mathf.RoundToInt(total));
        }

        /// <summary>
        /// 점수에 맞는 등급. 표가 비어 있으면 null이다 —
        /// 부르는 쪽이 등급 없이도 그릴 수 있어야 한다.
        /// </summary>
        public static GameConfig.Grade GradeOf(GameConfig cfg, int score)
        {
            if (cfg == null || cfg.grades == null || cfg.grades.Length == 0) return null;

            GameConfig.Grade best = null;
            for (int i = 0; i < cfg.grades.Length; i++)
            {
                GameConfig.Grade g = cfg.grades[i];
                if (g == null || score < g.minScore) continue;
                if (best == null || g.minScore > best.minScore) best = g;
            }

            // 어떤 점수든 등급이 나와야 한다. 표의 맨 아래가 0이 아니면 제일 낮은 칸을 준다.
            if (best == null)
            {
                for (int i = 0; i < cfg.grades.Length; i++)
                {
                    GameConfig.Grade g = cfg.grades[i];
                    if (g == null) continue;
                    if (best == null || g.minScore < best.minScore) best = g;
                }
            }
            return best;
        }

        /// <summary>별을 몇 칸 그릴 것인가. 표에서 제일 큰 별 수다.</summary>
        public static int MaxStars(GameConfig cfg)
        {
            if (cfg == null || cfg.grades == null) return 0;
            int max = 0;
            foreach (GameConfig.Grade g in cfg.grades)
                if (g != null && g.stars > max) max = g.stars;
            return max;
        }
    }
}
