using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 텍스처 임포터 설정을 일괄 적용한다 — 512 강제, 압축 (TECH_SPEC §6).
    ///
    /// <b>아직 구현되지 않았다.</b> 지금은 <see cref="StructureImporter"/>가 자기가 처리하는
    /// 텍스처에만 설정을 건다. 외부에서 들여온 것(CFXR 24MB, 폰트 등)은 손대지 않고 있다.
    ///
    /// CLAUDE.md 성능 예산: 기본 512 · 주요 오브젝트만 1024 · AAB 200MB · WebGL 100MB.
    /// 용량의 대부분이 텍스처에서 나오므로(벽면 1종 실측: 지오메트리 0.20MB vs 텍스처 3.51MB)
    /// 이 툴이 사실상 용량 관리의 본체다. D5 첫 빌드 전에는 있어야 한다.
    /// </summary>
    public static class TexturePresetApplier
    {
        [MenuItem("Demo/도구/텍스처 프리셋 일괄 적용 (미구현)", priority = 100)]
        public static void ApplyAll()
        {
            Debug.LogWarning("[TexturePresetApplier] 아직 구현되지 않았다 (TECH_SPEC §6). " +
                             "선택 폴더의 모든 텍스처에 maxSize·압축·밉맵을 강제하는 툴이 필요하다.");
            // TODO: 선택된 폴더를 훑어 TextureImporter를 일괄 수정.
            //       Android는 ASTC, WebGL은 DXT/ETC2 — 플랫폼별 오버라이드까지 건드려야 한다.
        }
    }
}
