using System.Collections.Generic;
using Demo.Data;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 이번 판에 고른 업그레이드 [지시 2026-10-13].
    ///
    /// <b>구멍 단계가 오를 때마다 세 장 중 하나를 고른다.</b> 자동으로 주지 않는 것이
    /// 핵심이다 — 반경이 저절로 커지는 지금의 성장은 플레이어가 한 일이 없어서
    /// 통보에 가깝다. 고르게 하면 같은 판도 매번 달라진다.
    ///
    /// <b>게임 쪽은 이름으로만 묻는다.</b> <c>Mul(Upgrades.MoveSpeed)</c>처럼 부르고,
    /// 무엇이 얼마나 오르는지는 전부 <see cref="GameConfig.upgrades"/> 표에 있다.
    /// 그래서 수치를 고치는 일에 코드가 안 끼어든다.
    ///
    /// <b>정적 상태다.</b> 한 판 동안만 산다 — 구역을 시작할 때
    /// <see cref="GameLoop"/>가 <see cref="Reset"/>을 부른다. 안 부르면 다시 하기로
    /// 들어간 판이 지난 판의 업그레이드를 그대로 들고 시작한다.
    /// </summary>
    public static class Upgrades
    {
        // ── 열쇠. 코드가 쓰는 이름은 여기 모아 둔다 ───────────────────────────
        public const string MoveSpeed       = "moveSpeed";        // 이동 속도 배율
        public const string CableLength     = "cableLength";      // 케이블 길이 배율
        public const string NoDrop          = "noDrop";           // 부딪혀도 안 흘림
        public const string LessScatter     = "lessScatter";      // 흩어짐 반경 배율
        public const string Magnet          = "magnet";           // 끌어오는 거리(m)
        public const string LoopGrace       = "loopGrace";        // 고리 판정 여유(잔해 반지름 배)
        public const string FastBreak       = "fastBreak";        // 필요 타수 배율
        public const string MoreXp          = "moreXp";           // 경험치 배율
        public const string HoleFollows     = "holeFollows";      // 구멍이 따라옴
        public const string WiderSuction    = "widerSuction";     // 흡인 여유 배율
        public const string QuickFoundation = "quickFoundation";  // 토대 수명 배율
        public const string Swallow         = "swallow";          // 건물을 삼킴

        static readonly Dictionary<string, float> taken = new Dictionary<string, float>();

        /// <summary>이번 판에 고른 카드 수.</summary>
        public static int Count => taken.Count;

        /// <summary>구역을 시작할 때 비운다.</summary>
        public static void Reset() => taken.Clear();

        /// <summary>그 카드를 가졌는가.</summary>
        public static bool Has(string id) => taken.ContainsKey(id);

        /// <summary>배율. 안 가졌으면 1이라 곱해도 아무 일이 없다.</summary>
        public static float Mul(string id)
        {
            float v;
            return taken.TryGetValue(id, out v) ? v : 1f;
        }

        /// <summary>값. 안 가졌으면 <paramref name="fallback"/>.</summary>
        public static float Value(string id, float fallback)
        {
            float v;
            return taken.TryGetValue(id, out v) ? v : fallback;
        }

        /// <summary>카드를 집는다.</summary>
        public static void Take(GameConfig.Upgrade u)
        {
            if (u == null || string.IsNullOrEmpty(u.id)) return;
            taken[u.id] = u.value;
            Debug.Log("[Upgrades] " + u.title + " (" + u.id + " = " + u.value + ")");
        }

        /// <summary>
        /// 이번 단계에 내밀 카드들.
        ///
        /// 후보는 <b>지금까지 풀린 것 중 아직 안 고른 전부</b>다 — 지난 단계에 안 고른
        /// 카드가 다시 올라온다. 거기서 무작위로 <paramref name="count"/>장을 뽑는다.
        ///
        /// <b>마지막 단계만 다르다.</b> 거기서는 그 단계에 풀린 카드만 내민다 —
        /// "건물을 삼킨다"는 뽑기가 아니라 다 키운 것에 대한 보상이라서다 [지시 2026-10-13].
        /// </summary>
        public static List<GameConfig.Upgrade> Offer(GameConfig cfg, int stage, List<GameConfig.Upgrade> into)
        {
            into.Clear();
            if (cfg == null || cfg.upgrades == null || cfg.upgrades.Length == 0) return into;

            int finalStage = 0;
            foreach (GameConfig.Upgrade u in cfg.upgrades)
                if (u != null && u.unlockStage > finalStage) finalStage = u.unlockStage;

            // 마지막 단계에 카드가 <b>딱 한 장</b>일 때만 그것만 내민다.
            // 여러 장이면 평소처럼 쌓인 것 중에서 뽑는다 — 안 그러면 앞 단계에서
            // 안 고른 카드들이 영영 안 나온다.
            int atFinal = 0;
            foreach (GameConfig.Upgrade u in cfg.upgrades)
                if (u != null && u.unlockStage == finalStage) atFinal++;

            bool lastOnly = stage >= finalStage && atFinal == 1;

            foreach (GameConfig.Upgrade u in cfg.upgrades)
            {
                if (u == null || string.IsNullOrEmpty(u.id)) continue;
                if (Has(u.id)) continue;
                if (lastOnly ? u.unlockStage != finalStage : u.unlockStage > stage) continue;
                into.Add(u);
            }

            Shuffle(into);

            int want = Mathf.Max(1, cfg.upgradeChoices);
            if (!lastOnly && into.Count > want) into.RemoveRange(want, into.Count - want);
            return into;
        }

        /// <summary>제자리 섞기(Fisher-Yates). 뒤에서부터 임의의 앞칸과 바꾼다.</summary>
        static void Shuffle(List<GameConfig.Upgrade> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                GameConfig.Upgrade t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }
}
