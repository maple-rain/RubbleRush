using System.Collections.Generic;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 잔해 크기 · 흩어짐 · 구멍 단계 · 케이블 길이를 <b>한 사다리로</b> 세운다 [지시 2026-10-12].
    ///
    /// 기획자가 말한 규칙은 둘이다:
    /// <list type="number">
    /// <item><b>단계마다 한 크기씩</b> — 1단계는 제일 작은 잔해만, 2단계는 그 다음까지.
    ///       튜토리얼 구역이라 그 사다리가 곧 설명이다.</item>
    /// <item><b>케이블도 같이 자란다</b> — t5까지 점진적으로 길어진다.</item>
    /// </list>
    ///
    /// <b>넷은 한 덩어리다.</b> 따로 정하면 반드시 어긋난다:
    /// 잔해가 커지면 구멍 문턱이 올라가고, 흩어짐이 넓어지면 고리 둘레가 길어진다.
    /// 그래서 <b>고르는 값은 아래 네 개뿐</b>이고 나머지 수십 개는 전부 거기서 나온다.
    ///
    /// <b>실측으로 드러난 것</b> (2026-10-12):
    /// 고치기 전 사다리는 1단계가 두 종류를 한꺼번에 받고, 2단계에서 네 종류가 더 들어와
    /// <b>3단계에 이미 전부</b> 들어갔다 — 4·5단계는 하는 일이 없었다.
    /// 케이블은 38.5m 고정이라 반지름 6.13m까지만 감쌌는데 <c>t5_solid</c>는 흩어짐이 8.0m라
    /// <b>처음부터 끝까지 2~3번 쪼개야</b> 했다.
    ///
    /// <b>안 하는 것</b>: 경험치 곡선. 칸이 늘면 직전 칸의 두 배로 이어 둘 뿐이다.
    /// CSV도 안 쓴다 — 적용 뒤 <c>Demo/4</c>로 내보내면 표가 따라온다.
    /// </summary>
    public static class HoleLadder
    {
        /// <summary>
        /// 제일 작은 조각의 반경(m).
        ///
        /// 0.16m(화면 19px)이 "작아서 안 보인다"는 지적에서 역산해 0.37m로 올렸고,
        /// <b>2026-10-14 지시로 다시 22% 올렸다</b> — 그래도 안 보인다는 판단이다.
        /// 화면에서 1m가 117px이므로 0.45m면 약 53px이다.
        ///
        /// <b>여기가 잔해 크기의 진짜 출처다.</b> StructureData에 손으로 써도
        /// 이 도구를 돌리면 이 값으로 다시 깔린다 — 바꾸려면 여기를 바꿔야 한다.
        /// </summary>
        const float FloorRadius = 0.45f;

        /// <summary>
        /// 제일 큰 조각의 반경(m) [지시 2026-10-12 +10%].
        ///
        /// <b>배율이 아니라 목표값이다.</b> 배율로 두면 도구를 두 번 돌릴 때 두 번 곱해진다 —
        /// 실제로 한 번 당했다(흩어짐이 1.98에서 3.27로 또 늘었다). 목표값이면 몇 번 돌려도 같다.
        /// </summary>
        const float TopRadius = 1.29f;

        // 흩어짐은 이 도구가 정하지 않는다 [고침 2026-10-13].
        //
        // 흩어짐의 상한은 건물 간격이 정한다 — 이웃까지 거리의 절반을 넘으면 두 건물의
        // 잔해 더미가 겹쳐서 고리 하나에 옆 건물 것까지 딸려 온다. 그 값은 구역 배치를
        // 보고 정해야 하므로, 여기서 비율로 밀면 반드시 어긋난다.
        // 이 도구는 이제 흩어짐을 읽어서 케이블 길이만 끌어낸다.

        /// <summary>
        /// 고리가 완벽한 원이 아니고 거기까지 걸어가야 하므로 두는 여유.
        ///
        /// <b>단계가 오를수록 줄어든다</b> [지시 2026-10-14]. 전에는 모든 단계에 20% 고정이었다.
        /// 1단계는 넉넉해서 처음 배우는 동안 고리가 잘 닫히고, 뒤로 갈수록 빠듯해져
        /// <b>"긴 로프" 업그레이드의 값어치가 올라간다</b>. 여유가 모자라도 막히지는 않는다 —
        /// 두세 번에 나눠 감으면 되고, 그게 느려지는 것이 곧 카드의 가치다.
        ///
        /// 두 값은 [제안·미승인 2026-10-14]. 사이는 단계 수로 고르게 나눈다.
        /// </summary>
        const float MarginFirst = 1.35f;   // 1단계 — 35% 여유
        const float MarginLast = 1.05f;    // 마지막 단계 — 5% 여유

        /// <summary>L단계(0부터)의 고리 여유. 칸이 하나뿐이면 첫 값을 쓴다.</summary>
        static float LoopMarginAt(int level, int levels)
        {
            if (levels <= 1) return MarginFirst;
            return Mathf.Lerp(MarginFirst, MarginLast, (float)level / (levels - 1));
        }

        sealed class Kind
        {
            public StructureData data;
            public float radius;
            public float scatter;
        }

        [MenuItem("Demo/31. 구멍·케이블 사다리 점검", priority = 131)]
        public static void Check() => Debug.Log(Report(false));

        [MenuItem("Demo/32. 구멍·케이블 사다리 다시 세우기", priority = 132)]
        public static void Apply() => Debug.Log(Report(true));

        public static string Report(bool write)
        {
            var sb = new StringBuilder();
            sb.AppendLine(write ? "사다리 다시 세우기" : "사다리 점검 (쓰지 않음)");
            sb.AppendLine();

            List<Kind> kinds = Gather();
            if (kinds.Count == 0) return sb.Append("  건물 데이터를 못 찾았다.").ToString();

            // ── 지금 ────────────────────────────────────────────────────────
            sb.AppendLine("  지금");
            sb.Append(Table(kinds, CurrentRadii(), CurrentCables()));

            // ── 규칙대로 ────────────────────────────────────────────────────
            List<Kind> want = Remap(kinds);
            float[] holes = Holes(want);
            float[] cables = Cables(want);

            sb.AppendLine();
            sb.AppendLine("  규칙대로  (잔해 " + FloorRadius + "~" + TopRadius
                          + "m · 고리 여유 x" + MarginFirst + " -> x" + MarginLast + ")");
            sb.Append(Table(want, holes, cables));

            if (!write)
            {
                sb.AppendLine();
                sb.AppendLine("  적용하려면 'Demo/32'. 그 뒤 'Demo/4'로 CSV를 내보내면 표가 따라온다.");
                return sb.ToString();
            }

            sb.AppendLine();
            sb.Append(Write(want, holes, cables));
            sb.AppendLine("  CSV는 안 썼다 — 'Demo/4. 밸런스 CSV 내보내기'로 표를 맞출 것.");
            sb.AppendLine("  안 보는 것: 경험치 곡선 · 한 단계에 머무는 시간 · 재미");
            return sb.ToString();
        }

        /// <summary>구역이 실제로 쓰는 종류만. 이름 바꾸기 전의 옛 에셋은 뺀다.</summary>
        static List<Kind> Gather()
        {
            var list = new List<Kind>();
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (d == null || d.facadeFront == null) continue;
                list.Add(new Kind { data = d, radius = d.debrisRadius, scatter = d.debrisScatterRadius });
            }
            list.Sort((a, b) => a.radius.CompareTo(b.radius));
            return list;
        }

        /// <summary>
        /// 크기를 <b>바닥부터 천장까지 같은 비율로</b> 다시 깐다.
        ///
        /// 균일 배율(전부 x2.1)을 쓰지 않는 이유는 흩어짐이 같이 커지기 때문이 아니라
        /// <b>바닥만 안 보이기 때문</b>이다 — 문제는 제일 작은 조각 하나이고, 천장은 멀쩡하다.
        /// 같은 비율로 깔면 "크기 차이로 들어갈지 읽힌다"는 설계가 유지되면서 바닥만 올라온다.
        /// </summary>
        static List<Kind> Remap(List<Kind> src)
        {
            var sizes = new List<float>();
            foreach (Kind k in src)
            {
                float r = Mathf.Round(k.radius * 1000f) / 1000f;
                if (!sizes.Contains(r)) sizes.Add(r);
            }
            sizes.Sort();

            int n = sizes.Count;
            float step = n > 1 ? Mathf.Pow(TopRadius / FloorRadius, 1f / (n - 1)) : 1f;

            var map = new Dictionary<float, float>();
            for (int i = 0; i < n; i++)
                map[sizes[i]] = Mathf.Round(FloorRadius * Mathf.Pow(step, i) * 100f) / 100f;

            var outList = new List<Kind>();
            foreach (Kind k in src)
            {
                float key = Mathf.Round(k.radius * 1000f) / 1000f;
                outList.Add(new Kind
                {
                    data = k.data,
                    radius = map[key],
                    scatter = k.scatter,          // 그대로 둔다 — 배치 간격이 정하는 값이다
                });
            }
            outList.Sort((a, b) => a.radius.CompareTo(b.radius));
            return outList;
        }

        static List<float> Distinct(List<Kind> kinds)
        {
            var s = new List<float>();
            foreach (Kind k in kinds) if (!s.Contains(k.radius)) s.Add(k.radius);
            s.Sort();
            return s;
        }

        /// <summary>L단계 반경 = (L번째 크기, L+1번째 크기]의 기하평균. 비율로 벌어지는 값이다.</summary>
        static float[] Holes(List<Kind> kinds)
        {
            List<float> s = Distinct(kinds);
            var r = new float[s.Count];
            for (int i = 0; i < s.Count; i++)
            {
                float v = i + 1 < s.Count ? Mathf.Sqrt(s[i] * s[i + 1]) : s[i] * 1.35f;
                r[i] = Mathf.Round(v * 100f) / 100f;
            }
            return r;
        }

        /// <summary>
        /// L단계 케이블 = 그 단계에서 <b>새로 열리는 종류</b>를 한 고리로 감쌀 둘레.
        /// 같은 크기가 둘이면 넓은 쪽(t4·t6)에 맞춘다. 단계가 오르며 줄어들 수는 없다.
        /// </summary>
        static float[] Cables(List<Kind> kinds)
        {
            List<float> s = Distinct(kinds);
            var c = new float[s.Count];
            float running = 0f;
            for (int i = 0; i < s.Count; i++)
            {
                float widest = 0f;
                foreach (Kind k in kinds)
                    if (Mathf.Approximately(k.radius, s[i])) widest = Mathf.Max(widest, k.scatter);

                float need = 2f * Mathf.PI * widest * LoopMarginAt(i, s.Count);
                running = Mathf.Max(running, need);
                c[i] = Mathf.Round(running * 10f) / 10f;
            }
            return c;
        }

        static string Table(List<Kind> kinds, float[] holes, float[] cables)
        {
            var sb = new StringBuilder();
            List<float> s = Distinct(kinds);
            sb.AppendLine(string.Format("    {0,-6} {1,-9} {2,-9} {3,-9} {4,-9} {5}",
                                        "단계", "잔해", "흩어짐", "구멍", "케이블", "새로 열리는 종류"));
            for (int i = 0; i < s.Count; i++)
            {
                string who = "";
                float widest = 0f;
                foreach (Kind k in kinds)
                    if (Mathf.Approximately(k.radius, s[i])) { who += k.data.id + " "; widest = Mathf.Max(widest, k.scatter); }

                string hole = holes != null && i < holes.Length ? holes[i].ToString("0.00") + "m" : "-";
                string cable = cables != null && i < cables.Length && cables[i] > 0f ? cables[i].ToString("0.0") + "m" : "-";
                sb.AppendLine(string.Format("    {0,-6} {1,7:0.00}m {2,7:0.00}m {3,9} {4,9}  {5}",
                                            (i + 1) + "단계", s[i], widest, hole, cable, who));
            }

            // 단계 수가 크기 수와 다르면 사다리가 성립하지 않는다 — 그것부터 보인다.
            if (holes != null && holes.Length != s.Count)
                sb.AppendLine("    구멍 단계가 " + holes.Length + "칸인데 크기는 " + s.Count + "가지다 — 어긋난다");
            return sb.ToString();
        }

        static GameConfig Config()
        {
            GameConfig cfg = Core.GameConfigRef.Current;
            if (cfg != null) return cfg;
            foreach (string guid in AssetDatabase.FindAssets("t:GameConfig"))
                return AssetDatabase.LoadAssetAtPath<GameConfig>(AssetDatabase.GUIDToAssetPath(guid));
            return null;
        }

        static float[] CurrentRadii()
        {
            GameConfig cfg = Config();
            if (cfg == null || cfg.holeLevels == null) return new float[0];
            var r = new float[cfg.holeLevels.Length];
            for (int i = 0; i < r.Length; i++) r[i] = cfg.holeLevels[i].radius;
            return r;
        }

        static float[] CurrentCables()
        {
            GameConfig cfg = Config();
            if (cfg == null || cfg.holeLevels == null) return new float[0];
            var r = new float[cfg.holeLevels.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = cfg.holeLevels[i].cableLength > 0f ? cfg.holeLevels[i].cableLength : cfg.cableMaxLength;
            return r;
        }

        static string Write(List<Kind> want, float[] holes, float[] cables)
        {
            var sb = new StringBuilder();
            foreach (Kind k in want)
            {
                var so = new SerializedObject(k.data);
                so.FindProperty("debrisRadius").floatValue = k.radius;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(k.data);
            }
            sb.AppendLine("  잔해 " + want.Count + "종의 반경을 썼다 (흩어짐은 배치가 정하므로 안 건드린다)");

            GameConfig cfg = Config();
            if (cfg == null) { sb.AppendLine("  GameConfig를 못 찾아 단계는 못 썼다"); return sb.ToString(); }

            var cso = new SerializedObject(cfg);
            var arr = cso.FindProperty("holeLevels");
            int was = arr.arraySize;

            // 경험치는 기획자 값이다. 칸이 늘면 <b>직전 칸의 두 배</b>로 이어 둘 뿐이다 —
            // 지금 들어 있는 8·16·32·64가 정확히 두 배씩이라 그 모양을 잇는 것이다 [제안·미승인].
            int lastXp = 0;
            for (int i = 0; i < was; i++)
            {
                int xp = arr.GetArrayElementAtIndex(i).FindPropertyRelative("xpToNext").intValue;
                if (xp > 0) lastXp = xp;
            }

            arr.arraySize = holes.Length;
            for (int i = 0; i < holes.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("radius").floatValue = holes[i];
                el.FindPropertyRelative("cableLength").floatValue = cables[i];

                var xpP = el.FindPropertyRelative("xpToNext");
                if (i == holes.Length - 1) xpP.intValue = 0;
                else if (i >= was || xpP.intValue <= 0) { lastXp = lastXp > 0 ? lastXp * 2 : 8; xpP.intValue = lastXp; }
                else lastXp = xpP.intValue;
            }
            cso.ApplyModifiedProperties();
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();

            sb.AppendLine("  구멍 단계 " + was + "칸 -> " + holes.Length + "칸 (반경 + 케이블 길이)");
            return sb.ToString();
        }
    }
}
