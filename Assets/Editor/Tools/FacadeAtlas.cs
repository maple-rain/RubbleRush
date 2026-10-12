using System.IO;
using System.Text;
using Demo.Data;
using Demo.Destruction;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 종류마다 <b>네 면을 한 장에</b> 모아 굽는다 [지시 2026-10-12].
    ///
    /// <b>왜 필요한가.</b> 필드의 건물은 기본 <c>Cube</c>(서브메시 하나·머티리얼 하나)라
    /// 그림 한 장이 네 벽 전부에 붙는다. 그 한 장이 늘 정면도였으므로 <b>어느 쪽에서 봐도
    /// 입구가 보였고</b>, 플레이어가 정면인 줄 알고 측면에서 E를 눌렀다 —
    /// 그리고 파괴 화면은 (맞게) 측면을 보여 줬다. 기획자가 세 번 적은 증상의 정체다.
    ///
    /// 면마다 머티리얼을 주면 드로우콜이 건물 수만큼 늘어 예산을 넘긴다 (CLAUDE.md 100).
    /// 네 장을 한 장에 모으고 UV를 메시에 구우면 머티리얼은 종류당 하나로 남는다 —
    /// 자리 계산은 <see cref="FaceSkin.Cell"/> 하나가 갖고, 굽는 쪽과 읽는 쪽이 그것을 같이 본다.
    ///
    /// <b>원본 PNG를 디스크에서 직접 읽는다.</b> 임포트된 텍스처는 DXT1로 압축돼 있고
    /// <c>isReadable</c>도 꺼져 있어서 픽셀을 못 읽는다. 임포트 설정을 잠깐 바꿨다 되돌리는
    /// 것보다, 파일을 그대로 읽는 쪽이 짧고 원본에 손을 안 댄다.
    ///
    /// <b>안 하는 것</b>: 파손·잔해 그림과 파괴 화면의 조각 판은 그대로 둔다. 파괴 화면은
    /// 벽 한 면이 화면을 가득 채우는 자리라, 거기서는 원본 한 장(1024)이 아틀라스 칸보다 낫다.
    /// </summary>
    public static class FacadeAtlas
    {
        public const string OutFolder = "Assets/_Project/Art/Facades/Atlas";

        /// <summary>
        /// 한 벽이 화면에서 차지하는 폭보다 칸이 <b>몇 배까지 작아도 되는가</b> [제안·미승인].
        ///
        /// 1이면 화면 픽셀과 텍셀이 1:1이고, 2면 두 배로 늘어나 뭉갠다. 1.5는 그 사이에서
        /// 2의 거듭제곱 한 단계를 아끼는 자리다 — 이 값을 1로 올리면 t5·t6의 아틀라스가
        /// 2048이 되어 텍스처가 9MB 더 는다. <b>기획자가 정할 값이라 여기 하나로 모아 둔다</b>
        /// (CLAUDE.md 규칙 7).
        /// </summary>
        const float StretchAllowed = 1.5f;

        /// <summary>카메라를 못 재는 경우의 칸 크기. 재서 정하는 것이 원칙이고 이것은 비상구다.</summary>
        const int FallbackCell = 512;

        const int MinCell = 256;
        const int MaxCell = 1024;

        [MenuItem("Demo/25. 건물 네 면 아틀라스 굽기", priority = 125)]
        public static void Bake() => Debug.Log(Run(true));

        [MenuItem("Demo/26. 네 면 아틀라스 점검", priority = 126)]
        public static void Check() => Debug.Log(Run(false));

        static string Run(bool write)
        {
            var sb = new StringBuilder();
            sb.AppendLine(write ? "네 면 아틀라스 굽기" : "네 면 아틀라스 점검 (쓰지 않음)");
            sb.AppendLine();

            BackdropView view = BackdropView.Measure();
            if (!view.Valid)
                sb.AppendLine("  카메라를 못 쟀다 (Main.unity를 열어야 한다) — 칸을 " + FallbackCell + "로 둔다");

            if (write && !Directory.Exists(OutFolder)) Directory.CreateDirectory(OutFolder);

            sb.AppendLine(string.Format("  {0,-16} {1,-11} {2,-9} {3,-9} {4}",
                                        "종류", "벽 폭(화면)", "칸", "늘어남", "결과"));
            int baked = 0, failed = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.facadeFront == null) continue;

                float wide = view.Valid ? data.colliderSize.x * view.PixelsPerMeter : 0f;
                int cell = CellFor(wide);
                string result = write ? Write(data, cell) : "굽지 않음";

                if (result == null) { baked++; result = "T_" + data.id + "_atlas.png"; }
                else if (write) failed++;

                sb.AppendLine(string.Format("  {0,-16} {1,8:0}px {2,7}px {3,8:0.00}배  {4}",
                                            data.id, wide, cell, cell > 0 ? wide / cell : 0f, result));
            }

            if (write) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }

            sb.AppendLine();
            if (write) sb.AppendLine("  구운 것 " + baked + "종 · 못 구운 것 " + failed + "종");
            sb.AppendLine("  칸 = 2의 거듭제곱으로 올린 (벽 폭 / " + StretchAllowed + "배), "
                          + MinCell + "~" + MaxCell + " 사이");
            sb.AppendLine("  안 재는 것: 실제 드로우콜 · 실기 메모리 · 밉맵 번짐 (플레이 모드와 폰이 있어야 안다)");
            return sb.ToString();
        }

        /// <summary>그 벽에 필요한 칸 크기. <b>고르는 것이 아니라 카메라에서 끌어낸다.</b></summary>
        static int CellFor(float screenWide)
        {
            if (screenWide <= 1f) return FallbackCell;
            int need = Mathf.CeilToInt(screenWide / StretchAllowed);
            return Mathf.Clamp(Mathf.NextPowerOfTwo(need), MinCell, MaxCell);
        }

        /// <summary>한 종류를 굽는다. null이면 성공, 아니면 그 이유로 못 구웠다.</summary>
        static string Write(StructureData data, int cell)
        {
            // 면 순서는 StructureFace의 상수 순서다. 빠진 면은 정면으로 물러선다 —
            // 비워 두면 그 벽만 검게 비어서, 틀린 그림보다 알아보기 어렵다.
            var faces = new[] { data.facadeFront, data.facadeRight, data.facadeBack, data.facadeLeft };
            var atlas = new Texture2D(cell * 2, cell * 2, TextureFormat.RGBA32, false);

            for (int f = 0; f < StructureFace.Count; f++)
            {
                Texture2D tex = faces[f] != null ? faces[f] : data.facadeFront;
                string path = AssetDatabase.GetAssetPath(tex);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    Object.DestroyImmediate(atlas);
                    return StructureFace.Name(f) + " 그림을 디스크에서 못 찾음";
                }

                var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(raw, File.ReadAllBytes(path)))
                {
                    Object.DestroyImmediate(raw);
                    Object.DestroyImmediate(atlas);
                    return Path.GetFileName(path) + " 를 못 읽음";
                }

                if (raw.width != raw.height)
                {
                    int w = raw.width, h = raw.height;
                    Object.DestroyImmediate(raw);
                    Object.DestroyImmediate(atlas);
                    return string.Format("{0}이 {1}x{2} — 정사각형이어야 한다", StructureFace.Name(f), w, h);
                }

                Color32[] px = Shrink(raw.GetPixels32(), raw.width, cell);
                Object.DestroyImmediate(raw);

                Rect c = FaceSkin.Cell(f);
                atlas.SetPixels32(Mathf.RoundToInt(c.x * cell * 2), Mathf.RoundToInt(c.y * cell * 2),
                                  cell, cell, px);
            }

            atlas.Apply();
            string outPath = OutFolder + "/T_" + data.id + "_atlas.png";
            File.WriteAllBytes(outPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
            Settle(outPath, cell * 2);

            var baked = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (baked == null) return "구웠는데 임포트가 안 됨 — " + outPath;

            var so = new SerializedObject(data);
            SerializedProperty slot = so.FindProperty("facadeAtlas");
            if (slot == null) return "StructureData에 facadeAtlas 칸이 없다";
            slot.objectReferenceValue = baked;
            so.ApplyModifiedProperties();
            return null;
        }

        /// <summary>
        /// 정사각 그림을 <b>정수 배로</b> 줄인다. 원본이 1024이고 칸이 256·512·1024뿐이라
        /// 배수가 언제나 2의 거듭제곱이고, 그래서 상자 평균 하나면 된다 —
        /// 보간을 쓰면 격자가 반 칸 어긋나 가로줄이 생긴다.
        /// </summary>
        static Color32[] Shrink(Color32[] src, int size, int target)
        {
            int k = size / Mathf.Max(1, target);
            if (k <= 1 || size % target != 0) return src;

            var dst = new Color32[target * target];
            int n = k * k;
            for (int y = 0; y < target; y++)
                for (int x = 0; x < target; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < k; j++)
                        for (int i = 0; i < k; i++)
                        {
                            Color32 c = src[(y * k + j) * size + (x * k + i)];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    dst[y * target + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            return dst;
        }

        /// <summary>
        /// 구운 것의 임포트 설정. <b>여기서 정한다</b> — FacadeImportSettings는 사람이 떨군
        /// 파사드를 1024로 묶는데, 아틀라스는 네 장이 든 한 장이라 그 규칙이 맞지 않는다.
        /// <c>Clamp</c>가 특히 중요하다: <c>Repeat</c>이면 보간이 칸 밖을 표본할 때 반대편 면을 끌어온다.
        /// </summary>
        static void Settle(string path, int size)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;

            imp.textureType = TextureImporterType.Default;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = true;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.maxTextureSize = size;
            imp.SaveAndReimport();
        }
    }
}
