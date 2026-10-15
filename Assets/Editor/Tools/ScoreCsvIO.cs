using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// `Tools/balance/score_rules.csv` · `grades.csv`를 <see cref="GameConfig"/>에 넣는다
    /// [신설 2026-10-15].
    ///
    /// <b>왜 이제야 만드는가.</b> 두 CSV는 2026-10-13부터 있었지만 <b>읽는 코드가 없었다.</b>
    /// 사람이 보고 인스펙터에 손으로 옮겨 적는 메모였던 셈이다. 그래서 등급 문턱 25,000이
    /// 실제 플레이와 한 번도 대조되지 않은 채로 남아 있었다 — 측정해 보니 잘 친 한 판이
    /// 20,582점이라 <b>별 둘이 애초에 도달 불가</b>였다.
    ///
    /// 값을 고칠 때마다 손으로 옮기면 또 어긋난다. CSV 한 줄을 고치고 이 메뉴를 누르는
    /// 것으로 끝나야 한다 (CLAUDE.md 규칙 8).
    ///
    /// <b>값을 만들지 않는다.</b> 여기는 옮기기만 한다. 비어 있는 칸은 건너뛰므로,
    /// 기획자가 안 채운 항목은 에셋의 값이 그대로 남는다.
    /// </summary>
    public static class ScoreCsvIO
    {
        const string Rules = "Tools/balance/score_rules.csv";
        const string Grades = "Tools/balance/grades.csv";

        [MenuItem("Demo/18. 점수·등급 불러오기 — score_rules.csv · grades.csv", priority = 72)]
        public static void Import()
        {
            GameConfig cfg = Find();
            if (cfg == null) { Debug.LogError("[점수] GameConfig 에셋을 못 찾았다"); return; }

            var so = new SerializedObject(cfg);
            var log = new System.Text.StringBuilder("[점수] CSV에서 불러왔다\n");

            if (!ImportRules(so, log)) return;
            if (!ImportGrades(so, log)) return;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString(), cfg);
        }

        /// <summary>key,value 두 열짜리 표를 그대로 필드에 꽂는다.</summary>
        static bool ImportRules(SerializedObject so, System.Text.StringBuilder log)
        {
            List<string[]> rows = Read(Rules, out string why);
            if (rows == null) { Debug.LogError("[점수] " + why); return false; }

            log.AppendLine("  score_rules.csv");
            foreach (string[] c in rows)
            {
                if (c.Length < 2) continue;
                string key = c[0].Trim(), raw = c[1].Trim();
                if (key.Length == 0 || raw.Length == 0) continue;   // 빈 칸은 에셋 값을 둔다

                SerializedProperty p = so.FindProperty(key);
                if (p == null) { log.AppendLine("    [?] " + key + " — 그런 필드가 없다"); continue; }
                if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                { log.AppendLine("    [?] " + key + " — 숫자가 아니다: " + raw); continue; }

                log.AppendLine(string.Format("    {0,-28} {1,10:0.##}  (전 {2:0.##})",
                                             key, v, p.floatValue));
                p.floatValue = v;
            }
            return true;
        }

        /// <summary>stars,minScore,label 표를 등급 배열로 옮긴다.</summary>
        static bool ImportGrades(SerializedObject so, System.Text.StringBuilder log)
        {
            List<string[]> rows = Read(Grades, out string why);
            if (rows == null) { Debug.LogError("[점수] " + why); return false; }

            var keep = new List<(int stars, float min, string label)>();

            // <b>반쯤 채운 표는 아예 거부한다.</b>
            //
            // 전에 쓰던 방식대로 "못 읽는 줄은 건너뛴다"로 두면, 문턱을 아직 안 채운
            // grades.csv를 불러왔을 때 <b>등급표가 한 줄로 쪼그라든다</b> — 멀쩡하던
            // 에셋을 못 쓰게 만들어 놓고 성공했다고 적는 꼴이다.
            // 덜 채웠으면 아무것도 안 바꾸고 무엇이 비었는지 말한다.
            foreach (string[] c in rows)
            {
                if (c.Length < 2) continue;
                string sRaw = c[0].Trim(), mRaw = c[1].Trim();
                if (sRaw.Length == 0 && mRaw.Length == 0) continue;     // 아예 빈 줄

                if (!int.TryParse(sRaw, out int stars))
                {
                    Debug.LogError("[점수] grades.csv — 별 개수를 못 읽었다: '" + sRaw
                                 + "'. 아무것도 바꾸지 않았다.");
                    return false;
                }
                if (!float.TryParse(mRaw, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out float min))
                {
                    Debug.LogError("[점수] grades.csv — 별 " + stars
                                 + "줄의 minScore가 비었다. 한 판 돌려 보고 채울 칸이다 "
                                 + "(grades.csv 머리글 참조). 등급표는 그대로 뒀다.");
                    return false;
                }
                keep.Add((stars, min, c.Length > 2 ? c[2].Trim() : ""));
            }

            if (keep.Count == 0) { Debug.LogError("[점수] grades.csv에 읽을 줄이 없다"); return false; }

            // <b>맨 아래가 0점이어야 한다.</b> 아니면 낮은 점수에 등급이 안 나온다.
            float lowest = float.MaxValue;
            foreach (var g in keep) if (g.min < lowest) lowest = g.min;
            if (lowest > 0f)
                Debug.LogWarning("[점수] grades.csv의 제일 낮은 minScore가 " + lowest
                               + "다. 0이어야 그 아래 점수도 등급이 나온다 — 지금은 "
                               + "Score.GradeOf가 제일 낮은 칸으로 메운다.");

            SerializedProperty arr = so.FindProperty("grades");
            arr.arraySize = keep.Count;
            log.AppendLine("  grades.csv");
            for (int i = 0; i < keep.Count; i++)
            {
                SerializedProperty e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("stars").intValue = keep[i].stars;
                e.FindPropertyRelative("minScore").floatValue = keep[i].min;
                e.FindPropertyRelative("label").stringValue = keep[i].label;
                log.AppendLine(string.Format("    별 {0}  {1,8:0}점  {2}",
                                             keep[i].stars, keep[i].min, keep[i].label));
            }
            return true;
        }

        static List<string[]> Read(string rel, out string why)
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, rel);
            if (!File.Exists(path)) { why = "CSV가 없다: " + path; return null; }

            var rows = new List<string[]>();
            bool header = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimStart('﻿').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (!header) { header = true; continue; }       // 열 이름 줄
                rows.Add(line.Split(','));
            }
            why = null;
            return rows;
        }

        static GameConfig Find()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GameConfig)))
            {
                var c = AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null) return c;
            }
            return null;
        }
    }
}
