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
            csv.AppendLine("iters,ropeGive,groundDrag,net_aspect,rear_share,nearHand_m,escaped,overlaps,contacts,ms_per_frame");

            sb.AppendLine($"주머니 값 쓸기 — {Case.name} (잔해 {Case.count}개 · 반경 {Case.radius}m), " +
                          "6초 직진 → 90도 회전 → 4초 직진 [2026-10-08]");
            sb.AppendLine();

            sb.AppendLine("[1] 반복 횟수 — 겹침과 샘이 0으로 가는가 (수렴 문제, 숫자가 정한다)");
            sb.AppendLine(Head());
            foreach (int it in new[] { 1, 2, 3, 4, 6 }) One(sb, csv, it, 0.35f, 0.12f);

            sb.AppendLine();
            sb.AppendLine("[2] 그물의 무름 — 0이면 쇠틀, 1이면 종잇장 (기획 값)");
            sb.AppendLine(Head());
            foreach (float g in new[] { 0.05f, 0.2f, 0.35f, 0.6f, 0.9f }) One(sb, csv, 4, g, 0.12f);

            sb.AppendLine();
            sb.AppendLine("[3] 바닥 마찰 — 잔해가 얼마나 끈적하게 끌려오는가 (기획 값)");
            sb.AppendLine(Head());
            foreach (float d in new[] { 0.02f, 0.06f, 0.12f, 0.25f, 0.5f }) One(sb, csv, 4, 0.35f, d);

            string path = "Tools/balance/pocket_tuning.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            sb.AppendLine();
            sb.AppendLine($"표: {path}");
            sb.AppendLine("값은 어느 것도 확정이 아니다 [제안·미승인].");
            Debug.Log(sb.ToString());
        }

        static void One(StringBuilder sb, StringBuilder csv, int iters, float give, float drag)
        {
            PocketCompare.Override = (iters, give, drag);
            PocketCompare.Drive(CargoTow.HaulShape.Pocket, Case,
                                out List<PocketCompare.Sample> log, out double ms);
            PocketCompare.Override = null;

            // 마지막 2초만 본다 — 그때쯤이면 자리를 잡는다. 앞쪽은 펼쳐지는 과도기다
            float aspect = 0, bias = 0, near = 0, esc = 0, ovl = 0, con = 0;
            int c = 0;
            foreach (PocketCompare.Sample s in log)
            {
                if (s.T < 9.0f) continue;
                aspect += s.NetAcross > 1e-3f ? s.NetAlong / s.NetAcross : 0f;
                bias += s.RearShare; near += s.NearHand;
                esc += s.Escaped; ovl += s.Overlaps; con += s.Contacts;
                c++;
            }
            if (c == 0) return;
            float inv = 1f / c;

            sb.AppendLine(string.Format("  {0,-6}{1,-9}{2,-9}{3,9}{4,9}{5,10}{6,8}{7,9}{8,9}{9,10}",
                iters, F(give), F(drag), F(aspect * inv), F(bias * inv), F(near * inv),
                F(esc * inv), F(ovl * inv), F(con * inv), ms.ToString("F4")));

            csv.AppendLine(string.Join(",", iters, F(give), F(drag), F(aspect * inv), F(bias * inv),
                F(near * inv), F(esc * inv), F(ovl * inv), F(con * inv),
                ms.ToString("F4", CultureInfo.InvariantCulture)));
        }

        static string Head() => string.Format("  {0,-6}{1,-9}{2,-9}{3,9}{4,9}{5,10}{6,8}{7,9}{8,9}{9,10}",
            "반복", "무름", "마찰", "그물비율", "뒤쪽", "손까지", "샘", "겹침", "접촉", "ms");

        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}
