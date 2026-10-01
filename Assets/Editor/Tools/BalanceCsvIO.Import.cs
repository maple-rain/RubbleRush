using System.Collections.Generic;
using System.IO;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// CSV를 읽어 SO에 되돌리는 쪽 [분할 2026-10-01].
    ///
    /// <see cref="BalanceCsvIO"/>가 325줄이어서 내보내기/불러오기로 갈랐다 (CLAUDE.md 규칙 3).
    /// <b>새 타입을 만들지 않고 partial로 둔 이유</b>: 이 둘은 같은 도구의 양 방향이고
    /// 경로·파일명·LoadTypes를 공유한다. 타입을 쪼개면 그 공유분을 또 어딘가로 옮겨야 하는데,
    /// 그건 파일 수만 늘릴 뿐 읽는 사람에게 아무것도 더 말해 주지 않는다.
    /// 규칙이 막으려는 것은 <b>한 파일이 커져서 부분 수정이 비싸지는 것</b>이고, partial이면 그게 풀린다.
    ///
    /// 공유하는 상수와 경로는 BalanceCsvIO.cs에 있다.
    /// </summary>
    public static partial class BalanceCsvIO
    {
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
