using System.Collections.Generic;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 파괴 단계에 맞는 그림을 필드의 상자에 입힌다 [2026-10-08].
    ///
    /// <b>왜 MaterialPropertyBlock이 아닌가.</b> 흔히 쓰는 손이지만 <b>텍스처</b>를 그걸로
    /// 바꾸면 SRP 배칭이 깨져 오브젝트마다 드로우콜이 하나씩 붙는다. 동시 구조물이 60이고
    /// 드로우콜 예산이 100인데 잔해 0개짜리 씬이 이미 82~84를 쓴다 (CLAUDE.md 성능 예산) —
    /// 여기서 60을 더 쓸 수는 없다.
    ///
    /// 대신 <b>텍스처 하나당 머티리얼 하나</b>를 만들어 돌려 쓴다. 같은 종류·같은 단계는
    /// 같은 머티리얼을 공유하므로 묶여서 그려진다. 종류 여섯에 단계 셋이면 최대 열여덟 개다.
    ///
    /// <b>원본 머티리얼을 복제하지 않는다.</b> 복제하면 회색 틴트까지 따라와 그림이 어두워진다
    /// (<see cref="WallMaterials"/>에서 같은 이유로 셰이더만 가져다 쓴다).
    /// </summary>
    public static class StageSkin
    {
        static readonly Dictionary<Texture2D, Material> s_cache = new Dictionary<Texture2D, Material>();
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// <paramref name="tex"/>를 입힌 공유 머티리얼. 같은 텍스처면 같은 것을 돌려준다.
        /// <paramref name="like"/>는 셰이더를 맞출 본보기다 — 파괴 화면만 다른 셰이더를 쓰면
        /// 그 화면에서만 조명이 달라 보인다.
        /// </summary>
        public static Material For(Texture2D tex, Material like)
        {
            if (tex == null) return like;
            if (s_cache.TryGetValue(tex, out Material found) && found != null) return found;

            Shader sh = like != null ? like.shader : Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(sh) { name = "Stage:" + tex.name };

            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, Color.white);
            if (mat.HasProperty(BaseMapId)) mat.SetTexture(BaseMapId, tex);
            else mat.mainTexture = tex;

            s_cache[tex] = mat;
            return mat;
        }

        /// <summary>구역을 다시 지을 때 거둔다. 안 거두면 플레이 모드를 드나들 때 새는 것이 쌓인다.</summary>
        public static void Clear()
        {
            foreach (Material m in s_cache.Values)
                if (m != null) Object.Destroy(m);
            s_cache.Clear();
        }
    }
}
