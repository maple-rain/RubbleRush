using Demo.Collection;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// VARCO 에셋이 오기 전에 루프를 돌리기 위한 임시 프리미티브와 데이터를 만든다.
    ///
    /// <b>여기 들어가는 수치는 전부 docs/GAME_DESIGN.md §12의 [제안·미승인] 초안이다.</b>
    /// AI가 새로 지어낸 값이 아니라, 이미 "승인받지 않음"으로 문서에 올라 있는 값을 그대로 옮긴 것이다.
    /// 에셋 이름에 _TEMP가 붙어 있고 실행할 때마다 경고가 뜬다 — 조용히 확정값이 되지 않게.
    ///
    /// VARCO FBX가 오면 StructureData의 mesh 3필드만 갈아끼우면 된다. 코드는 바뀌지 않는다.
    /// </summary>
    public static partial class PlaceholderAssets
    {
        const string DataRoot = "Assets/_Project/Data";
        const string PrefabRoot = "Assets/_Project/Prefabs";
        const string MatRoot = "Assets/_Project/Art/Materials";

        [MenuItem("Demo/1. 임시 에셋 생성", priority = 10)]
        public static void CreateAll()
        {
            EnsureFolders();

            Material concrete = MakeMaterial("M_TEMP_Concrete", new Color(0.62f, 0.60f, 0.57f));
            Material steel = MakeMaterial("M_TEMP_Steel", new Color(0.45f, 0.47f, 0.52f));
            Material rubbleMat = MakeMaterial("M_TEMP_Rubble", new Color(0.38f, 0.36f, 0.34f));
            Material voidMat = MakeMaterial("M_TEMP_Hole", new Color(0.05f, 0.06f, 0.09f));

            // ── 5종 × 구멍 단계 1:1 [확정 2026-09-22] ────────────────────────────────
            // 잔해 반경이 곧 "어느 구멍 단계에서 열리는가"다. 각 종류의 잔해는 자기 단계 반경보다
            // 작고 앞 단계 반경보다는 커야 한다. 대응표는 MakeConfig의 holeLevels 주석에 있다.
            // 시간·잔해수·산란은 GAME_DESIGN §12의 [제안·미승인] 초안에서 출발했다.
            //
            // 크기는 지시를 세 번 거쳐 지금 값이 됐다 [전부 2026-09-22].
            //  ① "큰 건물이 갑자기 없어진다"     → 캐릭터를 줄이고 건물 X·Z를 넓혔다
            //  ② "높이를 줄이고 층을 늘리고 넓이를 늘린다" → 층당 1.5m로 통일, 코어 9m 6층
            //  ③ "건물보다 잔해가 더 크다"       → 잔해 사다리를 약 4배 줄였다
            //  ④ "잔해가 구멍에 안 들어간다"      → 구멍 사다리에 1.75를 곱하고(최종 3.5)
            //                                     잔해를 다시 10% 줄였다
            //
            // 부수는 건물은 전부 2층 이상이다 [지시]. 1층이면 "아래층을 부수면 위층이 내려앉는다"가
            // 한 번도 일어나지 않는다. 그래서 제일 작은 것의 높이를 0.3 → 1.6으로 올렸다.
            // 그 제일 작은 것(t1_weak)이 <b>튜토리얼 구조물</b>이다 [확정 2026-09-22] —
            // 제일 작고(2.4×1.6×1.8) 제일 빠르며(0.8초 · Quick) 2층이라,
            // "부수면 위층이 내려앉는다"를 가장 싼 비용으로 한 번 가르치고 지나간다.
            //
            // 타격 길이는 두 갈래다 [지시]. Q=짧다, T=오래 걸린다.
            // 정확한 초는 breakTime이 갖고, 이 갈래는 플레이어가 종류 단위로 기억하는 축이다.
            const StructureData.BreakClass Q = StructureData.BreakClass.Quick;
            const StructureData.BreakClass T = StructureData.BreakClass.Tough;

            // ── 5종은 이제 <b>파괴 난이도 단계</b>다 [확정 2026-09-22] ──────────────────
            //
            // 원래 판자·기둥·벽면은 <b>건물을 조립할 부품</b>이었다. 그런데 이것들을 각각
            // 통째로 하나의 건물로 만들면서 그 전제가 사라졌다 — 부품으로 건물을 짓는 대신
            // 이미 지어진 건물을 종류별로 두게 됐다.
            // 그래서 축을 바꾼다: <b>얼마나 쉽게 부숴지는 건물인가</b>.
            //
            // 이름은 [제안·미승인]이다. 한 줄이니 마음에 안 들면 그 자리에서 바꾸면 된다.
            // 기둥은 원기둥에서 상자로 바꿨다 — 부품일 때는 기둥 모양이 맞았지만
            // 건물이 된 지금은 폭 1.6m 원기둥이 건물로 읽히지 않는다.
            var defs = new[] {
                new Def("T1Weak",   "t1_weak",   "취약 건물", 0, 0.8f,  2, 1.2f, 0.16f, false, 0f,
                        new Vector3(2.4f, 1.6f, 1.8f), PrimitiveType.Cube, concrete, 2, Q),
                new Def("T2Light",  "t2_light",  "경량 건물", 1, 1.5f,  4, 1.8f, 0.32f, false, 0f,
                        new Vector3(5.5f, 3.0f, 1.0f), PrimitiveType.Cube, concrete, 2, Q),
                new Def("T3Normal", "t3_normal", "일반 건물", 2, 4.0f, 12, 3.5f, 0.50f, true,  2.5f,
                        new Vector3(3.5f, 3.0f, 3.5f), PrimitiveType.Cube, steel,    2, T),
                new Def("T4Heavy",  "t4_heavy",  "중량 건물", 3, 5.5f, 20, 4.2f, 0.77f, true,  2.5f,
                        new Vector3(6.0f, 4.5f, 6.0f), PrimitiveType.Cube, steel,    3, T),
                new Def("T5Solid",  "t5_solid",  "견고 건물", 4, 8.0f, 32, 8.0f, 1.17f, true,  3.0f,
                        new Vector3(12.0f, 9.0f, 12.0f), PrimitiveType.Cube, steel,   6, T),
            };

            var made = new System.Collections.Generic.List<StructureData>();
            foreach (Def d in defs)
            {
                GameObject mi = MakePrimitive("P_TEMP_" + d.Asset + "_Intact",  d.Shape, d.Size, d.Mat);
                GameObject md = MakePrimitive("P_TEMP_" + d.Asset + "_Damaged", d.Shape,
                        new Vector3(d.Size.x, d.Size.y * 0.75f, d.Size.z), d.Mat);
                GameObject mr = MakePrimitive("P_TEMP_" + d.Asset + "_Rubble",  PrimitiveType.Cube,
                        new Vector3(d.Size.x * 0.9f, 0.4f, Mathf.Max(d.Size.z, 1f)), rubbleMat);

                made.Add(MakeStructure("SD_TEMP_" + d.Asset, d.Id, d.Display, d.Tier,
                    mi, md, mr, d.BreakTime, d.DebrisCount, d.Scatter, sizeClass: 1,
                    debrisRadius: d.DebrisRadius, isSupport: d.IsSupport, stun: d.Stun,
                    collider: d.Size, floors: d.Floors, cls: d.Class));
            }

            MakeConfig();
            MakeZone(made);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.LogWarning("[PlaceholderAssets] 임시 에셋을 만들었다. " +
                             "수치는 GAME_DESIGN §12의 [제안·미승인] 초안이며 기획자 승인값이 아니다. " +
                             "값을 확정하면 _TEMP 접미사를 떼고 문서에 등급을 올릴 것.\n" +
                             "종류 이름이 부품(판자/벽면/기둥/철골/코어)에서 난이도 단계로 바뀌었다 " +
                             "[지시 2026-09-22]. 예전 이름의 에셋은 이제 아무도 참조하지 않으니 " +
                             "지워도 된다 — SD_TEMP_Plank / Wall / Pillar / Frame / Core 와 " +
                             "P_TEMP_ 로 시작하는 같은 이름의 프리팹들.");
        }

        /// <summary>임시 구조물 한 종의 정의. 생성기 안에서만 쓴다.</summary>
        sealed class Def
        {
            public readonly string Asset, Id, Display;
            public readonly int Tier, DebrisCount, Floors;
            public readonly StructureData.BreakClass Class;
            public readonly float BreakTime, Scatter, DebrisRadius, Stun;
            public readonly bool IsSupport;
            public readonly Vector3 Size;
            public readonly PrimitiveType Shape;
            public readonly Material Mat;

            public Def(string asset, string id, string display, int tier, float breakTime,
                       int debrisCount, float scatter, float debrisRadius, bool isSupport,
                       float stun, Vector3 size, PrimitiveType shape, Material mat, int floors,
                       StructureData.BreakClass cls)
            {
                Asset = asset; Id = id; Display = display; Tier = tier;
                BreakTime = breakTime; DebrisCount = debrisCount; Scatter = scatter;
                DebrisRadius = debrisRadius; IsSupport = isSupport; Stun = stun;
                Size = size; Shape = shape; Mat = mat; Floors = floors; Class = cls;
            }
        }

        // ── 만들기 도우미 ────────────────────────────────────────────────────────

        static void EnsureFolders()
        {
            string[] paths = {
                DataRoot + "/Structures", DataRoot + "/Zones", DataRoot + "/Config",
                PrefabRoot + "/Structures", PrefabRoot + "/VFX", MatRoot,
            };
            foreach (string p in paths)
            {
                if (AssetDatabase.IsValidFolder(p)) continue;
                string parent = System.IO.Path.GetDirectoryName(p).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(p));
            }
        }

        static Material MakeMaterial(string name, Color color)
        {
            string path = $"{MatRoot}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);

            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject MakePrimitive(string name, PrimitiveType type, Vector3 size, Material mat)
        {
            var temp = GameObject.CreatePrimitive(type);
            temp.name = name;

            // 프리미티브에 딸려 오는 콜라이더는 버린다 —
            // 콜라이더의 유일한 출처는 StructureData.colliderSize다 (CLAUDE.md 규칙 4).
            Object.DestroyImmediate(temp.GetComponent<Collider>());

            // 실린더는 기본 높이가 2라 절반으로 맞춘다
            Vector3 scale = type == PrimitiveType.Cylinder
                ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
            temp.transform.localScale = scale;

            // 원점을 바닥에 맞춘다. 배치 좌표가 바닥 기준이어야 레벨 편집이 직관적이다.
            var visual = new GameObject(name);
            temp.transform.SetParent(visual.transform, false);
            temp.transform.localPosition = new Vector3(0f, size.y * 0.5f, 0f);

            temp.GetComponent<MeshRenderer>().sharedMaterial = mat;

            string path = $"{PrefabRoot}/Structures/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(visual, path);
            Object.DestroyImmediate(visual);
            return prefab;
        }

        static void MakeDebrisPrefab(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "P_TEMP_Debris";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.5f;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<Debris>();

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/Structures/P_TEMP_Debris.prefab");
            Object.DestroyImmediate(go);
        }

        static void MakeHolePrefab(Material mat)
        {
            var go = new GameObject("P_TEMP_Hole");
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(disc.GetComponent<Collider>());
            disc.transform.SetParent(go.transform, false);
            disc.transform.localScale = new Vector3(1f, 0.02f, 1f);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<Hole>();

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/VFX/P_TEMP_Hole.prefab");
            Object.DestroyImmediate(go);
        }

        static StructureData MakeStructure(string asset, string id, string display, int tier,
            GameObject i, GameObject d, GameObject r, float breakTime, int debrisCount,
            float scatter, int sizeClass, float debrisRadius, bool isSupport, float stun,
            Vector3 collider, int floors, StructureData.BreakClass cls)
        {
            var so = ScriptableObject.CreateInstance<StructureData>();
            so.id = id;
            so.displayName = display;
            so.tier = tier;
            so.meshIntact = i;
            so.meshDamaged = d;
            so.meshRubble = r;
            so.breakTime = breakTime;
            so.debrisCount = debrisCount;
            so.debrisScatterRadius = scatter;
            so.debrisSizeClass = sizeClass;
            so.debrisRadius = debrisRadius;
            so.isSupport = isSupport;
            so.collapseStunSeconds = stun;
            so.colliderSize = collider;
            so.floors = floors;
            so.breakClass = cls;

            AssetDatabase.CreateAsset(so, $"{DataRoot}/Structures/{asset}.asset");
            return so;
        }

    }
}
