using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 끄는 동안 이어지는 소리 [지시 2026-10-14].
    ///
    /// <b>왜 한 방짜리가 아닌가.</b> 다른 소리는 사건이 날 때 한 번 울리면 되는데,
    /// 견인은 <b>상태</b>다 — 끄는 내내 이어져야 "지금 뭔가를 당기고 있다"가 읽힌다.
    /// <see cref="Utils.SfxPool"/>은 한 방짜리만 내므로 여기서 소스를 따로 들고 있는다.
    ///
    /// <b>무게가 소리로 보인다.</b> 많이 끌수록 커지고 낮아진다. 지금 이 게임은
    /// 견인량이 속도를 깎지 않기로 했으므로(감속 없음 [확정 2026-10-13]),
    /// <b>얼마나 끌고 있는지를 알려 줄 것이 소리밖에 없다.</b>
    ///
    /// <b>음원은 2026-10-08부터 있었다.</b> <c>SFX_TowLoop_01</c>이 폴더에 있는데
    /// 아무도 부르지 않아 한 번도 울리지 않았다 — 같은 처지가 넷이었다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class TowSound : MonoBehaviour
    {
        [SerializeField, Tooltip("끄는 동안 이어 낼 소리. 비면 아무 일도 안 한다")]
        AudioClip loop;

        [SerializeField, Range(0f, 1f), Tooltip("제일 많이 끌 때의 음량 [제안·미승인].\n" +
                 "끄는 내내 들리는 소리라 한 방짜리보다 작아야 한다 — 크면 금방 피곤해진다")]
        float maxVolume = 0.30f;

        [SerializeField, Range(1, 40), Tooltip("이만큼 끌면 음량이 최대가 된다(단위).\n" +
                 "제일 큰 건물이 32단위라 그 언저리에서 정했다")]
        int loudAt = 24;

        [SerializeField, Range(0f, 0.5f), Tooltip("많이 끌수록 음이 낮아지는 정도.\n" +
                 "0이면 무게가 안 읽히고, 크면 소리가 늘어진 것처럼 들린다")]
        float pitchDrop = 0.18f;

        [SerializeField, Range(1f, 20f), Tooltip("음량이 따라붙는 속도. 낮으면 끊길 때 뚝 끊긴다")]
        float follow = 6f;

        AudioSource src;
        CargoTow cargo;

        void Awake()
        {
            src = GetComponent<AudioSource>();
            cargo = GetComponentInParent<CargoTow>();
            if (cargo == null) cargo = FindFirstObjectByType<CargoTow>();

            src.clip = loop;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.spatialBlend = 0f;   // 내가 끄는 소리다 — 좌우로 치우치면 남의 일처럼 들린다
        }

        void Update()
        {
            if (src == null || loop == null) return;

            int units = cargo != null ? cargo.TotalUnits : 0;
            float load = Mathf.Clamp01(units / (float)Mathf.Max(1, loudAt));
            float want = units > 0 ? maxVolume * Mathf.Lerp(0.35f, 1f, load) : 0f;

            src.volume = Mathf.MoveTowards(src.volume, want, follow * maxVolume * Time.deltaTime);
            src.pitch = 1f - pitchDrop * load;

            // 들릴 때만 돌린다 — 0으로 틀어 두면 소스 하나를 계속 쓴다.
            if (src.volume > 0.001f && !src.isPlaying) src.Play();
            else if (src.volume <= 0.001f && src.isPlaying) src.Stop();
        }
    }
}
