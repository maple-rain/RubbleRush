using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 화면의 벽이 쓰는 머티리얼 두 장 [분리 2026-10-01].
    ///
    /// <b>한 벌을 재사용한다.</b> 파괴 화면은 한 번에 하나뿐이라 인스턴스가 하나면 충분하고,
    /// 들어갈 때마다 만들면 나갈 때마다 버려야 하는 쓰레기가 된다 (CLAUDE.md 규칙 6).
    ///
    /// <see cref="DemolitionSession"/>에서 떼어냈다 — 세션은 '한 판의 진행'을 보는 곳이지
    /// 셰이더 프로퍼티를 아는 곳이 아니다.
    /// </summary>
    public sealed class WallMaterials
    {
        static readonly int BaseMapId   = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId     = Shader.PropertyToID("_Color");

        Material innerMat;
        Material facadeMat;

        /// <summary>벽의 속살. 그림 없는 콘크리트 한 색이다 — 파사드를 뒤에도 붙이면 창문이 겹쳐 보인다.</summary>
        public Material Inner(Color wallInnerColor)
        {
            if (innerMat != null) return innerMat;

            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
            innerMat = new Material(sh) { name = "WallInner (instance)" };
            if (innerMat.HasProperty(BaseColorId)) innerMat.SetColor(BaseColorId, wallInnerColor);
            if (innerMat.HasProperty(ColorId))     innerMat.SetColor(ColorId, wallInnerColor);
            return innerMat;
        }

        /// <summary>
        /// 그 면의 그림을 입힌 머티리얼. <b>그림이 아직 없으면 건물 머티리얼을 그대로 돌려준다</b> —
        /// 5종을 한꺼번에 뽑지 못하므로, 뽑힌 종부터 차례로 켜지게 두는 편이 낫다.
        ///
        /// 건물 머티리얼을 <b>복제하지 않고 셰이더만</b> 가져다 새로 만든다. 복제하면 회색 틴트까지
        /// 따라와서 그림이 그만큼 어두워진다. 셰이더를 맞추는 이유는 따로 있다 — 파괴 화면만
        /// 다른 셰이더를 쓰면 그 화면에서만 조명이 달라 보인다.
        /// </summary>
        public Material Facade(StructureData data, int face, Material fallback)
        {
            Texture2D tex = data != null ? data.Facade(face) : null;
            if (tex == null) return fallback;

            if (facadeMat == null)
            {
                Shader sh = fallback != null ? fallback.shader
                                             : Shader.Find("Universal Render Pipeline/Lit");
                facadeMat = new Material(sh) { name = "FacadeProxy (instance)" };
            }

            if (facadeMat.HasProperty(BaseColorId)) facadeMat.SetColor(BaseColorId, Color.white);
            if (facadeMat.HasProperty(ColorId))     facadeMat.SetColor(ColorId, Color.white);

            if (facadeMat.HasProperty(BaseMapId)) facadeMat.SetTexture(BaseMapId, tex);
            else facadeMat.mainTexture = tex;

            return facadeMat;
        }

        /// <summary>만든 것을 거둔다. 세션이 사라질 때 부른다.</summary>
        public void Dispose()
        {
            if (facadeMat != null) Object.Destroy(facadeMat);
            facadeMat = null;
            if (innerMat != null) Object.Destroy(innerMat);
            innerMat = null;
        }
    }
}
