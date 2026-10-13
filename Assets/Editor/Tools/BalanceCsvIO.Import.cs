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

            int made = 0;
            int changed = ReadStructures(byId, types, ref made);
            ReadHole();
            ReadZone(byId, types);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogWarning($"[BalanceCsvIO] CSV를 반영했다 (건물 {changed}종" +
                             (made > 0 ? $", 그중 {made}종은 새로 만듦" : "") + "). " +
                             "씬에 보이게 하려면 'Demo/2b. Main 씬 다시 짓기'를 실행할 것. " +
                             "값은 여전히 [제안·미승인]이다 — 확정하면 문서에 등급을 올릴 것.");
        }

        static int ReadStructures(Dictionary<string, StructureData> byId,
                                  List<StructureData> types, ref int made)
        {
            int n = 0;
            foreach (string[] r in Csv.Parse(File.ReadAllText(PathOf(StructuresCsv))))
            {
                string id = Csv.Str(r, 0, "");
                if (!byId.TryGetValue(id, out StructureData s))
                {
                    if (id.Length == 0) continue;

                    s = Create(id, types);
                    if (s == null) continue;
                    byId[id] = s;
                    types.Add(s);
                    made++;
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

        /// <summary>
        /// CSV에 있는데 에셋이 없는 id를 보면 <b>새 종류를 만든다</b> [2026-10-08].
        ///
        /// <b>왜 만들어도 되는가.</b> 전에는 경고만 찍고 건너뛰었다. 그래서 건물을 한 종 늘리려면
        /// 에디터에서 에셋을 손으로 만들고, 이름을 맞추고, 메시 셋을 끌어다 꽂은 다음에야
        /// CSV가 먹었다 — 그 네 단계가 전부 <b>기획자가 CSV에 한 줄 적었다</b>는 결정의 뒤치다꺼리다.
        /// 줄을 적는 것이 곧 결정이므로, 그 줄을 보고 만드는 것은 월권이 아니다 (CLAUDE.md 규칙 8).
        ///
        /// <b>메시는 본보기에서 베낀다.</b> 지금 여섯 종이 전부 같은 자리표시자 셋을 쓰고,
        /// 필드의 상자는 어차피 <c>colliderSize</c>로 크기가 정해진다 — 종류를 가르는 것은
        /// 메시가 아니라 <b>정면도</b>다. 비워 두면 <c>Validate()</c>가 바로 빨간 줄을 뱉으므로
        /// 만들자마자 못 쓰는 에셋이 된다.
        ///
        /// 오타로 없는 id를 적으면 쓸모없는 종류가 하나 생긴다. 지우면 그만이고,
        /// 로그에 <b>만들었다고 크게 찍어</b> 모르고 지나가지 않게 한다.
        /// </summary>
        static StructureData Create(string id, List<StructureData> types)
        {
            StructureData template = null;
            foreach (StructureData t in types)
                if (t.meshIntact != null && t.meshDamaged != null && t.meshRubble != null) { template = t; break; }

            if (template == null)
            {
                Debug.LogError($"[BalanceCsvIO] '{id}'를 만들려 했지만 메시를 베껴올 종류가 하나도 없다. " +
                               "먼저 'Demo/1. 임시 에셋 생성'을 실행할 것.");
                return null;
            }

            var s = ScriptableObject.CreateInstance<StructureData>();
            s.id = id;
            s.meshIntact = template.meshIntact;
            s.meshDamaged = template.meshDamaged;
            s.meshRubble = template.meshRubble;

            string path = AssetDatabase.GenerateUniqueAssetPath($"{StructureDir}/SD_TEMP_{Pascal(id)}.asset");
            AssetDatabase.CreateAsset(s, path);

            Debug.LogWarning($"[BalanceCsvIO] CSV에 있는 '{id}'의 에셋이 없어서 새로 만들었다: {path}\n" +
                             $"  메시는 '{template.id}'의 것을 그대로 쓴다. 정면도는 " +
                             "'Demo/17. 건물 정면도 연결'이 T_" + id + "_front.png 를 찾아 꽂는다.", s);
            return s;
        }

        /// <summary><c>t7_glass</c> → <c>T7Glass</c>. 에셋 이름 규칙을 기존 여섯 종과 맞춘다.</summary>
        static string Pascal(string id)
        {
            var sb = new System.Text.StringBuilder(id.Length);
            bool up = true;
            foreach (char c in id)
            {
                if (c == '_' || c == '-' || c == ' ') { up = true; continue; }
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = false;
            }
            return sb.Length == 0 ? "New" : sb.ToString();
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
                    cableLength = Csv.Num(r, 2, 0f),
                    xpToNext = Csv.Int(r, 3, 0),
                });
                if (first) { cfg.xpPerDebrisUnit = Csv.Int(r, 4, cfg.xpPerDebrisUnit); first = false; }
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
