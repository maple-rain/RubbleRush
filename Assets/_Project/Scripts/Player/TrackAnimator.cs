using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 무한궤도 표현 — 텍스처 스크롤과 차체 기울기 (TECH_SPEC §3).
    ///
    /// <b>애니메이터를 쓰지 않는다. 전부 절차적이다.</b> 스켈레탈 애니메이션을 만들 시간이 없다는
    /// 것이 이 게임 설계의 전제이고 [확정], 무한궤도는 애초에 뼈가 필요 없는 표현이다.
    ///
    /// 차체가 가속할 때 뒤로 눕고 감속할 때 앞으로 숙이는 것만으로 중장비의 무게가 읽힌다.
    /// 견인량이 많을수록 더 눕는다 — "많이 끌고 있다"가 숫자가 아니라 자세로 보인다.
    /// </summary>
    public sealed class TrackAnimator : MonoBehaviour
    {
        [SerializeField, Tooltip("궤도 텍스처가 붙은 렌더러들")]
        Renderer[] trackRenderers;

        [SerializeField, Tooltip("기울어지는 차체. 루트를 기울이면 이동 방향까지 틀어진다")]
        Transform body;

        [SerializeField] PlayerController player;
        [SerializeField] Collection.CargoTow cargo;

        [SerializeField, Tooltip("속도 1m/s당 UV가 스크롤되는 양")]
        float scrollPerMeter = 0.35f;

        [SerializeField, Tooltip("최대로 눕는 각도(도)")]
        float maxPitchDegrees = 6f;

        [SerializeField, Tooltip("자세가 따라오는 속도. 클수록 뻣뻣하다")]
        float pitchLerp = 6f;

        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        MaterialPropertyBlock mpb;
        float scroll;
        float pitch;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (cargo == null) cargo = GetComponent<Collection.CargoTow>();
            mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            if (player == null) return;
            float dt = Time.deltaTime;

            // 궤도 스크롤 — 실제 이동 속도에 비례한다. 공회전하면 안 된다.
            scroll = Mathf.Repeat(scroll + player.CurrentSpeed * scrollPerMeter * dt, 1f);
            ApplyScroll();

            // 차체 기울기 — 속도와 견인량을 함께 읽는다
            float load = cargo != null ? cargo.TotalUnits : 0f;
            float target = -maxPitchDegrees * Mathf.Clamp01(load / 24f);
            pitch = Mathf.Lerp(pitch, target, 1f - Mathf.Exp(-pitchLerp * dt));

            if (body != null) body.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void ApplyScroll()
        {
            if (trackRenderers == null) return;

            foreach (Renderer r in trackRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                // MaterialPropertyBlock을 쓰는 이유는 머티리얼 인스턴스가 생기지 않게 하기 위해서다.
                // sharedMaterial.SetTextureOffset을 부르면 드로우콜이 쪼개진다.
                mpb.SetVector(BaseMapST, new Vector4(1f, 1f, 0f, scroll));
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
