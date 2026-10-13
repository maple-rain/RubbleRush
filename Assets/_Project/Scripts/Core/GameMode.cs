using System;
using UnityEngine;

namespace Demo.Core
{
    /// <summary>
    /// 게임이 지금 어느 화면에 있는가 [지시 2026-09-22].
    ///
    /// 파괴를 필드에서 하지 않고 <b>전용 화면으로 분리</b>한다. 이건 연출 취향이 아니라
    /// 성능 전제다 — 조각 파쇄 + 연결 그래프는 벽 하나에 수십 개의 조각과 관계를 만든다.
    /// 필드의 구조물 60개에 그걸 동시에 돌리면 모바일에서 죽지만,
    /// <b>한 번에 하나만</b> 돌리면 감당할 수 있다.
    ///
    /// CLAUDE.md 규칙 2(씬은 하나다)는 깨지 않는다. 씬을 바꾸는 것이 아니라
    /// 같은 씬 안에서 카메라와 입력 소유권만 옮긴다.
    /// </summary>
    public enum Mode
    {
        /// <summary>이동·케이블·수집. 파괴는 '들어가기'만 할 수 있다.</summary>
        Field,

        /// <summary>건물 하나를 부수는 화면. 이동과 케이블이 멈춘다.</summary>
        Demolition,

        /// <summary>구역 종료. 입력을 받지 않는다.</summary>
        Ended,
    }

    /// <summary>
    /// 모드 전환의 단일 창구. 각 시스템이 서로를 끄고 켜는 대신 여기 하나를 본다.
    ///
    /// 시스템마다 "지금 파괴 중인가"를 따로 판단하게 두면 조건이 어긋나는 순간
    /// 이동은 멈췄는데 케이블은 그려지는 식의 상태가 생긴다. 소유권을 한 곳에 둔다.
    /// </summary>
    [DefaultExecutionOrder(-850)]
    public sealed class GameModeController : MonoBehaviour
    {
        public static GameModeController Instance { get; private set; }

        public Mode Current { get; private set; } = Mode.Field;

        /// <summary>지금 파괴 화면에서 부수고 있는 대상. Field에서는 null.</summary>
        public Destruction.Destructible Target { get; private set; }

        /// <summary>
        /// 상호작용한 <b>그 순간</b> 플레이어가 바라본 면 [지시 2026-09-23].
        /// <see cref="Destruction.StructureFace"/>의 상수다. 정면(0)에만 입구가 있다.
        ///
        /// 들어갈 때 한 번 정하고 <b>파괴가 끝날 때까지 바꾸지 않는다.</b> 매 프레임 다시
        /// 계산하면 카메라가 면을 넘나드는 순간 조각 판이 통째로 돌아가 버린다.
        /// 지시의 "상호작용을 한 순간 바라본 방향"이 문자 그대로 이 뜻이다.
        /// </summary>
        public int TargetFace { get; private set; }

        /// <summary>모드가 바뀔 때. (이전, 이후)</summary>
        public event Action<Mode, Mode> Changed;

        public bool IsField => Current == Mode.Field;
        public bool IsDemolition => Current == Mode.Demolition;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameModeController] 인스턴스가 둘이다. 나중 것을 끈다.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>파괴 화면으로 들어간다. 대상이 이미 잔해면 아무 일도 하지 않는다.</summary>
        public bool EnterDemolition(Destruction.Destructible target)
        {
            if (Current != Mode.Field) return false;
            if (target == null || !target.IsBreakable) return false;

            Target = target;

            // 바라본 면을 여기서 확정한다. 카메라도 조각 판도 이 값 하나를 읽는다 —
            // 각자 플레이어 방향을 계산하면 둘이 어긋나 텍스처가 비스듬히 잘린다.
            GameLoop loop = GameLoop.Instance;
            Vector3 eye = loop != null ? loop.PlayerPosition : target.transform.position + Vector3.back;
            Vector3 size = target.Data != null ? target.Data.colliderSize : Vector3.one;

            // <b>화면에 보이던 벽 중에서 고른다</b> [고침 2026-10-12].
            //
            // 필드 카메라는 수평각이 고정이라 한 건물에서 화면에 나오는 벽이 늘 두 면뿐이다.
            // 선 자리만 보고 고르면 <b>플레이어가 한 번도 본 적 없는 벽</b>이 나온다 —
            // 실측으로 24방향 중 51%가 그랬고, 정면은 구역 39채 전부에서 한 번도 안 보였다.
            // "정면에서 눌렀는데 측면이 나온다"의 정체다 (StructureFace.Facing 참조).
            //
            // <b>카메라의 트랜스폼이 아니라 FieldYaw를 읽는다.</b> 들어가는 순간의 트랜스폼은
            // 이전 파괴 화면에서 돌아오던 중일 수 있어서, 그걸 기준으로 삼으면 답이 흔들린다.
            TargetFace = Destruction.StructureFace.Facing(
                target.transform, eye, size, FieldLook());

            Set(Mode.Demolition);
            return true;
        }

        /// <summary>필드로 돌아온다. 대상이 부서졌든 도중에 나왔든 같은 경로다.</summary>
        public void ExitDemolition()
        {
            if (Current != Mode.Demolition) return;
            Target = null;
            Set(Mode.Field);
        }

        public void EndZone()
        {
            Target = null;
            Set(Mode.Ended);
        }

        FollowCamera view;

        /// <summary>필드 카메라가 보는 수평 방향. 없으면 0 — 그때는 예전 규칙으로 돌아간다.</summary>
        Vector3 FieldLook()
        {
            if (view == null) view = FindFirstObjectByType<FollowCamera>();
            if (view == null) return Vector3.zero;
            return Quaternion.Euler(0f, view.FieldYaw, 0f) * Vector3.forward;
        }

        void Set(Mode next)
        {
            if (Current == next) return;
            Mode prev = Current;
            Current = next;

            // 화면이 바뀌면 곡도 바뀐다 [2026-10-08]. 두 곡은 같은 조·같은 박자라
            // 겹쳐 넘어가도 어긋나지 않는다 (MusicPlayer가 crossfade한다).
            // 창구가 없으면 조용히 넘어간다 — 음악은 마지막에 붙는 것이다.
            Utils.MusicPlayer music = Utils.MusicPlayer.Current;
            if (music != null) music.SetDemolition(next == Mode.Demolition);

            Changed?.Invoke(prev, next);
        }
    }
}
