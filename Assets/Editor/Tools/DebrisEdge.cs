using System.IO;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 잔해 조각에 <b>검은 테두리</b>를 입히고 지면과 갈리게 어둡게 한다 [지시 2026-10-12].
    ///
    /// <b>왜 필요한가.</b> 실측하면 잔해와 지면이 사실상 같은 색이다 —
    /// <c>T_Rubble_Concrete</c> 평균 밝기 0.521, <c>T_Ground_Site</c> 0.485.
    /// 차이가 0.036, 전체 범위의 3.6%다. 크기 문제가 아니라 <b>대비가 0에 가까운</b> 문제다.
    ///
    /// <b>왜 기하가 아니라 그림인가.</b> 외곽선의 정석은 뒤집은 껍질을 한 겹 더 그리는 것인데,
    /// 그러면 조각마다 드로우콜이 하나씩 붙는다 — 동시 잔해 상한이 500이라 +500이고
    /// 예산은 100이다 (CLAUDE.md). 테를 <b>텍스처에 그려 두면</b> 드로우콜도 삼각형도 0이다.
    /// 기본 Cube는 여섯 면이 각각 UV 0~1을 쓰므로, 타일 한 장에 테를 그리면
    /// <b>여섯 면 전부</b>에 테가 생긴다 — 실루엣뿐 아니라 면과 면 사이 모서리에도 선이 선다.
    /// 뒤집은 껍질로는 실루엣밖에 못 딴다.
    ///
    /// <b>테 두께와 어둡기는 고르지 않고 잰다.</b> 두께는 "화면에서 몇 px로 보이게 할 것인가"에서,
    /// 어둡기는 "지면과 얼마나 벌릴 것인가"에서 거꾸로 끌어낸다.
    ///
    /// <b>안 하는 것</b>: 조각 크기별로 테 두께를 고정하는 것. 지금은 타일 한 장을 모든 크기가
    /// 공유하므로 테가 조각 크기에 비례해 굵어진다 (0.16m와 1.17m 사이 7.3배).
    /// 거슬리면 크기별 메시를 캐시해야 하는데, 먼저 구워 보고 재는 것이 순서다.
    /// </summary>
    public static class DebrisEdge
    {
        const string Source = "T_Rubble_Concrete";
        const string Ground = "T_Ground_Site";
        const string OutPath = "Assets/_Project/Art/Textures/T_Rubble_Edged.png";

        /// <summary>테두리가 화면에서 몇 px로 보이길 바라는가. 3px [지시 2026-10-12 더 진하게].</summary>
        const float EdgeScreenPixels = 3f;

        /// <summary>지면과 밝기를 얼마나 벌릴 것인가 (0~1) [제안·미승인]. 0.15면 한눈에 갈린다.</summary>
        const float WantedGap = 0.15f;

        [MenuItem("Demo/29. 잔해 테두리 굽기", priority = 129)]
        public static void Bake() => Debug.Log(Run(true));

        [MenuItem("Demo/30. 잔해 대비 점검", priority = 130)]
        public static void Check() => Debug.Log(Run(false));

        static string Run(bool write)
        {
            var sb = new StringBuilder();
            sb.AppendLine(write ? "잔해 테두리 굽기" : "잔해 대비 점검 (쓰지 않음)");
            sb.AppendLine();

            Texture2D rubble = Load(Source);
            Texture2D ground = Load(Ground);
            if (rubble == null) return sb.Append("  " + Source + " 를 못 읽었다.").ToString();

            float rubbleMean = Mean(rubble);
            float groundMean = ground != null ? Mean(ground) : 0.5f;
            sb.AppendLine(string.Format("  지금   잔해 {0:0.000} · 지면 {1:0.000} · 차이 {2:0.000}",
                                        rubbleMean, groundMean, Mathf.Abs(rubbleMean - groundMean)));

            // 어둡기: 지면보다 WantedGap만큼 어두워지는 배율을 거꾸로 푼다.
            float target = Mathf.Clamp01(groundMean - WantedGap);
            float darken = rubbleMean > 0.01f ? target / rubbleMean : 1f;

            // 테 두께: 화면에서 EdgeScreenPixels로 보이려면 타일에서 몇 텍셀인가.
            BackdropView view = BackdropView.Measure();
            float side = TypicalSide();
            float onScreen = view.Valid ? side * view.PixelsPerMeter : 60f;
            float share = onScreen > 1f ? EdgeScreenPixels / onScreen : 0.04f;
            int edge = Mathf.Max(1, Mathf.RoundToInt(rubble.width * share));

            sb.AppendLine(string.Format("  목표   잔해 {0:0.000} (지면보다 {1:0.00} 어둡게) -> 배율 {2:0.00}",
                                        target, WantedGap, darken));
            sb.AppendLine(string.Format("  조각   대표 한 변 {0:0.00}m = 화면 {1:0}px · 테 {2:0.0}px -> 타일 {3}텍셀 ({4:0.0}%)",
                                        side, onScreen, EdgeScreenPixels, edge, share * 100f));

            if (!write)
            {
                Object.DestroyImmediate(rubble);
                if (ground != null) Object.DestroyImmediate(ground);
                sb.AppendLine();
                sb.AppendLine("  굽지 않았다. 'Demo/29. 잔해 테두리 굽기'로 적용한다.");
                return sb.ToString();
            }

            Color32[] px = rubble.GetPixels32();
            int w = rubble.width, hgt = rubble.height;
            Paint(px, w, hgt, edge, darken);

            var made = new Texture2D(w, hgt, TextureFormat.RGBA32, false);
            made.SetPixels32(px);
            made.Apply();
            File.WriteAllBytes(OutPath, made.EncodeToPNG());
            float after = Mean(made);
            Object.DestroyImmediate(made);
            Object.DestroyImmediate(rubble);
            if (ground != null) Object.DestroyImmediate(ground);

            AssetDatabase.ImportAsset(OutPath, ImportAssetOptions.ForceSynchronousImport);
            Settle(OutPath);

            var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(OutPath);
            if (baked == null) return sb.Append("  구웠는데 임포트가 안 됐다.").ToString();

            string where = Wire(baked);
            sb.AppendLine();
            sb.AppendLine("  구웠다  " + Path.GetFileName(OutPath) + " (" + w + "x" + hgt + ")");
            sb.AppendLine(string.Format("  결과   잔해 {0:0.000} · 지면 {1:0.000} · 차이 {2:0.000}",
                                        after, groundMean, Mathf.Abs(after - groundMean)));
            sb.AppendLine("  " + where);
            sb.AppendLine();
            sb.AppendLine("  안 재는 것: 실기 화면 · 밉맵에서 테가 흐려지는 정도 · 크기별 테 두께 차이");
            return sb.ToString();
        }

        /// <summary>
        /// 테를 칠하고 전체를 어둡게 한다. 테는 <b>가장자리로 갈수록</b> 짙어진다 —
        /// 딱 끊으면 축소 단계에서 점선처럼 끊겨 보인다.
        /// </summary>
        static void Paint(Color32[] px, int w, int h, int edge, float darken)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    float k = darken;
                    if (d < edge)
                    {
                        // 0 = 가장자리, 1 = 테 안쪽 끝. 가장자리에서 거의 검정까지 간다.
                        float t = edge > 1 ? (float)d / (edge - 1) : 0f;
                        k *= Mathf.Lerp(0.05f, 1f, t * t);
                    }
                    int i = y * w + x;
                    Color32 c = px[i];
                    px[i] = new Color32((byte)Mathf.Clamp(c.r * k, 0f, 255f),
                                        (byte)Mathf.Clamp(c.g * k, 0f, 255f),
                                        (byte)Mathf.Clamp(c.b * k, 0f, 255f), c.a);
                }
        }

        /// <summary>조각 수로 가중한 대표 한 변(m). 흔한 조각에 테 두께를 맞춘다.</summary>
        static float TypicalSide()
        {
            float sum = 0f, n = 0f;
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.facadeFront == null) continue;
                sum += data.debrisRadius * data.debrisCount;
                n += data.debrisCount;
            }
            return n > 0f ? sum / n : 0.5f;
        }

        /// <summary>잔해 프리팹이 쓰는 머티리얼의 그림을 바꾼다.</summary>
        static string Wire(Texture2D tex)
        {
            var spawner = Object.FindFirstObjectByType<Demo.Destruction.DebrisSpawner>();
            if (spawner == null) return "씬에 DebrisSpawner가 없다 — 머티리얼은 손으로 바꿔야 한다";

            var prefab = new SerializedObject(spawner).FindProperty("debrisPrefab").objectReferenceValue as GameObject;
            if (prefab == null) return "잔해 프리팹이 비어 있다";

            var r = prefab.GetComponentInChildren<Renderer>(true);
            if (r == null || r.sharedMaterial == null) return "잔해 프리팹에 머티리얼이 없다";

            Material mat = r.sharedMaterial;
            int id = Shader.PropertyToID("_BaseMap");
            if (mat.HasProperty(id)) mat.SetTexture(id, tex);
            else mat.mainTexture = tex;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return "머티리얼 " + mat.name + " 의 그림을 바꿨다";
        }

        static void Settle(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            imp.textureType = TextureImporterType.Default;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;      // 테가 반대편으로 새면 안 된다
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = true;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.maxTextureSize = 512;                  // 조각은 화면에서 작다
            imp.SaveAndReimport();
        }

        static float Mean(Texture2D t)
        {
            float sum = 0f;
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    Color c = t.GetPixelBilinear((x + 0.5f) / 64f, (y + 0.5f) / 64f);
                    sum += c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                }
            return sum / (64f * 64f);
        }

        static Texture2D Load(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != name) continue;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(t, File.ReadAllBytes(path))) return t;
            }
            return null;
        }
    }
}
