using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 원클릭 빌드 (TECH_SPEC §8).
    ///
    /// <b>아직 구현되지 않았다.</b>
    ///
    /// | 타깃 | 설정 |
    /// |---|---|
    /// | Android | AAB · IL2CPP · ARM64 · 최소 API 24 · 가로 고정 |
    /// | WebGL | Brotli · Exception 지원 끄기 · 메모리 512MB |
    ///
    /// 일정상 <b>D5·D10·D14·D18에 매주 돌려야 한다.</b> "마지막에 몰아서 빌드하면 그날 하루를
    /// 통째로 잃는다"가 계획서의 경고이고, 첫 AAB가 D5에 스토어에 올라가야 14일 테스트 시계가
    /// 시작된다 — 이 프로젝트 최대 리스크의 대응책이다.
    ///
    /// 지금 Player Settings가 전부 기본값이라(IL2CPP·ARM64·API24·Landscape·Linear 미설정,
    /// applicationIdentifier 비어 있음) 이 툴을 쓰기 전에 그것부터 맞춰야 한다.
    /// </summary>
    public static class BuildPipelineTool
    {
        [MenuItem("Demo/빌드/Android AAB (미구현)", priority = 200)]
        public static void BuildAndroid()
        {
            Debug.LogWarning("[BuildPipeline] 아직 구현되지 않았다 (TECH_SPEC §8). " +
                             "Player Settings도 전부 기본값이라 먼저 맞춰야 한다 — SETUP.md §2.");
        }

        [MenuItem("Demo/빌드/WebGL (미구현)", priority = 201)]
        public static void BuildWebGL()
        {
            Debug.LogWarning("[BuildPipeline] 아직 구현되지 않았다 (TECH_SPEC §8).");
        }
    }
}
