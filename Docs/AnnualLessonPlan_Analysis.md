# 연간수업계획 (AnnualLessonPlanPage) 구현 분석

**분석일**: 2026-02-06  
**상태**: 구현 완료 (리팩토링 필요)  
**관련 설계 문서**: `연간수업 계획 설계.md`

---

## 1. 전체 구조

```
AnnualLessonPlanPage (3탭 Pivot)
├── 탭1: 단원 관리 ─── CourseSection (CRUD + CSV + 드래그 정렬)
├── 탭2: 시수 관리 ─── WeeklyClassHoursRow (시간표×학사일정 자동계산)
└── 탭3: 단원 배치 ─── SchedulingEngine (Anchor-Fill-Alert 알고리즘)
```

### 데이터 흐름

```
Course 선택 (CmbCourse)
  → LoadCourseSchedulesAsync()     // 시간표 로드 (Lesson 테이블)
  → BuildWeeklyClassTableAsync()   // 시수 테이블 생성 (학사일정 반영)
  → LoadCourseSectionsAsync()      // 단원 로드 (CourseSection 테이블)
```

---

## 2. 관련 파일 목록 (11개)

| 파일 | 역할 | 줄 수(약) |
|------|------|-----------|
| `Pages/AnnualLessonPlanPage.xaml` | UI (3탭 Pivot) | ~500 |
| `Pages/AnnualLessonPlanPage.xaml.cs` | Code-behind (핵심 로직 다수 포함) | ~1100 |
| `Pages/AnnualLessonPlanPageParameter.cs` | 네비게이션 파라미터 | ~20 |
| `ViewModels/AnnualLessonPlanViewModel.cs` | ViewModel | ~350 |
| `Models/CourseSection.cs` | 단원 모델 (대>중>소 계층) | ~250 |
| `Models/WeeklyUnitPlan.cs` | 주차별 단원 배치 모델 | - |
| `Services/SchedulingEngine.cs` | Anchor-Fill-Alert 배치 엔진 | ~400 |
| `Services/WeeklyHoursCalculator.cs` | 주차별 시수 계산 | - |
| `Services/WeeklyHoursHelper.cs` | 시수 편집/일괄추가 | - |
| `Services/ScheduleShiftService.cs` | 스케줄 이동 + Undo/Redo | - |
| `Services/ReportExportService.cs` | 엑셀 내보내기 | - |
| `Dialogs/CourseSectionDialog.xaml` | 단원 편집 다이얼로그 | - |
| `Repositories/CourseSectionRepository.cs` | 단원 DB CRUD | - |
| `Repositories/ScheduleRepository.cs` | 스케줄 DB | - |
| `Repositories/ScheduleUnitMapRepository.cs` | 스케줄-단원 매핑 | - |
| `Repositories/UndoHistoryRepository.cs` | Undo/Redo 이력 | - |

---

## 3. 탭별 기능 상세

### 탭1: 단원 관리

**CRUD**
- CourseSectionDialog를 통한 추가/편집
- 인라인 삭제 (Button.Tag 바인딩)
- `ObservableCollection<CourseSection> _courseSections` 관리

**CSV 입출력**
- 가져오기: `ParseCsv()` → BOM UTF-8, 16개 필드 지원
- 내보내기: `GenerateCsv()` → BOM UTF-8
- 템플릿 다운로드: `GenerateCsvTemplate()` → 예시 데이터 포함
- CSV 필드 순서: 대단원번호, 대단원명, 중단원번호, 중단원명, 소단원번호, 소단원명, 시작페이지, 끝페이지, 예상차시, 유형, 학습목표, 수업계획, 자료파일, 자료링크, 메모, 고정날짜

**드래그 정렬**
- `CanReorderItems=True`, `AllowDrop=True`, `ReorderMode=Enabled`
- `DragItemsCompleted` → 각 항목의 SortOrder를 개별 `UpdateAsync`로 DB 저장
- `ContainerContentChanging` → 동적 연번 표시 (1부터)

**단원 유형** (`CourseSection.SectionType`)
- Normal: 일반수업
- Exam: 지필고사 (자동 IsPinned)
- Assessment: 수행평가 (자동 IsPinned)
- Event: 행사/기타

**통계 표시**
- 대단원·중단원·소단원 개수, 총 차시
- 유형별 카운트 (일반, 지필, 수행, 고정)

### 탭2: 시수 관리

**자동 계산 로직** (`BuildWeeklyRowsWithTimetable`)
- 입력: Course + Lesson(시간표) + SchoolSchedule(학사일정)
- 과정:
  1. 학기 기간을 주 단위로 분할 (월~금)
  2. 각 주의 수업 가능일 계산 (공휴일·방학 제외, 학년별 행사 고려)
  3. 수업 가능일의 시간표 시수를 학급별로 합산
- 출력: `List<WeeklyClassHoursRow>` (주차별 학급별 시수)

**NEIS 학사일정 연동**
- `SchoolScheduleService.DownloadFromNeisAsync()` 호출
- 필수 설정: SchoolCode, ProvinceCode, NeisApiKey, WorkYear
- 학기 기간: 1학기 3/1~7/31, 2학기 8/1~다음해 2월말

**수동 편집**
- 셀 클릭 → TextBlock을 NumberBox로 교체
- LostFocus 시 `ManualHours` 딕셔너리에 저장
- 수동 편집값은 AccentColor + SemiBold로 시각 구분
- `GetEffectiveHours()`: ManualHours 우선, 없으면 자동계산값

**테이블 구조** (코드 기반 동적 생성)
```
주차 | 기간 | 일수 | [학급1] | [학급2] | ... | 합계 | 비고(학사일정)
```
- 학급 열은 `Course.RoomList` 기반 동적 생성
- 합계 행 별도 생성

**정보 카드 (상단)**
- 시간표 정보: 주당 시수, 학급 수, 요일별 배치
- NEIS 학사일정: 공휴일 수, 방학일 수, 주요 행사
- 통계 요약: 총 주차, 수업일, 총 시수, 단원시수

**시수 비교 경고 (InfoBar)**
- 총 시수 > 단원시수: "여유 시수" (Informational)
- 총 시수 < 단원시수: "시수 부족" (Warning)

### 탭3: 단원 배치

**SchedulingEngine** (Anchor-Fill-Alert 3단계)
1. **Anchor**: 고정 단원(IsPinned=true) → PinnedDate에 배치, 없으면 가장 가까운 슬롯
2. **Fill**: 일반 단원 → SortOrder 순서대로 빈 슬롯에 순차 배치 (EstimatedHours 기준)
3. **Alert**: 미배치 단원 목록 반환

**슬롯 생성** (`GenerateAvailableSlots`)
- Lesson 테이블의 요일별 시간표 × 학기 날짜 범위
- 공휴일·방학 제외
- 각 슬롯 = (날짜, 교시, 학급)

**배치 실행** (`PlaceSectionToSlotAsync`)
- Schedule 레코드 생성/조회
- ScheduleUnitMap 매핑 생성
- 고정 단원이면 Schedule.IsPinned = true

**Undo/Redo**
- `ScheduleShiftService` + `UndoHistoryRepository`
- Course + Room 기준으로 이력 관리
- 매번 5개 Repository를 using으로 생성

**학급 선택**: CmbRoom ComboBox
**기간 설정**: CalendarDatePicker (시작일/종료일)
**확정**: DRAFT → CONFIRMED 상태 전환

---

## 4. 데이터 모델 상세

### CourseSection (Models/CourseSection.cs)

```
핵심 필드:
- No (PK), Course (FK→Course.No)
- UnitNo, UnitName (대단원)
- ChapterNo, ChapterName (중단원)
- SectionNo, SectionName (소단원)
- StartPage, EndPage (교과서 페이지)
- EstimatedHours (예상 차시, 기본값 1)
- SortOrder (정렬 순서)
- SectionType (Normal/Exam/Assessment/Event)
- IsPinned, PinnedDate (날짜 고정)
- LearningObjective (학습 목표)
- MaterialPath, MaterialUrl (자료)
- Memo (수업 후기)
- LessonPlan (교수학습 활동)

주요 Computed Properties:
- FullPath → "1-1-1 덧셈과 뺄셈"
- IsFixed → IsPinned || IsEvaluation
- SectionTypeDisplay → "일반"/"지필고사"/"수행평가"/"행사"
- HoursDisplay → "2차시"
- PageRangeDisplay → "p.8~12"
```

### WeeklyClassHoursRow (Page.xaml.cs 내 정의)

```
- Week, WeekDisplay, DateRange
- StartDate, EndDate
- ClassDaysCount, ClassDays (List<DateTime>)
- ClassHours (Dictionary<string, int>): 학급별 자동계산 시수
- ManualHours (Dictionary<string, int?>): 학급별 수동편집 시수
- Remark (학사일정 이벤트)
- GetEffectiveHours(className): ManualHours 우선 반환
```

### HoursCellData (Page.xaml.cs 내 정의)

```
- Row (WeeklyClassHoursRow)
- ClassName (string)
```

---

## 5. 아키텍처 이슈 및 개선 포인트

### ⚠️ 이슈 1: Code-behind 과부하 (심각)

Page.xaml.cs에 약 **1,100줄**의 로직이 집중:
- `BuildWeeklyRowsWithTimetable()` — 시수 계산 비즈니스 로직
- `BuildTableHeader()` / `BuildTableBody()` — C# 코드로 UI 동적 생성 (~200줄)
- `IsHoliday()`, `GetClassDaysInWeek()`, `GetWeekEvents()` — 학사일정 처리 로직
- CSV 파싱/생성 로직
- `WeeklyClassHoursRow`, `HoursCellData` 데이터 클래스

**개선 방향**: ViewModel로 비즈니스 로직 이관, 데이터 클래스 별도 파일 분리

### ⚠️ 이슈 2: ViewModel과 Page 역할 분리 미흡

- `AnnualLessonPlanViewModel`이 존재하지만 Page에서 직접 Repository 호출
- ViewModel의 `GenerateUnitPlanAsync()`는 빈 메서드 (주석: "Page에서 직접 호출")
- ViewModel의 Courses/YearPlans/WeeklyHours 컬렉션이 사실상 미사용
- Page에서 자체 필드 `_weeklyRows`, `_courseSections`, `_courseSchedules` 등 관리

**현재 사용되는 ViewModel 기능**: Year/Semester 속성, SelectedCourse 설정 정도

### ⚠️ 이슈 3: 시수 테이블 코드 기반 UI

- `BuildTableBody()`에서 Grid/Border/TextBlock을 C# 코드로 수동 생성
- 수동 편집 시 NumberBox 교체도 코드로 처리
- ListView가 아닌 StackPanel에 직접 Children.Add 방식
- 합계 행 업데이트도 Children 순회로 처리

**개선 방향**: ItemsRepeater 또는 DataGrid 컨트롤로 전환

### ⚠️ 이슈 4: NEIS 학사일정 캐싱 미흡

- 페이지 로드 시 매번 NEIS API 호출 (`LoadSchoolSchedulesAsync`)
- 새로고침 버튼도 별도 제공
- 동일 학기 내 반복 호출 방지 로직 없음

**개선 방향**: DB 캐싱 + 타임스탬프 기반 갱신 전략

### ⚠️ 이슈 5: Undo/Redo 서비스 생성 비효율

```csharp
// 매번 5개 Repository를 using으로 생성
using var scheduleRepo = new ScheduleRepository(SchoolDatabase.DbPath);
using var mapRepo = new ScheduleUnitMapRepository(SchoolDatabase.DbPath);
using var undoRepo = new UndoHistoryRepository(SchoolDatabase.DbPath);
using var lessonRepo = new LessonRepository(SchoolDatabase.DbPath);
using var schoolScheduleRepo = new SchoolScheduleRepository(SchoolDatabase.DbPath);
var shiftService = new ScheduleShiftService(...);
```

**개선 방향**: DI 또는 서비스 팩토리 패턴 적용

### ⚠️ 이슈 6: 기타

- `WeeklyClassHoursRow`가 Page.xaml.cs 파일 하단에 선언됨 → Models 폴더로 분리 필요
- 엑셀 내보내기 시 매번 4개 Repository 생성
- `SectionListView`와 `SectionSummaryListView`에 같은 데이터소스를 별도로 바인딩
- `GetSemesterDateRange()`가 Page에 하드코딩 (1학기: 3/1~7/31, 2학기: 8/1~다음해 2월말)

---

## 6. 설계 문서와의 차이점

| 항목 | 설계 문서 (v1.1) | 현재 구현 |
|------|-------------------|-----------|
| 단원 모델 | CurriculumUnit (별도) | CourseSection (통합, 대>중>소 계층) |
| 시수 테이블 | WeeklyLessonHours (DB 저장) | WeeklyClassHoursRow (메모리, 학급별 분리) |
| 배치 엔진 | UnitPlanGenerator (단순 순차) | SchedulingEngine (Anchor-Fill-Alert 3단계) |
| 배치 단위 | 주차 단위 | 슬롯 단위 (날짜+교시) |
| 매핑 | WeeklyUnitPlan | Schedule + ScheduleUnitMap (2테이블) |
| Undo/Redo | 미설계 | UndoHistoryRepository + ScheduleShiftService |
| CSV 입출력 | 미설계 | 16필드 CSV (템플릿 포함) |
| NEIS 연동 | SchoolSchedule 참조만 | 페이지 로드 시 자동 다운로드 |
| 진행 추적 | ProgressTracker 설계됨 | ProgressMatrixPage에서 별도 구현 |

---

## 7. 리팩토링 우선순위 제안

1. **WeeklyClassHoursRow, HoursCellData** → `Models/` 폴더로 분리
2. **시수 계산 로직** (`BuildWeeklyRowsWithTimetable` 등) → ViewModel 또는 Service로 이관
3. **시수 테이블 UI** → XAML 기반 (ItemsRepeater + DataTemplate)으로 전환
4. **CSV 처리 로직** → 별도 Service 클래스로 분리
5. **ViewModel 정리** → 미사용 컬렉션 제거, Page의 비즈니스 로직 흡수
6. **NEIS 캐싱** → SchoolScheduleService에 캐시 레이어 추가
7. **Repository 생성 패턴** → DI 또는 팩토리 패턴 적용

---

## 8. 참고: SchedulingEngine 알고리즘 상세

```
입력:
  - CourseSection[] (단원 목록, IsPinned/PinnedDate 포함)
  - Lesson[] (시간표, Room 필터링)
  - SchoolSchedule[] (휴일 목록)
  - 학기 기간 (startDate ~ endDate)

Step 1 - 슬롯 생성:
  for each date in 학기기간:
    if 휴일이면 skip
    for each lesson in 해당요일시간표:
      slots.Add(date, period, room)

Step 2 - Anchor (고정 배치):
  for each pinnedSection (PinnedDate순):
    slot = slots.Find(date == PinnedDate && !occupied)
    if not found:
      slot = slots.FindNearest(date >= PinnedDate && !occupied)
    PlaceSection(slot, section)

Step 3 - Fill (순차 배치):
  emptySlots = slots.Where(!occupied).OrderBy(date, period)
  for each normalSection (SortOrder순):
    for i in 0..EstimatedHours:
      PlaceSection(emptySlots[next], section)

Step 4 - Alert (미배치 확인):
  unplaced = sections.Where(not placed)
  return SchedulingResult
```

---

*이 문서는 2026-02-06 시점의 구현 상태를 분석한 것입니다.*
*코드 변경 시 이 문서도 함께 업데이트해야 합니다.*
