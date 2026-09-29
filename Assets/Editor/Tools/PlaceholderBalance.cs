using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 임시 <see cref="GameConfig"/>와 <see cref="ZoneData"/>를 만든다.
    /// <see cref="PlaceholderAssets"/>에서 갈라져 나왔다 — 한 파일 300줄 상한(CLAUDE.md 규칙 3).
    ///
    /// 구조물 프리팹/데이터는 저쪽에, <b>밸런스에 해당하는 값</b>은 이쪽에 있다.
    /// 이 파일의 값이 곧 밸런스 CSV의 기본 행이 된다 (<see cref="BalanceCsvIO"/>).
    /// </summary>
    public static partial class PlaceholderAssets
    {
        static void MakeConfig()
        {
            var cfg = ScriptableObject.CreateInstance<GameConfig>();

            // §12 초안 [제안·미승인]
            cfg.moveSpeedBase = 6.0f;
            cfg.speedPenaltyPerUnit = 0.12f;
            cfg.moveSpeedMin = 2.5f;
            cfg.cableMaxLength = 38.5f;   // [지시 2026-09-22] 55에서 30% 줄였다
            cfg.cablePointInterval = 0.25f;
            cfg.holeSuctionMargin = 2.0f;
            cfg.holeSuctionSpeed = 7.0f;   // 이동 속도(6.0)보다 빨라야 끌려오는 느낌이 난다

            // TECH_SPEC §2 초안 [제안·미승인]
            cfg.hitStopSeconds = 0.08f;
            cfg.cameraZoomPerUnit = 0.02f;
            cfg.cameraZoomMax = 4f;

            // [지시 2026-09-22] 시점을 플레이어에게 더 가깝게. 초안 18 → 12 → 8.
            // 8로 한 번 더 내린 것은 같은 지시를 지키기 위해서다 — 캐릭터를 0.625배로 줄였으므로
            // 거리를 그대로 두면 화면상 로봇은 오히려 작아진다. 12 × 0.625 ≈ 7.5, 올림해서 8.
            // GAME_DESIGN §8의 트레이드오프를 기억할 것 — 가까이 갈수록 생성 메시의 결함과
            // 텍스처 해상도 한계가 드러난다. VARCO 에셋이 들어온 뒤 이 값을 다시 봐야 한다.
            cfg.cameraDistance = 8f;

            // 문서에 대응값이 없는 셋. 돌아가기만 하는 자리표시자이며 근거가 없다.
            cfg.breakerRange = 3.5f;
            cfg.structureDamagedAt01 = 0.5f;
            cfg.tapsPerSecond = 5f;   // 초당 5탭 가정 — 벽면(1.5s)이면 약 8탭
            cfg.cableMinLoopArea = 2.0f;

            // [지시 2026-09-22] 경험치 → 단계적 구멍 성장. 전부 자리표시자다.
            //
            // 단계 0의 반경 0.25는 의도적이다 — 판자 잔해(0.18)는 들어가고
            // 벽면 잔해(0.35)는 안 들어간다. 시작하자마자 크기 게이트가 눈에 보이고,
            // "판자부터 부숴 구멍을 키운 다음 벽으로 간다"는 순서가 규칙 하나에서 나온다.
            //
            // <b>미결</b> — "부수지 않고 통째로 밀어넣기"는 이 사다리로는 성립하지 않는다.
            // 잔해가 건물보다 크다는 지적을 고치면서 사다리를 약 4배 줄였고, 그 결과
            // 최종 구멍 2.0m가 제일 작은 구조물(판자 2.4m 폭)보다도 작아졌다.
            // 통째로 삼키려면 최종 단계를 따로 키우거나(예: 3.5), 그 기능을 접어야 한다.
            // 어느 쪽이든 기획자 결정이다 — HANDOFF §2 참조.
            cfg.xpPerDebrisUnit = 1;
            // [확정 2026-09-22] 최종 단계에서만 스스로 떠돈다. 플레이어는 조작하지 않는다.
            cfg.holeDriftSpeed = 1.2f;
            cfg.holeDriftRadius = 14f;

            // [지시 2026-09-22] "잔해가 구멍에 안 들어간다" — 최종 단계가 3.5가 되도록
            // 사다리 전체에 1.75를 곱했다. 동시에 잔해는 10% 줄였다.
            //
            // <b>이 조합은 5단계 × 5종의 1:1 대응을 깬다.</b> 실측값:
            //   L0 0.44 — 판자 0.16 · 벽면 0.32 둘 다 들어간다
            //   L1 0.79 — 기둥 0.50 · 철골 0.77 추가
            //   L2 1.23 — 코어 1.17 추가. 여기서 전부 들어간다
            //   L3 1.84 / L4 3.50 — 더 열리는 종류가 없다
            // 즉 의미 있는 단계가 5개에서 3개로 줄었다. 느슨하게 하려는 지시였으니
            // 의도한 결과일 수 있지만, 5단계를 되살리려면 잔해 간격을 넓혀야 한다 — 기획자 판단.
            cfg.holeLevels = new[] {
                new GameConfig.HoleLevel { radius = 0.44f, xpToNext =  8 },
                new GameConfig.HoleLevel { radius = 0.79f, xpToNext = 16 },
                new GameConfig.HoleLevel { radius = 1.23f, xpToNext = 32 },
                new GameConfig.HoleLevel { radius = 1.84f, xpToNext = 64 },
                new GameConfig.HoleLevel { radius = 3.50f, xpToNext =  0 },   // 최종
            };

            AssetDatabase.CreateAsset(cfg, $"{DataRoot}/Config/GameConfig_TEMP.asset");
        }

        static void MakeZone(System.Collections.Generic.List<StructureData> types)
        {
            var zone = ScriptableObject.CreateInstance<ZoneData>();
            zone.id = "zone_test";
            zone.displayName = "테스트 구역";
            zone.timeLimit = 360f;   // [확정 2026-09-22] 240 → 360. 모든 건물에 탭이 붙으면서 시간이 모자랐다

            // 구멍은 하나. 플레이어 시작 지점과 구조물 사이에 둔다.
            zone.holePosition = new Vector3(0f, 0f, -10f);

            // 작은 종류일수록 많이. 이 기본값은 밸런스 CSV로 덮어쓸 수 있다.
            var counts = new int[types.Count];
            var spreadMuls = new float[types.Count];
            var bandGaps = new float[types.Count];
            for (int ti = 0; ti < types.Count; ti++)
            {
                counts[ti] = Mathf.Max(2, 8 - ti);
                spreadMuls[ti] = DefaultSpreadMul;
                bandGaps[ti] = DefaultBandGap;
            }

            Layout(zone, types, counts, spreadMuls, bandGaps);

            // 할당량은 앞쪽 띠만으로도 닿을 수 있게 낮게 잡는다. 임시값이다.
            zone.quota = 60;

            AssetDatabase.CreateAsset(zone, $"{DataRoot}/Zones/Zone_TEMP_Test.asset");
        }

        /// <summary>좌우 간격 = 건물 폭 × 이 값. 기본값이며 CSV로 덮어쓴다.</summary>
        public const float DefaultSpreadMul = 2.4f;

        /// <summary>띠와 띠 사이의 여백(m). 기본값이며 CSV로 덮어쓴다.</summary>
        public const float DefaultBandGap = 6f;

        /// <summary>
        /// 종류별로 띠를 이뤄 배치한다. 작은 것이 앞(구멍 쪽), 큰 것이 뒤 —
        /// "지금 내 구멍으로 처리 가능한 종류는 어디까지인가"가 거리로도 읽히게 한다.
        ///
        /// 간격은 실제 크기에서 유도한다 [지시 2026-09-22로 건물이 커진 뒤].
        /// 고정값으로 두면 12m 건물이 17m 간격에 서서 서로 시야를 가리고,
        /// 파괴 화면 카메라가 물러설 자리도 없어진다.
        ///
        /// <b>여기가 배치 규칙의 유일한 구현이다.</b> 임시 에셋 생성과 밸런스 CSV 불러오기가
        /// 같은 함수를 부른다 — 두 벌로 두면 반드시 어긋난다.
        /// </summary>
        public static void Layout(ZoneData zone,
                                  System.Collections.Generic.IList<StructureData> types,
                                  System.Collections.Generic.IList<int> counts,
                                  System.Collections.Generic.IList<float> spreadMuls,
                                  System.Collections.Generic.IList<float> bandGaps)
        {
            var list = new System.Collections.Generic.List<ZoneData.Placement>();
            int id = 0;
            float z = 2f, prevDepth = 0f;

            for (int ti = 0; ti < types.Count; ti++)
            {
                StructureData sd = types[ti];
                if (sd == null) continue;

                int count = Mathf.Max(0, counts[ti]);
                float w = Mathf.Max(sd.colliderSize.x, sd.colliderSize.z);
                float spread = Mathf.Max(7f, w * Mathf.Max(1f, spreadMuls[ti]));

                if (ti > 0) z += prevDepth * 0.5f + w * 0.5f + Mathf.Max(0f, bandGaps[ti]);
                prevDepth = w;

                // 띠를 반 칸씩 엇갈린다. 앞뒤 띠의 x가 겹치면 뒤 건물을 부술 때
                // 앞 건물이 1인칭 화면을 그대로 가로막는다.
                float offset = (ti % 2 == 0) ? 0f : spread * 0.5f;

                for (int k = 0; k < count; k++)
                {
                    float x = (k - (count - 1) * 0.5f) * spread + offset;
                    list.Add(new ZoneData.Placement {
                        id = id++,
                        data = sd,
                        position = new Vector3(x, 0f, z),
                        yaw = (k % 2 == 0) ? 0f : 90f,
                    });
                }
            }

            zone.structures = list.ToArray();
            zone.hazards = new ZoneData.HazardPlacement[0];
            zone.supports = new ZoneData.SupportLink[0];
        }
    }
}
