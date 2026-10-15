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
    /// <b>할당량이 없다</b> [지시 2026-10-15]. 전에는 할당량 안팎을 나누고, 달성 보너스를
    /// 주고, 남은 시간을 점수로 바꿨다. 그 구조에는 두 가지 문제가 있었다:
    ///
    /// 1. <b>할당량을 채우는 순간 판이 끝났다.</b> 구역에 504단위가 깔려 있는데 할당량은
    ///    60(12%)이라, 초과 수집 배율은 마지막 한 묶음에만 붙어 사실상 죽은 항목이었다.
    /// 2. <b>치워야 하는 잔해와 점수의 연결이 안 보였다</b> — 기획자의 말이다. 한 판의
    ///    18%가 "남은 시간"에서 나왔는데, 그것은 치운 양과 아무 상관이 없다.
    ///
    /// 이제 <b>시간이 끝날 때까지 치운 만큼이 점수다.</b> 남은 시간 항목은 없앴다
    /// (시간을 다 쓰고 끝나므로 언제나 0이다).
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
        /// <param name="collected">구멍에 넣은 잔해 단위</param>
        /// <param name="structuresBroken">부순 건물 수</param>
        /// <param name="swallowedStructures">부수지 않고 통째로 삼킨 건물 수 [지시 2026-10-15]</param>
        /// <param name="holeLevel">구멍을 키운 칸 수 (0부터)</param>
        public static int Of(GameConfig cfg, int collected,
                             int structuresBroken, int swallowedStructures, int holeLevel)
        {
            if (cfg == null) return 0;

            float total = Mathf.Max(0, collected) * cfg.pointsPerDebrisUnit
                        + Mathf.Max(0, structuresBroken) * cfg.pointsPerStructure
                        + Mathf.Max(0, swallowedStructures) * cfg.pointsPerSwallowedStructure
                        + Mathf.Max(0, holeLevel) * cfg.holeLevelBonus;

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
