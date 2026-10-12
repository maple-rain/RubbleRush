using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// <c>Art/Facades/</c>에 들어오는 그림에 임포트 설정을 자동으로 건다 [지시 2026-09-23].
    ///
    /// 메뉴로 만들지 않고 <see cref="AssetPostprocessor"/>로 둔 이유는, 파사드가
    /// <b>종당 4장씩 20장</b> 들어오고 앞으로도 계속 다시 뽑힐 것이기 때문이다.
    /// 사람이 기억해서 눌러야 하는 단계는 언젠가 빠진다 (CLAUDE.md 규칙 8).
    ///
    /// 고치는 것은 셋이고, 셋 다 <b>조용히 틀리는</b> 종류다.
    ///
    /// <list type="number">
    /// <item><b><c>npotScale</c></b> — 기본값 <c>ToNearest</c>는 거듭제곱이 아닌 텍스처를
    ///       말없이 늘린다. 512×384 파사드가 512×512가 되면서 세로로 33% 늘어나고,
    ///       창문이 길쭉해진다. 원본과 나란히 두지 않으면 알아채기 어렵다.
    ///       파사드는 <b>비율 자체가 정보</b>다 — 판의 가로세로에 맞춰 잘라 왔기 때문이다.</item>
    /// <item><b><c>wrapMode</c></b> — 기본 <c>Repeat</c>이면 보간이 가장자리 밖을 표본할 때
    ///       반대편을 끌어온다. 벽 왼쪽 끝에 오른쪽 끝 벽돌이 한 줄 붙는다.</item>
    /// <item><b><c>maxTextureSize</c></b> — 기본 2048은 모바일 예산 위반이다.
    ///       다만 파사드는 CLAUDE.md가 말하는 <b>"주요 오브젝트"</b>라 512가 아니라 1024다.
    ///       층 집중 카메라가 6층 건물의 <b>1층만</b> 잡기 때문에 화면에 보이는 것은
    ///       텍스처 세로의 1/6뿐이고, 512로 두면 그 조각을 6배로 늘려 뭉개진다.
    ///       1024면 3배까지 내려온다. 이보다 올리면 20장이 예산을 먹는다 —
    ///       숫자는 HANDOFF의 결정 대기 목록에 올려 두었다 [제안·미승인].</item>
    /// </list>
    ///
    /// 밉맵은 <b>켜 둔다.</b> 화면밖 표시 화살표와 달리 파사드는 필드에서 멀리 보이다가
    /// 파괴 화면에서 가까워지므로, 축소 단계가 실제로 쓰인다.
    /// </summary>
    public sealed class FacadeImportSettings : AssetPostprocessor
    {
        const string Folder = "/_Project/Art/Facades/";
        const int MaxSize = 1024;

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (path.IndexOf(Folder, System.StringComparison.Ordinal) < 0) return;

            // 구운 아틀라스는 건드리지 않는다 [2026-10-12]. 한 장에 네 면이 들어 있어
            // 필요한 크기가 종류마다 다르고, 그 크기는 카메라에서 끌어낸다 —
            // 여기서 1024로 묶으면 큰 건물의 벽이 2.7배로 늘어난다 (FacadeAtlas 참조).
            if (path.IndexOf("/Facades/Atlas/", System.StringComparison.Ordinal) >= 0) return;

            var imp = (TextureImporter)assetImporter;

            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = MaxSize;
            imp.mipmapEnabled = true;
            imp.textureType = TextureImporterType.Default;

            // 파사드에는 투명한 곳이 없다. 알파를 빼면 압축 포맷이 한 단계 가벼워진다.
            imp.alphaSource = TextureImporterAlphaSource.None;
        }
    }
}
