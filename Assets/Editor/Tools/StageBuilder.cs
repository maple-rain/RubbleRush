using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// `Tools/balance/stages.csv`를 읽어 <see cref="ZoneData"/> 에셋을 짓는다
    /// [지시 2026-10-15: "총 스테이지 3개 정도로 구상중 튜토리얼을 제외하고"].
    ///
    /// <b>구역을 손으로 배치하지 않는다.</b> 지금 있는 `Zone_TEMP_Test`를 본으로 삼아
    /// <b>건물 종류의 수와 벌린 정도만 바꿔</b> 다시 깐다. 기획자가 말한 두 축이
    /// 그대로 열이다 — `quota`(필요한 양)와 `spread`(건물 배치 거리).
    ///
    /// <b>왜 생성인가.</b> 구역 하나에 건물이 39채다. 세 판을 손으로 놓으면 백 번이 넘고,
    /// 숫자를 한 번 고칠 때마다 다시 놓아야 한다. CSV 한 줄을 고치고 이 메뉴를 누르면 끝난다
    /// (CLAUDE.md 규칙 8).
    ///
    /// <b>본은 안 건드린다.</b> `Zone_TEMP_Test`는 그대로 두고 `Zone_s01` 같은 새 에셋을 만든다.
    /// </summary>
    public static class StageBuilder
    {
        const string Csv = "Tools/balance/stages.csv";
        const string Template = "Assets/_Project/Data/Zones/Zone_TEMP_Test.asset";
        const string OutDir = "Assets/_Project/Data/Zones";
        const string CatalogPath = "Assets/_Project/Data/Zones/StageCatalog.asset";

        sealed class Row
        {
            public string id, name, note;
            public int order, quota;
            public float timeLimit, spread;
            public Dictionary<string, int> counts = new Dictionary<string, int>();
        }

        [MenuItem("Demo/17. 스테이지 짓기 — stages.csv에서", priority = 71)]
        public static void Build()
        {
            var tpl = AssetDatabase.LoadAssetAtPath<ZoneData>(Template);
            if (tpl == null) { Debug.LogError("[스테이지] 본 구역을 못 찾았다: " + Template); return; }

            List<Row> rows = Read();
            if (rows == null || rows.Count == 0) { Debug.LogError("[스테이지] CSV가 비었다"); return; }

            // 본에 어떤 종류가 몇 채 있는지 — 비율을 맞추는 데 쓴다
            var byKind = new Dictionary<StructureData, List<ZoneData.Placement>>();
            foreach (var p in tpl.structures)
            {
                if (p == null || p.data == null) continue;
                if (!byKind.TryGetValue(p.data, out var list))
                    byKind[p.data] = list = new List<ZoneData.Placement>();
                list.Add(p);
            }

            var made = new List<ZoneData>();
            var sb = new System.Text.StringBuilder("[스테이지] stages.csv에서 지었다\n");

            foreach (Row r in rows)
            {
                ZoneData z = Make(tpl, byKind, r);
                made.Add(z);
                sb.AppendLine(string.Format("  {0,-4} {1,-10} 제한 {2:0}초 · 할당량 {3} · 벌림 x{4:0.00} · 건물 {5}채 (가능 {6}단위)",
                    r.id, r.name, r.timeLimit, r.quota, r.spread, z.structures.Length, z.TotalAvailableUnits));
            }

            Catalog(made, rows);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(sb.ToString());
        }

        static ZoneData Make(ZoneData tpl,
                             Dictionary<StructureData, List<ZoneData.Placement>> byKind, Row r)
        {
            string path = Path.Combine(OutDir, "Zone_" + r.id + ".asset").Replace('\\', '/');
            var z = AssetDatabase.LoadAssetAtPath<ZoneData>(path);
            if (z == null)
            {
                z = ScriptableObject.CreateInstance<ZoneData>();
                AssetDatabase.CreateAsset(z, path);
            }

            var so = new SerializedObject(z);
            so.FindProperty("id").stringValue = r.id;
            so.FindProperty("displayName").stringValue = string.IsNullOrEmpty(r.name) ? r.id : r.name;
            so.FindProperty("timeLimit").floatValue = r.timeLimit;
            so.FindProperty("quota").intValue = r.quota;
            so.FindProperty("holePosition").vector3Value = tpl.holePosition;

            // 종류마다 CSV가 시킨 수만큼 고른다. 본에 있는 자리를 앞에서부터 쓰고,
            // 모자라면 있는 것을 돌려 쓰되 자리를 조금 비껴 둔다.
            var picked = new List<ZoneData.Placement>();
            foreach (var kv in byKind)
            {
                int want = Want(r, kv.Key.name);
                var src = kv.Value;
                for (int i = 0; i < want; i++)
                {
                    var from = src[i % src.Count];
                    var p = new ZoneData.Placement
                    {
                        id = picked.Count + 1,
                        data = kv.Key,
                        yaw = from.yaw,
                        // <b>벌림은 구멍을 기준으로 민다.</b> 원점 기준으로 하면 구멍이
                        // 구역 한가운데가 아닐 때 한쪽만 멀어진다.
                        position = tpl.holePosition
                                 + (from.position - tpl.holePosition) * r.spread
                                 + Offset(i / Mathf.Max(1, src.Count)),
                    };
                    picked.Add(p);
                }
            }

            var arr = so.FindProperty("structures");
            arr.arraySize = picked.Count;
            for (int i = 0; i < picked.Count; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").intValue = picked[i].id;
                e.FindPropertyRelative("data").objectReferenceValue = picked[i].data;
                e.FindPropertyRelative("position").vector3Value = picked[i].position;
                e.FindPropertyRelative("yaw").floatValue = picked[i].yaw;
            }
            so.FindProperty("hazards").arraySize = 0;
            so.FindProperty("supports").arraySize = 0;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(z);
            return z;
        }

        /// <summary>같은 자리를 두 번 쓸 때 겹치지 않게 비껴 둔다.</summary>
        static Vector3 Offset(int round) =>
            round == 0 ? Vector3.zero : new Vector3(3.5f * round, 0f, 3.5f * round);

        static int Want(Row r, string structureName)
        {
            string n = structureName.ToLowerInvariant();
            foreach (var kv in r.counts)
                if (n.Contains(kv.Key.Replace("_", ""))) return kv.Value;
            return 0;
        }

        static void Catalog(List<ZoneData> zones, List<Row> rows)
        {
            var cat = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
            if (cat == null)
            {
                cat = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(cat, CatalogPath);
            }
            var so = new SerializedObject(cat);
            var arr = so.FindProperty("stages");
            arr.arraySize = zones.Count;
            for (int i = 0; i < zones.Count; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = rows[i].name;
                e.FindPropertyRelative("zone").objectReferenceValue = zones[i];
                e.FindPropertyRelative("locked").boolValue = false;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cat);
        }

        static List<Row> Read()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Csv);
            if (!File.Exists(path)) { Debug.LogError("[스테이지] CSV가 없다: " + path); return null; }

            var rows = new List<Row>();
            string[] header = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimStart('﻿').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] cell = line.Split(',');
                if (header == null) { header = cell; continue; }

                var r = new Row();
                for (int i = 0; i < header.Length && i < cell.Length; i++)
                {
                    string k = header[i].Trim(), v = cell[i].Trim();
                    switch (k)
                    {
                        case "stageId": r.id = v; break;
                        case "displayName": r.name = v; break;
                        case "note": r.note = v; break;
                        case "order": int.TryParse(v, out r.order); break;
                        case "quota": int.TryParse(v, out r.quota); break;
                        case "timeLimit": float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r.timeLimit); break;
                        case "spread": float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r.spread); break;
                        default:
                            if (int.TryParse(v, out int n)) r.counts[k] = n;
                            break;
                    }
                }
                if (r.spread <= 0f) r.spread = 1f;
                if (!string.IsNullOrEmpty(r.id)) rows.Add(r);
            }
            rows.Sort((a, b) => a.order.CompareTo(b.order));
            return rows;
        }
    }
}
