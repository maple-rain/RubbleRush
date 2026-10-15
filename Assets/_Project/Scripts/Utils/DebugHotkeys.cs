using Demo.Collection;
using Demo.Core;
using Demo.Destruction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Demo.Utils
{
    /// <summary>
    /// 시연·검증용 단축키와 오버레이. <b>출시 빌드에서는 빠진다</b> — D15 동결 때 제거 대상.
    ///
    /// 경험치는 설계상 화면에 보이지 않는다. 그런데 보이지 않는 값은 시연에서 확인할 방법도 없고,
    /// 가시화 여부가 추후 번복될 수 있다고 정해져 있다. 그래서 F1 토글을 달았다 —
    /// <b>이 오버레이가 그대로 경험치바 가시화의 시제품 역할</b>을 한다.
    /// </summary>
    public sealed class DebugHotkeys : MonoBehaviour
    {
        [SerializeField, Tooltip("F2가 한 번에 넣는 경험치")]
        int xpStep = 10;

        [SerializeField, Tooltip("시작할 때 오버레이를 켜둘지")]
        bool showOverlay = true;

        Hole hole;
        GameLoop loop;
        CargoTow cargo;
        DebrisSpawner spawner;
        Demo.Player.InputRouter input;
        Demo.Player.Breaker breaker;
        Demo.Player.InteractionProbe probe;
        Demo.Destruction.DemolitionSession session;
        GUIStyle style;

        void Awake()
        {
            loop = GameLoop.Instance;
            cargo = FindFirstObjectByType<CargoTow>();
            spawner = FindFirstObjectByType<DebrisSpawner>();
            input = FindFirstObjectByType<Demo.Player.InputRouter>();
            breaker = FindFirstObjectByType<Demo.Player.Breaker>();
            probe = FindFirstObjectByType<Demo.Player.InteractionProbe>();
            session = FindFirstObjectByType<Demo.Destruction.DemolitionSession>();
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // 구멍은 구역 생성 뒤에 만들어지므로 여기서 늦게 잡는다.
            if (hole == null) hole = FindFirstObjectByType<Hole>();
            if (loop == null) loop = GameLoop.Instance;

            if (kb.f1Key.wasPressedThisFrame) showOverlay = !showOverlay;

            if (kb.f2Key.wasPressedThisFrame && hole != null)
            {
                hole.AddXp(xpStep);
                Debug.Log($"[Debug] 경험치 +{xpStep}");
            }

            if (kb.f3Key.wasPressedThisFrame && hole != null)
            {
                if (!hole.ForceLevelUp()) Debug.Log("[Debug] 이미 최종 단계다.");
            }

            if (kb.f4Key.wasPressedThisFrame) BreakNearest();

            if (kb.f5Key.wasPressedThisFrame) RebuildZone();

            // F6 — 구멍이 왜 잔해를 안 먹는지 콘솔에 적는다 [지시 2026-10-15].
            // 재현이 안 되는 증상이라, 그 순간에 눌러 이유를 남기게 한다.
            if (kb.f6Key.wasPressedThisFrame) ReportHole();
        }

        /// <summary>가장 가까운 구조물을 즉시 부순다. 잔해를 빨리 쌓아 폐합을 시연할 때 쓴다.</summary>
        /// <summary>
        /// 구멍 둘레의 잔해가 <b>왜 안 들어가는지</b> 한 줄씩 적는다 [지시 2026-10-15].
        ///
        /// "잔해가 들어가지 않는 현상"이 재현되지 않아서, 그 순간에 눌러 상태를 남기도록
        /// 만들었다. 막는 이유는 셋 중 하나다 — 너무 크거나, 너무 멀거나, 이미 빠지는 중이거나.
        /// </summary>
        void ReportHole()
        {
            var hole = GameLoop.Instance != null ? GameLoop.Instance.ActiveHole : null;
            var cfg = GameConfigRef.Current;
            if (hole == null || cfg == null) { Debug.Log("[F6] 구멍이나 설정이 없다"); return; }

            float margin = cfg.holeSuctionMargin * Core.Upgrades.Mul(Core.Upgrades.WiderSuction);
            float reach = hole.Radius + margin;
            var at = new Vector2(hole.transform.position.x, hole.transform.position.z);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[F6] 구멍 " + (hole.Level + 1) + "단계 · 반지름 "
                + hole.Radius.ToString("0.00") + "m · 흡인 끝 " + reach.ToString("0.00") + "m");
            sb.AppendLine("     삼킨 단위 " + hole.SwallowedUnits
                + " · 잔해 " + Collection.Debris.Active.Count + "개"
                + " · 삼킴카드 " + Core.Upgrades.Has(Core.Upgrades.Swallow));
            sb.AppendLine("     따라오기 " + Core.Upgrades.Has(Core.Upgrades.HoleFollows)
                + " · 남은 자취 " + hole.TrailCount + "점"
                + " · 목적지까지 "
                + Vector3.Distance(hole.transform.position, hole.DriftTarget).ToString("0.0") + "m");

            int shown = 0;
            foreach (Collection.Debris d in Collection.Debris.Active)
            {
                if (d == null) continue;
                float dist = Vector2.Distance(d.FlatPosition, at);
                if (dist > reach + 4f || shown++ >= 10) continue;

                string why = d.IsFalling ? "이미 빠지는 중"
                    : !d.FitsInHole(hole.Radius) ? "너무 크다(반지름 " + d.SizeRadius.ToString("0.00") + ")"
                    : dist <= hole.Radius ? "구멍 안 — 다음 프레임에 먹힌다"
                    : dist <= reach ? "당기는 중" : "흡인 범위 밖";
                sb.AppendLine("     " + dist.ToString("0.00") + "m  반지름 "
                    + d.SizeRadius.ToString("0.00") + "  " + why);
            }
            if (shown == 0) sb.AppendLine("     구멍 둘레에 잔해가 없다");
            Debug.Log(sb.ToString());
        }

        void BreakNearest()
        {
            if (loop == null || spawner == null) return;

            Vector3 p = loop.PlayerPosition;
            Destructible best = null;
            float bestSqr = float.MaxValue;

            foreach (var d in FindObjectsByType<Destructible>(FindObjectsSortMode.None))
            {
                if (!d.IsBreakable) continue;
                float sqr = (d.transform.position - p).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = d; }
            }

            if (best == null) { Debug.Log("[Debug] 부술 것이 없다."); return; }

            if (best.ForceRubble())
            {
                spawner.SpawnFor(best.Data, best.transform.position);
                Debug.Log($"[Debug] 즉시 파괴 — {best.name}");
            }
        }

        void RebuildZone()
        {
            if (loop == null || loop.Zone == null) return;
            Debug.Log("[Debug] 구역 재생성");
            loop.StartZone(loop.Zone);
            hole = null;
        }

        void OnGUI()
        {
            if (!showOverlay) return;

            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };

            GUILayout.BeginArea(new Rect(12, 12, 460, 300), GUI.skin.box);

            if (loop != null)
                GUILayout.Label($"할당량  <b>{loop.Collected} / {loop.Quota}</b>   " +
                                $"남은 시간  <b>{loop.TimeRemaining:0.0}s</b>", style);

            if (hole != null)
            {
                string next = hole.IsMaxLevel ? "최종" : $"{hole.Xp} / {hole.XpToNext}";
                GUILayout.Label($"구멍  단계 <b>{hole.Level}</b>   반경 <b>{hole.Radius:0.00}m</b>   " +
                                $"경험치 <b>{next}</b>", style);

                Rect bar = GUILayoutUtility.GetRect(430, 14);
                GUI.Box(bar, GUIContent.none);
                var fill = new Rect(bar.x, bar.y, bar.width * hole.LevelProgress01, bar.height);
                GUI.DrawTexture(fill, Texture2D.whiteTexture);
            }

            if (cargo != null)
            {
                string over = "";
                if (breaker != null && breaker.IsJammed())
                    over = $"  <color=#ff8a5c>← 막힘: 가장 큰 것 {cargo.LargestRadius:0.00}m > 구멍. " +
                           "Q로 내려놓기</color>";
                GUILayout.Label($"견인  <b>{cargo.TotalUnits}</b>단위{over}", style);
            }

            if (input != null)
            {
                string onoff = input.ActionActive
                    ? "<color=#8cff9a><b>켜짐</b></color>" : "<color=#9aa4ad>꺼짐</color>";
                GUILayout.Label($"동작 토글  {onoff}   (정지=파괴 / 이동=케이블)", style);
            }

            var mode = GameModeController.Instance;
            if (mode != null && mode.IsDemolition && session != null)
            {
                GUILayout.Label($"<color=#ffd24a><b>파괴 화면</b></color>  " +
                                $"{(session.Target != null && session.Target.Data != null ? session.Target.Data.displayName : "?")}  " +
                                $"게이지 <b>{session.Gauge * 100f:0}%</b>  탭 <b>{session.TapCount}</b>회", style);
                Rect gb = GUILayoutUtility.GetRect(430, 14);
                GUI.Box(gb, GUIContent.none);
                GUI.DrawTexture(new Rect(gb.x, gb.y, gb.width * session.Gauge, gb.height),
                                Texture2D.whiteTexture);
            }
            else if (probe != null)
            {
                string line = probe.PromptVisible
                    ? $"<color=#8cff9a><b>E</b> 부수기</color>  " +
                      $"{(probe.Candidate.Data != null ? probe.Candidate.Data.displayName : "?")}"
                    : (string.IsNullOrEmpty(probe.BlockedReason) ? "가까운 건물 없음"
                                                                 : $"<color=#9aa4ad>{probe.BlockedReason}</color>");
                GUILayout.Label($"상호작용  {line}", style);
            }

            GUILayout.Space(6);
            GUILayout.Label("<b>F1</b> 오버레이   <b>F2</b> 경험치+   <b>F3</b> 단계+   " +
                            "<b>F4</b> 근처 파괴   <b>F5</b> 구역 재생성", style);
            GUILayout.Label("<b>WASD</b> 이동   <b>E</b> 부수기(파괴 화면)   " +
                            "<b>Space</b> 케이블 토글 / 파괴 화면에서 두드리기   <b>Q</b> 내려놓기", style);

            GUILayout.EndArea();
        }
    }
}
