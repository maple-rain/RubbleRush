using Demo.Core;
using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 파괴 화면 동안 플레이어 본체를 숨긴다 [지시 2026-09-22].
    ///
    /// 카메라가 부수는 층에 바짝 붙어 있어서, 로봇이 화면 한가운데를 막고 선다.
    /// 부수기가 끝나 필드로 돌아오면 다시 보인다.
    ///
    /// <b>예외 — 팔 리그</b> [지시 2026-09-28]. <see cref="StrikeArmRig"/>가 로봇을
    /// 카메라 옆으로 옮겨 놓았으면 숨기지 않는다. 그때는 몸통이 카메라 뒤에 있어서
    /// 화면을 막지 않고, 주먹을 뻗는 동안에만 팔이 프레임에 들어온다.
    ///
    /// <b>렌더러만 끈다.</b> 오브젝트를 끄면 CharacterController와 Debris 등록까지 같이 죽고,
    /// 켤 때 Awake가 다시 돌아 상태가 초기화된다.
    ///
    /// 이벤트 구독 대신 상태를 보는 이유는 순서 때문이다 —
    /// <see cref="GameModeController.Instance"/>가 이 컴포넌트의 OnEnable 시점에
    /// 아직 없을 수 있고, 그러면 한 번도 구독하지 못한 채 조용히 죽는다.
    /// 값이 바뀔 때만 렌더러를 건드리므로 매 프레임 비용은 비교 한 번이다.
    /// </summary>
    [DefaultExecutionOrder(200)]     // StrikeArmRig(100)가 자리를 잡은 뒤에 판단한다
    public sealed class PlayerVisibility : MonoBehaviour
    {
        Renderer[] parts;
        StrikeArmRig rig;
        bool hidden;

        void Awake()
        {
            parts = GetComponentsInChildren<Renderer>(true);
            rig = GetComponent<StrikeArmRig>();
        }

        /// <summary>
        /// <b>LateUpdate인 이유.</b> <see cref="StrikeArmRig"/>가 LateUpdate에서 로봇을
        /// 카메라 옆으로 옮긴다. 그보다 먼저 판단하면 한 프레임 낡은 값을 보게 되고,
        /// 파괴 화면을 빠져나오는 순간 <b>카메라 옆에 박힌 로봇이 한 프레임 번쩍인다.</b>
        /// </summary>
        void LateUpdate()
        {
            GameModeController mode = GameModeController.Instance;

            // 팔 리그가 붙어 있으면 보여 준다 — 몸통은 카메라 뒤라 어차피 안 보이고,
            // 주먹을 뻗는 동안에만 아래팔이 프레임을 지난다. 전환 중(아직 안 붙었을 때)은
            // 로봇이 필드 자리에서 날아오는 것이 보이므로 그대로 숨긴다.
            bool wantHidden = mode != null && !mode.IsField
                              && !(rig != null && rig.Attached);

            if (wantHidden == hidden) return;
            hidden = wantHidden;

            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].enabled = !hidden;
        }

        void OnDisable()
        {
            // 풀이나 씬 재생성으로 꺼질 때 숨은 채로 남지 않게 한다.
            if (!hidden || parts == null) return;
            hidden = false;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].enabled = true;
        }
    }
}
