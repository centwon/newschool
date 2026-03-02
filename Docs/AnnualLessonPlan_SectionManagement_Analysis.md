# 연간수업계획 - 단원 관리 (탭1) 상세 분석

**분석일**: 2026-02-07  
**상태**: 구현 완료  

---

## 1. 관련 파일 및 역할

| 파일 | 역할 | 줄 수(약) |
|------|------|-----------|
| `Models/CourseSection.cs` | 단원 모델 (대>중>소 계층, 16개 필드 + 20개 Computed) | ~260 |
| `Repositories/CourseSectionRepository.cs` | DB CRUD + 통계 + 일괄처리 | ~380 |
| `Dialogs/CourseSectionDialog.xaml` | 편집 다이얼로그 UI (13개 입력필드) | ~170 |
| `Dialogs/CourseSectionDialog.xaml.cs` | 다이얼로그 로직 (CRUD + 유효성검사) | ~190 |
| `Pages/AnnualLessonPlanPage.xaml` | 탭1 UI (도구모음 + 헤더행 + ListView) | 해당 부분 ~130 |
| `Pages/AnnualLessonPlanPage.xaml.cs` | 탭1 로직 (CSV + 드래그 + 저장) | 해당 부분 ~350 |

---

## 2. 데이터 모델: CourseSection

### 2.1 DB 스키마

```sql
CREATE TABLE IF NOT EXISTS CourseSection (
    No INTEGER PRIMARY KEY AUTOINCREMENT,
    Course INTEGER NOT NULL,              -- FK → Course.No
    UnitNo INTEGER NOT NULL,              -- 대단원 번호
    UnitName TEXT NOT NULL,               -- 대단원명
    ChapterNo INTEGER NOT NULL,           -- 중단원 번호
    ChapterName TEXT NOT NULL,            -- 중단원명
    SectionNo INTEGER NOT NULL,           -- 소단원 번호
    SectionName TEXT NOT NULL,            -- 소단원명
    StartPage INTEGER DEFAULT 0,          -- 시작 페이지
    EndPage INTEGER DEFAULT 0,            -- 끝 페이지
    EstimatedHours INTEGER DEFAULT 1,     -- 예상 차시
    SortOrder INTEGER DEFAULT 0,          -- 정렬 순서 (드래그)
    -- v2 추가 컬럼 (ALTER TABLE 마이그레이션)
    LessonPlan TEXT DEFAULT '',           -- 교수학습 활동
    SectionType TEXT DEFAULT 'Normal',    -- Normal/Exam/Assessment/Event
    IsPinned INTEGER DEFAULT 0,           -- 날짜 고정 여부
    PinnedDate TEXT,                      -- 고정 날짜 (yyyy-MM-dd)
    LearningObjective TEXT DEFAULT '',    -- 학습 목표
    MaterialPath TEXT DEFAULT '',         -- 자료 파일 경로
    MaterialUrl TEXT DEFAULT '',          -- 자료 웹 링크
    Memo TEXT DEFAULT ''                  -- 수업 후기/메모
);

-- 인덱스 4개
idx_coursesection_course   ON CourseSection(Course)
idx_coursesection_sort     ON CourseSection(Course, SortOrder)
idx_coursesection_type     ON CourseSection(Course, SectionType)
idx_coursesection_pinned   ON CourseSection(Course, IsPinned)
```

### 2.2 단원 유형 (SectionType)

| 유형 | 값 | 특성 |
|------|-----|------|
| 일반수업 | `Normal` | 기본값, 순차 배치 |
| 지필고사 | `Exam` | 자동 IsPinned=true, Anchor 배치 |
| 수행평가 | `Assessment` | 자동 IsPinned=true, Anchor 배치 |
| 행사/기타 | `Event` | 일반 배치 |

### 2.3 주요 Computed Properties

```csharp
FullPath          → "1-1-1 덧셈과 뺄셈"        // ListView 단원번호 열
UnitDisplay       → "1. 수와 연산"               // 대단원 표시
ChapterDisplay    → "1-1. 자연수의 혼합 계산"    // 중단원 표시
SectionDisplay    → "① 덧셈과 뺄셈"             // 원문자 연번
SectionTypeDisplay→ "일반"/"지필고사"/"수행평가"  // 한글 유형명
SectionTypeIcon   → "\uE7C3"/"\uE9D9"/"\uE82D"  // Segoe Fluent 아이콘
PageRangeDisplay  → "p.8~12"                     // 페이지 범위
HoursDisplay      → "2차시"                      // 시수 표시
ShortInfo         → "일반 | 2차시 | 📌3/15"      // 요약 정보
IsFixed           → IsPinned || IsEvaluation      // 이동 불가 여부
HasMaterial       → MaterialPath 또는 Url 존재   // 자료 여부
```

---

## 3. UI 구조 (탭1: 단원 관리)

### 3.1 레이아웃

```
┌─ 도구 모음 (Row 0) ────────────────────────────────────────┐
│ [CSV 가져오기] [CSV 내보내기] [템플릿 다운로드] │          │
│ [소단원 추가*] [전체 삭제]  │  [엑셀 내보내기]            │
├─ 헤더 행 (Row 1) ──────────────────────────────────────────┤
│ 연번 │ 단원번호 │ 소단원명      │ 유형 │ 페이지 │ 시수 │  │
├─ ListView (Row 2, * 크기) ─────────────────────────────────┤
│ [빈 상태] 또는 [드래그 가능 ListView]                      │
│   1  │ 1-1-1  │ 덧셈과 뺄셈    │ 일반 │ p.8~12 │ 2차시│🗑│
│   2  │ 1-1-2  │ 곱셈과 나눗셈  │ 일반 │ p.12~15│ 2차시│🗑│
│  ...                                                       │
├─ 하단 (Row 3) ─────────────────────────────────────────────┤
│ [통계: 대3·중6·소15·총30차시 | 지필2·수행1]               │
│ [InfoBar: 에러 표시]                [다음: 시수 계획 →]    │
└────────────────────────────────────────────────────────────┘
```

### 3.2 ListView 열 구성

| 열 | Width | 바인딩 | 비고 |
|----|-------|--------|------|
| 연번 | 40 | 동적 (ContainerContentChanging) | 1부터 시작, 드래그 시 자동 갱신 |
| 단원번호 | 70 | `{x:Bind FullPath}` | "1-1-1" 형식 |
| 소단원명 | * | `{x:Bind SectionName}` | CharacterEllipsis |
| 유형 | 60 | `{x:Bind SectionTypeDisplay}` | 한글 표시 |
| 페이지 | 60 | `{x:Bind PageRangeDisplay}` | "p.8~12" |
| 시수 | 50 | `{x:Bind HoursDisplay}` | AccentColor, SemiBold |
| 삭제 | Auto | Button, Tag=`{x:Bind}` | 🗑 아이콘 |

### 3.3 빈 상태 (SectionEmptyState)

- 아이콘: `&#xE8A5;` (48px)
- 텍스트: "등록된 단원이 없습니다"
- 부가설명: "CSV 파일을 가져오거나 소단원을 추가하세요"

---

## 4. 기능 상세

### 4.1 소단원 추가 (CourseSectionDialog)

**트리거**: `BtnAddSection` → `OnAddSectionClick`

**다이얼로그 구조** (13개 입력필드):

| 영역 | 컨트롤 | 필수 |
|------|--------|------|
| 대단원 번호 | NumberBox (0~20) | |
| 대단원명 | TextBox | |
| 중단원 번호 | NumberBox (0~20) | |
| 중단원명 | TextBox | |
| 소단원 번호 | NumberBox (1~50) | |
| **소단원명** | TextBox | **✓ 필수** |
| 유형 | ComboBox (Normal/Exam/Assessment/Event) | |
| 예상 차시 | NumberBox (1~20) | |
| 시작 페이지 | NumberBox (0~500) | |
| 끝 페이지 | NumberBox (0~500) | |
| 고정 날짜 | CalendarDatePicker | Exam/Assessment 시 표시 |
| 학습 목표 | TextBox (멀티라인, 500자) | |
| 수업 계획 | TextBox (멀티라인) | |
| 자료 파일 | TextBox + 찾아보기 버튼 | |
| 자료 링크 | TextBox (URL) | |
| 메모 | TextBox (멀티라인) | |

**유형 변경 시 동작**:
- Exam/Assessment 선택 → `PinnedDatePanel` 표시 + 자동 `IsPinned = true`
- Normal/Event 선택 → `PinnedDatePanel` 숨김 + `IsPinned = false`

**저장 흐름**:
```
OnPrimaryButtonClick
  → ValidateInput() (소단원명 필수)
  → CreateSectionFromForm() (폼 → CourseSection 객체)
  → CourseSectionRepository.CreateAsync() 또는 UpdateAsync()
  → 다이얼로그 닫힘
  → Page에서 LoadCourseSectionsAsync() 재로드
```

**파일 찾아보기 (OnBrowseMaterialClick)**:
- FileOpenPicker
- 지원 확장자: .ppt, .pptx, .pdf, .doc, .docx, .hwp, .hwpx, *
- WinRT.Interop으로 윈도우 핸들 전달

### 4.2 소단원 편집

**트리거**: ListView `IsItemClickEnabled=True` → `OnSectionItemClick`

```csharp
var dialog = new CourseSectionDialog(_selectedCourse, section);  // section 전달
var result = await dialog.ShowAsync();
if (result == ContentDialogResult.Primary)
    await LoadCourseSectionsAsync(_selectedCourse.No);  // 전체 재로드
```

- 동일한 CourseSectionDialog 재사용 (생성자에 section 전달)
- `_isEditMode = true` → Title "소단원 편집", LoadSectionData() 호출
- 저장 시 `repo.UpdateAsync(section)`

### 4.3 소단원 삭제

**트리거**: ListView 아이템 내 삭제 Button → `OnDeleteSectionClick`

```csharp
// Button.Tag에 CourseSection 바인딩
if (sender is Button button && button.Tag is CourseSection section)
{
    // ContentDialog 확인
    _courseSections.Remove(section);     // ObservableCollection에서 제거
    UpdateSectionUI();                    // UI 갱신
    await SaveSectionsAsync();            // DB 일괄 저장 (⚠️ BulkCreate)
}
```

**⚠️ 이슈**: 단일 삭제인데 `SaveSectionsAsync()` → `BulkCreateAsync()` 호출
→ 기존 전체 삭제 + 전체 재생성 (비효율적, 아래 4.8에서 상세)

### 4.4 전체 삭제

**트리거**: `BtnClearAll` → Flyout 확인 → `OnClearAllConfirmClick`

```csharp
_courseSections.Clear();
UpdateSectionUI();
await SaveSectionsAsync();  // BulkCreateAsync (빈 리스트 → 전부 삭제만 수행)
```

### 4.5 드래그 정렬

**ListView 설정**:
```xml
CanReorderItems="True"
AllowDrop="True"
ReorderMode="Enabled"
DragItemsCompleted="OnSectionDragCompleted"
ContainerContentChanging="OnSectionContainerContentChanging"
```

**드래그 완료 처리 (`OnSectionDragCompleted`)**:
```csharp
for (int i = 0; i < _courseSections.Count; i++)
{
    section.SortOrder = i + 1;
    await repo.UpdateAsync(section);  // 개별 UpdateAsync 호출 (N번 DB 접근)
}
RefreshListView();  // ItemsSource null → 재설정
```

**연번 표시 (`OnSectionContainerContentChanging`)**:
```csharp
// ItemContainer.ContentTemplateRoot → Grid → 첫 번째 TextBlock
int displayIndex = args.ItemIndex + 1;
indexText.Text = displayIndex.ToString();
```

**⚠️ 이슈**: 
1. 드래그 시 `repo.UpdateAsync()` N번 호출 (트랜잭션 없음)
   - `BulkUpdateSortOrderAsync()` 메서드가 Repository에 존재하지만 미사용
2. `RefreshListView()`가 ItemsSource를 null → 재설정 (깜빡임 가능성)

### 4.6 CSV 가져오기

**트리거**: `BtnImportCsv` → `OnImportCsvClick`

**흐름**:
```
FileOpenPicker (.csv)
  → FileIO.ReadTextAsync (UTF-8)
  → ParseCsv(content)
  → 기존 단원 있으면: ImportConfirmFlyout 표시
    → 확인: ApplyImportSectionsAsync()
  → 기존 단원 없으면: 바로 ApplyImportSectionsAsync()
```

**CSV 형식** (16개 필드, BOM UTF-8):
```
대단원번호,대단원명,중단원번호,중단원명,소단원번호,소단원명,
시작페이지,끝페이지,예상차시,유형,학습목표,수업계획,자료파일,
자료링크,메모,고정날짜
```

**파싱 (`ParseCsv`)**:
- 1행(헤더) 스킵, 2행부터 파싱
- `ParseCsvLine()`: 쌍따옴표 안의 쉼표 처리
- 최소 6개 필드 필요 (대단원번호~소단원명)
- `UnitNo > 0 && SectionName` 비어있지 않으면 유효
- Exam/Assessment → 자동 `IsPinned = true`
- EstimatedHours 파싱 실패 시 기본값 1

**ApplyImportSectionsAsync**:
```csharp
_courseSections.Clear();
foreach (var section in sections)
    _courseSections.Add(section);
await SaveSectionsAsync();  // BulkCreateAsync (전체 삭제 → 전체 생성)
```

### 4.7 CSV 내보내기 / 템플릿

**내보내기 (`OnExportCsvClick`)**:
- FileSavePicker → `GenerateCsv()` → BOM UTF-8 저장
- 필드 이스케이프: 쉼표/따옴표/줄바꿈 포함 시 `""`로 감싸기

**템플릿 (`OnDownloadTemplateClick`)**:
- `GenerateCsvTemplate()` → 예시 데이터 4행 포함
- 예시: 일반수업 2개, 지필고사 1개(PinnedDate 포함), 수행평가 1개

### 4.8 DB 저장 방식 (SaveSectionsAsync)

```csharp
private async Task SaveSectionsAsync()
{
    using var repo = new CourseSectionRepository(SchoolDatabase.DbPath);
    await repo.BulkCreateAsync(_selectedCourse.No, _courseSections.ToList());
}
```

**BulkCreateAsync 내부**:
```csharp
public async Task<int> BulkCreateAsync(int courseNo, List<CourseSection> sections)
{
    // 1. 기존 단원 전부 삭제 (DeleteByCourseAsync)
    //    → LessonProgress 삭제
    //    → ScheduleUnitMap 삭제  
    //    → CourseSection 삭제
    // 2. 새 단원 전부 생성 (CreateAsync × N)
}
```

**⚠️ 심각한 이슈**:
- 단 1개 삭제/추가에도 **전체 삭제 + 전체 재생성** 수행
- `ScheduleUnitMap`까지 삭제됨 → 이미 배치된 스케줄 매핑이 모두 소실
- `LessonProgress`까지 삭제됨 → 수업 진행 기록 연결 소실
- No(PK)가 매번 변경됨 → 다른 테이블의 FK 참조 무효화

---

## 5. Repository 주요 메서드

### 5.1 CRUD

| 메서드 | 용도 |
|--------|------|
| `CreateAsync(section)` | 단일 생성 (INSERT + last_insert_rowid) |
| `BulkCreateAsync(courseNo, sections)` | 일괄 생성 (전체 삭제 후 재생성) |
| `GetByIdAsync(no)` | PK 조회 |
| `GetByCourseAsync(courseNo)` | 과목별 전체 조회 (SortOrder 순) |
| `UpdateAsync(section)` | 단일 수정 (19개 필드) |
| `DeleteAsync(no)` | 단일 삭제 |
| `DeleteByCourseAsync(courseNo)` | 과목 전체 삭제 (연관 테이블 포함) |

### 5.2 조회 (배치 엔진용)

| 메서드 | 용도 | 정렬 |
|--------|------|------|
| `GetPinnedSectionsAsync(courseNo)` | Anchor 배치 대상 | PinnedDate, SortOrder |
| `GetNormalSectionsAsync(courseNo)` | Fill 배치 대상 | SortOrder, UnitNo, ChapterNo |
| `GetByTypeAsync(courseNo, type)` | 유형별 필터 | SortOrder |

### 5.3 통계

| 메서드 | 반환 |
|--------|------|
| `GetTotalEstimatedHoursAsync(courseNo)` | `int` (총 예상 차시) |
| `GetHoursByTypeAsync(courseNo)` | `Dictionary<string, int>` (유형별 차시) |
| `GetUnitsAsync(courseNo)` | `List<(int UnitNo, string UnitName)>` (대단원 목록) |

### 5.4 부분 수정

| 메서드 | 용도 |
|--------|------|
| `SetPinnedDateAsync(no, date)` | 고정 날짜만 변경 |
| `SetSectionTypeAsync(no, type)` | 유형만 변경 |
| `UpdateMemoAsync(no, memo)` | 메모만 변경 |
| `BulkUpdateSortOrderAsync(sections)` | SortOrder 일괄 변경 (트랜잭션) |

### 5.5 테이블 마이그레이션

```csharp
AddNewColumnsIfNeeded()
// v2 추가 컬럼: LessonPlan, EndPage, SectionType, IsPinned, 
//               PinnedDate, LearningObjective, MaterialPath, MaterialUrl, Memo
// pragma_table_info로 존재 여부 확인 후 ALTER TABLE ADD COLUMN
```

---

## 6. 통계 표시 (UpdateSectionUI)

```csharp
// 탭1 하단 통계
TxtSectionStatistics.Text = "대단원 3개 · 중단원 6개 · 소단원 15개 · 총 30차시 | 지필 2개 · 수행 1개";

// 탭3 단원 요약
TxtUnitSummary.Text = "3개 대단원 · 15개 소단원 · 30차시";

// 탭3 유형별 통계 (4개 Badge)
TxtNormalCount.Text = "일반: 12";
TxtExamCount.Text = "지필: 2";
TxtAssessmentCount.Text = "수행: 1";
TxtPinnedCount.Text = "📌 고정: 3";
```

**통계 계산**:
- 대단원 수: `_courseSections.Select(s => s.UnitNo).Distinct().Count()`
- 중단원 수: `_courseSections.Select(s => (s.UnitNo, s.ChapterNo)).Distinct().Count()`
- 총 차시: `_courseSections.Sum(s => s.EstimatedHours)`

---

## 7. 이슈 및 개선 포인트

### 🔴 심각 (데이터 손실 위험)

**이슈 1: SaveSectionsAsync의 BulkCreateAsync 사용**
- 단일 삭제/추가에도 전체 삭제 + 전체 재생성
- `ScheduleUnitMap` (배치 매핑) 삭제됨 → 탭3 배치 결과 소실
- `LessonProgress` (수업 진행) 삭제됨 → 진행률 데이터 소실
- PK(No)가 매번 변경 → 외부 FK 참조 무효화

**개선 방향**: 
- 삭제: `DeleteAsync(no)` 개별 호출
- 추가: `CreateAsync(section)` 개별 호출
- CSV 가져오기만 `BulkCreateAsync` 사용 (명시적 "대체" 동작)

### 🟡 주의

**이슈 2: 드래그 시 개별 UpdateAsync N번 호출**
- Repository에 `BulkUpdateSortOrderAsync()` (트랜잭션 버전) 이미 존재
- 현재 코드는 이를 사용하지 않고 개별 `UpdateAsync()` N번 호출

**개선**: `OnSectionDragCompleted`에서 `BulkUpdateSortOrderAsync()` 사용

**이슈 3: RefreshListView()의 ItemsSource 초기화**
```csharp
SectionListView.ItemsSource = null;
SectionListView.ItemsSource = _courseSections;
```
- ObservableCollection 사용 중인데 ItemsSource를 null로 재설정 → 깜빡임
- 드래그 후 연번 갱신 목적이지만, ContainerContentChanging으로 이미 처리됨

**개선**: ObservableCollection의 CollectionChanged가 자동 반영하므로 불필요

**이슈 4: SectionSummaryListView에 동일 데이터소스 별도 바인딩**
- 탭1의 SectionListView와 탭3의 SectionSummaryListView에 동일한 `_courseSections` 설정
- 각각 `SectionListView.ItemsSource = _courseSections` + `SectionSummaryListView.ItemsSource = _courseSections`
- 하나의 ObservableCollection이므로 양쪽 동시 반영됨 (문제는 아님, 구조적 중복)

### 🟢 경미

**이슈 5: CourseSectionDialog에서 직접 DB 저장**
- 다이얼로그 내부에서 `new CourseSectionRepository()` 생성 + 저장
- Page에서는 저장 후 `LoadCourseSectionsAsync()`로 전체 재로드
- MVVM 관점에서 다이얼로그가 Repository에 직접 의존

**이슈 6: 유효성 검사 최소**
- 소단원명 비어있는지만 확인 (`ValidateInput`)
- 대단원번호=0, 중단원번호=0도 허용
- 시작페이지 > 끝페이지 검증 없음
- 동일 단원번호 중복 검증 없음

**이슈 7: x:Bind OneTime 바인딩**
- ListView DataTemplate에서 `{x:Bind}` 사용 (기본 OneTime)
- 다이얼로그에서 수정 후에는 `LoadCourseSectionsAsync`로 전체 재로드하므로 문제없음
- 단, 인라인 편집을 추가한다면 OneWay/TwoWay로 변경 필요

---

## 8. 데이터 흐름 다이어그램

```
[사용자 액션]                    [Page.xaml.cs]                [Repository]            [DB]
                                                                                        
소단원 추가 클릭 ───────→ OnAddSectionClick()                                          
                           │ new CourseSectionDialog(course, null)                      
                           │ dialog.ShowAsync()                                         
                           │   ├─ [다이얼로그 내부]                                    
                           │   │  ValidateInput()                                       
                           │   │  CreateSectionFromForm()                               
                           │   │  repo.CreateAsync(section) ─────→ INSERT ──→ CourseSection
                           │   └─ return Primary                                        
                           └─ LoadCourseSectionsAsync() ──→ GetByCourseAsync() ──→ SELECT
                              └─ _courseSections 갱신 + UpdateSectionUI()               
                                                                                        
아이템 클릭 (편집) ─────→ OnSectionItemClick()                                         
                           │ new CourseSectionDialog(course, section)                   
                           │ dialog.ShowAsync()                                         
                           │   ├─ LoadSectionData(section)                              
                           │   │  [사용자 편집]                                         
                           │   │  repo.UpdateAsync(section) ─────→ UPDATE ──→ CourseSection
                           │   └─ return Primary                                        
                           └─ LoadCourseSectionsAsync()                                 
                                                                                        
삭제 버튼 클릭 ─────────→ OnDeleteSectionClick()                                       
                           │ ContentDialog 확인                                         
                           │ _courseSections.Remove(section)                            
                           └─ SaveSectionsAsync() ─────→ BulkCreateAsync() ──→ ⚠️전체삭제+재생성
                                                                                        
CSV 가져오기 ───────────→ OnImportCsvClick()                                            
                           │ FileOpenPicker → ParseCsv()                                
                           │ [기존 데이터 있으면 확인 Flyout]                           
                           └─ ApplyImportSectionsAsync()                                
                              └─ SaveSectionsAsync() ──→ BulkCreateAsync() ──→ 전체삭제+재생성
                                                                                        
드래그 정렬 ────────────→ OnSectionDragCompleted()                                      
                           │ for each section:                                          
                           │   section.SortOrder = i + 1                                
                           │   repo.UpdateAsync(section) ──→ UPDATE ──→ ⚠️개별 N번      
                           └─ RefreshListView()                                         
```

---

## 9. 엑셀 내보내기 (OnExportExcelClick)

- FileSavePicker → `.xlsx`
- 4개 Repository 생성: CourseSectionRepo, ScheduleRepo, ScheduleUnitMapRepo, LessonProgressRepo
- `ReportExportService.ExportYearPlanToExcelAsync()` 호출
- 파일명: `연간수업계획_{과목명}_{날짜}.xlsx`

---

## 10. 리팩토링 우선순위

1. **🔴 SaveSectionsAsync 수정** — 개별 삭제/추가로 변경, BulkCreateAsync는 CSV 가져오기에서만 사용
2. **🟡 BulkUpdateSortOrderAsync 활용** — 드래그 완료 시 트랜잭션 일괄 업데이트
3. **🟡 RefreshListView 제거** — ObservableCollection 자동 갱신 활용
4. **🟢 유효성 검사 강화** — 페이지 범위, 중복 단원번호 검증
5. **🟢 다이얼로그 DB 직접 접근 제거** — 콜백 패턴 또는 ViewModel 경유로 변경

---

*이 문서는 2026-02-07 시점의 단원 관리 기능을 상세 분석한 것입니다.*
