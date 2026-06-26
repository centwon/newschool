# Changelog

## v1.0.6 (2026-06-18 ~ 2026-06-26)

### 에디터 교체: Jodit(WebView2) → WinUIRichEditor(Win2D) + WebView2 완전 제거 (2026-06-26)
- **WinUIRichEditor 도입** — 웹 기반 Jodit 에디터(WebView2 + ~900KB JS)를 네이티브 Win2D 리치에디터로 교체
  - `Controls/RichTextEditor` — `RichEditorView` 호스팅 어댑터(UserControl). 기존 `JoditEditor` 와 동일 공개 API
    (`Text`/`Mode`/`PlainText`/`TextChanged`/`GetHtmlAsync`/`InsertHtml`/`PrintAsync`/`IsInitialized`/`IDisposable`) 로 드롭인 교체
  - `Controls/RichTextEditorWin` — 구 `JoditEditorWin`(전체화면 편집 윈도우) 대체
  - WinUIRichEditor 는 `ProjectReference` 로 참조(Win2D 1.4.0 추이 의존)
  - **9개 사용처 전수 교체**: `MemoEditDialog`·`MemoBoard`(인라인 reparent)·`PostEditPage`(표삽입 `InsertHtml`)·
    `PostDetailPage`·`ClassDiaryBox`·`StudentInfoExportPage`·`UnifiedExportPage`·`UnifiedItemDialog`(2)
- **WebView2 의존 완전 제거**
  - 도움말: `HelpPage`(WebView2) 제거 → `MainWindow.OpenHelpInBrowserAsync` 가 `help.html` 을 **기본 웹 브라우저**로 띄움
    (`help.html` 은 Content 로 유지 — CSS·다크모드·TOC·JS 전부 실브라우저에서 동작)
  - 설치 프로그램(`installer.iss`·`Installer/NewSchoolSetup.iss`): WebView2 부트스트래퍼 전부 제거 → 런타임 의존 소멸
  - 제거 파일은 `NewSchool-archive/jodit-webview-20260626/` 에 별도 보관(`JoditEditor*`·`JoditEditorWin*`·`HelpPage*`·`Assets/Jodit/*`)
- **인쇄 UX 변경**: WinUIRichEditor 는 시스템 인쇄 다이얼로그가 없어 `PrintAsync` 가 PDF 렌더 후 기본 뷰어로 열기(추후 PrintManager 검토)
- 검증: 빌드 경고 0/오류 0, 앱 기동·Win2D 에디터 렌더·한글 입력·도움말 브라우저 열기 확인

### Windows App SDK 1.8 → 2.2 업그레이드 (2026-06-26)
- `NewSchool.csproj`·`NewSchool.Tests.csproj`: `Microsoft.WindowsAppSDK 1.8 → 2.2.0`, TFM `net10.0-windows10.0.19041.0 → 10.0.26100.0`
  (`TargetPlatformMinVersion 17763` 유지)
- 설치 프로그램: 런타임 패키지명 `Microsoft.WindowsAppRuntime.1.8 → Microsoft.WindowsAppRuntime.2`(2.x 부터 PFN 이 메이저 버전에 정렬),
  레지스트리·다운로드 URL 갱신
- breaking change 영향 없음 — 제거/변경은 대부분 AI/ML 영역(미사용), deprecated WinUI API(`Window.Current` 등)도 미사용(modern `DispatcherQueue`)

### 테스트 프로젝트 신설 (2026-06-18)
- `NewSchool.Tests` (xUnit 2.9.2) — `NeisHelper.CountByte/GetMaxBytes` 29건 + `CsvExportService.Escape` 21건 = **50 테스트 통과**
- 실행: `dotnet test -p:Platform=x64 -p:RuntimeIdentifier=win-x64`. `CsvExportService.Escape` 를 internal 전환 + `InternalsVisibleTo`

### 버그 수정 (2026-06-18)
- 좌석 복원 시 미사용 경고 오발 수정, `PhotoCard` 비동기 경합 해소, 명렬표 숫자만 표기

## v1.0.5 (2026-04-19 ~ 2026-04-22)

### CA1063/CA1001 전수 해결 (2026-04-22)
- **CA1063 — IDisposable 구현 패턴**: 22개 클래스를 `sealed` 로 전환
  - Services 13개: `TeacherService`·`SchoolService`·`StudentDetailService`·`GoogleAuthService`·`GoogleSyncService`·`ClassDiaryService`·`SchedulerService`·`TimetableService`·`SchoolScheduleService`·`StudentSpecialService`·`EnrollmentService`·`StudentService`·`SeatService`
  - Repositories 2개: `LessonProgressRepository`·`UndoHistoryRepository`
  - Infrastructure 3개: `DatabaseInitializer`·`FileLogger`·`UnitOfWork`
  - ViewModels 3개: `StudentLogViewModel`·`ClassDiaryViewModel`·`StudentCardViewModel` (`protected virtual OnPropertyChanged`/`SetProperty` → `private` 로 전환해 봉인 충돌 해결)
  - Controls 1개: `JoditEditor` (UserControl sealed)
- **CA1001 — IDisposable 필드 보유 타입**: 11개 클래스에 IDisposable 구현
  - Pages 7개: `PageSeats`·`PageStudentInfo`·`PageStudentLog`·`SchoolScheduleManagementPage`·`StudentInfoExportPage`·`StudentManagementPage`·`StudentSpecPage` — `Dispose()` 에서 서비스 필드 해제, 생성자에서 `Unloaded += (_,_) => Dispose();` 훅업
  - Controls 3개: `MemoBoard`·`ClassDiaryListWin`(Window·`Closed` 훅)·`LessonLogList`
  - Singleton 1개: `Caching/CacheManager` — `_cleanupCts` 정리 책임 명확화, sealed
- **editorconfig 정리**: CA1063/CA1001 을 `warning` 으로 승격(이전엔 전수 리팩토링 유예 주석), 빌드 경고 0 유지

### 정적 분석 표준화 + Dead code 정리 (2026-04-22)
- **`.editorconfig` 신설** (프로젝트 루트)
  - C# 코드 스타일 규칙 고정 — `var` 사용, 식 본문 멤버, 패턴 매칭, `using` 선언, 파일 범위 네임스페이스
  - 진단 severities 명시
    - **warning**: CS8600~8618(null 가능성), CA1825(빈 배열), IDE0005(불필요 using)
    - **suggestion**: CA1822(static 승격), IDE0051/0052/0060(미사용 멤버), IDE0090(new 간소화), CA1063·CA1001·CA2100(전수 리팩토링 전까지)
    - **none**: CA1304~1310(CultureInfo — 한국어 고정 환경), CA1031(catch Exception — 프로젝트 정책), CA1848(LoggerMessage — FileLogger 구조상 과잉)
  - 인코딩 CRLF/UTF-8, XAML/JSON 2칸, C# 4칸 일관성
  - **빌드 경고 0 유지**(suggestion 은 IDE 에서만 표시)
- **Dead code 2차 정리** — Helpers 계층 호출처 0건 메서드 일괄 제거
  - `Helpers/ExcelReader.cs` (ExcelHelper) — 7개 제거: `GetData`, `ReadSheetAsObjectArray`(private 헬퍼), `ReadWithHeaders`, `GetSheetNames`, `ReadAsDataTable`, `WriteArray`, `CreateExcelStream(DataTable)`, `CreateExcelStream<T>`
  - `Helpers/NeisHelper.cs` — 4개 제거: `GetAreaDisplayName`, `IsOverLimit`, `GetByteInfo`, `GetRemainingBytes`
  - **총 11개 메서드** 제거, 유지되는 공개 API 는 주석으로 명시
  - **제외 사항**: `Helpers/TextBoxDropHelper` 의 `GetEnableTextDrop`/`SetEnableTextDrop` 은 XAML attached property 프레임워크가 호출하므로 유지(초기 스캔 오탐)

### 키보드 단축키 · 다크모드 일관성 (2026-04-21 UI 정리)
- **Ctrl+S 표준화** — 저장 버튼 9곳에 `KeyboardAccelerator` 추가, 툴팁에 `(Ctrl+S)` 표기
  - `Pages/PageStudentInfo` · `Pages/PageStudentLog` (BtnSaveActLog) · `Pages/LessonActivityPage` · `Pages/ClubActivityPage` · `Pages/PageSeats` · `Pages/AddStudentsPage` · `Pages/SchoolScheduleManagementPage` (BtnSaveSelected) · `Board/Pages/PostEditPage` (SaveButton) · `Dialogs/StudentSpecBatchDialog` (BtnSaveAll)
  - 기존 적용 페이지(StudentManagementPage, StudentSpecPage) 합쳐 총 11곳에서 Ctrl+S 통일
- **Ctrl+F 검색창 포커스** — `Page.KeyboardAccelerators` + `Invoked` 핸들러로 검색 TextBox `Focus(Keyboard)` + `SelectAll`
  - `Board/Pages/PostListPage` → `SearchTextBox`
  - `Board/ListViewer` → `TBoxSearch`
- **다크모드 하드코딩 제거**
  - `Scheduler/Kcalendar.xaml` — `Background="White"` / `Foreground="Black"` / `BorderBrush="#E0E0E0"` → `ThemeResource LayerFillColorDefaultBrush` · `TextFillColorPrimaryBrush` · `ControlStrokeColorDefaultBrush`
  - `Scheduler/DayCell.xaml` — BrdBase `Background="White"` + `BorderBrush` → ThemeResource 로 전환
  - `Scheduler/KAgendaControl.xaml` — 외곽 Border `Background="White"` 제거
  - `Board/Controls/CommentBox.xaml` — UserControl `Background="White"` 제거
  - `Controls/MonthPicker.xaml` — Flyout 그리드 `Background="White"` → `FlyoutPresenterBackground`
  - `Board/ListViewer.xaml` — Page 배경 + 상/하/검색바(`#FFF8F8F8`), 테두리(`#E0E0E0`/`#F0F0F0`), 보조/삼차 텍스트(`#666666`/`#999999`) 를 모두 ThemeResource 로 일괄 치환 (13곳)
- **유지 사항**: 인쇄용 HTML/PDF 서비스(`HtmlExportService`/`SeatsPrintService`/`StudentCardPrintService`/`StudentLogPrintService`)의 흰 배경·검은 글자는 **의도적 보존** — 종이 출력물은 항상 라이트

### 이벤트 핸들러 람다 → named method 전환 (2026-04-21 마감)
- **배경**: `dialog.Closed += async (s,args) => { ... await LoadLogsAsync(); }` 형태의 람다가 10여 곳에 분산돼 있었음. 다이얼로그가 GC 되기까지 페이지 메서드를 계속 캡처하고, 핸들러 체인에서 자기 자신을 떼어낼 수 없어 잠재적 누수·테스트 불가 코드였음
- **대응**: 각 Page/Control 에 공용 named method(`OnLogDialogClosedReload`, `OnLogDialogClosedReloadDaily`, `OnLogDialogClosedReloadStudent` 등)를 추가하고 핸들러 내부에서 `sender.Closed -= ...` 로 자기 제거
  - `Pages/PageStudentLog.xaml.cs` — 3건 (개별 추가, 일괄, 컨텍스트 메뉴)
  - `Pages/PageStudentInfo.xaml.cs` — 1건 (학생 로그 재로드)
  - `Pages/LessonActivityPage.xaml.cs` — 3건 (일괄+개별+컨텍스트). 일괄 다이얼로그는 `IsSuccess/SavedLogs` 참조가 필요해 지역 named function 사용
  - `Pages/ClubActivityPage.xaml.cs` — 2건
  - `Pages/ClassDiaryPage.xaml.cs` — 3건. `ClassDiaryListWin.DiarySelected` 람다도 `OnDiarySelected` 로 분리, `listWin.Closed` 에서 해제
  - `Controls/LogListViewer.xaml.cs` — 편집 다이얼로그 1건 (지역 named function + `vm` 캡처 유지)
- **효과**: 핸들러 체인 투명성 회복, 누수 경로 제거, 디버깅 시 스택 트레이스가 람다 메타명 대신 실제 메서드명으로 표시

### 성능·메모리·가상화 개선 2차 (2026-04-21 심화)
- **시작 시간 단축**
  - `App.OnLaunched` 의 자동 백업(`Settings.RunAutoBackupIfNeeded`)을 `Task.Run` 으로 밀어 시작 경로 비차단 — 백업 주기 도래 시 체감되던 1~3초 블로킹 제거
  - `TodayPage.Page_Loaded` 의 시간표·학사일정·할일/일정·급식 4종 로드를 `Task.WhenAll` 로 병렬화 (기존 순차) — 누적 400ms~1.2초 → 가장 느린 하나 시간으로 단축, 개별 실패는 `SafeLoadAsync` 로컬 함수로 격리
- **메모리 누수 해소**
  - `PageSeats` — `PhotoCard` 당 6개 이벤트(StudentChanged·UnUsedChanged·FixedChanged·DragOver·Drop·Tapped) 구독이 `InitSeats` 재호출 시마다 누적되던 문제 해결. `DetachCardEvents` 헬퍼 신설 → `InitSeats` 선두 + `Page_Unloaded` 에서 일괄 해제, `StudentList.StudentSelected` 도 해제
  - `StudentSpecBatchDialog` — `this.Closed` 에서 `StudentList.StudentSelected -= OnStudentSelected` 추가, 다이얼로그 반복 열기 누수 차단
- **ListView/ItemsRepeater 가상화 복구**
  - `StudentManagementPage.xaml` — 구조를 `ScrollViewer > StackPanel > [Header + ItemsRepeater]` → `Grid(Auto + *) > [Header 고정 + ScrollViewer > ItemsRepeater]` 로 재구성. 수백 명 학생도 실 렌더 영역만 DOM 생성
  - `Board/ListViewer.xaml` — 최상위 `ScrollViewer` 제거(내부 `PostsRepeater` 의 ScrollViewer 가상화를 무력화하던 이중 구조). 페이징·검색 행은 Grid RowDefinition 이 이미 `Auto` 라 레이아웃 동일
- **Dead code 정리** — 호출처 0건 확인된 public 메서드 2건 제거
  - `StudentRepository.DeleteByIdAsync` (물리 삭제; 사용처는 논리 삭제 `DeleteAsync` 만)
  - `LessonProgressRepository.MarkAsCancelledAsync` (결강 처리; 형제 메서드 `MarkAsSkipped/Makeup` 만 호출됨)

### 성능·안정성·내보내기 개선 (2026-04-21)
- **TextBox 한글 IME 기본 입력** (`Helpers/KoreanImeHelper.cs` 신설)
  - `App.xaml` 전역 `Style` 에 `helpers:KoreanImeHelper.UseHangul="True"` 첨부 속성 자동 적용 — 모든 `TextBox` 에 별도 설정 없이 한글 입력 활성
  - `GotFocus` → `DispatcherQueue` 한 틱 지연 후 현재 IME 상태를 `ImmGetConversionStatus` 로 조회, 한글이 아니면 `SendInput(VK_IME_ON)` 으로 전환 (레거시 폴백 `VK_HANGUL`)
  - **구현 포인트**: WinUI 3 TextBox 는 TSF(Text Services Framework) 로 입력을 처리하므로 `ImmSetConversionStatus` 는 호출이 수락(`ok=True`)되어도 실제로는 무시됨 — 가상 키 입력 시뮬레이션이 유일하게 동작하는 경로
  - 이미 한글 상태면 skip, 영문 전환은 기존 한/영 키로 자유롭게
- **사용자 행동 경로 silent catch 스윕** — 파일 업로드·저장 등 사용자가 명시적으로 트리거한 작업에서 `Debug.WriteLine` 만 남기던 예외 처리를 모두 `UserErrorReporter.ReportAsync` 알림으로 승격
  - 대상: `StudentSpecBatchDialog` · `CourseEnrollmentDialog` · `CourseSectionDialog` · `PostEditPage` · `MemoEditDialog` · `MaterialEditDialog` · `MemoBoard(자동저장)`
- **Google Calendar 일시 오류 재시도** (`GoogleCalendarApiClient.SendWithRetryAsync`)
  - `HttpRequestException` · `TaskCanceledException`(네트워크 타임아웃) · HTTP 5xx 를 일시 오류로 간주, 지수 백오프(2·4·8초) 로 최대 3회 재시도
  - 사용자 취소(`CancellationToken`) 는 즉시 전달, `MaxTransientRetries = 3` 통일
  - 재시도 시 `CreateAuthRequestAsync` 로 새 요청 생성하여 HttpContent 재사용 문제 회피

- **N+1 쿼리 해소** (`UnifiedExportService`)
  - `StudentLogRepository.GetByStudentIdsAsync` / `StudentSpecialRepository.GetByStudentIdsAsync` 신설 — `WHERE StudentID IN (@id0,@id1,...)` 단일 쿼리로 학급 전체 일괄 조회, `Dictionary<StudentID, List<T>>` 반환
  - `LoadClassLogsAsync`: 학생당 2쿼리 × N명 → **총 2쿼리**(1·2학기 일괄)로 축소
  - `LoadClassSpecsAsync`: 학생당 1쿼리 × N명 → **총 1쿼리**로 축소
  - 30명 학급 기준 DB 라운드트립 약 30배↓, 빈 리스트 기본값으로 키 존재 보장
- **전역 예외 안전망** (`App.xaml.cs`)
  - `Application.UnhandledException` 확장: 기존 Debug·FileLogger 로깅 유지 + 사용자 대화상자 알림 + `e.Handled = true`로 앱 강제 종료 방지
  - `TaskScheduler.UnobservedTaskException` 포착 — `async void` / fire-and-forget Task에서 샌 예외도 사용자에 알림 후 `SetObserved()`
  - `AppDomain.CurrentDomain.UnhandledException` — 최종 안전망(파일 로그)
  - `Controls/UserErrorReporter.cs` 신설 — `ReportAsync(context, ex, title?)` 공용 헬퍼(자체 실패 방어)
- **CSV 내보내기 + 클립보드 복사** (`UnifiedExportPage`)
  - `Services/CsvExportService.cs` 신설 — UTF-8 BOM + RFC 4180 인용 + CSV Injection 방지(`=`,`+`,`-`,`@` 선행 인용)
  - `UnifiedExportService.ExportFormat.Csv` 추가, 누가기록/학생부 분기 연결
  - `BuildClassLogsCsvAsync` / `BuildClassSpecsCsvAsync` — 클립보드용 문자열 반환
  - UI: "CSV (.csv)" 라디오 + "클립보드 복사" 버튼 — `Clipboard.SetContent(DataPackage)`로 엑셀·구글 시트 즉시 붙여넣기
  - 좌석배정/학생카드 선택 시 CSV·Excel·클립보드 버튼 자동 비활성화

### 좌석배정표 인쇄 옵션 강화 (2026-04-20)
- **출력 방향 설정** (`SeatPrintOptionsDialog`)
  - 자동 (좌석 가로 칸수 > 세로 칸수이면 가로 출력) · 세로 · 가로 선택
  - `PrintOrientation` enum 추가 (`SeatsPrintService`)
  - PDF: `PageSizes.A4.Landscape()` 분기, 가용 영역(535×782 ↔ 782×535) 스왑 후 셀 크기·상단 여백 재계산
  - HTML: `@page { size: A4 landscape }` 조건부 출력
- **학급 명렬표 삽입 옵션**
  - 왼쪽 사이드바(82pt)에 번호·이름 2열 표, 헤더 `번호 | 이름`
  - 모든 학생이 한 페이지에 들어가도록 행 높이·폰트(5~11pt) 자동 조정
  - HTML: flex 레이아웃 (`.layout > .sidebar + .main`), `.main` 의 교탁은 column-flex 하단 정렬
- **교탁 라벨 개선** — `"교 탁"` → `"{grade}학년 {classRoom}반"`

### 게시판 명렬표 표 구조 개편 (2026-04-20)
- **그룹 세로 레이아웃** (`RosterTableDialog.BuildGroupedVerticalTable`) — 그룹명을 별도 구분 행이 아닌 **칼럼**으로 이동해 엑셀 복사·정렬·필터 용이
- **학급 전체**(학년 내): `학급 | 번호 | 이름 | 사용자칼럼...` (`1반`, `2반`)
- **수업 전체**(강의실 그룹): `강의실 | 학년 | 학급 | 번호 | 이름 | ...` — 강의실 내 여러 학급 혼재 대응, 학년→학급→번호 순 정렬
- **동아리**: `학년 | 학급 | 번호 | 이름 | ...` (학년·학급은 숫자만 표기)
- `BuildGroupedVerticalTable` 시그니처를 `leadHeaders[]` + flat `rows` 로 일반화
- `LoadCourseStudentsByRoomDetailedAsync`, `LoadClubStudentsDetailedAsync` 신설

### 통합 내보내기 — 학생카드 타입 추가 (2026-04-20)
- `DataType.StudentCard` 추가 (PDF/HTML 전용, Excel 자동 비활성화)
- `StudentCardPrintService.GenerateClassCardsPdfFromDbAsync` — 학급 전체를 단일 PDF(학생당 1 페이지)
- `HtmlExportService.BuildClassCardsHtml` / `ExportClassCardsToHtml` — 학생당 1 섹션 구성(기본 정보·보호자·건강·진로)
- `StudentCardViewModel.LoadFromModels` 공개 메서드 — DB 호출 없이 Enrollment + Student + StudentDetail 주입

### 비밀 정보 관리 구조 개편 (2026-04-20)
- **`secrets.json` 통합 도입** — Google OAuth + NEIS API key 를 하나의 파일로 관리 (`.gitignore`)
- `SecretsService` — 런타임에 `AppContext.BaseDirectory/secrets.json` 로드, 없으면 빈 값
- `secrets.template.json` 커밋 — 새 환경용 템플릿
- 기존 `google_oauth.json` 제거, `GoogleAuthService.ClientId`/`ClientSecret` → `SecretsService` 경유
- `Settings.NeisApiKey` 기본값 → `SecretsService.NeisApiKey` (런타임 주입)
- 과거 커밋에 포함되어 있던 NEIS API 인증키 제거 — `git-filter-repo` 로 히스토리 재작성 및 force-push 완료
- 설치 프로그램(`installer.iss`, `NewSchoolSetup.iss`) — `google_oauth.json` → `secrets.json`

### 좌석 배정 전면 개편
- **DB 영속화**: 학급별 좌석 배치를 DB에 저장/복원 (`SeatService`, 4개 테이블)
  - `SeatArrangement` — 학급별 배치 메타 (Jul/Jjak/Rows/사진표시/메시지/잠금/옵션)
  - `SeatAssignment` — 좌석 셀 (Row/Col/StudentID/미사용/미표시/고정)
  - `SeatHistory` — 짝 이력 (회차별 인접 학생 쌍)
  - `SeatPosHistory` — 위치 이력 (회차별 학생-좌표)
  - 학급 선택 시 저장된 배치 자동 복원 (`PageSeats.TryLoadSavedArrangementAsync`)
- **옵션 다이얼로그** (`SeatOptionsDialog`, Pivot 4개 탭)
  - **이력**: 최근 N회차 짝 배제 / 같은 자리 배제
  - **속성**: 남녀 교차 짝 (짝 모드 우선), 앞자리 우선 학생(허용 범위 지정)
  - **짝 제약**: 분리(🚫) · 고정(📌) 쌍 관리
  - **방식**: 배치 시도 횟수 (100/500/1000/3000)
  - 옵션은 `OptionsJson`으로 직렬화해 배치와 함께 저장 (AOT-safe `SeatOptionsJsonContext`)
- **배치 알고리즘 개선** (`PageSeats.ArrangeSeatAsync`)
  - 앞자리 우선 학생을 범위(Row 0~N) 내에 우선 배치
  - 이력 기반 회피: 최근 짝/위치 기피
  - 유효성 검증: 분리/고정 쌍, 최근 짝, 성별 교차를 만족할 때까지 재시도
- **저장/잠금 버튼** (`PageSeats`)
  - 💾 수동 저장 (회차 누적, 이력 기록)
  - 🔒 배치 잠금 (드래그/재배치 금지)
- **통합 내보내기 연동** (`UnifiedExportService`, `UnifiedExportPage`)
  - `DataType.Seats` 추가 — 좌석배정은 PDF/HTML 전용 (Excel 자동 비활성화)
  - `SeatsPrintService.GenerateSeatsPdfFromDbAsync` — DB에서 직접 로드해 PDF 생성
  - `SeatsPrintService.GenerateSeatsHtmlFromDbAsync` / `BuildSeatsHtmlFromDbAsync` — HTML 파일/문자열 출력
  - 렌더링 코어를 `SeatCellData` DTO로 리팩터링해 PhotoCard 비의존
- **컨텍스트 메뉴 동기화 수정** (`PhotoCard`)
  - 프로그램이 `IsUnUsed`/`IsFixed`/`IsHidden`을 설정할 때 우클릭 메뉴의 `ToggleMenuFlyoutItem.IsChecked`도 함께 갱신 → 복원된 배치에서 메뉴 상태 정상 표시

## v1.0.4 (2026-04-06)

### UI 개선
- 달력 시인성 강화: 오늘 날짜에 파란 원형 배경 + 파란 테두리, 마우스 호버 시 회색 배경 효과 추가 (`DayCell`)
- 홈 학사일정 스크롤 수정: `ItemsRepeater` → `ListView`로 교체하여 마우스 휠 스크롤 정상 작동 (`SchoolScheduleListControl`)
- 게시물 보기 레이아웃 개선: `ScrollViewer+StackPanel` → `Grid` 레이아웃으로 변경, JoditEditor 내부 스크롤 지원 (`PostDetailPage`)
  - 첨부파일이 없을 때 첨부 영역 자동 숨김
  - JoditEditor ReadOnly 모드에서 내부 스크롤 활성화 (CSS height chain + overflow 설정)
- 일정 추가 TimePicker 교체: WinUI 3 TimePicker → 커스텀 `CompactTimePicker` (NumberBox 기반)
  - 마우스 휠/스핀버튼/직접 입력으로 시·분 변경 가능
- 교사 시간표 표시 모드 수정: `DisplayMode`를 `Teacher`로 설정하여 과목+강의실 표시 (`TeacherTimetablePage`)

### 기능 개선
- 업데이트 확인 메뉴 추가: 설정 하위에 수동 업데이트 확인 항목 추가 (`MainWindow`)
- 일정 구글 동기화 라벨 수정: 카테고리 "학급"일 때만 구글 동기화 옵션 표시, "담임"은 제외 (`UnifiedItemDialog`)
- 게시물 읽기 인쇄 기능 개선: PDF 생성 방식 → JoditEditor 네이티브 인쇄로 변경 (`PostDetailPage`)
  - HTML 서식(표, 이미지, 글꼴, 색상) 완전 보존
  - 화면 85% 크기 미리보기 창(최대 1024×900)을 중앙에 열고 시스템 인쇄 대화상자 표시
- 좌석배정표 인쇄 개선 (`SeatsPrintService`, `PhotoCard`)
  - 미표시 좌석 옵션 추가: 우클릭 메뉴에서 "미표시 좌석" 체크 시 인쇄에서 해당 좌석 완전 숨김
  - A4 하단 정렬: 좌석+교탁이 용지 아래쪽부터 배치, 상단에 여백
  - 사진 최대화: 셀 내 3:4 비율 유지하면서 가용 공간에 최대 크기로 표시
  - 줄 사이 통로: 짝(jjak)은 붙이고 줄(jul) 사이에 10pt 통로 간격 삽입
  - 클린 레이아웃: 셀 테두리/배경 제거, 이름은 사진 너비 기준 가는 테두리 박스로 표시
  - 1페이지 보장: 모든 요소 높이를 A4 가용 영역에서 역산하여 오버플로 방지
- 좌석배정 짝 분리/고정 설정 (`SeatExclusionDialog`, `PageSeats`)
  - 짝 분리: 특정 학생 쌍이 인접하지 않도록 자동배정 시 제약 적용
  - 짝 고정: 특정 학생 쌍이 반드시 인접하도록 자동배정 시 제약 적용
  - "● 자리 배정" 제목의 불릿 버튼으로 설정 다이얼로그 진입
- 좌석배정 배열 UI 개선 (`PageSeats`)
  - 줄 설정: ComboBox → NumberBox(2~8, "N줄" 접미사 포맷터) 교체
  - 짝 설정: ComboBox → CheckBox 교체 (체크=짝2명, 해제=1명)
  - 인라인 도트 패턴: 배열 시각화 (예: ●● ●● ●●)
  - 좌측 패널 레이아웃 통합: 필터+학생목록 단일 블록

### 성능 개선
- GoogleSyncService 리소스 누수 수정: 정적 필드로 인스턴스 보관, 앱 종료 시 Dispose 호출 (`App.xaml.cs`)
- StudentLogViewModel IDisposable 구현: 3개 서비스(StudentLogService, EnrollmentService, StudentService) Dispose 보장
- ClassDiaryViewModel IDisposable 구현: ClassDiaryService Dispose 보장
- StudentLogDialog 이벤트 누수 수정: `Activated` 핸들러를 1회 실행 후 즉시 해제
- PageStudentLog 이벤트 해제 추가: `Unloaded` 시 `StudentSelected` 핸들러 해제
- 일괄 내보내기 N+1 쿼리 제거: `LoadStudentAsync` (학생당 DB 4회) → `LoadFromEnrollment` (DB 0회)
  - 30명 기준 120회 DB 호출 제거
- StudentLogRepository GetOrdinal 캐싱: `ReadAllLogsAsync` 헬퍼 추가, 첫 row에서 캐시 초기화 후 재사용
- HttpClient 연결 풀 최적화: `SocketsHttpHandler` 적용 (연결 수명 2분, GZip 압축, 쿠키 비활성) (`GoogleCalendarApiClient`)
- FileLogger Dispose 개선: 이미 완료된 Task는 Wait 생략, 타임아웃 5초→2초 단축
- 이미지 캐시 LRU 개선: 히트 시 제거→재삽입으로 최근 접근 항목 보존 (`PhotoService`)
- 일괄 내보내기 중복 정렬 제거: 단일 학기+필터 없음일 때 불필요한 `OrderByDescending` 생략
- 앱 시작 DB 초기화 병렬화: Board/Scheduler/School 3개 DB `Task.WhenAll` 동시 초기화 (`App.xaml.cs`)
- 진도 매트릭스 SolidColorBrush 캐싱: 20개 브러시를 static readonly 필드로 캐시, 셀·호버·선택 시 재생성 제거 (`ProgressMatrixPage`)
- PostListPage PropertyChanged 누수 수정: `NavigationCacheMode.Enabled`에서 `OnNavigatedFrom`/`OnNavigatedTo` 구독 관리로 중복 핸들러 방지
- StudentCard 이중 Unloaded 구독 수정: DI 생성자에서 `-=` 후 `+=`으로 중복 방지 (`StudentCard`)
- N+1 배치 INSERT: `LessonRepository.CreateFromSchedulesAsync` — 개별 INSERT → 트랜잭션+파라미터 재사용 배치
- N+1 배치 INSERT: `CourseEnrollmentRepository.BulkCreateAsync` — 개별 CreateAsync → 트랜잭션+파라미터 재사용 배치
- N+1 배치 INSERT: `SchoolScheduleRepository.CreateBulkAsync` — 행별 중복체크 SELECT+INSERT → 일괄 SELECT 후 메모리 필터+배치 INSERT
- Board ReaderColumnCache 적용: `PostRepository`, `CommentRepository`, `PostFileRepository`에 `ReaderColumnCache`+`ExecuteListAsync` 도입
  - 댓글 100개 기준 GetOrdinal 1,200회 → 9회 (99% 감소)
- StudentCardViewModel IDisposable 인터페이스 선언 추가 (기존 Dispose 본문은 유지)
- StudentLogRepository date() 함수 제거: WHERE 절에서 `date(sl.Date)` → `sl.Date` 직접 비교로 인덱스 활용
- SchoolScheduleConverters 정적 브러시 캐싱: `BoolToVacationColorConverter`, `BoolToTodayBackgroundConverter`에 static readonly 브러시
- DayCell 인스턴스 브러시 캐싱: `_whiteBrush`, `_transparentBrush` 필드 추가
- AnnualLessonPlanViewModel PropertyChanged 등호 검사: 10개 setter에 `if (_field == value) return;` 추가
- PostRepository SELECT 컬럼 명시: GetListAsync에서 `SELECT *` → 14개 컬럼 지정
- COUNT(*) → EXISTS 변환: 11개 파일, 13개 존재 확인 쿼리 최적화
- Board BaseRepository `[Conditional("DEBUG")]`: LogDebug/LogInfo에 적용, Release 빌드에서 문자열 보간 제거
- SQLite PRAGMA 성능 튜닝: `cache_size=10000`, `mmap_size=30000000` — Board.cs, Scheduler, 양쪽 BaseRepository에 추가
- AnnualLessonPlanPage `{Binding}` → `{x:Bind}` 변환 (DataTemplate 내 string 바인딩)
- OptimizedObservableCollection 확대: PostListViewModel, PostDetailViewModel에서 Clear+Add 루프 → ReplaceAll 단일 호출
- DayCell 불필요한 Task.Run(async) 래핑 제거: 직접 await로 스레드풀 홉 제거
- App.xaml.cs fire-and-forget 안전망: TryStartGoogleSyncAsync에 ContinueWith(OnlyOnFaulted) 추가
- COUNT(*) OVER() 윈도우 함수: PostRepository.GetListWithCountAsync 신규, BoardService 페이징에서 DB 2회 → 1회 호출
- AddWithValue boxing 제거: PostRepository, CommentRepository, LessonProgressRepository, StudentLogRepository — 73개 int/bool 파라미터를 typed Add()로 변환

### 정리
- Tools.cs 미사용 코드 삭제 (496줄 → 155줄):
  - 메서드 6개: `WaitAsync`, `IsConnected`, `IsNumeric`, `FindVisualParent`, `FindVisualChild`, `GetWeekOfMonth`
  - 컨버터 8개: `YearToAcademicYearConverter`, `GradeConverter`, `ClassConverter`, `HtmlToPlainTextConverter`, `SizeFormatConverter`, `UtcToLocal`, `NeisByteConverter`, `InverseBooleanConverter`
- StudentLogViewModel 미사용 코드 삭제: `CalculateNeisByte()` 메서드, 주석 처리된 `LogByteCount`/`LogCharCount`/`LogByteInfo` 프로퍼티

## v1.0.3 (2026-03-30)

### 보안
- SQL Injection 취약 메서드 제거: `Sqlite.cs`의 `CountRecord`, `GetCondition` (미사용 레거시 코드 삭제)
- XSS 차단: JoditEditor 붙여넣기/드래그앤드롭 시 DOMPurify로 HTML sanitize 적용
  - `<script>`, `<iframe>`, `<embed>`, `onclick=` 등 위험 요소 차단
- 주민번호 DPAPI 암호화: `StudentRepository`에서 저장 시 암호화, 읽기 시 복호화
  - 기존 평문 데이터 호환 (복호화 실패 시 평문으로 간주, 재저장 시 자동 암호화)
- 로그 민감정보 제거: `StudentLogRepository`, `StudentRepository`, `TeacherRepository`
  - 파라미터 값 덤프 삭제, 학생/교사 이름·ID 로깅 제거

### 정리
- 미사용 코드 대규모 삭제 (17개 파일):
  - 기능 체인: TodoItem, WorkLog, SchoolEvent (Model+Repository+Service), Evaluation (Model+Repository)
  - 첨부파일 시스템: AttachmentBox, AttachmentService, AttachmentRepository, Attachment 모델
  - 미사용 개별 파일: StudentDragEventArgs, StudentLogWin, SchoolTextBox, UIHelpers, ValidationHelper, EventAggregator
  - DB의 Attachment/Evaluation 테이블·인덱스 생성 코드 제거
- SchoolService 계층 통일: 3곳의 SchoolRepository 직접 호출 → SchoolService 경유로 변경
  - `SaveSchoolAsync()` Upsert 메서드 추가, 중복 코드 ~85줄 → 9줄로 축소
  - Page → Service → Repository → DB 패턴 통일

### 기능 개선
- 진도 매트릭스 × 연간계획 연동
  - "계획" 열 추가: WeeklyUnitPlan에서 각 단원의 계획 주차 표시
  - 현재 주차 경계선: 오늘 날짜 기준 빨간 가로선으로 "여기까지 진행 예정" 표시
  - 지연 경고: 계획 주차를 지났으나 미완료인 셀에 "!" 표시 및 붉은 배경
  - 하단 요약에 계획 진도/현재 주차 통계 표시
- 수업 배치(단원 배치) 전체 강의실 보기
  - 학급 선택에 "전체" 옵션 추가: 모든 강의실 배치 현황을 통합 표시
  - 강의실명(`[1-3]` 등) 각 슬롯에 표시
  - 자동 배치/실행취소는 개별 학급 선택 시에만 활성화
- 업데이트 서비스 GitHub 사용자명 수정 (Centwons → centwon)

### 버그 수정
- 달력 할일 2일 표시 버그: 할일 생성 시 `End = Start + 1일` → `End = Start`로 수정 (inclusive 방식 통일)
  - `NewTaskEvent`, `TaskDueDatePicker_DateChanged`, 반복 할일 생성 모두 수정
- StudentLogViewModel.Date: `ToUniversalTime()` → `new DateTimeOffset()` 변환 수정 (UTC 변환으로 날짜가 하루 전으로 표시되는 버그)
- GoogleSyncService: `DateTime.Parse` → `RoundtripKind` + `.ToLocalTime()` 추가 (시간대 무시 파싱 수정)

### 정리
- 중복 `DateTimeToDateTimeOffsetConverter` 제거 (Tools.cs 삭제, CommonConverters.cs만 유지)

## v1.0.1 (2026-03-24)

### 버그 수정
- 학생 삭제 쿼리 컬럼명 오류 수정 (`WHERE ID` → `WHERE StudentID`)
- SchoolCode 미설정 시 필터 연쇄 실패 방어 처리
- 진도 분석 시 빈 컬렉션 크래시 수정 (`First()` → `FirstOrDefault()`)
- 구글 캘린더 시간대 오차 수정 (`DateTime.Now` → `DateTime.UtcNow`)
- CourseSectionRepository SQL 문자열 보간 → 파라미터 바인딩
- StudentRepository `DateTime.Parse` → `TryParse` (잘못된 DB 값 크래시 방지)
- StudentRepository 내 EnrollmentRepository 연결 누수 수정 (`using` 추가)
- EnrollmentRepository `GetByGradeAsync` 불필요한 파라미터 바인딩 제거
- JoditEditor 붙여넣기 중복 삽입 수정 (`async` → 동기 함수로 `return false` 정상 동작)
- 메모 저장 시 기존 제목 덮어쓰기 방지 (제목이 비어있을 때만 자동 생성)

### 성능 개선
- 진도 매트릭스 조회 O(n²) → Dictionary 인덱싱 O(1)
- HelpPage WebView2 메모리 해제 (`OnNavigatedFrom`에서 `Close()`)
- 메뉴 네비게이션 시 Frame BackStack 정리

### 기능 개선
- 누가기록 일괄 출력: PDF 표 형식으로 전체 학생 한 문서 출력 (동일 학생 번호/이름 행 병합, 가로 A4)
- 누가기록 일괄 출력 버튼 단순화: MenuFlyout 제거 → 필터 다이얼로그에서 형식 선택
- 파일 저장 위치 통일: `문서\NewSchool` → `Settings.UserDataPath` (포터블 모드 호환)
  - 적용: 누가기록 PDF/엑셀, 게시글 PDF, 좌석배정표 PDF, 학생명단 템플릿
- 학생부 일괄 출력: PDF/엑셀로 학급 전체 특기사항 한 문서 출력 (영역/상태/빈항목 필터)
- 학생부 일괄 입력 버튼 추가: 페이지에서 바로 BatchDialog 진입
- 학생부 일괄 입력 시 누가기록 참조 패널 (토글식, 해당 영역 DraftSummary 표시)
- 학생부 초안 자동 생성: 누가기록 DraftSummary 병합하여 특기사항 초안 채움
- 학생부 삭제 로직 변경: 내용 초기화 → DB에서 실제 삭제

### 정리
- Ktask/KtaskList 레거시 테이블 및 인덱스 제거
- KEvent CREATE TABLE에 `ItemType`, `IsDone`, `Completed` 컬럼 포함
- 게시 폴더 정리 강화 (빈 폴더, Installer, prerequisites 제외)

## v1.0.0 (2026-03-22)

- 최초 릴리스
- 학급 경영: 학급 일지, 학생 정보, 누가 기록, 학생부, 자리 배정, 게시판, 시간표
- 수업 관리: 교과 등록, 수강 배정, 연간 계획, 진도 관리, 수업 기록, 동아리
- 일정 관리: 캘린더, 구글 캘린더 양방향 동기화
- 업무/아카이브: 카테고리별 게시판
- 설정: 학교 검색(NEIS), 학사일정, 학생 엑셀 가져오기, 백업/복원
- HTML 도움말 페이지 (WebView2)
- GitHub 기반 업데이트 확인
