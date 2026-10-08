using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// <c>Art/Textures/</c>에 들어오는 <b>반복해서 까는</b> 텍스처의 임포트 설정 [2026-10-08].
    /// 파사드 쪽의 <see cref="FacadeImportSettings"/>와 같은 이유로 메뉴가 아니라 후처리기다 —
    /// 사람이 기억해서 눌러야 하는 단계는 언젠가 빠진다 (CLAUDE.md 규칙 8).
    ///
    /// 파사드와 <b>정반대로</b> 맞춰야 하는 것이 둘 있다.
    ///
    /// <list type="number">
    /// <item><b><c>wrapMode</c>는 <c>Repeat</c>이어야 한다.</b> 파사드는 가장자리가 반대편을
    ///       끌어오면 안 되니 <c>Clamp</c>지만, 이쪽은 <b>이어 붙이는 것이 목적</b>이다.
    ///       <c>Clamp</c>면 타일 경계마다 같은 픽셀이 길게 번진 줄이 생긴다.</item>
    /// <item><b>이방성 필터링을 올린다.</b> 이게 이 파일의 핵심이다. 카메라가 지면을
    ///       <b>50도로 비스듬히</b> 내려다보므로, 화면의 한 픽셀이 지면에서는 세로로 길쭉한
    ///       영역을 덮는다. 기본값 <c>anisoLevel = 1</c>은 그 영역을 정사각형으로 어림잡아
    ///       <b>거리가 멀어질수록 지면을 뭉갠다</b>. 밉맵을 아무리 잘 만들어도 못 고친다 —
    ///       원인이 해상도가 아니라 표본 모양이기 때문이다.</item>
    /// </list>
    ///
    /// <b>안 건드리는 것</b>: 압축 포맷. 이 폴더의 그림은 전부 1024 정사각이라
    /// 기본 경로로도 압축이 먹는다 (NPOT이면 조용히 건너뛰므로 거기서만 문제가 된다).
    /// </summary>
    public sealed class GroundImportSettings : AssetPostprocessor
    {
        const string Folder = "/_Project/Art/Textures/";
        const int MaxSize = 1024;

        /// <summary>
        /// 비스듬히 보는 면에 쓰는 값. 16까지 올릴 수 있지만 모바일 GPU에서 대역을 먹는다.
        /// 8이면 50도에서 거의 끝까지 또렷하고, 4와 8의 차이는 20m쯤에서 보이기 시작한다.
        /// </summary>
        const int Aniso = 8;

        void OnPreprocessTexture()
        {
            if (assetPath.Replace('\\', '/').IndexOf(Folder, System.StringComparison.Ordinal) < 0)
                return;

            var imp = (TextureImporter)assetImporter;

            imp.textureType = TextureImporterType.Default;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.wrapMode = TextureWrapMode.Repeat;      // 이어 붙이는 것이 목적이다
            imp.filterMode = FilterMode.Trilinear;      // 밉맵 단계 사이를 섞는다. 경계선이 안 보인다
            imp.anisoLevel = Aniso;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = MaxSize;
            imp.alphaSource = TextureImporterAlphaSource.None;
        }
    }
}
