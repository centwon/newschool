# NewSchool 프로젝트 기능 리스트

## 1. 메인 네비게이션 구조 (MainWindow.xaml)

### 홈
- **Tag**: `Home`
- **Page**: `TodayPage.xaml`
- **기능**: 오늘의 일정/정보 대시보드

### 달력
- **Tag**: `Calendar`
- **Page**: `Scheduler/Kcalendar.xaml`
- **기능**: 일정 관리 캘린더

---

## 2. 학급 관리

| Tag | Page | 기능 |
|-----|------|------|
| `ClassDiary` | `ClassDiaryPage.xaml` | 학급 일지 작성/관리 |
| `StudentInfo` | `PageStudentInfo.xaml` | 학생 정보 조회 |
| `StudentLog` | `PageStudentLog.xaml` | 학생 기록 관리 |
| `StudentSpec` | `StudentSpecPage.xaml` | 학생부 관리 |
| `Seats` | `PageSeats.xaml` | 자리 배정 |
| `StudentInfoExport` | `StudentInfoExportPage.xaml` | 학생 정보 출력 |
| `Timetable_ClassManagement` | `ClassTimetableManagementPage.xaml` | 학급 시간표 관리 |

---

## 3. 수업 관리

| Tag | Page | 기능 |
|-----|------|------|
| `LessonHome` | `LessonHomePage.xaml` | 수업 홈 대시보드 |
| `AnnualLessonPlan` | `AnnualLessonPlanPage.xaml` | 연간 수업 계획 (단원 관리, 시수 계획, 자동 배치) |
| `ProgressMatrix` | `ProgressMatrixPage.xaml` | 진도 관리 매트릭스 |
| `LessonActivity` | `LessonActivityPage.xaml` | 수업 누가 기록 |
| `Timetable_Teacher` | `TeacherTimetablePage.xaml` | 수업 시간표 |

---

## 4. 동아리 관리

| Tag | Page | 기능 |
|-----|------|------|
| `ClubHome` | `ClubHomePage.xaml` | 동아리 홈 대시보드 |
| `ClubActivity` | `ClubActivityPage.xaml` | 동아리 활동 기록 |
| `ClubManagement` | `ClubManagementPage.xaml` | 동아리 관리 (생성/수정/삭제) |

---

## 5. 설정

| Tag | Page | 기능 |
|-----|------|------|
| `Settings_General` | `SettingsPage.xaml` | 일반 설정 |
| `Settings_Student` | `StudentManagementPage.xaml` | 학생 관리 |
| `Settings_SchoolSchedule` | `SchoolScheduleManagementPage.xaml` | 학사일정 관리 |
| `CourseManagement` | `CourseManagementPage.xaml` | 수업(교과) 관리 |

---

## 6. 게시판 모듈 (Board/)

### Pages
| 파일 | 기능 |
|------|------|
| `PostListPage.xaml` | 게시글 목록 (리스트/그리드/메모 뷰모드) |
| `PostDetailPage.xaml` | 게시글 상세 보기 |
| `PostEditPage.xaml` | 게시글 작성/수정 |

### Controls
| 파일 | 기능 |
|------|------|
| `CommentBox.xaml` | 댓글 박스 컨트롤 |
| `FileItemBox.xaml` | 첨부파일 아이템 |
| `PostFileListBox.xaml` | 첨부파일 목록 |
| `MemoBoard.xaml` | 메모 보드 뷰 |
| `MemoItem.xaml` | 메모 아이템 |

### Dialogs
| 파일 | 기능 |
|------|------|
| `MemoEditDialog.xaml` | 메모 편집 다이얼로그 |

### ViewModels
| 파일 | 기능 |
|------|------|
| `PostListViewModel.cs` | 게시글 목록 뷰모델 |
| `PostDetailViewModel.cs` | 게시글 상세 뷰모델 |

### Models
| 파일 | 기능 |
|------|------|
| `Post.cs` | 게시글 모델 |
| `Comment.cs` | 댓글 모델 |
| `PostFile.cs` | 첨부파일 모델 |
| `BoardViewMode.cs` | 보기 모드 (List/Grid/Memo) |

---

## 7. 일정 관리 모듈 (Scheduler/)

| 파일 | 기능 |
|------|------|
| `Kcalendar.xaml` | 메인 캘린더 컨트롤 |
| `DayCell.xaml` | 일별 셀 컨트롤 |
| `TaskDialog.xaml` | 일정 편집 다이얼로그 |
| `KtaskListControl.xaml` | 일정 목록 컨트롤 |
| `Ktask.cs` | 일정 모델 |
| `SchedulerService.cs` | 일정 서비스 |
| `CachedSchedulerService.cs` | 캐시된 일정 서비스 |

---

## 8. 공통 컨트롤 (Controls/)

| 파일 | 기능 |
|------|------|
| `StudentCard.xaml` | 학생 카드 표시 |
| `StudentLogBox.xaml` | 학생 기록 박스 |
| `StudentLogWin.xaml` | 학생 기록 윈도우 |
| `StudentSpecBox.xaml` | 학생부 특기사항 박스 |
| `SpecListViewer.xaml` | 특기사항 목록 뷰어 |
| `ListStudent.xaml` | 학생 목록 |
| `PhotoCard.xaml` | 사진 카드 |
| `MonthPicker.xaml` | 월 선택기 |
| `TimetableControl.xaml` | 시간표 컨트롤 |
| `ClassDiaryBox.xaml` | 학급일지 박스 |
| `ClassDiaryListWin.xaml` | 학급일지 목록 |
| `LessonLogList.xaml` | 수업 기록 목록 |
| `LogListViewer.xaml` | 기록 목록 뷰어 |
| `SchoolFilterPicker.xaml` | 학교 필터 선택기 |
| `SchoolMealBox.xaml` | 급식 정보 박스 |
| `SchoolScheduleListControl.xaml` | 학사일정 목록 |
| `JoditEditor.xaml` | 리치 텍스트 에디터 |
| `JoditEditorWin.xaml` | 에디터 윈도우 |
| `AttachmentBox.xaml` | 공통 첨부파일 컨트롤 (파일 추가/삭제/열기) |

---

## 9. 다이얼로그 (Dialogs/)

| 파일 | 기능 |
|------|------|
| `InitialSetupDialog.xaml` | 초기 설정 |
| `SchoolSearchDialog.xaml` | 학교 검색 (NEIS API) |
| `StudentLogDialog.xaml` | 학생 기록 입력 |
| `StudentPrintOptionsDialog.xaml` | 학생 정보 출력 옵션 |
| `CourseEditDialog.xaml` | 수업(교과) 편집 |
| `CourseEnrollmentDialog.xaml` | 수강 등록 |
| `CourseScheduleDialog.xaml` | 수업 일정 |
| `CourseSectionDialog.xaml` | 수업 단원 |
| `ClassTimetableEditDialog.xaml` | 학급 시간표 편집 |
| `ClubEditDialog.xaml` | 동아리 편집 |
| `ClubEnrollmentDialog.xaml` | 동아리 등록 |
| `LessonLogEditDialog.xaml` | 수업 기록 편집 |
| `MaterialEditDialog.xaml` | 자료 편집 |

---

## 10. 서비스 레이어 (Services/)

### 학생 관련
| 파일 | 기능 |
|------|------|
| `StudentService.cs` | 학생 CRUD |
| `StudentDetailService.cs` | 학생 상세정보 |
| `StudentSpecialService.cs` | 학생부 특기사항 |
| `StudentLogService.cs` | 학생 기록 |
| `StudentLogExportService.cs` | 학생 기록 내보내기 |
| `StudentLogPrintService.cs` | 학생 기록 출력 |
| `StudentCardPrintService.cs` | 학생 카드 출력 |

### 수업/교과 관련
| 파일 | 기능 |
|------|------|
| `CourseService.cs` | 수업(교과) 관리 |
| `LessonService.cs` | 수업 |
| `LessonLogService.cs` | 수업 기록 |
| `EnrollmentService.cs` | 수강 등록 |
| `TimetableService.cs` | 시간표 |

### 진도/계획 관련
| 파일 | 기능 |
|------|------|
| `ProgressSyncService.cs` | 진도 동기화 |
| `WeeklyHoursCalculator.cs` | 주간 시수 계산 |
| `WeeklyHoursHelper.cs` | 주간 시수 헬퍼 |
| `SchedulingEngine.cs` | 일정 엔진 |
| `ScheduleShiftService.cs` | 일정 이동 |

### 기타
| 파일 | 기능 |
|------|------|
| `SchoolService.cs` | 학교 정보 |
| `SchoolScheduleService.cs` | 학사일정 |
| `TeacherService.cs` | 교사 정보 |
| `ClubService.cs` | 동아리 |
| `ClassDiaryService.cs` | 학급일지 |
| `AttachmentService.cs` | 공통 첨부파일 (파일 복사/삭제 + DB 관리) |
| `PhotoService.cs` | 사진 |
| `SeatsPrintService.cs` | 자리배치 출력 |
| `ReportExportService.cs` | 리포트 내보내기 |

---

## 11. 데이터 모델 (Models/)

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
- `Lesson.cs` - 수업
- `LessonLog.cs` - 수업 기록
- `LessonProgress.cs` - 수업 진도

### 계획/일정
- `Schedule.cs` - 일정
- `SchoolSchedule.cs` - 학사일정
- `ClassTimetable.cs` - 학급 시간표
- `SubjectYearPlan.cs` - 교과 연간계획
- `WeeklyLessonHours.cs` - 주간 수업시수
- `WeeklyUnitPlan.cs` - 주간 단원계획

### 조직
- `School.cs` - 학교
- `Teacher.cs` - 교사
- `TeacherSchoolHistory.cs` - 교사 학교 이력
- `Club.cs` - 동아리
- `ClubEnrollment.cs` - 동아리 등록
- `ClassDiary.cs` - 학급일지

### 공통
- `Attachment.cs` - 공통 첨부파일 (LessonLog, ClassDiary, CourseSection 등)
- `Interfaces.cs` - 공통 인터페이스 (IEntity, IDailyRecord, IStudentRecord 등)

### 기타
- `Evaluation.cs` - 평가
- `LogEnums.cs` - 기록 열거형

---

## 12. 리포지토리 (Repositories/)

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
| `LessonLogRepository.cs` | LessonLog |
| `LessonProgressRepository.cs` | LessonProgress |
| `ScheduleRepository.cs` | Schedule |
| `SchoolScheduleRepository.cs` | SchoolSchedule |
| `ClassTimetableRepository.cs` | ClassTimetable |
| `SubjectYearPlanRepository.cs` | SubjectYearPlan |
| `WeeklyLessonHoursRepository.cs` | WeeklyLessonHours |
| `WeeklyUnitPlanRepository.cs` | WeeklyUnitPlan |
| `SchoolRepository.cs` | School |
| `TeacherRepository.cs` | Teacher |
| `TeacherSchoolHistoryRepository.cs` | TeacherSchoolHistory |
| `ClubRepository.cs` | Club |
| `ClubEnrollmentRepository.cs` | ClubEnrollment |
| `ClassDiaryRepository.cs` | ClassDiary |
| `AttachmentRepository.cs` | Attachment |
| `EvaluationRepository.cs` | Evaluation |
| `EnrollmentRepository.cs` | Enrollment |
| `ScheduleUnitMapRepository.cs` | ScheduleUnitMap |
| `UndoHistoryRepository.cs` | UndoAction |

---

## 13. 헬퍼/유틸리티

| 파일 | 기능 |
|------|------|
| `Helpers/ExcelReader.cs` | 엑셀 파일 읽기 |
| `Helpers/ExcelHelpers.cs` | 엑셀 유틸리티 |
| `Helpers/NeisHelper.cs` | NEIS API 헬퍼 |
| `Converters/CommonConverters.cs` | 공통 컨버터 |
| `UI/UIHelpers.cs` | UI 유틸리티 |
| `Validation/ValidationHelper.cs` | 유효성 검증 |
| `Logging/FileLogger.cs` | 파일 로깅 |
| `Events/EventAggregator.cs` | 이벤트 집계 |
| `Tools.cs` | 공통 도구 |
| `Functions.cs` | 공통 함수 |

---

## 14. 설정 (Settings.cs)

### 스케줄러 설정
- `SchedulerDB` - 스케줄러 DB 경로
- `ShowEvents` / `ShowTasks` - 이벤트/태스크 표시
- `EventFontSize` / `TaskFontSize` - 폰트 크기
- `UseGoogle` - 구글 캘린더 연동
- `GoogleCalendarName` / `GoogleCalendarID` - 구글 캘린더 정보

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
- `Board_DB` - 게시판 DB 경로
- `EnableCache` - 캐시 활성화
- `EnableFTS` - 전문 검색 활성화
- `DefaultPageSize` - 페이지 크기

### 일반 설정
- `AutoBackup` / `AutoBackupIntervalDays` / `BackupRetentionCount` - 백업
- `Theme` / `Language` - 테마/언어
- `WindowWidth` / `WindowHeight` - 창 크기
- `LogLevel` - 로그 레벨

---

## 15. 데이터베이스 구조

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
| 자리 배정 | `Pages/PageSeats.xaml`, `Services/SeatsPrintService.cs` |
| 시간표 | `Controls/TimetableControl.xaml`, `Services/TimetableService.cs` |
| 수업 관리 | `Pages/CourseManagementPage.xaml`, `Services/CourseService.cs` |
| 진도 관리 | `Pages/ProgressMatrixPage.xaml`, `Services/ProgressSyncService.cs` |
| 동아리 | `Pages/ClubHomePage.xaml`, `Services/ClubService.cs` |
| 게시판 | `Board/Pages/PostListPage.xaml`, `Board/Services/BoardService.cs` |
| 달력/일정 | `Scheduler/Kcalendar.xaml`, `Scheduler/SchedulerService.cs` |
| 학급일지 | `Pages/ClassDiaryPage.xaml`, `Services/ClassDiaryService.cs` |
| 학사일정 | `Pages/SchoolScheduleManagementPage.xaml`, `Services/SchoolScheduleService.cs` |
