# 기술 설계

`docs/GAME_DESIGN.md`의 구현 대응표. 클래스 이름과 책임은 제안이며, 구현하며 조정해도 된다.
다만 **씬 하나 · 데이터 기반 구역 · 프리미티브 콜라이더**는 바꾸지 않는다.

---

## 1. 씬 구성

`Assets/_Project/Scenes/Main.unity` — **이 씬 하나만 존재한다.**

```
Main
├── [Bootstrap]          GameLoop, ZoneBuilder, PoolManager
├── [Camera]             Main Camera + FollowCamera
├── [Player]             PlayerController, Breaker, CableController, CargoTow
├── [Zone]               ← 런타임에 ZoneBuilder가 채운다. 씬에는 빈 오브젝트만
├── [VFX Pools]          런타임 생성
└── [UI]                 Canvas — HUD, ResultScreen
```

**씬에 배치하는 것은 위 6개 루트뿐이다.** 구조물·구멍·잔해는 전부 코드가 생성한다.
이렇게 해야 레벨 구성이 사람 손이 아니라 데이터에 남고, Claude가 소유할 수 있다.

---

## 2. ScriptableObject 스키마

### `StructureData`
```csharp
string   id;
string   displayName;
int      tier;                  // 0~4. 구멍 반경 게이팅에 사용
GameObject meshIntact;          // 프리팹
GameObject meshDamaged;
GameObject meshRubble;
float    breakTime;             // 홀드 소요 (초)
bool     isHighlight;           // true면 연타 시퀀스 진입
bool     isSupport;             // true면 붕괴 유발원
int      debrisCount;
float    debrisScatterRadius;
int      debrisSizeClass;       // 잔해 1개의 단위 무게
float    collapseStunSeconds;   // 깔렸을 때 정지 시간
Vector3  colliderSize;          // BoxCollider 크기. 메시에서 유도하지 않는다
```

### `HazardData`
```csharp
string   id;
GameObject meshIntact;
GameObject meshDestroyed;
float    breakTime;
float    explosionRadius;
float    explosionStunSeconds;
int      chainDamageToTier;     // 이 티어 이하 구조물을 연쇄 파괴
```

### `ZoneData`
```csharp
string   id;
float    timeLimit;
int      quota;                 // 클리어에 필요한 잔해 총 단위
float    initialHoleRadius;

Placement[]  structures;        // { StructureData data; Vector3 pos; float yaw; int id; }
Placement[]  hazards;
Vector3[]    holePositions;
SupportLink[] supports;         // { int supporterId; int[] dependentIds; }
```

지지 관계를 `SupportLink` 배열로 데이터에 박는다. 물리 시뮬레이션이나 레이캐스트로 유도하지 않는다 — 결정적이고 튜닝 가능해야 한다.

### `GameConfig` (단일 에셋)
```csharp
float moveSpeedBase;            // 6.0
float speedPenaltyPerUnit;      // 0.12
float moveSpeedMin;             // 2.5
float cableMaxLength;           // 40
float cablePointInterval;       // 0.25
float holeGrowthPerUnit;        // 0.04
float holeSuctionMargin;        // 2.0
float[] holeRadiusForTier;      // [1.5, 2.5, 4.0, 6.0, 9.0]
float hitStopSeconds;           // 0.08
float cameraDistance;           // 18
float cameraZoomPerUnit;        // 0.02 (최대 +4)
```

---

## 3. 시스템 분해

### Core
| 클래스 | 책임 |
|---|---|
| `GameLoop` | 구역 시작/종료, 타이머, 할당량 판정, 정지(stun) 처리 |
| `ZoneBuilder` | `ZoneData`를 읽어 구조물·위험설비·구멍을 생성하고 지지 그래프를 구축 |
| `PoolManager` | 잔해·파티클·오디오 풀. 모든 런타임 생성이 여기를 거친다 |
| `GameConfigRef` | `GameConfig` 전역 접근 |

### Player
| 클래스 | 책임 |
|---|---|
| `PlayerController` | 이동, 견인 무게에 따른 감속, 정지 상태 |
| `InputRouter` | New Input System → 이동 벡터 + 버튼 상태. 터치/키보드 동시 지원 |
| `Breaker` | 범위 내 대상 감지, 홀드 진행도 누적, 하이라이트 시퀀스 상태 머신 |
| `TrackAnimator` | 무한궤도 텍스처 스크롤, 차체 기울기 — **절차적. 애니메이터 없음** |

### Destruction
| 클래스 | 책임 |
|---|---|
| `Destructible` | Intact/Damaged/Rubble 상태 머신. `StructureData` 참조 |
| `SupportGraph` | 지지 관계 보유. 지지원 파괴 시 종속 오브젝트 붕괴 트리거 |
| `CollapseMotion` | 낙하 연출 + 접촉 판정. 물리 아님 — 트윈 + 오버랩 체크 |
| `DestructionFX` | 진행도(0~1)를 받아 셰이크·균열·파티클·사운드 피치를 동시에 구동 |
| `DebrisSpawner` | 파괴 시 `debrisCount`만큼 풀에서 꺼내 산란 |

### Collection
| 클래스 | 책임 |
|---|---|
| `CableController` | 궤적 포인트 기록, 길이 관리, 잔해 충돌로 인한 절단 |
| `LoopDetector` | 2D 선분 교차로 폐합 판정 |
| `PolygonCapture` | point-in-polygon(ray casting)으로 내부 잔해 선별. **폐합 순간 1회** |
| `CargoTow` | 견인 중 잔해 목록, 총 무게, 추종 배치 |
| `Hole` | 흡인 판정, 반경 성장, 티어 게이팅, 삼키기 연출 |
| `Debris` | 개체 단위. sizeClass 보유 |

### Hazard
| 클래스 | 책임 |
|---|---|
| `ExplosiveProp` | `HazardData` 기반 폭발, 연쇄 파괴 |
| `DebrisBlocker` | 잔해 영역 조회 — 케이블 절단 판정에 사용 |

### UI
| 클래스 | 책임 |
|---|---|
| `HUD` | 타이머, 할당량 진행, 구멍 반경, 견인 적재량 |
| `VirtualStick` / `ActionButton` | 터치 입력 위젯 |
| `ResultScreen` | 클리어/실패, 항목별 내역 |

---

## 4. 알고리즘 노트

### 폐합 판정
새 포인트 `P(n)`을 추가할 때 선분 `[P(n-1), P(n)]`이 기존 선분 `[P(i), P(i+1)]`(i ≤ n-3)과 교차하는지 검사한다.
교차하면 `i`부터 `n`까지가 폴리곤이 된다. 표준 2D 선분 교차(외적 부호)로 충분하다.

### 내부 판정
잔해의 XZ 좌표에 대해 ray casting(수평 반직선과 폴리곤 변의 교차 횟수 홀짝). 폐합 순간 1회만 실행하므로 잔해 80개 × 폴리곤 변 160개 = 12,800회여도 문제없다. **매 프레임 돌리지 않는다.**

### 붕괴 연쇄
`SupportGraph`는 `Dictionary<int, List<int>>`. 지지원이 Rubble이 되면 종속 목록을 순회하며 `CollapseMotion`을 시작한다. 재귀 허용(무너진 것이 또 다른 것을 무너뜨림)하되 방문 집합으로 순환을 막는다.

### 낙하 접촉
물리를 쓰지 않는다. 낙하 트윈 중 매 프레임 `Physics.OverlapSphere`로 플레이어만 검사하고, 닿으면 `GameLoop.ApplyStun()`.

---

## 5. Blender 파이프라인 (`Tools/blender/`)

VARCO 3D 출력을 Unity에 넣기 위한 bpy 스크립트. **한 번 성공한 조작은 반드시 `.py`로 남긴다** — 대화로만 하면 D11에 20번 다시 설명해야 한다.

| 스크립트 | 역할 |
|---|---|
| `import_clean.py` | 임포트, 스케일 정규화, 원점 재설정, 축 정렬 |
| `decimate.py` | Decimate 모디파이어 적용. 티어별 목표 폴리곤 수 |
| `export_fbx.py` | FBX 익스포트. **스케일 100배 · Z-up↔Y-up · 축 설정을 여기에 고정** |
| `batch.py` | 폴더 단위 일괄 처리 |

FBX 왕복의 3대 함정은 스케일·축·본 롤이다. 설정을 D2 스파이크에서 확정하고 스크립트에 박은 뒤 손으로 다시 맞추지 않는다.

## 6. 에디터 툴 (`Assets/Editor/Tools/`)

| 툴 | 역할 |
|---|---|
| `StructureImporter` | FBX 폴더 → 프리팹 3종 생성 + `StructureData` 에셋 생성 + 콜라이더 자동 부착 |
| `TexturePresetApplier` | 임포터 설정 일괄 적용 (512 강제, 압축) |
| `ZoneEditor` | `ZoneData` 배치 편집 — 씬 뷰 핸들로 위치 조정, 지지 관계 연결 |
| `BalanceCsvIO` | `StructureData` 일괄 export/import (밸런싱용) |

`ZoneEditor`가 가장 중요하다. 이게 없으면 구역 배치를 Inspector에서 손으로 찍어야 한다.

---

## 7. 시뮬레이터 (D16–18)

`Assets/Editor/Sim/`에 EditMode 전용으로 만든다. 렌더링 없이 게임 규칙만 돌린다.

측정 항목:
- 전략별 기대 처리량 — 벽면만 / 기둥 위주 / 몰아서 부수고 한 번에 수집 / 폭발 활용
- 구멍 성장 곡선과 티어 해금 시점
- 구역 제한 시간 대비 할당량 달성률 분포

**목표는 "의도한 플레이가 최적해인가"를 확인하는 것이다.** 안전한 벽면 반복이 최적해로 나오면 기둥 보상을 올리거나 시간 비용을 재조정한다.

---

## 8. 빌드

| 타깃 | 설정 |
|---|---|
| Android | AAB · IL2CPP · ARM64 · 최소 API 24 · 가로 고정 |
| WebGL | Brotli 압축 · Exception 지원 끄기 · 메모리 512MB |

빌드 스크립트를 `Assets/Editor/BuildPipeline.cs`에 만들어 원클릭으로 돌린다. 매주(D5·D10·D14·D18) 실행한다.
