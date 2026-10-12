using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// <see cref="Destructible"/>의 <b>겉모습</b> 쪽 — 어느 메시에 어떤 그림을 입히는가.
    ///
    /// <b>왜 갈랐나.</b> 한 파일이 300줄을 넘었다 (CLAUDE.md 규칙 3). 가른 자리는
    /// '파괴 상태 머신'과 '그 상태의 그림'의 사이다. 둘을 잇는 것은
    /// <see cref="Destructible.ApplySkin"/> 호출 한 줄뿐이라, 상태를 고칠 때
    /// 셰이더 프로퍼티를 읽을 일이 없고 그 반대도 마찬가지다.
    /// </summary>
    public sealed partial class Destructible
    {
        // 단계마다 하나씩. 하나로 묶어 캐시하면 켜져 있지 않은 메시에 그림을 입히게 된다.
        Renderer skinIntact, skinDamaged, skinRubble;
        Material baseIntact, baseDamaged, baseRubble;

        /// <summary>온전 메시의 UV가 네 칸으로 접혀 있는가. 접혀 있으면 한 장짜리 그림을 못 입힌다.</summary>
        bool intactAtlassed;

        /// <summary>
        /// 지금 단계의 그림을 상자에 입힌다 [2026-10-08].
        ///
        /// <b>단계용 그림이 없으면 아무것도 안 한다.</b> 다섯 종류 중 뽑힌 것부터 차례로
        /// 켜지게 두는 편이 낫다 — 전부 모일 때까지 기다리면 아무것도 안 보인다
        /// (<see cref="WallMaterials.Facade"/>와 같은 태도다).
        ///
        /// <b>그 단계의 메시에 입힌다</b> [고침 2026-10-08]. 전에는 렌더러를 처음 한 번만
        /// 찾아 캐시했는데, 그때 켜져 있던 것은 늘 Intact였다. 그래서 파손·잔해 그림이
        /// <b>꺼져 있는 Intact 메시</b>에 붙고, 화면에 보이는 메시는 민짜로 남았다 —
        /// 실측에서 Damaged 상태의 보이는 메시가 <c>M_TEMP_Steel</c>이었다.
        /// 종당 세 장씩 뽑아 둔 단계 그림이 온전 단계 말고는 한 번도 안 보이고 있었다.
        /// </summary>
        void ApplySkin(State next)
        {
            if (Data == null) return;

            Texture2D tex = Data.FacadeForState(next);
            if (tex == null) return;

            Renderer r = next == State.Damaged ? skinDamaged
                       : next == State.Rubble ? skinRubble : skinIntact;
            Material baseMat = next == State.Damaged ? baseDamaged
                             : next == State.Rubble ? baseRubble : baseIntact;

            // 그 단계의 메시가 없으면 온전 쪽으로 물러선다 — 아무것도 안 하는 것보다 낫다.
            //
            // <b>UV가 접혀 있으면 물러서지 않는다</b> [2026-10-12]. 온전 메시는 네 면
            // 아틀라스를 읽도록 UV가 칸으로 접혀 있어서, 거기에 한 장짜리 파손 그림을 입히면
            // 네 벽에 그 그림의 <b>사분면 하나씩</b>이 붙는다. 그대로 두는 편이 덜 틀린다.
            if (r == null && (next == State.Intact || !intactAtlassed)) { r = skinIntact; baseMat = baseIntact; }
            if (r == null) return;

            Material mat = StageSkin.For(tex, baseMat);
            if (mat != null && r.sharedMaterial != mat) r.sharedMaterial = mat;
        }

    }
}
