using System.Collections.Generic;
using System.IO;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// VARCO 3D 출력을 게임에 쓸 수 있는 프리팹 + 머티리얼로 만든다 (TECH_SPEC §6).
    ///
    /// 2026-09-22에 벽면 1종을 손으로 처리하며 확인한 절차를 그대로 옮긴 것이다.
    /// 남은 14개(5종 × 3단계 − 완료분)에 같은 일이 반복되므로 CLAUDE.md 규칙 8에 따라 툴로 만든다.
    ///
    /// <b>전제 — 파일 이름 규약</b>
    /// <code>
    /// Assets/_Project/Art/Models/&lt;아무 폴더&gt;/
    ///   SM_&lt;id&gt;_&lt;stage&gt;.fbx              stage = intact | damaged | rubble
    ///   T_&lt;id&gt;_&lt;stage&gt;_BaseColor.png
    ///   T_&lt;id&gt;_&lt;stage&gt;_Normal.png       (없어도 된다)
    /// </code>
    /// `id`는 `StructureData.id`와 같아야 한다 (wall, pillar, …).
    ///
    /// <b>텍스처는 FBX가 아니라 GLB에 들어 있다.</b> FBX 익스포트에는 머티리얼조차 없어서
    /// URP 기본 Lit으로 폴백된다. GLB를 받아 BaseColor/Normal을 꺼내 위 이름으로 두는 것까지가
    /// 이 툴을 돌리기 전의 준비다.
    ///
    /// <b>높이는 설계값, 폭·깊이는 실측값이다.</b> 모델을 `StructureData.colliderSize.y`에 맞춰
    /// 스케일하고, 그 결과 나온 x·z를 다시 colliderSize에 써넣는다. 순환이 아니다 —
    /// y는 기획자가 정한 입력이고 x·z는 모델에서 나온 출력이다.
    /// </summary>
    public static class StructureImporter
    {
        const string ModelRoot = "Assets/_Project/Art/Models";
        const string PrefabDir = "Assets/_Project/Prefabs/Structures";

        [MenuItem("Demo/3. VARCO 모델 → 프리팹 일괄 변환", priority = 30)]
        public static void ImportAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { ModelRoot });
            if (guids.Length == 0)
            {
                Debug.LogWarning($"[StructureImporter] {ModelRoot} 아래에 모델이 없다.");
                return;
            }

            var report = new List<string>();
            int done = 0, skipped = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);

                if (!TryParseName(file, out string id, out string stage))
                {
                    report.Add($"건너뜀  {file} — 이름 규약(SM_<id>_<stage>)과 다르다");
                    skipped++;
                    continue;
                }

                StructureData sd = FindStructureData(id);
                if (sd == null)
                {
                    report.Add($"건너뜀  {file} — id '{id}'인 StructureData를 못 찾았다");
                    skipped++;
                    continue;
                }

                string msg = BuildOne(path, id, stage, sd);
                report.Add(msg);
                done++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StructureImporter] 변환 {done}건 · 건너뜀 {skipped}건\n  " +
                      string.Join("\n  ", report));
        }

        // ── 한 건 처리 ───────────────────────────────────────────────────────────

        static string BuildOne(string fbxPath, string id, string stage, StructureData sd)
        {
            string dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            string baseName = $"{id}_{stage}";

            Texture2D albedo = SetUpTexture($"{dir}/T_{baseName}_BaseColor.png", false, 1024);
            Texture2D normal = SetUpTexture($"{dir}/T_{baseName}_Normal.png", true, 512);

            Material mat = MakeMaterial($"{dir}/M_{baseName}.mat", albedo, normal);
            GameObject prefab = MakePrefab(fbxPath, baseName, mat, sd.colliderSize.y, out Vector3 size);

            AssignToSlot(sd, stage, prefab);

            // 높이는 유지하고 폭·깊이만 실측값으로 갱신한다
            if (stage == "intact")
            {
                sd.colliderSize = new Vector3(size.x, sd.colliderSize.y, size.z);
                EditorUtility.SetDirty(sd);
            }

            return $"OK  {baseName}  크기 {size:0.00}" +
                   (albedo == null ? "  (BaseColor 없음 — 무텍스처)" : "") +
                   (normal == null ? "  (Normal 없음)" : "");
        }

        static bool TryParseName(string file, out string id, out string stage)
        {
            id = stage = null;
            if (!file.StartsWith("SM_")) return false;

            string[] parts = file.Substring(3).Split('_');
            if (parts.Length < 2) return false;

            stage = parts[parts.Length - 1].ToLowerInvariant();
            if (stage != "intact" && stage != "damaged" && stage != "rubble") return false;

            id = string.Join("_", parts, 0, parts.Length - 1);
            return true;
        }

        static StructureData FindStructureData(string id)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var sd = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (sd != null && sd.id == id) return sd;
            }
            return null;
        }

        static void AssignToSlot(StructureData sd, string stage, GameObject prefab)
        {
            if (stage == "intact") sd.meshIntact = prefab;
            else if (stage == "damaged") sd.meshDamaged = prefab;
            else sd.meshRubble = prefab;
            EditorUtility.SetDirty(sd);
        }

        // ── 텍스처·머티리얼·프리팹 ───────────────────────────────────────────────

        static Texture2D SetUpTexture(string path, bool isNormal, int maxSize)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return null;

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.maxTextureSize = maxSize;              // CLAUDE.md 텍스처 예산
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
            return tex;
        }

        static Material MakeMaterial(string path, Texture2D albedo, Texture2D normal)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }

            // VARCO GLB의 material_0은 metallicFactor 0 / roughnessFactor 1 — 무광 콘크리트·철골
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject MakePrefab(string fbxPath, string baseName, Material mat,
                                     float targetHeight, out Vector3 size)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }

            // 생성 메시에 딸려 온 콜라이더는 버린다. 유일한 출처는 StructureData.colliderSize다
            // (CLAUDE.md 규칙 4 — MeshCollider 금지).
            foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            Bounds b = WorldBounds(inst);
            float scale = b.size.y > 0.0001f ? targetHeight / b.size.y : 1f;

            // PlaceholderAssets와 같은 규약: 루트는 identity, 메시는 자식, 원점은 바닥 중앙
            var root = new GameObject($"P_{baseName}");
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale = Vector3.one * scale;

            b = WorldBounds(root);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            b = WorldBounds(root);
            size = b.size;

            if (!Directory.Exists(PrefabDir)) Directory.CreateDirectory(PrefabDir);
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/P_{baseName}.prefab");
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>
        /// 렌더러 월드 바운드를 쓴다. 메시 로컬 바운드를 쓰면 안 된다 —
        /// VARCO FBX 루트에는 Z-up→Y-up 변환(rot 270,0,0)이 걸려 있어서 로컬로 보면 누워 보인다.
        /// </summary>
        static Bounds WorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }
    }
}
