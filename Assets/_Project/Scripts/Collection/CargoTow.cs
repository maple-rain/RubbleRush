using System.Collections.Generic;
using Demo.Core;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 케이블에 딸려오는 잔해 뭉치. 플레이어에 붙인다.
    ///
    /// 물리로 끌지 않는다 — 잔해 수십 개에 Rigidbody를 달면 모바일에서 죽고,
    /// 줄에 매달린 물체는 물리로 하면 거의 항상 발작한다. 슬롯 위치로 부드럽게 따라오게만 한다.
    /// </summary>
    public sealed class CargoTow : MonoBehaviour
    {
        [SerializeField, Tooltip("뭉치가 플레이어 뒤로 떨어지는 거리(m)")]
        float trailDistance = 2.5f;

        [SerializeField, Tooltip("슬롯 사이 간격(m)")]
        float slotSpacing = 0.9f;

        [SerializeField, Tooltip("슬롯을 따라붙는 속도. 클수록 뻣뻣하다")]
        float followLerp = 8f;

        readonly List<Debris> towed = new List<Debris>(64);

        /// <summary>끌고 있는 총 단위. 이동 속도와 카메라 줌이 읽는다.</summary>
        public int TotalUnits { get; private set; }

        public IReadOnlyList<Debris> Towed => towed;
        public bool IsEmpty => towed.Count == 0;

        /// <summary>고리가 닫혀 포획된 잔해를 넘겨받는다. 견인 중에 또 둘러싸면 여기로 합쳐진다.</summary>
        public void Add(IReadOnlyList<Debris> captured)
        {
            for (int i = 0; i < captured.Count; i++)
            {
                Debris d = captured[i];
                if (d == null || d.IsTowed) continue;

                d.IsTowed = true;
                towed.Add(d);
                TotalUnits += d.SizeClass;
            }
        }

        /// <summary>
        /// 조각 하나를 화물에서 뺀다. 구멍이 삼킬 때 부른다.
        ///
        /// 일괄 반납이 아니라 개체 단위다 — 그물이 구멍 위를 지나가는 동안 들어가는 것만
        /// 차례로 빠지고, 구멍보다 큰 것은 그대로 남아 계속 끌려온다.
        /// </summary>
        public bool Detach(Debris d)
        {
            if (d == null) return false;
            int i = towed.IndexOf(d);
            if (i < 0) return false;

            towed.RemoveAt(i);
            TotalUnits -= d.SizeClass;
            d.IsTowed = false;
            return true;
        }

        /// <summary>끌고 있는 것 중 가장 큰 것의 반경. 구멍이 얼마나 커져야 하는지를 HUD가 보여줄 때 쓴다.</summary>
        public float LargestRadius
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < towed.Count; i++)
                    if (towed[i] != null && towed[i].SizeRadius > max) max = towed[i].SizeRadius;
                return max;
            }
        }

        /// <summary>
        /// 끌고 있던 것을 그 자리에 내려놓는다. 풀로 돌려보내지 않는다 —
        /// 잔해는 맵에 남아서 다시 주울 수 있어야 한다.
        /// </summary>
        /// <returns>내려놓은 총 단위</returns>
        public int DropAll()
        {
            int units = TotalUnits;
            for (int i = 0; i < towed.Count; i++)
                if (towed[i] != null) towed[i].IsTowed = false;

            towed.Clear();
            TotalUnits = 0;
            return units;
        }

        void LateUpdate()
        {
            if (towed.Count == 0) return;

            float lerp = 1f - Mathf.Exp(-followLerp * Time.deltaTime);   // 프레임레이트에 안 흔들리는 감쇠
            Vector3 anchor = transform.position - transform.forward * trailDistance;

            // 그물에 싸여 끌려오는 덩어리다 [지시 2026-09-22].
            //
            // 예전에는 뒤로 줄을 세웠는데, 그러면 꼬리처럼 보이고 개수가 늘수록 화면 밖으로 나갔다.
            // 대신 <b>해바라기 배열(피보나치 나선)</b>로 원판을 채운다 — 개수가 늘어도 반지름이
            // sqrt로만 자라고 간격이 고르다. 덩어리가 구멍 위를 지나가면 바깥쪽부터 빠진다.
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));   // 황금각 ≈ 137.5°

            for (int i = 0; i < towed.Count; i++)
            {
                Debris d = towed[i];
                if (d == null) continue;

                // 구멍이 당기는 중이면 놓아준다. 두 힘이 맞서면 잔해가 그 사이에서 떤다.
                if (d.IsBeingSucked) continue;

                float r = slotSpacing * Mathf.Sqrt(i + 0.5f);
                float a = i * golden;
                Vector3 slot = anchor + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                slot.y = d.transform.position.y;

                d.transform.position = Vector3.Lerp(d.transform.position, slot, lerp);
            }
        }
    }
}
