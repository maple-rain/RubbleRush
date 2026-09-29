using System.Text;
using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 위험 설비 한 종의 정의 — 연료탱크 · 가스관 · 배전반.
    /// 우선순위 4 (D11). 지금은 ZoneData가 참조할 스키마만 확정해 둔다 —
    /// 나중에 필드를 추가하면 그때까지 손으로 채운 에셋을 다시 건드려야 한다.
    ///
    /// 수치는 기획자가 채운다.
    /// </summary>
    [CreateAssetMenu(menuName = "Demo/Hazard Data", fileName = "Hazard_")]
    public sealed class HazardData : ScriptableObject
    {
        [Header("식별")]
        public string id;
        public string displayName;

        [Header("메시 — 2단계")]
        public GameObject meshIntact;
        public GameObject meshDestroyed;

        [Header("파괴")]
        [Tooltip("홀드 소요 시간(초)")]
        public float breakTime;

        [Header("폭발")]
        [Tooltip("폭발 반경(m). 이 안의 구조물이 연쇄 파괴되고, 플레이어도 맞는다")]
        public float explosionRadius;

        [Tooltip("폭발에 맞았을 때 플레이어 정지 시간(초)")]
        public float explosionStunSeconds;

        [Tooltip("이 등급 이하의 구조물만 연쇄 파괴한다. 벽면 0 / 기둥 1")]
        public int chainDamageToTier;

        [Header("콜라이더")]
        public Vector3 colliderSize;

        /// <summary>미입력 필드를 찾는다. 전부 채워져 있으면 null.</summary>
        public string Validate()
        {
            var sb = new StringBuilder();

            if (string.IsNullOrWhiteSpace(id)) sb.Append("id, ");
            if (meshIntact == null) sb.Append("meshIntact, ");
            if (meshDestroyed == null) sb.Append("meshDestroyed, ");
            if (breakTime <= 0f) sb.Append("breakTime, ");
            if (explosionRadius <= 0f) sb.Append("explosionRadius, ");
            if (explosionStunSeconds <= 0f) sb.Append("explosionStunSeconds, ");
            if (colliderSize == Vector3.zero) sb.Append("colliderSize, ");

            if (sb.Length == 0) return null;
            sb.Length -= 2;
            return $"HazardData '{name}' 미입력: {sb}";
        }
    }
}
