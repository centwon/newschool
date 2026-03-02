# NewSchool 프로젝트 정리 계획

> 검증일: 2026-03-01 — 각 항목의 참조 여부를 코드베이스 전체 검색으로 확인 완료

## 1단계: Dead Code 삭제 (사용하지 않는 코드)

완전히 참조가 없는 코드를 삭제합니다.

### 1-1. 미사용 Repository
- **`Repositories/AttendanceRepository.cs`** — `Attendance` 모델은 ClassDiary에서 사용되나, Repository 자체는 어디서도 인스턴스화/참조 없음. 삭제
- **`Board/Repositories/FullTextSearchRepository.cs`** — FTS5 검색 기능, 완전 미사용. 삭제

### 1-2. 삭제 불가 (사용 중인 코드)

아래 파일들은 기존 계획에서 삭제 대상이었으나, 검증 결과 **실제 사용 중**이므로 유지합니다.

| 파일 | 사용처 |
|------|--------|
| `Tools.cs` | `Board/Controls/CommentBox.xaml.cs`에서 `Tool.FormatSize()` 호출 |
| `Models/SchoolEvent.cs` | `TodayPageViewModel`, `SchoolEventService` 등에서 사용 |
| `Repositories/SchoolEventRepository.cs` | `SchoolEventService` 생성자에서 인스턴스화 |
| `Services/SchoolEventService.cs` | `TodayPage`에서 간접 사용 |
| `Models/UndoAction.cs` | `ScheduleShiftService`에서 8곳 이상 참조, 완전 구현됨 |
| `Repositories/UndoHistoryRepository.cs` | `ScheduleShiftService`에서 DI로 주입하여 사용 |

---

## 2단계: Legacy UI 코드 정리

UnifiedItemDialog/KAgendaControl로 대체된 레거시 UI를 정리합니다.

### 2-1. TaskDialog → UnifiedItemDialog
- **`Scheduler/TaskDialog.xaml`** + **`TaskDialog.xaml.cs`** — UnifiedItemDialog가 완전 대체
- 유일한 사용처: `KtaskListControl.xaml.cs` (아래와 함께 처리)

### 2-2. KtaskListControl → KAgendaControl
- **`Scheduler/KtaskListControl.xaml`** + **`KtaskListControl.xaml.cs`** — KAgendaControl이 대체
- 유일한 사용처: `Pages/LessonHomePage.xaml` (line 93)
- **작업**: LessonHomePage에서 KtaskListControl을 KAgendaControl로 교체

---

## 3단계: 캐싱 인프라 정리

### 3-1. 삭제 가능
- **`Caching/RepositoryCache.cs`** — 어디서도 참조 없음. 삭제
- **`Scheduler/CachedSchedulerService.cs`** — `Scheduler.cs`에 팩토리 메서드 존재하나 호출하는 곳 없음. 삭제
  - **작업**: `Scheduler/Scheduler.cs`의 `CreateCachedService()` 팩토리 메서드도 함께 제거

### 3-2. 삭제 불가 (사용 중)
- **`Board/Services/CachedBoardService.cs`** — `Board/Pages/PostEditPage.xaml.cs` (line 159)에서 `Board.CreateCachedService()`로 사용 중
- **`Caching/CacheManager.cs`** — `CachedBoardService`가 `CacheManager.Instance`를 참조하므로 함께 유지

### 3-3. 향후 검토
CachedBoardService를 일반 BoardService로 대체할지 별도로 검토 필요. 대체 시 CacheManager도 삭제 가능.
- **`Caching/` 디렉토리** — RepositoryCache 삭제 후에도 CacheManager.cs가 남으므로 디렉토리 유지

---

## 4단계: 미사용 Settings 및 UI 정리

### 4-1. 기능 플래그 정리
- **`EnableFTS`** (Settings.cs) — FullTextSearchRepository 삭제와 함께 제거. SettingsPage UI 토글도 제거
- **`EnablePerformanceMonitoring`** (Settings.cs) — 기능적으로 어디서도 참조 안 함. SettingsPage UI 토글도 제거

### 4-2. SettingsPage.xaml 정리
- `EnableFTSToggle`, `EnablePerformanceMonitoringToggle` 관련 XAML 및 이벤트 핸들러 제거

---

## 5단계: 주석 코드 대량 정리

대량의 주석 코드가 있는 파일들을 정리합니다. (git 히스토리에 보존되므로 안전하게 삭제 가능)

| 파일 | 주석 줄 수 | 내용 |
|------|-----------|------|
| Services/SchoolScheduleService.cs | ~201줄 | 오래된 API 호출 로직 |
| Models/LessonProgress.cs | ~168줄 | 이전 모델 정의 |
| Pages/AddStudentsPage.xaml.cs | ~166줄 | 이전 학생 추가 로직 |
| Pages/AnnualLessonPlanPage.xaml.cs | ~160줄 | 이전 수업 계획 로직 |
| Models/Base.cs | ~141줄 | 이전 베이스 클래스 |
| Services/ClassDiaryService.cs | ~134줄 | 이전 학급일지 로직 |
| Settings.cs | ~120줄 | 이전 설정 코드 |
| Board/Board.cs | ~82줄 | 이전 ShowMessageAsync 등 |
| Functions.cs | ~50줄 | Obsolete GetSchoolSchedulesAsync 등 |
| Controls/MessageBox.cs | ~48줄 | 동기 래퍼 메서드 |

---

## 6단계: 빈 이벤트 핸들러 / TODO 정리

### 6-1. 빈 핸들러
- `UnifiedItemDialog.xaml.cs:373` — `Dialog_Closing` 빈 핸들러 삭제 (XAML 바인딩도 함께 제거)

### 6-2. 구현 불필요한 TODO 제거
- `Kcalendar.xaml.cs:515` — `BtnSetup_Click`의 TODO 주석 정리
- `Functions.cs:35` — Obsolete 메서드 주석 삭제 (이미 완전 주석)

---

## 7단계: csproj 정리

### 7-1. 제외 폴더 확인
csproj에 제외된 레거시 폴더 — 실제 디렉토리가 모두 존재하지 않으므로 Exclude 항목 삭제:
- `DataBase/`, `Department/`, `Lecture/`, `Room/`, `School/`, `Views/`

---

## 8단계: MessageBox 다이얼로그 통일

에러/경고/확인 다이얼로그를 `Controls/MessageBox`로 통일합니다.

### 8-1. MessageBox 확장
현재 MessageBox에 부족한 기능을 추가합니다:
- **커스텀 버튼 텍스트 오버로드** — `ShowAsync(message, title, primaryText, secondaryText, closeText)` 추가
- **`ShowErrorAsync(message, Exception?)`** — 에러 전용 간편 메서드 (제목 "오류" 고정)
- **`ShowConfirmAsync(message, title)`** — `bool` 반환하는 확인 다이얼로그

### 8-2. 로컬 ShowMessageAsync 중복 제거 (6개 파일)
아래 파일에 있는 private `ShowMessageAsync` 메서드를 삭제하고 `MessageBox.ShowAsync()` 호출로 대체:
- `Controls/AttachmentBox.xaml.cs`
- `Controls/CommentBox.xaml.cs`
- `Controls/LogListViewer.xaml.cs`
- `Pages/StudentSpecPage.xaml.cs`
- `Pages/PageSchoolWork.xaml.cs`
- `Pages/StudentInfoExportPage.xaml.cs`

### 8-3. 인라인 ContentDialog → MessageBox 치환
단순 알림/에러/확인용 인라인 ContentDialog를 MessageBox 호출로 대체합니다.
대상: 각 Page/Control에서 `new ContentDialog`로 생성한 단순 메시지 다이얼로그

### 8-4. UIHelpers 정리
`UI/UIHelpers.cs`의 `ShowMessageAsync`, `ShowConfirmDialogAsync`, `ShowErrorAsync`를 MessageBox로 대체.
`ShowLoadingDialogAsync<T>`는 MessageBox 범위 밖이므로 유지.

---

## 작업 순서 요약

| 순서 | 작업 | 삭제/수정 | 영향도 |
|------|------|----------|--------|
| 1단계 | Dead Code 삭제 | 2개 파일 삭제 | 낮음 (참조 없음) |
| 2단계 | Legacy UI 정리 | 4개 파일 삭제 + 1곳 수정 | 중간 (LessonHomePage 수정) |
| 3단계 | 캐싱 인프라 정리 | 2개 파일 삭제 + 1곳 수정 | 낮음 (미사용) |
| 4단계 | Settings 정리 | 수정 2개 파일 | 낮음 (미사용 토글) |
| 5단계 | 주석 코드 정리 | 수정 10개 파일 | 낮음 (코드 변경 없음) |
| 6단계 | 빈 핸들러/TODO | 수정 2~3개 파일 | 낮음 |
| 7단계 | csproj 정리 | 수정 1개 파일 | 낮음 |
| 8단계 | MessageBox 통일 | 확장 1개 + 수정 ~20개 파일 | 중간 (다이얼로그 통일) |

**총 삭제 대상**: 8개 파일 삭제, ~30개 파일 수정
