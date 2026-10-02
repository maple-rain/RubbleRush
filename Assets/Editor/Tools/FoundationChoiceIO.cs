using System.IO;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 11번 — 토대 규칙을 <c>foundation.csv</c>의 <c>choose</c> 열에서 읽어 온다 [2026-10-02].
    ///
    /// <b>왜 <see cref="HaulTuningIO"/>와 따로인가.</b> 저쪽은 씬 컴포넌트(<c>CargoTow</c>)의
    /// key/value 표를 왕복시킨다. 이쪽은 <b>ScriptableObject 한 필드</b>이고, 표의 모양도 다르다 —
    /// 값을 적는 것이 아니라 <b>다섯 줄 중 하나에 x를 치는</b> 결정표다.
    /// 한 도구에 억지로 합치면 두 모양을 다 아는 분기가 생기고, 두 표가 서로의 주석을 밟는다.
    ///
    /// <b>왜 10-01에는 없었나.</b> 그때는 다섯 안 중 하나도 구현돼 있지 않아서, 고르고 나면
    /// 코드를 고쳐야 했다. 지금은 <see cref="GameConfig.foundationRule"/> 하나로 전부 갈린다.
    /// </summary>
    public static class FoundationChoiceIO
    {
        const string CsvPath = "Tools/balance/foundation.csv";

        /// <summary>CSV의 option 이름 → 코드의 규칙. 표와 코드를 잇는 유일한 자리다.</summary>
        static readonly (string Option, GameConfig.FoundationRule Rule)[] Map =
        {
            ("A_그대로",  GameConfig.FoundationRule.None),
            ("B_장애물",  GameConfig.FoundationRule.Obstacle),
            ("C_발판",    GameConfig.FoundationRule.Platform),
            ("D_케이블",  GameConfig.FoundationRule.CableOnly),
            ("E_치움",    GameConfig.FoundationRule.Decay),
        };

        static string Full(string rel) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", rel);

        static GameConfig Config()
        {
            string[] guids = AssetDatabase.FindAssets("t:GameConfig");
            if (guids.Length > 0)
                return AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));

            Debug.LogError("[FoundationChoiceIO] GameConfig 에셋을 못 찾았다.");
            return null;
        }

        [MenuItem("Demo/13. 토대 규칙 CSV 불러오기", priority = 64)]
        public static void Import()
        {
            string path = Full(CsvPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[FoundationChoiceIO] {CsvPath} 가 없다.");
                return;
            }

            GameConfig cfg = Config();
            if (cfg == null) return;

            var picked = new System.Collections.Generic.List<string>();
            GameConfig.FoundationRule rule = GameConfig.FoundationRule.None;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("option,")) continue;

                string[] cell = line.Split(',');
                if (cell.Length < 2) continue;

                string option = cell[0].Trim();
                if (cell[1].Trim().Length == 0) continue;      // 안 고른 줄

                bool known = false;
                foreach (var m in Map)
                    if (m.Option == option) { rule = m.Rule; known = true; break; }

                if (!known)
                {
                    Debug.LogWarning($"[FoundationChoiceIO] 모르는 선택지 '{option}'는 건너뛴다.");
                    continue;
                }
                picked.Add(option);
            }

            // <b>둘 이상 고르면 안 쓴다.</b> 조용히 첫 줄을 택하면 기획자가 적은 두 번째 줄이
            // 사라진 줄 모른 채 측정하게 된다 — 그게 제일 비싼 실수다.
            if (picked.Count > 1)
            {
                Debug.LogError($"[FoundationChoiceIO] choose에 x가 {picked.Count}개다 " +
                               $"({string.Join(", ", picked)}). 하나만 남기고 다시 실행할 것. " +
                               "아무것도 바꾸지 않았다.");
                return;
            }
            if (picked.Count == 0)
            {
                Debug.LogWarning("[FoundationChoiceIO] choose가 전부 비어 있다 — " +
                                 "A_그대로(None)로 둔다. 지금 동작과 같다.");
            }

            var so = new SerializedObject(cfg);
            so.FindProperty("foundationRule").enumValueIndex = (int)rule;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            Debug.LogWarning($"[FoundationChoiceIO] 토대 규칙을 {rule} 로 바꿨다 " +
                             $"({(picked.Count > 0 ? picked[0] : "기본값")}). 값은 여전히 [제안·미승인]이다.");
        }

        [MenuItem("Demo/13b. 토대 규칙 CSV에 적기", priority = 65)]
        public static void Export()
        {
            string path = Full(CsvPath);
            if (!File.Exists(path))
            {
                Debug.LogError($"[FoundationChoiceIO] {CsvPath} 가 없다. 표가 있어야 x를 칠 수 있다.");
                return;
            }

            GameConfig cfg = Config();
            if (cfg == null) return;

            var sb = new StringBuilder();
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("option,"))
                {
                    sb.AppendLine(raw);
                    continue;
                }

                string[] cell = raw.Split(',');
                if (cell.Length < 2) { sb.AppendLine(raw); continue; }

                bool mine = false;
                foreach (var m in Map)
                    if (m.Option == cell[0].Trim() && m.Rule == cfg.foundationRule) { mine = true; break; }

                cell[1] = mine ? "x" : "";
                sb.AppendLine(string.Join(",", cell));
            }

            File.WriteAllText(path, sb.ToString(), Csv.ExcelSafe);
            AssetDatabase.Refresh();
            Debug.Log($"[FoundationChoiceIO] 지금 규칙({cfg.foundationRule})을 {CsvPath}의 choose에 적었다.");
        }
    }
}
