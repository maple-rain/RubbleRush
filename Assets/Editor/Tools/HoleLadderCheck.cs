using System.Collections.Generic;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 구멍 단계 ↔ 건물 종류 사다리가 성립하는지 검사한다 [확정 설계 2026-09-22 · 검사기 2026-10-02].
    ///
    /// <b>설계</b>: n단계에서는 n번째까지의 종류만 구멍에 들어간다. 단계가 오르면 한 종류가
    /// 새로 열리고, 새로 열린 종류를 부수는 것이 이득이도록 수치를 맞춘다.
    ///
    /// <b>왜 검사기가 필요한가.</b> 이 설계는 수치 <b>세 묶음이 서로 맞물려야</b> 성립한다 —
    /// 잔해 반경(종류별), 구멍 반경(단계별), 경험치(필요량과 산출량). 한 곳만 건드려도
    /// 조용히 깨지고, 깨져도 게임은 멀쩡히 돌아간다. 실제로 깨져 있었다 [2026-10-02]:
    /// 0단계에 두 종류가 들어가고 2단계에서 다섯 종류가 전부 열려, 3·4단계가 아무 일도 안 했다.
    /// 그 상태로 오래 측정했고, 그 위에서 낸 수치를 여럿 보고했다.
    ///
    /// <b>이 검사기는 값을 고치지 않는다.</b> 어디가 어긋났는지와 <b>어디에 있어야 하는지</b>만
    /// 말한다 — 값은 기획자가 정한다 (CLAUDE.md 역할 분담).
    /// </summary>
    public static class HoleLadderCheck
    {
        sealed class Tier
        {
            public string Name;
            public float DebrisRadius;
            public int DebrisCount;
            public int SizeClass;
            public float BreakTime;
            public float Scatter;
            public int Count;          // 구역에 몇 채 있는가
        }

        [MenuItem("Demo/14. 구멍 사다리 검사", priority = 66)]
        public static void Check()
        {
            GameConfig cfg = Load<GameConfig>("t:GameConfig");
            if (cfg == null) { Debug.LogError("[사다리] GameConfig를 못 찾았다."); return; }

            List<Tier> tiers = Tiers();
            if (tiers.Count == 0) { Debug.LogError("[사다리] StructureData를 못 찾았다."); return; }

            var levels = cfg.holeLevels ?? new GameConfig.HoleLevel[0];
            var sb = new StringBuilder();
            int bad = 0;

            sb.AppendLine("구멍 사다리 검사 — 설계: n단계에서 n번째까지만 들어간다 [확정 2026-09-22]");
            sb.AppendLine();

            if (levels.Length != tiers.Count)
            {
                bad++;
                sb.AppendLine($"[X] 단계 {levels.Length}개 ↔ 종류 {tiers.Count}개 — 1:1이 아니다.");
                sb.AppendLine();
            }

            // ── 1. 게이트 ────────────────────────────────────────────────────
            sb.AppendLine("1) 게이트 — 각 단계에 몇 종류가 들어가는가");
            sb.AppendLine(Line("단계", "반경", "들어가는 종류", "있어야 할 곳", "판정"));
            int n = Mathf.Min(levels.Length, tiers.Count);
            for (int L = 0; L < n; L++)
            {
                int fits = 0;
                for (int t = 0; t < tiers.Count; t++)
                    if (tiers[t].DebrisRadius < levels[L].radius) fits++;

                // 설계가 성립하려면  dr[L] < r[L] <= dr[L+1]
                float lo = tiers[L].DebrisRadius;
                bool last = L + 1 >= tiers.Count;
                float hi = last ? float.PositiveInfinity : tiers[L + 1].DebrisRadius;
                string band = last ? $"{lo:F2} 초과" : $"({lo:F2}, {hi:F2}]";
                bool ok = fits == L + 1;
                if (!ok) bad++;

                sb.AppendLine(Line(L.ToString(), $"{levels[L].radius:F2}m",
                    $"{fits}종 (기대 {L + 1})", band,
                    ok ? "O" : "X"));
            }

            // ── 2. 단조 증가 ─────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("2) 구멍은 줄어들지 않는다");
            bool mono = true;
            for (int L = 1; L < levels.Length; L++)
                if (levels[L].radius <= levels[L - 1].radius)
                {
                    mono = false; bad++;
                    sb.AppendLine($"   [X] {L - 1}단계 {levels[L - 1].radius:F2} → {L}단계 {levels[L].radius:F2}");
                }
            if (mono) sb.AppendLine("   [O] 단조 증가한다");

            // ── 3. 승급 가능성 ───────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("3) 그 단계에서 열린 종류만으로 다음 단계에 갈 수 있는가");
            sb.AppendLine(Line("단계", "필요 XP", "한 채당", "필요/보유", "판정"));
            for (int L = 0; L < n; L++)
            {
                int need = levels[L].xpToNext;
                if (need <= 0) { sb.AppendLine(Line(L.ToString(), "-", "-", "-", "마지막")); continue; }

                int per = tiers[L].DebrisCount * Mathf.Max(1, tiers[L].SizeClass)
                        * Mathf.Max(1, cfg.xpPerDebrisUnit);
                int req = per > 0 ? Mathf.CeilToInt(need / (float)per) : int.MaxValue;
                bool ok = req <= tiers[L].Count;
                if (!ok) bad++;
                sb.AppendLine(Line(L.ToString(), need.ToString(), per.ToString(),
                    $"{req}채 / {tiers[L].Count}채", ok ? "O" : "X — 이 종류만으론 못 올라간다"));
            }

            // ── 4. 다음 종류가 이득인가 ──────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("4) 다음 종류를 부수는 것이 이득인가 (경험치 / 한 바퀴 시간)");
            sb.AppendLine("   * 왕복 거리는 빼고 잰다 — 그건 구역 배치마다 다르다");
            sb.AppendLine(Line("종류", "XP", "부수기+고리", "XP/초", "판정"));
            float prev = -1f;
            for (int t = 0; t < tiers.Count; t++)
            {
                int xp = tiers[t].DebrisCount * Mathf.Max(1, tiers[t].SizeClass)
                       * Mathf.Max(1, cfg.xpPerDebrisUnit);
                float loop = 2f * Mathf.PI * tiers[t].Scatter / Mathf.Max(0.1f, cfg.moveSpeedBase);
                float cost = tiers[t].BreakTime + loop;
                float rate = cost > 0f ? xp / cost : 0f;
                bool ok = t == 0 || rate > prev;
                if (!ok) bad++;
                sb.AppendLine(Line(tiers[t].Name, xp.ToString(), $"{cost:F1}s", $"{rate:F2}",
                    t == 0 ? "기준" : (ok ? "O 오른다" : "X 꺾인다 — 앞 종류가 더 이득이다")));
                prev = rate;
            }

            // ── 5. 할당량 안에서 마지막 단계가 열리는가 ──────────────────────
            sb.AppendLine();
            sb.AppendLine("5) 할당량 안에서 마지막 단계가 열리는가");
            int cum = 0;
            for (int L = 0; L < n - 1; L++) cum += levels[L].xpToNext;
            var zone = Load<ZoneData>("t:ZoneData");
            int quota = zone != null ? zone.quota : 0;
            if (quota <= 0) sb.AppendLine("   [?] ZoneData를 못 찾아 건너뛴다");
            else if (cum <= quota) sb.AppendLine($"   [O] 마지막 단계 누적 {cum} XP ≤ 할당량 {quota}");
            else
            {
                bad++;
                sb.AppendLine($"   [X] 마지막 단계는 누적 {cum} XP가 필요한데 할당량이 {quota}다 — " +
                              $"{tiers[tiers.Count - 1].Name} {tiers[tiers.Count - 1].Count}채가 한 판에서 안 쓰인다");
            }

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "어긋난 곳 없음."
                                   : $"어긋난 곳 {bad}군데. 값은 Tools/balance/hole_xp.csv에서 고치고 'Demo/5'로 되돌린다.");
            if (bad == 0) Debug.Log(sb.ToString()); else Debug.LogWarning(sb.ToString());
        }

        static string Line(string a, string b, string c, string d, string e) =>
            string.Format("   {0,-5} {1,-10} {2,-16} {3,-20} {4}", a, b, c, d, e);

        static T Load<T>(string filter) where T : Object
        {
            string[] g = AssetDatabase.FindAssets(filter);
            return g.Length > 0 ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g[0])) : null;
        }

        /// <summary>종류를 잔해 반경 순으로 세운다 — '몇 번째로 작은가'가 곧 단계다.</summary>
        static List<Tier> Tiers()
        {
            var counts = new Dictionary<string, int>();
            var zone = Load<ZoneData>("t:ZoneData");
            if (zone != null && zone.structures != null)
                foreach (var p in zone.structures)
                {
                    if (p?.data == null) continue;
                    counts.TryGetValue(p.data.name, out int c);
                    counts[p.data.name] = c + 1;
                }

            var list = new List<Tier>();
            var unused = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d == null) continue;

                // <b>구역이 실제로 쓰는 것만 본다</b> [고침 2026-10-02].
                // 프로젝트에 StructureData가 10개 있는데 구역은 5개만 쓴다 — 같은 수치로 쌍을 이룬
                // 두 벌이 공존한다(Plank/Wall/… 과 T1Weak/T2Light/…). 전부 세면 "10종"이 되어
                // 사다리 판정이 통째로 틀어진다. 안 쓰이는 쪽은 아래에서 따로 알린다.
                counts.TryGetValue(d.name, out int cnt);
                if (cnt == 0) { unused.Add(d.name); continue; }

                var so = new SerializedObject(d);
                list.Add(new Tier
                {
                    Name = d.name.Replace("SD_TEMP_", ""),
                    DebrisRadius = so.FindProperty("debrisRadius").floatValue,
                    DebrisCount = so.FindProperty("debrisCount").intValue,
                    SizeClass = so.FindProperty("debrisSizeClass").intValue,
                    BreakTime = so.FindProperty("breakTime").floatValue,
                    Scatter = so.FindProperty("debrisScatterRadius").floatValue,
                    Count = cnt,
                });
            }
            list.Sort((x, y) => x.DebrisRadius.CompareTo(y.DebrisRadius));

            if (unused.Count > 0)
                Debug.Log($"[사다리] 구역이 안 쓰는 StructureData {unused.Count}개는 검사에서 뺐다: " +
                          string.Join(", ", unused) + ". 어느 쪽이 정본인지는 기획자가 정할 일이다.");
            return list;
        }
    }
}
