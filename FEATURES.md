# NewSchool 프로젝트 기능 리스트

## 1. 메인 네비게이션 구조 (MainWindow.xaml)

메뉴는 **홈 · 달력 · 학급 · 수업 · 업무 · 아카이브 · 설정** 일곱 갈래다.
`Tag` → 페이지 매핑은 `MainWindow.xaml.cs` 의 `switch (tag)` 한 곳에 모여 있다.

### 단독 항목
| Tag | 메뉴 | Page | 기능 |
|-----|------|------|------|
| `Home` | 홈 | `TodayPage.xaml` | 오늘 대시보드 — 날짜 이동(`◀ 날짜 ▶ 오늘`), 현재 교시, 오늘 시간표(내 수업/우리 반, 담임만 '우리 반'), 그날의 휴강·교체·보강·대강 반영, 학사일정, 급식, 할 일·일정, 메모 |
| `Calendar` | 달력 | `Scheduler/Kcalendar.xaml` | 일정 관리 캘린더 |
| `Archive` | 아카이브 | `Board/Pages/PostListPage.xaml` | 전 카테고리 게시글(카테고리 변경 허용) |

### 학급
| Tag | 메뉴 | Page | 기능 |
|-----|------|------|------|
| `ClassDiary` | 학급 일지 | `ClassDiaryPage.xaml` | 학급 일지 작성/관리 |
| `StudentInfo` | 학생 정보 | `PageStudentInfo.xaml` | 학생 정보 조회 |
| `StudentLog` | 누가 기록 | `PageStudentLog.xaml` | 학생 누가 기록 |
| `StudentSpec` | 학생부 기록 | `StudentSpecPage.xaml` | 학생부 특기사항 |
| `Seats` | 자리 배정 | `PageSeats.xaml` | 자리 배정 |
| `ClassBoard` | 학급 게시판 | `Board/Pages/PostListPage.xaml` | 카테고리 `학급` 고정 게시판 |
| `StudentInfoExport` | 학생정보 출력 | `StudentInfoExportPage.xaml` | 학생 정보 출력 |
| `UnifiedExport` | 통합 내보내기 | `UnifiedExportPage.xaml` | 누가기록·학생부·좌석배정·학생카드·학생정보 × Excel/PDF/HTML |
| `Timetable_ClassManagement` | 학급 시간표 관리 | `ClassTimetableManagementPage.xaml` | 학급 시간표 관리 |

### 수업 (동아리 포함)
| Tag | 메뉴 | Page | 기능 |
|-----|------|------|------|
| `LessonHome` | 수업홈 | `LessonHomePage.xaml` | 수업 홈 대시보드 — 내 시간표(주 이동·그 주 변경 반영), 오늘의 수업, 최근 수업 일지. 시간표 칸·오늘의 수업을 누르면 그 수업의 수업 일지를 쓴다 |
| `LessonActivity` | 누가 기록 | `LessonActivityPage.xaml` | 수업 누가 기록 |
| `CourseSpec` | 학생부 기록 | `CourseSpecPage.xaml` | 교과세특 (과목/강의실 필터) |
| `LessonJournal` | 수업 일지 | `Board/Pages/PostListPage.xaml` | 카테고리 `수업`·주제 `수업일지` 전용 게시판. 새 글은 전용 창(`LessonJournalWindow`)에서 쓴다 |
| `LessonBoard` | 수업 게시판 | `Board/Pages/PostListPage.xaml` | 카테고리 `수업` 고정 게시판 |
| `CourseManagement` | 수업 관리 | `CourseManagementPage.xaml` | 6탭 — 수업 개설 · 단원 관리 · 수업 시수 · 진도 관리 · 수업 시간표 입력 · 주별 시간표 확인 및 변경 |
| `ClubManagement` | 동아리 관리 | `ClubManagementPage.xaml` | 동아리 생성/수정/삭제 |
| `ClubActivity` | 동아리 활동 기록 | `ClubActivityPage.xaml` | 동아리 활동 기록 |

### 업무
| Tag | 메뉴 | Page | 기능 |
|-----|------|------|------|
| `SchoolWork` | 업무 관리 | `PageSchoolWork.xaml` | 업무 관리 |
| `WorkBoard` | 업무 게시판 | `Board/Pages/PostListPage.xaml` | 카테고리 `업무` 고정 게시판 |

### 설정
| Tag | 메뉴 | Page | 기능 |
|-----|------|------|------|
| `Settings_School` | 학교 설정 | `SettingsPage.xaml` | 학교·교시·연동 설정 |
| `Settings_SchoolSchedule` | 학사일정 관리 | `SchoolScheduleManagementPage.xaml` | 학사일정 관리 |
| `Settings_Student` | 학생 관리 | `StudentManagementPage.xaml` | 학생 관리 (→ `AddStudentsPage` 로 일괄 입력) |
| `Settings_App` | 앱 설정 | `AppSettingsPage.xaml` | 앱 설정 |
| `Help` | 도움말 | — | `Assets/help.html` 을 기본 브라우저로 연다 |
| `CheckUpdate` | 업데이트 확인 | — | `Services/UpdateService.cs` |

---

## 2. 게시판 모듈 (Board/)

### Pages
| 파일 | 기능 |
|------|------|
| `PostListPage.xaml` | 게시글 목록 (리스트/그리드/메모 뷰모드). **중요 글**(`Post.IsPinned`)은 어떤 정렬·필터에서도 그 목록 맨 앞에 온다 |
| `PostDetailPage.xaml` | 게시글 상세 보기 |
| `PostEditPage.xaml` | 게시글 작성/수정 |

### Controls
| 파일 | 기능 |
|------|------|
| `FileItemBox.xaml` | 첨부파일 아이템 |
| `PostFileListBox.xaml` | 첨부파일 목록 |
| `MemoBoard.xaml` | 메모 보드 뷰 |
| `LessonJournalList.xaml` | 최근 수업 일지 카드 (수업 홈에 얹힘) |

### Dialogs
| 파일 | 기능 |
|------|------|
| `MemoEditDialog.xaml` | 메모 편집 창 (리사이즈 가능한 별도 Window, ContentDialog 아님) |

### ViewModels
| 파일 | 기능 |
|------|------|
| `PostListViewModel.cs` | 게시글 목록 뷰모델 |
| `PostDetailViewModel.cs` | 게시글 상세 뷰모델 |

### Models
| 파일 | 기능 |
|------|------|
| `Post.cs` | 게시글 모델 (`IsPinned` = 중요 글, 글쓰기·수정 화면의 체크박스로 켠다) |
| `Comment.cs` | 댓글 모델 |
| `PostFile.cs` | 첨부파일 모델 |
| `BoardViewMode.cs` | 보기 모드 (List/Grid/Memo) |
| `PostSortOrder.cs` | 정렬 순서 |

### Services
| 파일 | 기능 |
|------|------|
| `BoardService.cs` | 게시글·댓글·첨부 비즈니스 로직 |
| `CachedBoardService.cs` | 캐시 계층. 항상 켜져 있다 — 끄는 설정은 저장만 되고 읽는 곳이 없어 40차에 걷어냈다 |
| `Board.cs` | 정적 진입점 (DB 초기화·서비스 생성) |

---

## 3. 일정 관리 모듈 (Scheduler/)

할 일(task)과 일정(event)은 `KEvent` 단일 모델로 통합 관리(`ItemType`으로 구분: `task`/`event`/`schoolschedule`).
옛 `Ktask`/`TaskDialog`/`KtaskListControl`은 제거됨.

| 파일 | 기능 |
|------|------|
| `Kcalendar.xaml` | 메인 캘린더 페이지 (월별 그리드) |
| `DayCell.xaml` | 일별 셀 컨트롤 (할 일/일정 표시, 완료 토글). `ItemType="schoolschedule"`는 날짜 옆 텍스트와 중복되므로 목록에서 제외 |
| `KAgendaControl.xaml` | 목록형(아젠다) 일정/할일 뷰. 날짜별 그룹 헤더 없이 단일 목록, 각 행 `[분류 배지][날짜][시간][제목][완료/진행]` |
| `UnifiedItemDialog.xaml` | 할 일/일정 통합 편집 다이얼로그 (반복 생성, 시리즈 삭제) |
| `RecurrenceHelper.cs` | 반복 규칙 전개 |
| `KEvent.cs` | 일정/할일 통합 모델 (Google Calendar Event 대응) |
| `KEventRepository.cs` | KEvent DB 접근 (Google 동기화 쿼리, 캘린더+ItemType별 조회 포함) |
| `KCalendarList.cs` | 캘린더 목록(카테고리+색상) 모델. `SchoolCode`로 학사일정 캘린더를 학교별로 분리 |
| `KCalendarListRepository.cs` | KCalendarList DB 접근 (학교별 조회/생성 포함) |
| `Scheduler.cs` | 정적 진입점 (Service/UnitOfWork 생성, `InitAsync`). DB 백업/복원/검증/최적화는 부르는 곳이 없어 걷어냈다 — 실제 백업·복원은 `Settings.cs` 한 곳이 맡는다 |
| `SchedulerService.cs` | 일정/할일 비즈니스 로직 레이어 |
| `UnitOfWork.cs` | 단일 트랜잭션으로 여러 Repository 원자적 처리 |
| `DatabaseInitializer.cs` | 스케줄러 DB 스키마 초기화/마이그레이션 |
| `DispatcherQueueExtensions.cs` | DispatcherQueue 비동기 실행 확장 메서드 |

### 학사일정 → Google Calendar 동기화

`Dialogs/CalendarSettingsDialog.xaml`의 "학사일정 동기화" 버튼(`Google/GoogleSyncService.cs`
`UploadSchoolSchedulesAsync`)은 `school.db`의 `SchoolSchedule`을 **학교 전용 Google 캘린더**(로컬
`KCalendarList.SchoolCode`로 학교별 분리, `SyncMode="None"` 고정)와 재조정(reconcile) 동기화한다.
단순 업로드가 아니라 신규/날짜변경/삭제를 모두 비교하므로, `school.db`를 수정한 뒤 다시 실행하면
구글에도 그대로 반영된다. 로컬 KEvent에 `GoogleId`를 저장해두어 이후 어떤 경로로 Pull이 일어나도
중복 생성되지 않는다.

---

## 4. 공통 컨트롤 (Controls/)

| 파일 | 기능 |
|------|------|
| `StudentCard.xaml` | 학생 카드 표시·편집. 입력이 멎고 3초 뒤 자동 저장(앱을 그냥 닫아도 잃지 않게) |
| `StudentLogBox.xaml` | 학생 기록 박스 |
| `StudentSpecBox.xaml` | 학생부 특기사항 박스 |
| `SpecListViewer.xaml` | 특기사항 목록 뷰어 |
| `ListStudent.xaml` | 학생 목록 |
| `PhotoCard.xaml` | 사진 카드 |
| `MonthPicker.xaml` | 월 선택기 |
| `CompactTimeButton.xaml` | 시각 입력 버튼 |
| `TimetableControl.xaml` | 시간표 컨트롤 (주 이동·그 주 변경 표시). `HasAnyLesson` 으로 호출부가 빈 시간표에 안내를 얹는다 |
| `ClassDiaryBox.xaml` | 학급일지 박스 |
| `ClassDiaryListWin.xaml` | 학급일지 목록 |
| `LogListViewer.xaml` | 기록 목록 뷰어 |
| `YearSemesterPicker.xaml` | 학년도/학기 선택기 |
| `ClassPicker.xaml` | 학년/반 선택기 (확정 시 학생 목록 이벤트 전달) |
| `CoursePicker.xaml` | 과목/강의실 선택기 (확정 시 수강생 목록 이벤트 전달) |
| `SchoolMealBox.xaml` | 급식 정보 박스 |
| `SchoolScheduleListControl.xaml` | 학사일정 목록 |
| `RichTextEditor.xaml` | 리치 텍스트 에디터 (WinUIRichEditor/Win2D 어댑터) |
| `RichTextEditorWin.xaml` | 에디터 윈도우 (전체화면 편집) |

### 수업 관리 탭 컨트롤
`CourseManagementPage` 의 여섯 탭은 각각 아래 컨트롤이 담당한다. 탭 위의 범위 선택줄은
`CourseScopeBar` 하나를 탭마다 두고 페이지가 값을 맞춘다(학년 콤보는 수업 개설 탭에만 노출).

| 파일 | 탭 |
|------|-----|
| `CourseScopeBar.xaml` | 공통 범위 선택줄 (학년도·학기·학년·수업) |
| `CourseSectionView.xaml` | 단원 관리 (CRUD · 드래그 정렬 · CSV 입출력) |
| `CourseHoursView.xaml` | 수업 시수 (주차 × 학급, 손으로 고친 칸만 저장) |
| `ProgressMatrixView.xaml` | 진도 관리 (단원 × 학급 매트릭스, 격차 분석·CSV) |
| `CourseTimetableBoard.xaml` | 수업 시간표 입력 (요일 × 교시 배치판, 드래그·키보드) |
| `WeeklyTimetableView.xaml` | 주별 시간표 확인 및 변경 (날짜 × 교시, 3주치) |

---

## 5. 다이얼로그 (Dialogs/)

| 파일 | 기능 |
|------|------|
| `SchoolSearchDialog.xaml` | 학교 검색 (NEIS API) |
| `CalendarSettingsDialog.xaml` | 구글 캘린더 연동·학사일정 동기화 |
| `StudentLogDialog.xaml` | 학생 기록 입력 |
| `StudentPrintOptionsDialog.xaml` | 학생 정보 출력 옵션 |
| `StudentSpecBatchDialog.xaml` | 학생부 일괄 입력 |
| `SpecExportFilterDialog.xaml` | 학생부 내보내기 필터 |
| `BatchExportFilterDialog.xaml` | 통합 내보내기 필터 |
| `BulkStudentInfoPreviewDialog.xaml` | 학생 정보 일괄 입력 미리보기 |
| `RosterTableDialog.xaml` | 명렬표 |
| `SeatOptionsDialog.xaml` | 자리 배정 옵션 |
| `SeatExclusionDialog.xaml` | 자리 배정 제외 |
| `SeatPrintOptionsDialog.xaml` | 자리 배치 출력 옵션 |
| `CourseEditDialog.xaml` | 수업(교과) 편집 |
| `CourseEnrollmentDialog.xaml` | 수강 등록 |
| `CourseSectionDialog.xaml` | 수업 단원 |
| `ClassTimetableEditDialog.xaml` | 학급 시간표 편집 |
| `LessonChangeDialog.xaml` | 앞으로 걸린 수업 변경 목록 (읽기·되돌리기) |
| `SubstituteInputDialog.xaml` | 보결 입력 (남의 수업 과목명 직접 적기) |
| `LessonJournalWindow.xaml` | 수업 일지 작성·편집 창 (머리 정보 + 제목 + 본문 + 첨부, 저장까지). 시간표 칸에서 열면 채워진 채 뜬다 |
| `MaterialEditDialog.xaml` | 자료 편집 |
| `ClubEditDialog.xaml` | 동아리 편집 |
| `ClubEnrollmentDialog.xaml` | 동아리 등록 |

---

## 6. 서비스 레이어 (Services/)

### 학생 관련
| 파일 | 기능 |
|------|------|
| `StudentService.cs` | 학생 CRUD |
| `StudentDetailService.cs` | 학생 상세정보 |
| `StudentSpecialService.cs` | 학생부 특기사항 |
| `StudentLogService.cs` | 학생 기록 |
| `StudentLogExportService.cs` | 학생 기록 내보내기 |
| `StudentLogPrintService.cs` | 학생 기록 출력 |
| `StudentSpecExportService.cs` | 학생부 내보내기 |
| `StudentSpecPrintService.cs` | 학생부 출력 |
| `StudentCardPrintService.cs` | 학생 카드 출력 |

### 수업/교과 관련
| 파일 | 기능 |
|------|------|
| `CourseService.cs` | 수업(교과) 관리 |
| `LessonService.cs` | 수업 |
| `EnrollmentService.cs` | 수강 등록 |
| `TimetableService.cs` | 시간표 |

### 시수/시간표 계산
화면마다 규칙을 복붙하지 않도록 계산은 이 둘에 모으고 테스트로 고정한다.

| 파일 | 기능 |
|------|------|
| `WeeklyHoursCalculator.cs` | 주차별 수업 가능 시수 계산 (시간표 배치 + 학사일정). 학기 경계는 여름방학(수업일 14일 이상 공백)에서 유추 |
| `TimetableChangeMerger.cs` | 날짜별 시간표 변경(`LessonChange`)을 평소 시간표에 얹기 — 오늘 화면·수업 홈이 함께 쓴다 |
| `Helpers/SchoolCalendar.cs` | 휴업일·학년 행사(`IsGradeOnlyEvent`)·수업일(`IsTeachingDayFor`) 판정 |

### 기타
| 파일 | 기능 |
|------|------|
| `SchoolService.cs` | 학교 정보 |
| `SchoolScheduleService.cs` | 학사일정 |
| `TeacherService.cs` | 교사 등록 (첫 실행 초기 설정 전용) |
| `ClubService.cs` | 동아리 |
| `ClassDiaryService.cs` | 학급일지 |
| `PhotoService.cs` | 사진 |
| `SeatsPrintService.cs` | 자리배치 PDF/HTML 출력 (DB 로드 오버로드 포함) |
| `SeatService.cs` | 자리배치 저장/로드, 짝·위치 이력 누적 |
| `UnifiedExportService.cs` | 통합 내보내기 (누가기록·학생부·좌석배정·학생카드·학생정보 × Excel/PDF/HTML) |
| `HtmlExportService.cs` | 공통 HTML 내보내기 |
| `CsvExportService.cs` | 공통 CSV 이스케이프·파싱 (단원 입출력, 통합 내보내기) |
| `UpdateService.cs` | 업데이트 확인 |
| `SecretsService.cs` | `secrets.json` 런타임 로더 (Google OAuth + NEIS API key 통합) |

---

## 7. 데이터 모델 (Models/)

### 학생 관련
- `Student.cs` - 학생
- `StudentDetail.cs` - 학생 상세정보
- `StudentSpecial.cs` - 학생부 특기사항
- `StudentLog.cs` - 학생 기록
- `Enrollment.cs` - 등록(재적)

### 수업 관련
- `Course.cs` - 수업(교과)
- `CourseSection.cs` - 수업 단원
- `CourseEnrollment.cs` - 수강 등록
- `Lesson.cs` - 정기 수업(요일 × 교시 배치)
- `LessonChange.cs` - 날짜·교시 단위 예외 — 휴강·교체·보강·대강. `Lesson` 은 건드리지 않는다
- `LessonProgress.cs` - 수업 진도 (단원 × 학급)
- `CourseWeeklyHours.cs` - 주차별 시수 조정(손으로 고친 주차만 저장)

### 계획/일정
- `SchoolSchedule.cs` - 학사일정
- `SchoolScheduleGroup.cs` - 연속 학사일정 묶음(표시용)
- `ClassTimetable.cs` - 학급 시간표

> 출결(`Attendance.cs`)은 읽고 쓰는 곳이 한 군데도 없어 지웠다(2026-08-24). 결석·지각·조퇴는
> 학급일지(`ClassDiary`)의 텍스트 칸으로 적는다.

### 조직
- `School.cs` - 학교
- `Teacher.cs` - 교사
- `TeacherSchoolHistory.cs` - 교사 학교 이력
- `Club.cs` - 동아리
- `ClubEnrollment.cs` - 동아리 등록
- `ClassDiary.cs` - 학급일지

### 자리 배정
- `SeatArrangement.cs` - 자리 배치(짝·위치 이력 포함)
- `SeatOptions.cs` - 자리 배정 옵션

### 공통
- `Base.cs` - 모델 공통 기반
- `NotifyPropertyChangedBase.cs` - INotifyPropertyChanged 기반
- `Constants.cs` - 공통 상수
- `LogEnums.cs` - 기록 열거형

---

## 8. 리포지토리 (Repositories/)

| 파일 | 대상 모델 |
|------|----------|
| `StudentRepository.cs` | Student |
| `StudentDetailRepository.cs` | StudentDetail |
| `StudentSpecialRepository.cs` | StudentSpecial |
| `StudentLogRepository.cs` | StudentLog |
| `CourseRepository.cs` | Course |
| `CourseSectionRepository.cs` | CourseSection |
| `CourseEnrollmentRepository.cs` | CourseEnrollment |
| `LessonRepository.cs` | Lesson |
| `LessonChangeRepository.cs` | LessonChange |
| `LessonProgressRepository.cs` | LessonProgress |
| `CourseWeeklyHoursRepository.cs` | CourseWeeklyHours |
| `SchoolScheduleRepository.cs` | SchoolSchedule |
| `ClassTimetableRepository.cs` | ClassTimetable |
| `SchoolRepository.cs` | School |
| `TeacherRepository.cs` | Teacher |
| `TeacherSchoolHistoryRepository.cs` | TeacherSchoolHistory |
| `ClubRepository.cs` | Club |
| `ClubEnrollmentRepository.cs` | ClubEnrollment |
| `ClassDiaryRepository.cs` | ClassDiary |
| `EnrollmentRepository.cs` | Enrollment |
| `BaseRepository.cs` | (공통 기반 — 연결·트랜잭션·매핑) |

> 스키마 정본은 `DatabaseInitializer.cs` 의 `CREATE TABLE` 한 벌이다. 1.0 을 첫 배포로 잡았으므로
> 그 이전 모양을 위한 `ALTER TABLE` 마이그레이션은 두지 않는다.
> 소유 관계는 `NewSchool.Tests/SchemaOwnershipTests.cs` 가 못박는다.
>
> `board.db` 도 같은 방침이다(정본은 `Board/Board.cs`). 열을 더할 때 **쓰던 파일은 손으로 고친다** —
> 예: 중요 글의 `IsPinned` 는 `ALTER TABLE Post ADD COLUMN IsPinned INTEGER NOT NULL DEFAULT 0`.

---

## 9. 헬퍼/유틸리티

| 파일 | 기능 |
|------|------|
| `Helpers/ExcelReader.cs` | 엑셀 파일 읽기 |
| `Helpers/ExcelHelpers.cs` | 엑셀 유틸리티 |
| `Helpers/NeisHelper.cs` | NEIS API 헬퍼 |
| `Helpers/SchoolCalendar.cs` | 휴업일·학년 행사·수업일 판정 (시수 계산이 쓴다) |
| `Helpers/ImportParsing.cs` | 일괄 입력 파싱 ("1학년"→1, 성별 정규화) |
| `Helpers/FileNameHelper.cs` | 파일명 정리 |
| `Helpers/DbIntegrity.cs` | DB 무결성 점검 |
| `Helpers/ThemeHelper.cs` | 저장된 테마를 창에 적용 (메인 창 시작 시 + 보조 창 열 때) |
| `Helpers/KoreanImeHelper.cs` | 한글 IME 조합 처리 |
| `Helpers/TextBoxDropHelper.cs` | 텍스트박스 드롭 처리 |
| `DateTimeHelper.cs` | 날짜·주차 계산 |
| `Converters/CommonConverters.cs` | 공통 컨버터 |
| `Converters/SchoolScheduleConverters.cs` | 학사일정 표시 컨버터 |
| `Logging/FileLogger.cs` | 파일 로깅 |
| `Tools.cs` | 공통 도구 |
| `Functions.cs` | 공통 함수 (교시 판정 `GetPeriodAt` 등) |
| `Settings.cs` | 설정 키-값 접근 |
| `SchoolDatabase.cs` | 메인 DB 경로·연결 |
| `DatabaseInitializer.cs` | 메인 DB 스키마 정본 (`CREATE TABLE` 한 벌) |

---

## 10. 설정 (Settings.cs)

### 스케줄러 설정
- `SchedulerDB` - 스케줄러 DB 경로
- `ShowEvents` / `ShowTasks` - 이벤트/태스크 표시
- `EventFontSize` - **학사일정** 텍스트(DayCell의 `TbDateName`, 날짜 옆 표시) 폰트 크기 — 설정창 라벨은 "학사일정 폰트"
- `TaskFontSize` - DayCell **이벤트/할일 목록**(`EvtTitleText`/`EvtTimeText`/`TaskTitleText`) 폰트 크기 — 설정창 라벨은 "할 일 폰트 (이벤트/할일 목록)". `KAgendaControl`(아젠다 목록)은 별도 컨트롤이라 이 설정과 무관하게 고정 폰트(제목/날짜/시간 12px) 사용
- `DateFontSize` - 날짜 숫자(`LbDate`)·요일 헤더·년월 선택(`MonthPicker.DisplayFontSize`) 폰트 크기 — 설정창 라벨은 "요일/날짜 폰트"
- `UseGoogle` - 구글 캘린더 연동
- `GoogleCalendarID` - 구글 캘린더 정보 (`GoogleCalendarName` 은 읽는 곳이 없어 40차에 제거)

### 학교 설정
- `SchoolDB` - 학교 DB 경로
- `WorkYear` / `WorkSemester` - 근무년도/학기
- `ProvinceCode` / `SchoolCode` / `SchoolName` - 학교 정보
- `NeisApiKey` - NEIS API 키
- `HomeGrade` / `HomeRoom` - 담임 학년/반

### 교시 설정
- `AssemblyTime` - 조회 시간
- `DayStarting` - 수업 시작 시간
- `BreakTime` - 쉬는 시간
- `OnePeriod` - 1교시 길이
- `LunchTime` - 점심 시간

### 게시판 설정
- `Board_DB` - 게시판 DB 경로 (페이지 크기는 목록 화면의 콤보가 직접 들고 있다)

### 일반 설정
- `AutoBackup` / `AutoBackupIntervalDays` / `BackupRetentionCount` - 백업
- `Theme` - 테마. 시작 시 `Helpers/ThemeHelper.Apply` 로 메인 창과 보조 창에 건다
- `TopMost` - 항상 위에(기본 꺼짐, `OverlappedPresenter.IsAlwaysOnTop`)
- `WindowWidth` / `WindowHeight` - 창 크기
- `LogLevel` - 로그 레벨

---

## 11. 데이터베이스 구조

### 저장 위치 (`Settings.cs`)

실행 파일 옆 `portable.txt` 유무로 루트를 정한다 — 있으면 실행 파일 폴더(포터블),
없으면 `%USERPROFILE%\NewSchool`(설치본). 두 모드 모두 루트 아래 배치는 같다.

| 폴더 | 내용 | 성격 |
|------|------|------|
| `Data\` | DB 4종 · `Photos\{연도}\` · `Files\{게시판}\` | 사용자 자산 — 이 폴더가 이관 단위 |
| `Backups\` | `backup_*.zip` (DB만) | 산출물. `Data\` 안에 두면 백업이 자기 자신을 담는다 |
| `Exports\` · `Prints\` · `Logs\` | xlsx·html·csv / pdf / 로그 | 산출물 |

- 판정을 표식 파일로 바꾼 이유: 옛 방식(`Settings.db` 위치로 판정)은 DB 가 사라지거나
  동기화가 이름을 바꾸면 조용히 사용자 폴더 모드로 넘어가 빈 화면을 띄웠다
- 다만 표식만 보는 것은 아니다(`Settings.IsPortableLayout`) — 표식이 있어도 실행 파일 폴더에
  **쓸 수 없으면**(읽기 전용 매체·`Program Files`) 설치본으로 물러서고, 표식이 없어도 옛 배치면
  포터블로 보고 표식을 만들어 준다
- 1.0 이전 배치의 이관은 **손으로 한다** — 대상이 사실상 폴더 하나뿐이라 영영 안 도는 코드를
  남기지 않았다. 옮긴 뒤에는 `Data\Settings.db` 로 포터블을 알아본다
- 백업·복원은 관리 DB 4종만 담는 화이트리스트를 쓴다 — `*.db` 전수로 훑던 옛 방식은
  동기화 충돌본(`school-이름.db`)까지 ZIP 에 담고 복원 때 되살렸다

### 메인 DB (school.db)
- 학생, 교사, 학교, 수업, 시간표 등 핵심 데이터

### 스케줄러 DB (scheduler.db)
- 일정, 태스크 데이터

### 게시판 DB (board.db)
- 게시글, 댓글, 첨부파일 데이터

### 설정 DB (Settings.db)
- 앱 설정 키-값 저장

---

## 빠른 검색 가이드

### 기능별 파일 찾기

| 기능 | 주요 파일 |
|------|----------|
| 학생 정보 | `Pages/PageStudentInfo.xaml`, `Services/StudentService.cs` |
| 학생 기록 | `Pages/PageStudentLog.xaml`, `Services/StudentLogService.cs` |
| 학생부 | `Pages/StudentSpecPage.xaml`, `Services/StudentSpecialService.cs` |
| 학생부(교과세특) | `Pages/CourseSpecPage.xaml`, `Controls/CoursePicker.xaml` |
| 자리 배정 | `Pages/PageSeats.xaml`, `Services/SeatService.cs`, `Services/SeatsPrintService.cs`, `Dialogs/SeatOptionsDialog.xaml` |
| 시간표 | `Controls/TimetableControl.xaml`, `Services/TimetableService.cs` |
| 수업 관리(6탭) | `Pages/CourseManagementPage.xaml`, `Controls/CourseScopeBar.xaml`, `Services/CourseService.cs` |
| 단원 관리 | `Controls/CourseSectionView.xaml`, `Repositories/CourseSectionRepository.cs` |
| 수업 시수 | `Controls/CourseHoursView.xaml`, `Services/WeeklyHoursCalculator.cs` |
| 진도 관리 | `Controls/ProgressMatrixView.xaml`, `Repositories/LessonProgressRepository.cs` |
| 수업 시간표 입력 | `Controls/CourseTimetableBoard.xaml`, `Repositories/LessonRepository.cs` |
| 주별 시간표·수업 변경 | `Controls/WeeklyTimetableView.xaml`, `Models/LessonChange.cs`, `Services/TimetableChangeMerger.cs` |
| 수업 일지 | `Board/Pages/PostListPage.xaml`(카테고리 `수업`·주제 `수업일지`), `Dialogs/LessonJournalComposer.cs`(진입점·제목 규칙), `Dialogs/LessonJournalWindow.xaml`(작성·편집 창), `Board/Controls/LessonJournalList.xaml` |
| 동아리 | `Pages/ClubManagementPage.xaml`, `Services/ClubService.cs` |
| 게시판 | `Board/Pages/PostListPage.xaml`, `Board/Services/BoardService.cs` |
| 달력/일정 | `Scheduler/Kcalendar.xaml`, `Scheduler/SchedulerService.cs` |
| 학급일지 | `Pages/ClassDiaryPage.xaml`, `Services/ClassDiaryService.cs` |
| 학사일정 | `Pages/SchoolScheduleManagementPage.xaml`, `Services/SchoolScheduleService.cs` |
| 통합 내보내기 | `Pages/UnifiedExportPage.xaml`, `Services/UnifiedExportService.cs`, `Services/HtmlExportService.cs` |
| 비밀 정보(API 키) | `secrets.json` (.gitignore), `secrets.template.json`, `Services/SecretsService.cs` |

---

## 차후 과제

| 과제 | 요약 | 상세 |
|------|------|------|
| ~~**테스트 확충**~~ ✅ 완료 | 테스트 25개 → 211개(0~4단계, 2026-07-12) → **514개**(2026-08-26 기준, 이후 회귀 테스트 누적). 리포지토리 CRUD·경계 → 서비스 로직·회귀 → 헬퍼·파서 → VM 변환. 잠재 버그 2건도 작성 중 발견·수정. 잔여(Settings 파서·Excel 헤더 탐지 등)는 ROI 낮아 보류 | [TEST_PLAN.md](TEST_PLAN.md) |
| ~~**자체 포함 전환 + 실행 파일을 `bin\` 하위로**~~ ✅ 완료 (2026-08-26, v1.0.0 재게시에 포함) — 실측 **설치 파일 117.1MB → 26MB(−78%)**, 설치 폴더 46파일·4폴더·96.5MB, 루트엔 `bin\` 과 언인스톨러만. 런타임 설치 단계·`prerequisites\`·`RequiredRuntimeVersion` 검사 전부 제거. ⚠ 로 적어 둔 포터블 문제는 **`Settings.FindPortableRoot` 가 실행 파일 폴더의 부모까지 보도록** 고쳐 해결(회귀 테스트 5건 추가, 실제 설치로 `{app}\Data\` 생성 확인). 아래는 결정 당시 기록 | 1.0.0 은 런타임 번들로 냈다(설치 파일 **122.8MB** = 런타임 설치기 106.9MB + 앱 15.9MB). 자체 포함으로 바꾸면 **설치 파일 29.1MB**(1/4)로 줄고 **런타임 설치 단계가 통째로 사라진다** — 1.0.0 게시를 막았던 "번들 2.3 vs 요구 2.4" 부류가 원천 소멸한다. 대가는 설치 폴더 8개·40MB → **53개·93MB**(정리 후). 지저분함은 **앱 전체를 `{app}\bin\` 하위에 넣어** 해결한다(루트엔 그 폴더와 언인스톨러만). ⚠ 포터블 판정이 exe 폴더 기준이라 `Data\` 가 `bin\` 아래로 들어간다 — 그 부분 조정 필요 | `NewSchool.csproj:255`, `Installer/NewSchoolSetup.iss` |
| ~~`lib\` 로 DLL 격리~~ ❌ 불가 (2026-08-24 실측) | exe 는 페이로드 DLL 을 **정적 임포트하지 않아**(시스템 DLL 18개뿐) 가능해 보였다. PATH 는 .NET 이 `SetDefaultDllDirectories` 로 빼서 실패, `AddDllDirectory`+`SetDllImportResolver` 로 **DLL 로드까지는 성공**했으나 **WinRT 활성화에서 막힌다**(`Microsoft.UI.Xaml.Application` 팩토리 → COMException). 액티베이션 컨텍스트가 클래스→DLL 을 앱 디렉터리 기준으로 찾기 때문. 지원되는 구성이 아니고 SDK 업데이트마다 깨진다 → **재시도 금지**, 위 `bin\` 안으로 갈음 | — |
| ~~게시 필터의 접두어 오탐~~ ✅ 처리 | `kok-IN`·`en-GB` 는 `CleanPublishOutput` 에서 명시적으로 제거한다. AI 계열(`Windows.Search`·`Widgets`·`PerceptiveStreaming`·`NPUDetect`·`Workloads`)도 이름으로 직접 지운다 | `NewSchool.csproj` `CleanPublishOutput` |
| ~~qpdf.dll 게시 제외~~ ✅ 처리 | 제외 확정. 근거 — `QuestPdfSkia.dll` 이 `qpdf.dll` 을 **정적 임포트하지 않고**(PE 임포트 4개뿐), qpdf 로 가는 길은 `QuestPDF.Qpdf`(병합·PDF/A) 뿐인데 이 앱은 `GeneratePdf` 7곳만 쓴다. **렌더링 본체 `QuestPdfSkia.dll` 은 지우지 말 것** | `NewSchool.csproj` `CleanPublishOutput` |
| 학생 관리 상태 편집 | 학생 목록에서 재학/전학/휴학 상태를 콤보로 변경(현재 읽기 전용) | `Pages/StudentManagementPage.xaml` |
| 진급 처리 | 학년도가 바뀔 때 같은 StudentID 로 다음 학년도 학적을 잇는 기능. **구현과 화면 모두 없다** — `PromoteStudentsAsync` 는 부를 방법이 없어 한 번도 실사용되지 않은 채 제거했다(2026-08-23, 지운 코드는 git 에 있다). 되살린다면 **반·번호 재배정을 어떻게 할지부터** 정해야 한다(옛 코드는 이전 반·번호를 그대로 복사했다). 졸업 마감(GraduateAsync)은 불필요 판단으로 제거함(2026-07-15) | `Services/EnrollmentService.cs` |
