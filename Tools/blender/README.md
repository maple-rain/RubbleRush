# Blender 파이프라인 — 전제가 바뀌었다

`docs/TECH_SPEC.md` §5는 bpy 스크립트 4개를 요구한다.

| 스크립트 | 역할 |
|---|---|
| `import_clean.py` | 임포트, 스케일 정규화, 원점 재설정, 축 정렬 |
| `decimate.py` | Decimate 모디파이어. 티어별 목표 폴리곤 수 |
| `export_fbx.py` | FBX 익스포트. 스케일 100배 · Z-up↔Y-up 고정 |
| `batch.py` | 폴더 단위 일괄 처리 |

**이 넷을 만들지 않았다.** 없어서 빠뜨린 것이 아니라, 2026-09-22 실측으로 **전제가 무너졌기
때문이다.** 죽은 스크립트를 네 개 두는 것보다 이유를 남기는 쪽이 낫다고 판단했다.

---

## 왜 필요 없어졌나

§5는 "생성 메시는 그대로 모바일에 못 올린다"를 전제로 쓰였다. 실제로 `Varco3DScene`의
기존 VARCO 출력은 **1종당 메시 55MB + 텍스처 24MB = 79MB**였고, AAB 200MB 예산에서는
2.5종이 끝이다. Decimate가 필수로 보였다.

그런데 VARCO의 `Generate3D` 노드에는 **생성 시점 파라미터**가 있다.

| intrinsic | 범위 | 효과 |
|---|---|---|
| `polygonCount` | 1,000 ~ 1,000,000 | 지정한 수가 **정확히** 나온다 |
| `textureSize` | 1024 / 2048 / 4096 | BaseColor에 적용 |
| `usePbrTexture` | 0 / 1 | 0이면 albedo만 |

`polygonCount: 8000` · `textureSize: 1024` · `usePbrTexture: 0`으로 벽면 1종을 뽑은 결과:

```
삼각형    8,000  (요청과 정확히 일치)
지오메트리 0.20 MB
텍스처    3.51 MB   ← BaseColor 1024² + Normal 2048²
FBX 전체  4.10 MB   ← 기존 54.72 MB의 1/13
```

**Decimate로 줄일 것이 남아 있지 않다.** 그리고 용량의 95%가 텍스처이므로,
폴리곤을 더 깎아도 의미가 없다 — 줄여야 할 것은 텍스처이고 그건 Unity 임포터가 한다.

축·스케일 문제도 Blender 없이 끝났다. FBX 루트에 `rot(270,0,0)`이 붙어 오는데 이는
Z-up→Y-up 변환이고 **정상**이다. 스케일 정규화와 원점 바닥 정렬은
`Assets/Editor/Tools/StructureImporter.cs`가 Unity 안에서 처리한다.

---

## 그래도 Blender가 필요해질 수 있는 경우

아래 중 하나라도 걸리면 §5를 되살린다.

- **생성 메시에 구멍·뒤집힌 노멀이 있어** 게임에서 깨져 보일 때 (아직 안 겪음)
- **한 종류의 3단계를 하나의 FBX로 합쳐야** 할 때 (지금은 3개 파일로 충분하다)
- **LOD를 만들어야** 할 때 — 구조물 60개 동시 표시가 D2 실기 측정에서 안 나오면 필요해진다
- **충돌용 저폴리 프록시**가 필요할 때 — 지금은 `StructureData.colliderSize`의 BoxCollider
  하나로 끝나서 불필요하다 (CLAUDE.md 규칙 4: MeshCollider 금지)

Blender MCP 연결은 이미 있다 (`blender-mcp` 서버, 포트 9876). 필요해지면 바로 쓸 수 있다.

---

> 이 폴더를 비워 둔 것은 **판단이지 누락이 아니다.** 되살릴 조건은 위에 적어 뒀다.
> 판단이 틀렸다고 보면 §5대로 네 개를 만들면 된다.

---

## 2026-09-30 — 부활 조건 하나가 걸렸다

> **한 종류의 여러 판본을 따로 굽어야 할 때** — 위에 적어 둔 조건에 해당한다.

파괴 화면은 1인칭이라 팔만 보여야 한다 [지시 2026-09-30]. 몸통과 머리를 카메라 뒤로
밀어내던 방식(`StrikeArmRig.headClearance`)은 **주먹 거리의 상한**을 만들어서,
주먹이 화면 폭의 25%를 덮는 것을 고칠 수 없었다. 아예 안 그리면 그 상한이 사라진다.
실측: 화면 폭 24.6% -> **17.9%**.

| 스크립트 | 역할 |
|---|---|
| `glb2fbx.py` | VARCO GLB -> Unity FBX. VARCO의 FBX가 애니메이션을 빠뜨려서 필요하다 |
| `arms_carve.py` | `Base.glb`에서 팔만 남긴 `R_Arms.fbx`를 굽는다 |

```bash
"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -noaudio -P Tools/blender/arms_carve.py -- Tools/blender/glb <출력폴더>
```

### 뼈는 23개를 그대로 둔다

`AC_Demolition.controller`, 아바타, 마스크 3종(`UpperBody`/`LowerBody`/`StrikeTwist`),
클립이 전부 뼈를 **경로 이름**으로 부른다. 뼈를 줄이면 그 전부가 깨진다.
지우는 것은 정점뿐이다 — 8268 -> 4054 (팔 뼈 가중치 합 0.5 이상만 남김).

이 로봇은 데포르메라 **주먹만 정점의 24.2%**다 (왼 1082 + 오른 918). 머리 14.6%,
몸통은 2.9%뿐이다. 팔이 절반이나 남는 것이 정상이다.

### 왜 애니메이터를 두 벌 만들지 않는가

같은 GLB를 같은 설정으로 다시 구웠으므로 bindpose가 원본과 **완전히 같다**
(실측 최대 차이 0.00000000). `SkinnedMeshRenderer`는 `bones[]`를 씬의 리그에서 가져오고
메시에서는 `bindposes`만 읽으므로, `StrikeArmRig`가 `sharedMesh` 한 줄만 바꿔 끼운다.
같은 애니메이터·같은 클립이 그대로 돌아 동기화 문제가 원천적으로 없다.

### 원본 GLB는 저장소에 없다

`Tools/blender/glb/` (59MB)는 `.gitignore`에 있다. **지우면 안 된다** — VARCO 크레딧으로
만든 것이라 다시 만들 수 없고, 팔 FBX를 다시 구우려면 `Base.glb`가 필요하다.
