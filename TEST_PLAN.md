# 테스트 확충 계획 (차후 과제)

> 작성: 2026-07-09 · 상태: **0단계 완료 (2026-07-10)** — 1단계 진행 가능
> 현황: xUnit 2.9 · 테스트 25개(헬퍼 2파일: CsvEscape, NeisHelper) / 프로덕션 약 8.5만 줄
> 문제의식: 2026-07 점검에서 발견된 버그들(이름 저장 유실, 학기 필터 무시, MessageBox 인자 뒤바뀜,
> 졸업일 연도 오프바이원 등)은 전부 기초적인 서비스/리포지토리 테스트로 잡혔을 부류였다.

## 전략

- UI(WinUI) 자동화 테스트는 비용 대비 효과가 낮아 **제외**.
- 버그가 실제로 나온 계층 = **리포지토리 · 서비스 · 파서**에 집중.
- 목킹 대신 **임시 파일 SQLite + DatabaseInitializer 실스키마**를 쓰는 통합형 단위 테스트.
  (리포지토리들이 생성자에서 dbPath 를 받으므로 주입 가능)

## 단계별 계획 — 총 신규 약 150개 (누적 약 175개)

### 0단계. 테스트 인프라 (선행, 약 반나절)
- [x] `SqliteTestFixture` — 임시 파일 DB 생성 → `DatabaseInitializer` 스키마 초기화 → FK 대상(School·Teacher) 시드 → Dispose 시 풀 정리 후 삭제
- [x] 시드 빌더(TestData) — 학생/학적 빌더 + 실규칙 학생ID 생성 (과목은 1단계에서 추가)
- [x] 파라미터리스 서비스 4종(Enrollment·StudentLog·StudentSpecial·Course)에 dbPath 주입 생성자 추가
- [ ] 정적 `Settings` 의존 로직 정리 — 테스트에서 경로 오버라이드하거나 순수 함수로 추출
      (예: 졸업일 계산, GetPeriodNow 의 시각 의존) — *리포지토리 계층은 불필요 확인, 2단계 서비스 테스트 때 필요분만*

### 1단계. 리포지토리 CRUD·경계 (~60개, 1~2세션)
대상: Enrollment · StudentLog · StudentSpecial · ClassTimetable · Course/CourseSection · Post(Board)
- [ ] CRUD 왕복(Insert→Get→Update→Delete)
- [ ] IN 배치 조회 — 빈 목록 / 1건 / 다건 / 존재하지 않는 ID 혼재
- [ ] `semester=0` = 학년도 전체 조회 계약
- [ ] `IsDeleted` 논리삭제 필터
- [ ] (학년·반·번호) 중복 방어 — DB UNIQUE 제약이 없으므로 조회 기반 검사 로직 검증
- [ ] 트랜잭션 롤백 — Student+Enrollment 동시 저장 실패 시 원자성

### 2단계. 서비스 로직·회귀 방지 (~40개, 1세션)
2026-07 점검에서 잡은 버그의 재발 방지가 핵심:
- [ ] `EnrollmentService.GraduateAsync` — 졸업일 = 학년도+1년 2월 말일(윤년 2/29 포함)
- [ ] `StudentService.UpdateBasicInfoAsync` — Student.Name 갱신 시 Enrollment.Name 자동 동기화
- [ ] `SchoolScheduleService` — 날짜범위 상한 배타(AA_YMD < End), 학년도 종료일 = 다음 해 2월 말일
- [ ] NEIS 동기화 중복 제외(기존 데이터와 dedupe)
- [ ] `BoardService.UpdatePostIsCompletedAsync` / `GetMemosAsync(includeCompleted)` 계약
- [ ] Settings 파서 — bool("true"/"false"), TimeSpan, int 라운드트립

### 3단계. 헬퍼·파서 (~30개, 반 세션)
사용자 데이터가 걸린 파싱 경로 보호:
- [ ] Excel 헤더 탐지(번호/이름/성명/학년/반/성별 열 찾기, 10행 내)
- [ ] `TryParseNumberFromText` — "1학년"→1, "3반"→3, 공백/비숫자
- [ ] `NormalizeSex` — 남/여/M/F/남자/여자/기타
- [ ] 학생ID 생성 규칙(학교코드7+연도4+일련4 = 15자리) 및 고유성 검사
- [ ] `Functions.GetPeriodNow` — 주말/등교 전/조례/교시 경계/방과후
- [ ] `GetWeekNumber` — 학기 시작일 기준 주차 계산

### 4단계. ViewModel 변환 로직 (선택, ~20개, 반 세션)
- [ ] StudentLogViewModel 변환(CreateAsync 포함)
- [ ] SchoolScheduleGroupHelper 그룹핑(연속 일정 묶음, 방학 표시)
- [ ] KEvent 할일/일정 필터·정렬

## 실행 방법

```bash
dotnet test NewSchool.Tests -p:Platform=x64
```

CI 없이도 로컬에서 실행 가능 — **게시(publish) 전 1회 실행을 습관화**한다.

## 완료 기준

| 단계 | 산출 | 판정 |
|---|---|---|
| 0 | Fixture·시드 인프라 | Enrollment 왕복 스모크 1개 통과 |
| 1 | ~60개 | 6개 리포지토리 전부 CRUD+경계 커버 |
| 2 | ~40개 | 2026-07 발견 버그 전 항목 회귀 테스트 존재 |
| 3 | ~30개 | 엑셀 일괄 입력 파싱 경로 전부 커버 |

총 예상 작업량: **3~4세션**. 1·2단계만 완료해도 이번에 발견된 버그 부류는 전부 회귀망에 들어간다.
