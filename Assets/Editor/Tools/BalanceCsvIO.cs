using System.Collections.Generic;
using System.IO;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 밸런스 수치를 CSV로 내보내고 되돌린다 (TECH_SPEC §6 · D16~18).
    ///
    /// 이 프로젝트 기획자의 주 역량이 수치 기획과 검증이고, 그 작업은 스프레드시트에서 돈다.
    /// SO를 하나씩 Inspector로 고치는 것과, 표로 한 번에 내보내 고치고 되돌리는 것은
    /// 반복 횟수가 자릿수로 다르다.
    ///
    /// <b>표 세 장이다</b> — 건물(잔해 포함) · 경험치와 구멍 · 건물의 수.
    /// 파일은 <c>Tools/balance/</c>에 둔다. Assets 밖이라 .meta가 생기지 않고,
    /// 같은 자리에 있는 <c>tools/balance_sim.py</c>가 나중에 같은 표를 읽을 수 있다.
    ///
    /// <b>작업 순서</b>
    /// <list type="number">
    /// <item>메뉴 1로 임시 에셋을 만든다 (코드 기본값)</item>
    /// <item>메뉴 4로 내보낸다 → 지금 값이 CSV에 찍힌다</item>
    /// <item>엑셀에서 고친다</item>
    /// <item>메뉴 5로 되돌린다 → SO에 반영되고 구역이 다시 깔린다</item>
    /// <item>메뉴 2b로 씬을 다시 짓는다</item>
    /// </list>
    /// <b>메뉴 1은 코드 기본값으로 되돌린다</b> — CSV로 고친 값이 날아가므로 순서를 지켜야 한다.
    /// </summary>
    public static class BalanceCsvIO
    {
        const string StructureDir = "Assets/_Project/Data/Structures";
        const string ConfigPath = "Assets/_Project/Data/Config/GameConfig_TEMP.asset";
        const string ZonePath = "Assets/_Project/Data/Zones/Zone_TEMP_Test.asset";

        static string Dir => Path.Combine(
            Directory.GetParent(Application.dataPath).FullName, "Tools", "balance");

        static string PathOf(string file) => Path.Combine(Dir, file);

        const string StructuresCsv = "structures.csv";
        const string HoleCsv = "hole_xp.csv";
        const string ZoneCsv = "zone.csv";

        // ── 내보내기 ─────────────────────────────────────────────────────────────

        [MenuItem("Demo/4. 밸런스 CSV 내보내기", priority = 40)]
        public static void Export()
        {
            Directory.CreateDirectory(Dir);

            List<StructureData> types = LoadTypes();
            GameConfig cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            ZoneData zone = AssetDatabase.LoadAssetAtPath<ZoneData>(ZonePath);

            if (types.Count == 0 || cfg == null || zone == null)
            {
                Debug.LogError("[BalanceCsvIO] 에셋을 못 찾았다. 먼저 'Demo/1. 임시 에셋 생성'을 실행할 것.");
                return;
            }

            // 구역이 실제로 쓰는 종류를 앞에 둔다. 이름을 바꾼 뒤 남은 구세대 에셋이
            // 표 한가운데 섞이면 기획자가 어느 줄을 고쳐야 할지 알 수 없다.
            var inZone = new HashSet<string>();
            foreach (var p in zone.structures ?? new ZoneData.Placement[0])
                if (p != null && p.data != null) inZone.Add(p.data.id);

            var live = new List<StructureData>();
            var orphans = new List<StructureData>();
            foreach (StructureData s in types) (inZone.Contains(s.id) ? live : orphans).Add(s);

            WriteStructures(live, orphans);
            WriteHole(cfg);
            WriteZone(zone, live, orphans);

            if (orphans.Count > 0)
            {
                var names = new List<string>();
                foreach (StructureData s in orphans) names.Add(s.id);
                Debug.LogWarning("[BalanceCsvIO] 구역이 쓰지 않는 종류가 " + orphans.Count + "개 있다 — " +
                                 string.Join(", ", names) + ".\n" +
                                 "이름을 바꾸기 전의 에셋이라면 Project 창에서 지워도 된다. " +
                                 "표에서는 맨 아래에 따로 모아 뒀다.");
            }

            Debug.Log($"[BalanceCsvIO] 내보냈다 → {Dir}\n" +
                      $"  {StructuresCsv} · {HoleCsv} · {ZoneCsv}\n" +
                      "엑셀에서 고친 뒤 'Demo/5. 밸런스 CSV 불러오기'로 되돌린다. " +
                      "메뉴 1을 다시 돌리면 코드 기본값으로 덮어써지니 순서를 지킬 것.");
        }

        static void WriteStructures(List<StructureData> live, List<StructureData> orphans)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 건물 종류와 잔해. id는 바꾸지 말 것 — 이 열로 에셋을 찾는다.");
            sb.AppendLine("# breakClass: Quick(짧게 두드림) 또는 Tough(오래 두드림)");
            sb.AppendLine("# floors는 2 이상이어야 한다. 1이면 위층이 내려앉는 일이 안 일어난다.");
            sb.AppendLine("# debrisRadius는 구멍 게이트용 반경이다 — hole_xp.csv의 radius와 비교된다.");
            sb.AppendLine("# 빈 칸은 '건드리지 않음'이다. 0으로 만들려면 0을 적을 것.");
            sb.AppendLine(Csv.Row("id", "displayName", "tier", "breakClass", "breakTime", "floors",
                                  "sizeX", "sizeY", "sizeZ",
                                  "debrisCount", "debrisScatterRadius", "debrisSizeClass",
                                  "debrisRadius", "isSupport", "collapseStunSeconds"));

            foreach (StructureData s in live) sb.AppendLine(StructureRow(s));

            if (orphans.Count > 0)
            {
                sb.AppendLine("# ── 아래는 구역에 한 채도 없는 종류다 ──────────────────────────");
                sb.AppendLine("# 이름을 바꾸기 전의 에셋이면 Project 창에서 지워도 된다.");
                sb.AppendLine("# 지우지 않으면 다음 내보내기에도 계속 여기 남는다.");
                foreach (StructureData s in orphans) sb.AppendLine(StructureRow(s));
            }

            File.WriteAllText(PathOf(StructuresCsv), sb.ToString(), Csv.ExcelSafe);
        }

        static string StructureRow(StructureData s) =>
            Csv.Row(s.id, s.displayName, Csv.I(s.tier), s.breakClass.ToString(),
                    Csv.F(s.breakTime), Csv.I(s.floors),
                    Csv.F(s.colliderSize.x), Csv.F(s.colliderSize.y), Csv.F(s.colliderSize.z),
                    Csv.I(s.debrisCount), Csv.F(s.debrisScatterRadius),
                    Csv.I(s.debrisSizeClass), Csv.F(s.debrisRadius),
                    Csv.B(s.isSupport), Csv.F(s.collapseStunSeconds));

        static void WriteHole(GameConfig cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 경험치와 구멍 성장. 위에서부터 0단계, 1단계 … 순서다.");
            sb.AppendLine("# radius: 이 단계의 구멍 반경(m). 이보다 작은 잔해만 들어간다.");
            sb.AppendLine("#         앞 단계보다 반드시 커야 한다 — 구멍은 줄어들지 않는다.");
            sb.AppendLine("# xpToNext: 다음 단계까지 필요한 누적 경험치. 마지막 단계는 0.");
            sb.AppendLine("# xpPerDebrisUnit: 잔해 1단위가 주는 경험치. 전역값이라 첫 줄만 읽는다.");
            sb.AppendLine(Csv.Row("level", "radius", "xpToNext", "xpPerDebrisUnit"));

            var levels = cfg.holeLevels ?? new GameConfig.HoleLevel[0];
            for (int i = 0; i < levels.Length; i++)
                sb.AppendLine(Csv.Row(Csv.I(i), Csv.F(levels[i].radius), Csv.I(levels[i].xpToNext),
                                      i == 0 ? Csv.I(cfg.xpPerDebrisUnit) : ""));

            File.WriteAllText(PathOf(HoleCsv), sb.ToString(), Csv.ExcelSafe);
        }

        static void WriteZone(ZoneData zone, List<StructureData> live, List<StructureData> orphans)
        {
            var counts = new Dictionary<string, int>();
            foreach (var p in zone.structures ?? new ZoneData.Placement[0])
            {
                if (p == null || p.data == null) continue;
                counts.TryGetValue(p.data.id, out int n);
                counts[p.data.id] = n + 1;
            }

            var sb = new StringBuilder();
            sb.AppendLine("# 구역에 종류별로 몇 채를 둘 것인가. 줄 순서가 곧 앞뒤 순서다 —");
            sb.AppendLine("# 위에 있는 종류가 구멍 쪽(앞)에 깔린다.");
            sb.AppendLine("# spreadMul: 좌우 간격 = 건물 폭 × 이 값. 작으면 서로 시야를 가린다.");
            sb.AppendLine("# bandGap:   앞 띠와의 간격(m).");
            sb.AppendLine("# timeLimit(초)와 quota는 구역 전체 값이라 첫 줄만 읽는다.");
            sb.AppendLine(Csv.Row("structureId", "count", "spreadMul", "bandGap", "timeLimit", "quota"));

            for (int i = 0; i < live.Count; i++)
            {
                counts.TryGetValue(live[i].id, out int n);
                sb.AppendLine(Csv.Row(live[i].id, Csv.I(n),
                                      Csv.F(PlaceholderAssets.DefaultSpreadMul),
                                      Csv.F(PlaceholderAssets.DefaultBandGap),
                                      i == 0 ? Csv.F(zone.timeLimit) : "",
                                      i == 0 ? Csv.I(zone.quota) : ""));
            }

            if (orphans.Count > 0)
            {
                sb.AppendLine("# ── 아래는 이 구역에 한 채도 없는 종류다. count를 올리면 들어온다 ──");
                foreach (StructureData s in orphans)
                    sb.AppendLine(Csv.Row(s.id, "0",
                                          Csv.F(PlaceholderAssets.DefaultSpreadMul),
                                          Csv.F(PlaceholderAssets.DefaultBandGap), "", ""));
            }

            File.WriteAllText(PathOf(ZoneCsv), sb.ToString(), Csv.ExcelSafe);
        }

        // ── 불러오기 ─────────────────────────────────────────────────────────────

        [MenuItem("Demo/5. 밸런스 CSV 불러오기", priority = 41)]
        public static void Import()
        {
            if (!File.Exists(PathOf(StructuresCsv)))
            {
                Debug.LogError($"[BalanceCsvIO] {PathOf(StructuresCsv)} 가 없다. " +
                               "먼저 'Demo/4. 밸런스 CSV 내보내기'를 실행할 것.");
                return;
            }

            List<StructureData> types = LoadTypes();
            var byId = new Dictionary<string, StructureData>();
            foreach (StructureData s in types) byId[s.id] = s;

            int changed = ReadStructures(byId);
            ReadHole();
            ReadZone(byId, types);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogWarning($"[BalanceCsvIO] CSV를 반영했다 (건물 {changed}종). " +
                             "씬에 보이게 하려면 'Demo/2b. Main 씬 다시 짓기'를 실행할 것. " +
                             "값은 여전히 [제안·미승인]이다 — 확정하면 문서에 등급을 올릴 것.");
        }

        static int ReadStructures(Dictionary<string, StructureData> byId)
        {
            int n = 0;
            foreach (string[] r in Csv.Parse(File.ReadAllText(PathOf(StructuresCsv))))
            {
                string id = Csv.Str(r, 0, "");
                if (!byId.TryGetValue(id, out StructureData s))
                {
                    if (id.Length > 0) Debug.LogWarning($"[BalanceCsvIO] id '{id}'에 맞는 에셋이 없다 — 건너뛴다.");
                    continue;
                }

                s.displayName = Csv.Str(r, 1, s.displayName);
                s.tier = Csv.Int(r, 2, s.tier);
                if (System.Enum.TryParse(Csv.Str(r, 3, s.breakClass.ToString()), true,
                                         out StructureData.BreakClass bc)) s.breakClass = bc;
                s.breakTime = Csv.Num(r, 4, s.breakTime);
                s.floors = Mathf.Max(2, Csv.Int(r, 5, s.floors));
                s.colliderSize = new Vector3(Csv.Num(r, 6, s.colliderSize.x),
                                             Csv.Num(r, 7, s.colliderSize.y),
                                             Csv.Num(r, 8, s.colliderSize.z));
                s.debrisCount = Csv.Int(r, 9, s.debrisCount);
                s.debrisScatterRadius = Csv.Num(r, 10, s.debrisScatterRadius);
                s.debrisSizeClass = Csv.Int(r, 11, s.debrisSizeClass);
                s.debrisRadius = Csv.Num(r, 12, s.debrisRadius);
                s.isSupport = Csv.Bool(r, 13, s.isSupport);
                s.collapseStunSeconds = Csv.Num(r, 14, s.collapseStunSeconds);

                string bad = s.Validate();
                if (bad != null) Debug.LogError($"[BalanceCsvIO] {bad}", s);

                EditorUtility.SetDirty(s);
                n++;
            }
            return n;
        }

        static void ReadHole()
        {
            if (!File.Exists(PathOf(HoleCsv))) return;
            GameConfig cfg = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (cfg == null) return;

            var levels = new List<GameConfig.HoleLevel>();
            bool first = true;
            foreach (string[] r in Csv.Parse(File.ReadAllText(PathOf(HoleCsv))))
            {
                levels.Add(new GameConfig.HoleLevel {
                    radius = Csv.Num(r, 1, 0f),
                    xpToNext = Csv.Int(r, 2, 0),
                });
                if (first) { cfg.xpPerDebrisUnit = Csv.Int(r, 3, cfg.xpPerDebrisUnit); first = false; }
            }

            if (levels.Count > 0) cfg.holeLevels = levels.ToArray();

            string bad = cfg.Validate();
            if (bad != null) Debug.LogError($"[BalanceCsvIO] {bad}", cfg);

            EditorUtility.SetDirty(cfg);
        }

        static void ReadZone(Dictionary<string, StructureData> byId, List<StructureData> fallback)
        {
            if (!File.Exists(PathOf(ZoneCsv))) return;
            ZoneData zone = AssetDatabase.LoadAssetAtPath<ZoneData>(ZonePath);
            if (zone == null) return;

            var ordered = new List<StructureData>();
            var counts = new List<int>();
            var spreads = new List<float>();
            var gaps = new List<float>();
            bool first = true;

            foreach (string[] r in Csv.Parse(File.ReadAllText(PathOf(ZoneCsv))))
            {
                string id = Csv.Str(r, 0, "");
                if (!byId.TryGetValue(id, out StructureData s)) continue;

                ordered.Add(s);
                counts.Add(Csv.Int(r, 1, 0));
                spreads.Add(Csv.Num(r, 2, PlaceholderAssets.DefaultSpreadMul));
                gaps.Add(Csv.Num(r, 3, PlaceholderAssets.DefaultBandGap));

                if (first)
                {
                    zone.timeLimit = Csv.Num(r, 4, zone.timeLimit);
                    zone.quota = Csv.Int(r, 5, zone.quota);
                    first = false;
                }
            }

            if (ordered.Count == 0) ordered.AddRange(fallback);
            else PlaceholderAssets.Layout(zone, ordered, counts, spreads, gaps);

            EditorUtility.SetDirty(zone);
        }

        static List<StructureData> LoadTypes()
        {
            var list = new List<StructureData>();
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData", new[] { StructureDir }))
            {
                var s = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (s != null) list.Add(s);
            }
            // 티어 순서가 곧 앞뒤 배치 순서다. 파일 이름 순서에 기대면 안 된다.
            list.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier)
                                                 : string.CompareOrdinal(a.id, b.id));
            return list;
        }
    }
}
