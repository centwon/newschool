# Changelog

## v1.0.5 (2026-04-19 ~ 2026-04-20)

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
