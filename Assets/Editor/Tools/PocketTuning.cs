using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Demo.Collection;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 주머니 솔버의 값 셋을 쓸어 본다 [2026-10-08] — 반복 횟수·그물의 무름·바닥 마찰.
    ///
    /// <b>왜 쓸어야 하는가.</b> 반복 횟수는 "몇 번이면 수렴하는가"의 문제라 추론으로 못 정한다.
    /// 그물의 무름(<c>ropeGive</c>)과 바닥 마찰은 그와 달리 <b>기획 값</b>이다 — 타원이
    /// 얼마나 깊어지는지와 짐이 얼마나 뒤로 처지는지를 가른다. 둘을 같은 표에 놓아야
    /// "이건 수렴 문제, 저건 취향 문제"가 구별된다.
    ///
    /// <b>이 도구는 값을 정하지 않는다.</b> 어떤 값에서 무엇이 어떻게 변하는지만 적는다.
    /// 수렴은 숫자가 정하고(겹침·샘이 0으로 가는 지점), 모양은 기획자가 보고 정한다
    /// (CLAUDE.md 역할 분담).
    /// </summary>
    public static class PocketTuning
    {
        // t5_solid — 제일 빡빡한 경우다. 조각이 제일 굵고(1.17m) 제일 많고(32개)
        // 제일 넓게 흩어진다(8.0m). 여기서 수렴하면 나머지는 더 쉽다.
        static readonly (string name, int count, float radius, float scatter) Case =
            ("t5_solid", 32, 1.17f, 8.0f);

        [MenuItem("Demo/15b. 주머니 값 쓸기", priority = 68)]
        public static void Sweep()
        {
            var sb = new StringBuilder();
            var csv = new StringBuilder();
            csv.AppendLine("# 주머니 솔버 값 쓸기 — t5_solid 잔해 32개 반경 1.17m, 같은 경로 [2026-10-08]");
            csv.AppendLine("# iters는 수렴(겹침·샘이 0으로 가는가), give/drag는 기획 값(모양)");
            csv.AppendLine("type,iters,ropeGive,groundDrag,bounce,net_aspect,rear_share,nearHand_m,overlaps,mean_speed,max_speed,ring_step,ring_tremble,move_contain,ms_per_frame");

            sb.AppendLine("<b>'구속요구'는 상한에 걸리기 전에 솔버가 요구한 이동량이다</b> — " +
                          "실제 이동이 아니다. 상한을 조이면 이 값은 커지는 게 정상이다.");
            sb.AppendLine($"주머니 값 쓸기 — {Case.name} (잔해 {Case.count}개 · 반경 {Case.radius}m), " +
                          "6초 직진 → 90도 회전 → 4초 직진 [2026-10-08]");
            sb.AppendLine();

            sb.AppendLine("[0] 튕김 — 떼어낸 밀기가 속도로 얼마나 남는가 [기획자 지적 2026-10-08]");
            sb.AppendLine("    1.00이 2026-10-08에 플레이한 그 동작이다. 플레이어는 6m/s로 간다.");
            sb.AppendLine("    <b>종류마다 따로 본다</b> — 견고 32개는 짐이 그물에 안 들어가 꽉 끼므로");
            sb.AppendLine("    분리 밀기가 감쇠로 작동한다. 거기 수치로 다른 종류를 판단하면 거꾸로 읽는다.");
            Bounce(sb, csv);

            sb.AppendLine();
            sb.AppendLine("[0a] 줄 모양 상한 — 한 프레임에 테두리 모양이 바뀔 수 있는 거리 [기획자 지적 2026-10-08]");
            sb.AppendLine("     0이 그 지적을 받은 동작이다(안 묶음). 줄떨림은 테두리 점이 매 프레임 방향을");
            sb.AppendLine("     뒤집는 비율이고, 줄변형은 모양이 바뀌는 속도(m/s)다. 슬롯은 둘 다 0.00이다.");
            foreach (var cs in PocketCompare.Cases)
            {
                if (cs.name != "t3_normal" && cs.name != "t4_heavy") continue;
                sb.AppendLine();
                sb.AppendLine($"  -- {cs.name} --");
                sb.AppendLine(Head());
                foreach (float r in new[] { 0f, 0.03f, 0.06f, 0.12f, 0.25f })
                {
                    PocketCompare.Override = (2, 0.35f, 0.12f, Keep, Step, Resp, r);
                    PocketCompare.Drive(CargoTow.HaulShape.Pocket, cs,
                                        out List<PocketCompare.Sample> log, out double ms);
                    PocketCompare.Override = null;
                    Fold(sb, csv, cs.name + " shape" + F(r), 2, 0.35f, 0.12f, Keep, log, ms);
                }
            }

            sb.AppendLine();
            sb.AppendLine("[0b] 한 프레임 이동 상한 — 총량을 묶는다. 플레이어는 0.10m/프레임 간다");
            sb.AppendLine("     단계별로 손질해도 속도가 통만 바꿔 담기므로 합계를 묶는 쪽을 본다");
            foreach (var cs in PocketCompare.Cases)
            {
                if (cs.name != "t3_normal" && cs.name != "t5_solid") continue;
                sb.AppendLine();
                sb.AppendLine($"  -- {cs.name} --");
                sb.AppendLine(Head());
                foreach (float st in new[] { 0.08f, 0.12f, 0.20f, 0.35f, 0.60f })
                {
                    PocketCompare.Override = (2, 0.35f, 0.12f, Keep, st, Resp, Shape);
                    PocketCompare.Drive(CargoTow.HaulShape.Pocket, cs,
                                        out List<PocketCompare.Sample> log, out double ms);
                    PocketCompare.Override = null;
                    Fold(sb, csv, cs.name + " step" + F(st), 2, 0.35f, 0.12f, Keep, log, ms);
                }
            }

            sb.AppendLine();
            sb.AppendLine("[1] 반복 횟수 — 겹침과 샘이 0으로 가는가 (수렴 문제, 숫자가 정한다)");
            sb.AppendLine(Head());
            foreach (int it in new[] { 1, 2, 3, 4, 6 }) One(sb, csv, it, 0.35f, 0.12f, Keep, Step);

            sb.AppendLine();
            sb.AppendLine("[2] 그물의 무름 — 0이면 쇠틀, 1이면 종잇장 (기획 값)");
            sb.AppendLine(Head());
            foreach (float g in new[] { 0.05f, 0.2f, 0.35f, 0.6f, 0.9f }) One(sb, csv, 2, g, 0.12f, Keep, Step);

            sb.AppendLine();
            sb.AppendLine("[3] 바닥 마찰 — 잔해가 얼마나 끈적하게 끌려오는가 (기획 값)");
            sb.AppendLine(Head());
            foreach (float d in new[] { 0.02f, 0.06f, 0.12f, 0.25f, 0.5f }) One(sb, csv, 2, 0.35f, d, Keep, Step);

            string path = "Tools/balance/pocket_tuning.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            sb.AppendLine();
            sb.AppendLine($"표: {path}");
            sb.AppendLine("값은 어느 것도 확정이 아니다 [제안·미승인].");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 튕김만 종류별로 쓴다. 기획자가 플레이한 것은 일반 건물(12조각)이고,
        /// 거기서 "잔해끼리 튕기는 정도가 너무 크다"가 나왔다.
        /// </summary>
        static void Bounce(StringBuilder sb, StringBuilder csv)
        {
            foreach (var cs in PocketCompare.Cases)
            {
                sb.AppendLine();
                sb.AppendLine($"  -- {cs.name} (조각 {cs.count}개 · 반경 {cs.radius}m) --");
                sb.AppendLine(Head());
                foreach (float b in new[] { 0f, 0.15f, 0.35f, 0.6f, 1f })
                {
                    PocketCompare.Override = (2, 0.35f, 0.12f, b, Step, Resp, Shape);
                    PocketCompare.Drive(CargoTow.HaulShape.Pocket, cs,
                                        out List<PocketCompare.Sample> log, out double ms);
                    PocketCompare.Override = null;
                    Fold(sb, csv, cs.name, 2, 0.35f, 0.12f, b, log, ms);
                }
            }
            sb.AppendLine();
        }

        /// <summary>다른 쓸기에서 고정해 두는 기본값.</summary>
        const float Keep = 0.15f, Step = 0.12f, Resp = 0.2f, Shape = 0.06f;

        static void One(StringBuilder sb, StringBuilder csv, int iters, float give, float drag,
                        float bounce, float step)
        {
            PocketCompare.Override = (iters, give, drag, bounce, step, Resp, Shape);
            PocketCompare.Drive(CargoTow.HaulShape.Pocket, Case,
                                out List<PocketCompare.Sample> log, out double ms);
            PocketCompare.Override = null;
            Fold(sb, csv, Case.name, iters, give, drag, bounce, log, ms);
        }

        static void Fold(StringBuilder sb, StringBuilder csv, string type, int iters, float give,
                         float drag, float bounce, List<PocketCompare.Sample> log, double ms)
        {

            // 마지막 2초만 본다 — 그때쯤이면 자리를 잡는다. 앞쪽은 펼쳐지는 과도기다
            float aspect = 0, bias = 0, near = 0, esc = 0, ovl = 0, con = 0;
            float mean = 0, top = 0, mi = 0, mc = 0, ms2 = 0;
            int c = 0;
            foreach (PocketCompare.Sample s in log)
            {
                if (s.T < 9.0f) continue;
                aspect += s.NetAcross > 1e-3f ? s.NetAlong / s.NetAcross : 0f;
                bias += s.RearShare; near += s.NearHand;
                esc += s.Escaped; ovl += s.Overlaps; con += s.Contacts;
                mean += s.MeanSpeed; top += s.MaxSpeed;
                mi += s.RingStep; mc += s.RingTremble; ms2 += s.MoveContain;
                c++;
            }
            if (c == 0) return;
            float inv = 1f / c;

            sb.AppendLine(string.Format("  {0,-5}{1,-7}{2,-7}{3,-7}{4,9}{5,8}{6,9}{7,10}{8,9}{9,9}",
                iters, F(give), F(drag), F(bounce), F(aspect * inv), F(bias * inv),
                F(ovl * inv), F(mean * inv), F(top * inv),
                F(mi * inv) + "/" + F(mc * inv) + "/" + F(ms2 * inv)));

            csv.AppendLine(string.Join(",", type, iters, F(give), F(drag), F(bounce), F(aspect * inv),
                F(bias * inv), F(near * inv), F(ovl * inv), F(mean * inv), F(top * inv),
                F(mi * inv), F(mc * inv), F(ms2 * inv),
                ms.ToString("F4", CultureInfo.InvariantCulture)));
        }

        static string Head() => string.Format("  {0,-5}{1,-7}{2,-7}{3,-7}{4,9}{5,8}{6,9}{7,10}{8,9}{9,9}",
            "반복", "무름", "마찰", "튕김", "그물비율", "뒤쪽", "겹침", "평균속m/s", "최고속", "줄변형 / 줄떨림 / 테두리밀기");

        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}
