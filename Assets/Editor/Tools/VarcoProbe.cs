using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// VARCO Sound가 왜 빈손으로 돌아오는지 묻는다 [2026-10-08].
    ///
    /// <b>왜 따로 만드는가.</b> 효과음 9건을 요청했더니 전부 <c>Result = null</c>로 돌아왔다.
    /// 예외도 없고 VARCO 쪽 로그도 없다 — <b>클라이언트가 조용히 null을 돌려준다.</b>
    /// 그러면 "크레딧이 없는 건지, 키가 막힌 건지, 프롬프트가 거절된 건지"를 못 가른다.
    /// 추측으로 고치면 틀린 데를 고치게 된다.
    ///
    /// <b>왜 두 번에 나눠 누르는가.</b> <see cref="VarcoSfx"/>와 같은 이유다 —
    /// 결과를 기다리려고 메인 스레드를 막으면 <b>UnityWebRequest가 영영 안 끝난다.</b>
    /// 그 요청을 펌프하는 것이 바로 그 스레드이기 때문이다. 던져 놓고 나중에 물어본다.
    ///
    /// 이 파일은 리플렉션만 쓴다 — VARCOSound 에셋이 없어도 컴파일된다 (라이선스상 저장소에 없다).
    /// </summary>
    public static class VarcoProbe
    {
        static Task probe;
        static string what;

        [MenuItem("Demo/8a VARCO 진단 — 키·크레딧 묻기", priority = 80)]
        public static void Ask()
        {
            Assembly va = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "VARCOSound.Editor") va = a;

            if (va == null) { Debug.LogError("[진단] VARCOSound.Editor 어셈블리가 없다."); return; }

            Type api = va.GetType("VARCOSound.Editor.API.ApiClient");
            MethodInfo getKey = api?.GetMethod("GetApiKey", Flags);
            MethodInfo validate = api?.GetMethod("ValidateApiKeyAsync", Flags);
            if (getKey == null || validate == null)
            { Debug.LogError("[진단] GetApiKey/ValidateApiKeyAsync를 못 찾았다."); return; }

            string key = getKey.Invoke(null, null) as string;
            if (string.IsNullOrEmpty(key))
            { Debug.LogError("[진단] API 키가 비어 있다 — VARCO Sound 창에서 로그인할 것."); return; }

            probe = validate.Invoke(null, new object[] { key }) as Task;
            what = $"키 {key.Length}자 ({key.Substring(0, Math.Min(4, key.Length))}…)";
            Debug.Log($"[진단] {what} 검증 요청. 잠시 뒤 'Demo/8b'를 누를 것.");
        }

        [MenuItem("Demo/8b VARCO 진단 — 답 보기", priority = 81)]
        public static void Read()
        {
            if (probe == null) { Debug.LogWarning("[진단] 먼저 Demo/8a를 누를 것."); return; }
            if (!probe.IsCompleted) { Debug.Log("[진단] 아직 도는 중이다."); return; }

            if (probe.Exception != null)
            {
                Debug.LogError("[진단] 검증이 예외로 끝났다: " + probe.Exception.GetBaseException());
                return;
            }

            object r = probe.GetType().GetProperty("Result")?.GetValue(probe);
            if (r == null) { Debug.LogError("[진단] 검증 결과도 null이다 — 응답 자체가 비었다."); return; }

            Debug.Log("[진단] " + what + " → " + Dump(r));
        }

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>응답 객체가 어떤 모양이든 읽어 낸다 — 타입을 미리 모른다.</summary>
        static string Dump(object o)
        {
            Type t = o.GetType();
            if (t.IsPrimitive || o is string) return t.Name + " = " + o;

            var sb = new System.Text.StringBuilder(t.Name + " {");
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object v = null;
                try { v = f.GetValue(o); } catch { }
                sb.Append($" {f.Name}={v ?? "null"};");
            }
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object v = null;
                try { v = p.GetValue(o); } catch { }
                sb.Append($" {p.Name}={v ?? "null"};");
            }
            return sb.Append(" }").ToString();
        }
    }
}
