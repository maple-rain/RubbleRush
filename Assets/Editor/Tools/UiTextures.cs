using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// UI 텍스처를 불러오면서 임포트 설정까지 강제한다.
    ///
    /// 유니티의 기본 임포트 설정은 3D 표면용이다. 화면에 그대로 붙는 그림에 그걸 쓰면
    /// 두 가지가 어긋난다.
    ///
    /// <list type="bullet">
    /// <item><b>밉맵</b> — 화면 좌표에 1:1로 그리는 그림에는 축소 단계가 쓰이지 않는다.
    ///       용량만 33% 더 먹는다.</item>
    /// <item><b>Repeat 래핑</b> — 회전시켜 그리면 표본이 경계 밖으로 나가는데, Repeat이면
    ///       반대쪽 가장자리를 끌어와 화살표 꼬리에 잘린 조각이 붙는다. Clamp여야 한다.</item>
    /// </list>
    ///
    /// <paramref>alphaIsTransparency</paramref>는 <see cref="Tools/key_checkerboard.py"/>가
    /// 투명한 자리까지 같은 색으로 덮어 두므로 사실상 무해하지만, 나중에 다른 경로로 들어온
    /// 그림에도 이 함수가 쓰일 것이므로 같이 켜 둔다.
    ///
    /// 설정이 이미 맞으면 재임포트하지 않는다 — Demo/2b를 돌릴 때마다 텍스처를 다시 굽지 않게.
    /// </summary>
    public static class UiTextures
    {
        public const string ArrowOffscreen = "Assets/_Project/Art/UI/T_ArrowOffscreen.png";

        /// <summary>화면 밖 방향 표시용 화살표. 없으면 null과 함께 고치는 법을 찍는다.</summary>
        public static Texture2D LoadArrow() => Load(ArrowOffscreen, 256);

        public static Texture2D Load(string path, int maxSize)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogError(
                    $"[UiTextures] {path} 가 없다. 원본 JPG에서 다시 만들려면:\n" +
                    "  python Tools/key_checkerboard.py \"<화살표 원본>.jpg\" " +
                    $"\"{ArrowOffscreen}\"");
                return null;
            }

            if (AssetImporter.GetAtPath(path) is TextureImporter imp && NeedsFix(imp, maxSize))
            {
                imp.textureType = TextureImporterType.Default;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.maxTextureSize = maxSize;
                imp.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return tex;
        }

        static bool NeedsFix(TextureImporter i, int maxSize) =>
            !i.alphaIsTransparency || i.mipmapEnabled ||
            i.wrapMode != TextureWrapMode.Clamp || i.maxTextureSize != maxSize;
    }
}
