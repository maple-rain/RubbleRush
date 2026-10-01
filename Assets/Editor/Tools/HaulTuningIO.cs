using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 견인 튜닝(13·14번)을 CSV 두 장으로 조절한다 [지시 2026-10-01].
    ///
    /// <b>왜 필요한가.</b> 13·14번으로 조절할 값이 <b>21개</b> 생겼다
    /// (케이블 물리 17 + 걸림 4). 전부 씬 <c>[Player]</c>의 <see cref="Collection.CargoTow"/>
    /// 한 컴포넌트 안에 있지만, 인스펙터로만 만지면 기획자가 값을 <b>기록으로 남길 수가 없다</b> —
    /// 무엇을 왜 그 값으로 뒀는지가 씬 바이너리에만 남는다.
    /// CSV로 빼면 값이 git diff에 보이고, 실측 근거를 주석으로 같이 둘 수 있다.
    ///
    /// <b>왜 <see cref="PunchTuningIO"/>처럼 get/set 람다 표가 아닌가.</b> 저쪽은 값이
    /// 컨트롤러·PlayerAnimator·DestructionFX 세 군데에 흩어져 있어 읽는 법이 제각각이다.
    /// 여기는 전부 한 컴포넌트의 <see cref="SerializedProperty"/>라, <b>CSV 키에서
    /// 속성 경로로 가는 표 하나</b>면 끝난다. 람다를 쓰면 줄만 늘고 더 말해 주는 것이 없다.
    ///
    /// <b>11번(토대)은 여기 없다.</b> <c>foundation.csv</c>는 값이 아니라 <b>결정</b>을 담은 표다 —
    /// 고르고 나서야 비로소 손댈 곳이 정해지고, 그중 둘(B·D)은 씬 값이 아니라 코드를 고쳐야 한다.
    /// </summary>
    public static class HaulTuningIO
    {
        const string CableCsv = "Tools/balance/cable_physics.csv";
        const string SnagCsv = "Tools/balance/debris_snag.csv";

        /// <summary>CSV 키 → CargoTow의 속성 경로. 내보내기와 불러오기가 이 표 하나를 같이 쓴다.</summary>
        sealed class Knob
        {
            public string Key;      // CSV에 적히는 이름
            public string Path;     // SerializedObject 경로
            public string Note;
        }

        static readonly Knob[] Cable =
        {
            new Knob { Key = "mode", Path = "mode",
                       Note = "Off / TensionOnly / RopeOnly / Both 중 하나" },
            new Knob { Key = "trailDistance", Path = "trailDistance",
                       Note = "짐이 없을 때의 줄 길이(m). 실제 길이는 max(이 값 · 짐 반지름 + leashClearance)" },
            new Knob { Key = "slotSpacing", Path = "slotSpacing",
                       Note = "슬롯 사이 간격(m). 짐 반지름 = 이 값 x sqrt(잔해 수)" },
            new Knob { Key = "followLerp", Path = "followLerp",
                       Note = "슬롯을 따라붙는 속도. 클수록 뻣뻣하다" },
            new Knob { Key = "outerLag", Path = "outerLag",
                       Note = "바깥쪽 잔해가 얼마나 더 늦게 따라오는가(0~1). 0이면 판때기처럼 보인다" },
            new Knob { Key = "leashClearance", Path = "leashClearance",
                       Note = "짐 앞 가장자리와 플레이어 사이 여유(m). 0이면 앞 가장자리에 딱 선다" },
            new Knob { Key = "rope.ringPoints", Path = "net.ringPoints",
                       Note = "그물 테두리 점 수. 비용이 여기에만 비례한다 — 잔해 수와 무관" },
            new Knob { Key = "rope.iterations", Path = "net.iterations",
                       Note = "구속 반복 횟수. 클수록 뻣뻣하다. 힘을 키우는 것과 달리 안 터진다" },
            new Knob { Key = "rope.gravity", Path = "net.gravity",
                       Note = "줄에 걸리는 중력(m/s^2). 바닥에 끌리는 그물이라 처짐보다 눌림에 가깝다" },
            new Knob { Key = "rope.damping", Path = "net.damping",
                       Note = "속도 감쇠(0~0.5). 0이면 영원히 출렁이고 크면 물리처럼 안 보인다" },
            new Knob { Key = "rope.hugStrength", Path = "net.hugStrength",
                       Note = "테두리가 짐을 조이는 힘. 클수록 착 붙는다" },
            new Knob { Key = "rope.floorY", Path = "net.floorY",
                       Note = "바닥 높이(m). 줄이 이 밑으로 안 내려간다" },
            new Knob { Key = "rope.pullToHand", Path = "net.pullToHand",
                       Note = "손에 가장 가까운 점을 손 쪽으로 당기는 정도(0~1). 1이면 고리가 창처럼 찌그러진다" },
            new Knob { Key = "tension.leashLength", Path = "tension.leashLength",
                       Note = "장력의 기본 줄 길이(m). 실제로는 CargoTow가 짐 크기에 맞춰 덮어쓴다" },
            new Knob { Key = "tension.drag", Path = "tension.drag",
                       Note = "공기/바닥 저항. 클수록 빨리 멎는다" },
            new Knob { Key = "tension.restoring", Path = "tension.restoring",
                       Note = "짐을 등 뒤로 되돌리려는 힘(m/s^2). 0이면 옆에 매달린 채 끌려온다" },
            new Knob { Key = "tension.inextensible", Path = "tension.inextensible",
                       Note = "줄이 팽팽할 때 반지름 방향 속도를 죽이는 비율(0~1). 1이면 안 늘어나는 줄" },
            new Knob { Key = "tension.inertiaPerUnit", Path = "tension.inertiaPerUnit",
                       Note = "끌고 있는 단위당 관성 증가. 많이 끌수록 늦게 반응한다" },
        };

        static readonly Knob[] Snag =
        {
            new Knob { Key = "snag.mode", Path = "snag.mode",
                       Note = "Pass / Block / DropOff / Snap / Slow 중 하나" },
            new Knob { Key = "snag.graceSeconds", Path = "snag.graceSeconds",
                       Note = "이만큼 연속으로 걸려야 DropOff/Snap이 터진다(초). 0이면 스치기만 해도 터진다" },
            new Knob { Key = "snag.slowFactor", Path = "snag.slowFactor",
                       Note = "Slow에서 걸려 있는 동안의 속도 배율(0.1~1)" },
            new Knob { Key = "snag.radiusScale", Path = "snag.radiusScale",
                       Note = "판정에 쓸 잔해 반경 배율(0.2~2). 작게 하면 스칠 때는 안 걸린다" },
        };

        static string Full(string rel) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", rel);

        static SerializedObject Open()
        {
            var tow = Object.FindFirstObjectByType<Collection.CargoTow>();
            if (tow != null) return new SerializedObject(tow);

            Debug.LogError("[HaulTuningIO] 씬에서 CargoTow를 못 찾았다. " +
                           "Main.unity가 열려 있는지 확인할 것.");
            return null;
        }

        /// <summary>
        /// 값 하나를 사람이 읽을 문자열로. 열거형은 숫자가 아니라 <b>이름</b>으로 적는다 —
        /// "3"이 무엇인지 CSV만 보고는 알 수 없다.
        /// </summary>
        static string Read(SerializedObject so, Knob k)
        {
            SerializedProperty p = so.FindProperty(k.Path);
            if (p == null) return "";

            switch (p.propertyType)
            {
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
                         ? p.enumNames[p.enumValueIndex] : p.enumValueIndex.ToString();
                case SerializedPropertyType.Integer:
                    return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return p.boolValue ? "TRUE" : "FALSE";
                default:
                    return p.floatValue.ToString("0.####", CultureInfo.InvariantCulture);
            }
        }

        /// <returns>반영했으면 true. 값이 이상하면 경고를 남기고 false</returns>
        static bool Write(SerializedObject so, Knob k, string raw)
        {
            SerializedProperty p = so.FindProperty(k.Path);
            if (p == null)
            {
                Debug.LogWarning($"[HaulTuningIO] '{k.Key}'의 자리({k.Path})를 못 찾았다.");
                return false;
            }
            raw = raw.Trim();

            if (p.propertyType == SerializedPropertyType.Enum)
            {
                int at = System.Array.IndexOf(p.enumNames, raw);
                if (at < 0)
                {
                    Debug.LogWarning($"[HaulTuningIO] '{k.Key}'의 값 '{raw}'는 없는 이름이다. " +
                                     $"쓸 수 있는 것: {string.Join(" / ", p.enumNames)}");
                    return false;
                }
                p.enumValueIndex = at;
                return true;
            }

            if (p.propertyType == SerializedPropertyType.Integer)
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                {
                    Debug.LogWarning($"[HaulTuningIO] '{k.Key}'의 값을 정수로 못 읽었다: '{raw}'");
                    return false;
                }
                p.intValue = i;
                return true;
            }

            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                Debug.LogWarning($"[HaulTuningIO] '{k.Key}'의 값을 숫자로 못 읽었다: '{raw}'");
                return false;
            }
            p.floatValue = v;
            return true;
        }

        // ── 내보내기 ────────────────────────────────────────────────────────

        [MenuItem("Demo/12. 견인 튜닝 CSV 내보내기", priority = 62)]
        public static void Export()
        {
            SerializedObject so = Open();
            if (so == null) return;

            WriteOne(so, CableCsv, Cable, Head(CableCsv));
            WriteOne(so, SnagCsv, Snag, Head(SnagCsv));
            AssetDatabase.Refresh();
            Debug.Log($"[HaulTuningIO] {CableCsv} / {SnagCsv} 에 내보냈다. " +
                      "고친 뒤 'Demo/12b'로 되돌린다.");
        }

        /// <summary>
        /// 기존 파일의 <c>#</c> 주석을 그대로 살린다.
        ///
        /// 주석에 <b>실측 근거</b>가 들어 있다 — 내보낼 때마다 덮어쓰면 기획자가 적어 둔
        /// 판단 근거와 내가 적어 둔 측정값이 같이 날아간다. 숫자만 갈아 끼운다.
        /// </summary>
        static string Head(string rel)
        {
            string path = Full(rel);
            if (!File.Exists(path)) return "";

            var sb = new StringBuilder();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("#") || line.Trim().Length == 0) sb.AppendLine(line);
                else break;
            }
            return sb.ToString();
        }

        static void WriteOne(SerializedObject so, string rel, Knob[] table, string head)
        {
            var sb = new StringBuilder();
            sb.Append(head);
            sb.AppendLine("key,value,설명");
            foreach (Knob k in table)
                sb.AppendLine(Csv.Row(k.Key, Read(so, k), k.Note));

            string path = Full(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), Csv.ExcelSafe);
        }

        // ── 불러오기 ────────────────────────────────────────────────────────

        [MenuItem("Demo/12b. 견인 튜닝 CSV 불러오기", priority = 63)]
        public static void Import()
        {
            SerializedObject so = Open();
            if (so == null) return;

            var byKey = new Dictionary<string, Knob>();
            foreach (Knob k in Cable) byKey[k.Key] = k;
            foreach (Knob k in Snag) byKey[k.Key] = k;

            int applied = 0, missing = 0;
            var unknown = new List<string>();

            foreach (string rel in new[] { CableCsv, SnagCsv })
            {
                string path = Full(rel);
                if (!File.Exists(path))
                {
                    Debug.LogError($"[HaulTuningIO] {rel} 가 없다. 먼저 'Demo/12'를 실행할 것.");
                    missing++;
                    continue;
                }

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("key,")) continue;

                    string[] cell = line.Split(',');
                    if (cell.Length < 2) continue;

                    string key = cell[0].Trim();
                    if (!byKey.TryGetValue(key, out Knob knob)) { unknown.Add(key); continue; }
                    if (Write(so, knob, cell[1])) applied++;
                }
            }
            if (missing > 0 && applied == 0) return;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(so.targetObject);

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

            if (unknown.Count > 0)
                Debug.LogWarning($"[HaulTuningIO] 모르는 키 {unknown.Count}개는 건너뛰었다: " +
                                 string.Join(", ", unknown));
            Debug.LogWarning($"[HaulTuningIO] {applied}개 값을 반영하고 씬을 저장했다. " +
                             "값은 여전히 [제안·미승인]이다.");
        }
    }
}
