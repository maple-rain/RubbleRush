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
    public static partial class BalanceCsvIO
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

            SortByBand(live, zone);

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

        /// <summary>
        /// 구역에 깔린 <b>앞뒤 순서</b>대로 세운다 [고침 2026-10-08].
        ///
        /// <c>zone.csv</c>는 <b>줄 순서가 곧 띠 순서</b>다. 그런데 내보내기는 에셋을 찾은 순서로
        /// 적었으므로, 한 번 내보냈다가 그대로 다시 불러오면 <b>띠 순서가 바뀐다</b> —
        /// t7(4.6m)을 t3과 t4 사이에 넣어 두었는데 내보내니 맨 뒤로 가서, 다시 불러오면
        /// 크기가 t5(12m) · t6(8m) · t7(4.6m)로 거꾸로 작아지는 배치가 된다.
        /// 아무 값도 안 고치고 버튼만 두 번 눌러도 배치가 망가지는 셈이라, 내보내는 쪽을 맞춘다.
        /// </summary>
        static void SortByBand(List<StructureData> live, ZoneData zone)
        {
            var depth = new Dictionary<string, float>();
            foreach (var p in zone.structures ?? new ZoneData.Placement[0])
            {
                if (p == null || p.data == null) continue;
                if (!depth.TryGetValue(p.data.id, out float z) || p.position.z < z)
                    depth[p.data.id] = p.position.z;
            }

            live.Sort((a, b) =>
            {
                float za = depth.TryGetValue(a.id, out float x) ? x : float.MaxValue;
                float zb = depth.TryGetValue(b.id, out float y) ? y : float.MaxValue;
                return za.CompareTo(zb);
            });
        }

        /// <summary>
        /// 내보내기가 매번 찍는 머리말. <b>보존 대상을 가려내는 데도 쓴다</b> —
        /// 이 목록에 없는 <c>#</c> 줄은 사람이 적은 것이므로 살려 둔다 (<see cref="KeepNotes"/>).
        /// </summary>
        static readonly string[] StructureHead =
        {
            "# 건물 종류와 잔해. id는 바꾸지 말 것 — 이 열로 에셋을 찾는다.",
            "# breakClass: Quick(짧게 두드림) 또는 Tough(오래 두드림)",
            "# floors는 2 이상이어야 한다. 1이면 위층이 내려앉는 일이 안 일어난다.",
            "# debrisRadius는 구멍 게이트용 반경이다 — hole_xp.csv의 radius와 비교된다.",
            "# 빈 칸은 '건드리지 않음'이다. 0으로 만들려면 0을 적을 것.",
            "# ── 아래는 구역에 한 채도 없는 종류다 ──────────────────────────",
            "# 이름을 바꾸기 전의 에셋이면 Project 창에서 지워도 된다.",
            "# 지우지 않으면 다음 내보내기에도 계속 여기 남는다.",
        };

        static readonly string[] HoleHead =
        {
            "# 경험치와 구멍 성장. 위에서부터 0단계, 1단계 … 순서다.",
            "# radius: 이 단계의 구멍 반경(m). 이보다 작은 잔해만 들어간다.",
            "#         앞 단계보다 반드시 커야 한다 — 구멍은 줄어들지 않는다.",
            "# cableLength: 그 단계에서 쓸 수 있는 케이블 길이(m). 구멍과 같은 사다리다 —",
            "#              단계가 오르면 삼킬 수 있는 크기도 감쌀 수 있는 덩어리도 같이 커진다.",
            "#              0이면 cableMaxLength로 물러선다.",
            "# xpToNext: 다음 단계까지 필요한 누적 경험치. 마지막 단계는 0.",
            "# xpPerDebrisUnit: 잔해 1단위가 주는 경험치. 전역값이라 첫 줄만 읽는다.",
        };

        static readonly string[] ZoneHead =
        {
            "# 구역에 종류별로 몇 채를 둘 것인가. 줄 순서가 곧 앞뒤 순서다 —",
            "# 위에 있는 종류가 구멍 쪽(앞)에 깔린다.",
            "# spreadMul: 좌우 간격 = 건물 폭 × 이 값. 작으면 서로 시야를 가린다.",
            "# bandGap:   앞 띠와의 간격(m).",
            "# timeLimit(초)와 quota는 구역 전체 값이라 첫 줄만 읽는다.",
            "# ── 아래는 이 구역에 한 채도 없는 종류다. count를 올리면 들어온다 ──",
        };

        /// <summary>
        /// 사람이 CSV에 적어 둔 주석을 되살린다 [2026-10-08].
        ///
        /// <b>왜 필요한가.</b> 내보내기는 파일을 <b>통째로 다시 쓴다</b>. 그래서
        /// <c>structures.csv</c> 머리에 손으로 적어 둔 실측 기록(효율 사다리가 T5에서 꺾이는
        /// 이유, 한 숫자만 바꿔 되살리는 네 가지 방법)이 버튼 한 번에 전부 날아갔다.
        /// 그 글은 기획자가 재서 적은 결론이고, 어디에도 복사본이 없다.
        ///
        /// 자동으로 찍는 줄(<paramref name="head"/>)과 글자까지 같은 것만 버리고 나머지는 남긴다.
        ///
        /// <b>세 파일 전부에 걸어야 한다</b> [고침 2026-10-08]. 처음에는 structures.csv에만
        /// 걸었는데, 바로 그 한 번의 내보내기가 <c>hole_xp.csv</c>에 적혀 있던 실측 기록
        /// 37줄("구멍 사다리가 설계를 어기고 있다", 띠별 radius 범위, 어느 쪽 끝이든 결과가
        /// 같더라는 측정)을 지웠다. 반쪽짜리 보호는 보호가 아니다.
        /// </summary>
        static List<string> KeepNotes(string path, string[] head)
        {
            var kept = new List<string>();
            if (!File.Exists(path)) return kept;

            var known = new HashSet<string>(head);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (!line.StartsWith("#")) continue;
                if (known.Contains(line)) continue;
                kept.Add(line);
            }
            return kept;
        }

        static void WriteStructures(List<StructureData> live, List<StructureData> orphans)
        {
            List<string> notes = KeepNotes(PathOf(StructuresCsv), StructureHead);

            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++) sb.AppendLine(StructureHead[i]);
            foreach (string n in notes) sb.AppendLine(n);
            sb.AppendLine(Csv.Row("id", "displayName", "tier", "breakClass", "breakTime", "floors",
                                  "sizeX", "sizeY", "sizeZ",
                                  "debrisCount", "debrisScatterRadius", "debrisSizeClass",
                                  "debrisRadius", "isSupport", "collapseStunSeconds"));

            foreach (StructureData s in live) sb.AppendLine(StructureRow(s));

            if (orphans.Count > 0)
            {
                for (int i = 5; i < StructureHead.Length; i++) sb.AppendLine(StructureHead[i]);
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
            foreach (string h in HoleHead) sb.AppendLine(h);
            foreach (string n in KeepNotes(PathOf(HoleCsv), HoleHead)) sb.AppendLine(n);
            sb.AppendLine(Csv.Row("level", "radius", "cableLength", "xpToNext", "xpPerDebrisUnit"));

            var levels = cfg.holeLevels ?? new GameConfig.HoleLevel[0];
            for (int i = 0; i < levels.Length; i++)
                sb.AppendLine(Csv.Row(Csv.I(i), Csv.F(levels[i].radius), Csv.F(levels[i].cableLength),
                                      Csv.I(levels[i].xpToNext),
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
            for (int i = 0; i < ZoneHead.Length - 1; i++) sb.AppendLine(ZoneHead[i]);
            foreach (string n in KeepNotes(PathOf(ZoneCsv), ZoneHead)) sb.AppendLine(n);
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
                sb.AppendLine(ZoneHead[ZoneHead.Length - 1]);
                foreach (StructureData s in orphans)
                    sb.AppendLine(Csv.Row(s.id, "0",
                                          Csv.F(PlaceholderAssets.DefaultSpreadMul),
                                          Csv.F(PlaceholderAssets.DefaultBandGap), "", ""));
            }

            File.WriteAllText(PathOf(ZoneCsv), sb.ToString(), Csv.ExcelSafe);
        }
    }
}
