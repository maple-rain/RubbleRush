# VARCO 그래픽 프롬프트 — 건물 정면도와 바닥 텍스처

기록: 2026-10-08. 등급 `[기록]` — 이미 쓴 프롬프트를 적어 둔 것이라 승인할 값이 없다.

**왜 여기 적는가.** VARCO 워크플로의 노드는 **돌려 쓴다**. 새 건물을 뽑을 때마다 기존
`TextInput`의 글을 덮어쓰므로 그래프 안에는 **마지막에 쓴 것만** 남는다. 실제로 t6(벽돌 창고)의
프롬프트는 t7을 뽑으면서 덮여 사라졌다 — 이 문서가 유일한 사본이다. 다시 뽑거나 면을 추가할
때는 여기서 복사해 쓴다.

## 세 단계를 한 물건으로 유지하는 법

**`EditImage`가 핵심이다.** 단계마다 `GenerateImage`로 따로 뽑으면 셋이 서로 다른 건물이 된다.
온전한 것을 한 번 뽑고, 그것을 `EditImage`의 `sourceImage`로 넣은 뒤 "같은 것인데 조건만
바꿔라"를 `referenceText`로 준다. 체인은 이렇다:

```
TextInput -> GenerateImage -> EditImage(파손) -> EditImage(잔해)
```

프롬프트에 반드시 들어가야 하는 네 가지. 빼면 매번 틀린다:

| 넣는 말 | 빼면 |
|---|---|
| `FILLS THE ENTIRE FRAME edge to edge` | 하늘·여백이 남고, 판의 UV가 그것을 벽의 일부로 만든다 |
| `ZERO perspective, camera exactly perpendicular` | 비스듬한 그림이 나와 상자에 붙으면 어긋난다 |
| `Keep the EXACT same ...` (파손·잔해) | 단계마다 다른 건물이 된다 |
| `leave that area plain flat neutral grey` (잔해) | 잔해 위에 멀쩡한 윗층이 그려진다 |

**실측 2026-10-08**: 위를 지킨 여섯 장에서 하늘로 남은 줄이 t2 두 장은 6줄(0.6%), t7 세 장은
0줄이었다. `Tools/facade_prep.py`로 자를 필요가 없었다. 다만 **잔해 단계에는 그 도구를 쓰면
안 된다** — 의도한 회색 여백(t2 기준 60%)을 하늘로 보고 통째로 먹는다.

---

## 건물 정면도

### t1_weak — 취약 건물 (2.4 x 2.4 x 1.8)

```
Orthographic front elevation of a SMALL, FLIMSY two-storey shack at an abandoned industrial site. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Cheap construction: corrugated metal sheeting and thin plywood panels nailed over a timber frame, rusted and buckled. BOTTOM half: a crooked boarded-up doorway slightly off centre. TOP half: one small broken window with a cracked pane. A sagging thin roof edge along the very top. Faded peeling paint, rust bleed, patched holes. Flat overcast lighting, no cast shadows, no lens effects, no text, no logos, no people. Clean readable mobile game texture asset.
```

### t2_light — 경량 건물 (3.4 x 3.4 x 3.0) · 온전

**2026-10-08에 다시 뽑았다.** 전에는 5.5 x 3.8 x 1.0짜리 가로로 긴 창고였는데 바닥이 5.5:1이라 건물이 아니라 벽 한 장이었다 — 네모난 그림이 옆면에서 3.80배로 늘어나 붙었다. 정사각에 가깝게 바꾸면서 `almost square - as wide as it is tall, NOT a long wide shed`를 못 박았다.

```
Orthographic front elevation of a SMALL COMPACT two-storey prefabricated site office block at a derelict industrial site, almost square - as wide as it is tall, NOT a long wide shed. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Light prefabricated construction: pale grey ribbed metal cladding panels with visible vertical seams on a steel frame. Divided into two equal horizontal floor bands. BOTTOM band: a single rusted steel personnel door slightly off centre with one small square window beside it and a short concrete step. TOP band: two square windows with simple dark frames, one cracked, and a small wall-mounted vent unit. A thin flat roof edge with a short safety railing along the very top. Rust streaks running below each seam, dents in the cladding, faded peeling paint, a bent downpipe at one side. Flat overcast lighting, no cast shadows, no readable text, no logos, no people. Clean readable mobile game texture asset.
```

### t2_light · 파손

```
Keep the EXACT same prefabricated site office: same pale grey ribbed metal cladding, same seam spacing, same door and window positions, same two floor bands, same roof railing, same framing, same flat orthographic elevation filling the whole frame. Change ONLY its condition to DAMAGED: several cladding panels buckled and peeled away exposing the dark steel frame and insulation behind, one upper window blown out with a bent frame, the roof railing crumpled at one end, deep dents and long tears in the metal, heavy rust and scorch smears. The building is still standing and still fills the entire frame. No sky, no ground, no rubble pile, no margin, no background.
```

### t2_light · 잔해

```
Keep the EXACT same pale grey ribbed metal cladding colour and material. Now the building is almost gone: only a low broken STUMP of the ground floor remains along the bottom, perhaps one third of the original height, with a ragged jagged top edge of torn metal sheeting and snapped steel studs. Crushed cladding panels and twisted framing heaped against it. Above the stump there is nothing - leave that area plain flat neutral grey, completely empty. Still a flat orthographic elevation filling the whole frame, no perspective. No sky, no ground plane, no margin, no background, no people.
```

### t3_normal — 일반 건물 (3.5 x 3.8 x 3.5)

```
Orthographic front elevation of an ORDINARY two-storey concrete office block at a derelict industrial site, roughly square in proportion. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Poured concrete with visible formwork panel lines. BOTTOM band: a central recessed entrance with a dark boarded double door and a small concrete canopy. TOP band: a row of four evenly spaced rectangular windows with thin sills, two of them boarded. A plain concrete parapet along the very top. Weathered grey concrete, water stains, hairline cracks, faded paint patches. Flat overcast lighting, no cast shadows, no text, no logos, no people. Clean readable mobile game texture asset.
```

### t4_heavy — 중량 건물 (6.0 x 5.3 x 6.0)

```
Orthographic front elevation of a HEAVY three-storey reinforced concrete industrial building. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Massive brutalist construction: thick concrete piers between recessed bays, heavy lintels, exposed steel bracing. Divided into three equal horizontal floor bands. BOTTOM band: a tall arched machinery opening with a rusted steel shutter at the centre. MIDDLE band: deep-set industrial windows with steel grilles. TOP band: smaller windows and a heavy projecting concrete cornice along the very top. Dark weathered concrete, heavy rust streaks, spalled patches showing rebar. Flat overcast lighting, no cast shadows, no text, no logos, no people. Clean readable mobile game texture asset.
```

### t5_solid — 견고 건물 (12.0 x 9.8 x 12.0)

```
Orthographic front elevation of a HUGE SIX-STOREY derelict concrete factory tower, the largest building on the site. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Divided into six equal horizontal floor bands. BOTTOM band: a large rusted roll-up service door and a boarded double doorway at the centre, clearly wider and taller than any window. MIDDLE four bands: rows of identical rectangular industrial windows, many cracked or boarded over, with thin concrete sills, separated by vertical pilasters. TOP band: a flat ROOFTOP edge - concrete parapet, bent safety railing, rusted ventilation ducts and a water tank silhouette sitting on the roof slab. The top band must read unmistakably as a ROOF, not another storey. Weathered grey concrete, rust streaks, peeling paint, patched panels. Flat overcast lighting, no cast shadows, no text, no logos, no people. Clean readable mobile game texture asset.
```

### t6_brick — 벽돌 창고 (8.0 x 7.2 x 8.0) · 온전

```
Orthographic front elevation of a derelict four-storey RED BRICK warehouse block, old masonry construction. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. Dark red-brown brickwork with pale mortar lines, soot staining and efflorescence. Divided into four equal horizontal floor bands. BOTTOM band: a tall arched brick loading bay with a rusted steel door at the centre. MIDDLE two bands: rows of tall arched windows with stone lintels and sills, several bricked up or boarded. TOP band: a corbelled brick cornice and a low parapet along the very top. Cracked mortar, missing bricks in places, a faded painted advertisement ghost sign worn almost away. Flat overcast lighting, no cast shadows, no readable text, no logos, no people. Clean readable mobile game texture asset.
```

### t6_brick · 파손

```
Keep the EXACT same brick building: same brickwork colour, same mortar, same window positions, same arched loading bay, same floor bands, same framing, same flat orthographic elevation filling the whole frame. Change ONLY its condition to DAMAGED: wide structural cracks zigzagging across the brickwork, several patches where bricks have fallen away exposing dark cavities, two windows blown out with bent frames, part of the top cornice broken off, scorch and dust smears. The wall is still standing and still fills the entire frame. No sky, no ground, no rubble pile, no margin, no background.
```

### t6_brick · 잔해

```
Keep the EXACT same brickwork colour, mortar and material. Now the wall is almost gone: only a low broken STUMP of the brick wall remains along the bottom, perhaps one third of the original height, with a ragged jagged top edge where it has collapsed. Loose bricks and crushed masonry heaped against it. Above the stump there is nothing - leave that area plain flat neutral grey, completely empty. Still a flat orthographic elevation filling the whole frame, no perspective. No sky, no ground plane, no margin, no background, no people.
```

### t7_glass — 유리 사무동 (4.6 x 5.0 x 4.6) · 온전

**2026-10-08 신설.** 바닥 면적 사다리에서 제일 벌어진 칸(t3 12.25 -> t4 36, 2.9배 점프)을 메운다. 재질로도 구분을 노렸다 — 나머지 여섯이 회색 콘크리트 넷 · 붉은 벽돌 하나 · 회색 금속 하나라 청록 유리가 멀리서 한눈에 갈린다.

```
Orthographic front elevation of an abandoned three-storey GLASS CURTAIN WALL office building at a derelict industrial site, roughly as wide as it is tall. Perfectly flat straight-on architectural elevation, ZERO perspective, camera exactly perpendicular to the wall. The facade FILLS THE ENTIRE FRAME edge to edge - no sky, no ground, no margin, no border, no background. A regular grid of large blue-green tinted glass panels held in slim dark anodised aluminium mullions, divided into three equal horizontal floor bands. BOTTOM band: a recessed entrance with dark glass doors and a dented metal kick plate. MIDDLE and TOP bands: continuous glazing, several panels cracked with spiderweb fractures, two panels missing showing a dark empty interior, dirt and water streaks running down the glass. A slim flat parapet along the very top. Muted dusty reflections, no bright highlights or sun glare, flat overcast lighting, no cast shadows, no readable text, no logos, no people. Clean readable mobile game texture asset.
```

### t7_glass · 파손

```
Keep the EXACT same glass office building: same blue-green glass tint, same aluminium mullion grid, same floor bands, same entrance position, same framing, same flat orthographic elevation filling the whole frame. Change ONLY its condition to DAMAGED: many glass panels shattered leaving jagged shards in their frames, whole sections of glazing gone showing dark empty floor slabs inside, several mullions bent and twisted out of line, dust and scorch smears across the remaining glass. The building is still standing and still fills the entire frame. No sky, no ground, no rubble pile, no margin, no background.
```

### t7_glass · 잔해

```
Keep the EXACT same aluminium frame colour and blue-green glass material. Now the building is almost gone: only a low broken STUMP of the ground floor remains along the bottom, perhaps one third of the original height, with a ragged jagged top edge of snapped mullions and shattered glass. Crushed glass and twisted metal framing heaped against it. Above the stump there is nothing - leave that area plain flat neutral grey, completely empty. Still a flat orthographic elevation filling the whole frame, no perspective. No sky, no ground plane, no margin, no background, no people.
```

### 공용 파손 (t1 · t3 · t4 · t5)

이 넷은 온전한 그림을 `ImageInput`으로 올리고 같은 지시문 하나를 돌려 썼다.

```
Keep the EXACT same building: same material, same colour, same window and door positions, same floor bands, same proportions, same flat orthographic elevation filling the entire frame edge to edge. Change ONLY its condition to DAMAGED: wide structural cracks spreading across the wall, several patches where the surface has broken away exposing dark cavities and bent reinforcement, two or three windows blown out with twisted frames, part of the top edge broken off, soot and dust smears. The wall is STILL STANDING and still fills the entire frame. No sky, no ground, no rubble pile on the floor, no margin, no border, no background, no people.
```

### 공용 잔해 (t1 · t3 · t4 · t5)

```
Keep the EXACT same material and colour. Now the building is almost gone: only a low broken STUMP of the ground floor remains along the BOTTOM of the frame, about one quarter of the original height, with a ragged jagged top edge where it collapsed, and loose broken chunks heaped along that edge. The original ground-floor doorway is still recognisable in the stump. EVERYTHING ABOVE the stump must be plain flat neutral grey, completely empty, no sky, no haze, no debris floating. Still a flat orthographic elevation filling the whole frame, zero perspective. No ground plane, no margin, no border, no people.
```

---

## 바닥 · 표면 텍스처

이쪽은 **이어 붙이는 것이 목적**이라 정면도와 정반대로 쓴다. `wrapMode=Repeat`에 이방성 8이 필요하다 (`GroundImportSettings.cs`) — 지면을 50도로 비스듬히 보기 때문에 이방성이 1이면 거리가 멀어질수록 뭉개진다.

### T_Ground_Site — 현장 바닥

**실측**: 카메라가 담는 폭이 16.4m라 화면에서 1m가 117px이다. 1024px 그림이면 8.7m마다 반복돼야 1:1로 또렷하다. `Demo/19. 배경 입히기`가 그 계산을 해서 타일링을 건다 — 값을 손으로 적어 둔 곳은 없다.

```
Seamless tileable texture of the bare ground of a demolition site. Flat orthographic top-down view, evenly lit, no shadows, no perspective. Compacted grey-brown dirt and crushed stone, faint tyre and track marks, scattered small concrete chips and grit, patches of pale dust, a few dry weeds in cracks. Flat and uniform enough to tile across a large area without an obvious repeating feature. Fills the entire frame edge to edge, no background, no margin, no text. Muted and low contrast so that debris and buildings read clearly on top of it.
```

### T_Roof_Flat — 옥상 (일곱 종 공용)

**2026-10-08 신설.** 카메라가 50도로 내려다보므로 한 변 s인 상자에서 윗면의 화면 투영 면적은 `s^2 * sin50 = 0.766 s^2`이고 벽면은 `0.643 s^2`다 — **지붕이 어느 벽보다 크게 보인다.** 거기에 벽 그림이 붙어 있어서 건물이 바닥에 깔린 그림판처럼 보였다. `darker than bare ground`를 넣어 지면과 갈리게 했다.

```
Seamless tileable texture of the FLAT ROOF of a derelict industrial building, seen from directly above. Flat orthographic top-down view, evenly lit, no shadows, no perspective, no building edges or parapet in frame. Dark grey weathered bitumen roofing felt in overlapping strips with visible seams, scattered loose grey gravel ballast, pale dried puddle stains, patches of tar repair, light rust streaks, a few cracks with moss. Muted and low contrast, darker than bare ground so the roofs read as a different surface from the site floor. Flat and uniform enough to tile across a large area without an obvious repeating feature. Fills the entire frame edge to edge, no background, no margin, no text, no people, no rooftop equipment.
```

### T_Rubble_Concrete

```
Seamless tileable texture of a broken chunk of grey reinforced CONCRETE rubble. Flat orthographic top-down view, evenly lit, no shadows, no perspective. Rough fractured surface with sharp broken edges, exposed grey aggregate stones, fine crack lines, pale concrete dust settled in the hollows, a short stub of rusted rebar. Dry and dusty. Fills the entire frame edge to edge, no background, no margin, no object silhouette, no text. Gritty but still readable at small size on a mobile screen.
```

### T_Rubble_Brick

```
Seamless tileable texture of broken RED BRICK rubble. Flat orthographic top-down view, evenly lit, no shadows, no perspective. Snapped brick fragments in dark red and orange-brown, crumbling pale mortar still clinging to the faces, sharp fractured edges, brick dust in the hollows. Fills the entire frame edge to edge, no background, no margin, no object silhouette, no text. Gritty but still readable at small size on a mobile screen.
```

### T_Rubble_Metal

```
Seamless tileable texture of twisted scrap METAL debris. Flat orthographic top-down view, evenly lit, no shadows, no perspective. Buckled corrugated steel sheeting, torn edges, flaking blue-grey paint over heavy orange rust, bent bolts and a snapped steel bar. Dirty industrial salvage. Fills the entire frame edge to edge, no background, no margin, no object silhouette, no text. Gritty but still readable at small size on a mobile screen.
```

### T_Concrete_BaseColor

```
Seamless tileable texture of weathered grey concrete wall of a derelict industrial building. Flat orthographic view, evenly lit with no shadows and no perspective. Visible cracks, water stains, patches of exposed aggregate, faded paint. Gritty and realistic but clean enough to read at small size on a mobile screen. No text, no logos, no objects, no people.
```

---

## 비용과 설정

| 항목 | 값 |
|---|---|
| 노드 | `GenerateImage` / `EditImage`, 모델 `gpt-image-2.5-flare`, `aspectRatio 1:1`, `count 1` |
| 크레딧 | 장당 20 (`GENERATE_IMAGE_V2`). 구형 `GENERATE_IMAGE`는 200이니 V2 노드를 고를 것 |
| 이번 작업 | 7장 (t7 세 장 + t2 세 장 + 지붕 한 장) = 140 크레딧 |
| 받는 곳 | `https://3d.varco.ai/api/objects/<hash>.png` — 인증 없이 `curl`로 받는다 |
| 정면도 넣는 곳 | `Assets/_Project/Art/Facades/T_<id>_front.png` (그리고 `_front_damaged` · `_front_rubble`) |
| 바닥·지붕 넣는 곳 | `Assets/_Project/Art/Textures/` |
| 연결 | `Demo/17. 건물 정면도 연결` — 파일 이름만 맞으면 꽂힌다 |
| 확인 | `Demo/18. 배경·비율 점검` — 그림이 몇 배로 늘어나 붙는지 잰다 |
