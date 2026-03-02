# 수업 계획 시스템 - 작업 체크리스트

> **시작일:** 2025-01-23  
> **예상 총 시간:** ~21시간  
> **진행 상황:** 24 / 24 완료 (Phase 6 완료!) 🎉

---

## Phase 1: 데이터 모델 정리 ⏱️ 예상 3시간

### 작업 1-1: CourseSection.cs 수정 ✅
- [x] SectionType 필드 추가 (Normal/Exam/Assessment/Event)
- [x] IsPinned, PinnedDate 필드 추가
- [x] EndPage 필드 추가
- [x] LearningObjective 필드 추가
- [x] MaterialPath, MaterialUrl 필드 추가
- [x] Memo 필드 추가
- [x] Computed Properties 추가 (IsEvaluation, PageRangeDisplay 등)
- [x] Clone() 메서드 추가 (백업용)

**파일:** `Models/CourseSection.cs`  
**의존:** 없음  
**완료일:** 2025-01-23  
**메모:** SectionTypeIcon, ShortInfo 등 UI용 속성도 추가  

---

### 작업 1-2: CourseSectionRepository.cs 수정 ✅
- [x] EnsureTableExists()에 새 컬럼 ALTER TABLE 추가
- [x] AddParameters() 메서드 수정
- [x] MapSection() 메서드 수정
- [x] GetPinnedSectionsAsync() 메서드 추가
- [x] GetNormalSectionsAsync() 메서드 추가
- [x] GetByTypeAsync() 메서드 추가
- [x] GetHoursByTypeAsync() 메서드 추가
- [x] SetPinnedDateAsync() 메서드 추가
- [x] SetSectionTypeAsync() 메서드 추가
- [x] UpdateMemoAsync() 메서드 추가

**파일:** `Repositories/CourseSectionRepository.cs`  
**의존:** 작업 1-1  
**완료일:** 2025-01-23  
**메모:** 기존 DB 마이그레이션 지원 (AddNewColumnsIfNeeded)  

---

### 작업 1-3: Schedule.cs 신규 생성 ✅
- [x] 기본 필드 정의 (No, CourseId, Room, Date, Period)
- [x] 상태 필드 정의 (IsCompleted, IsCancelled, IsPinned)
- [x] Navigation Properties (UnitMaps, Course)
- [x] Computed Properties (DateDisplay, SlotDisplay, IsMerged 등)
- [x] 상태 변경 메서드 (MarkAsCompleted, MarkAsCancelled 등)
- [x] CloneToNewSlot() 메서드

**파일:** `Models/Schedule.cs` (신규)  
**의존:** 없음  
**완료일:** 2025-01-23  
**메모:** StatusColor, IsMovable 등 UI용 속성 포함  

---

### 작업 1-4: ScheduleRepository.cs 신규 생성 ✅
- [x] EnsureTableExists() - 테이블 및 인덱스 생성
- [x] CRUD 기본 메서드
- [x] GetByDateRangeAsync()
- [x] GetByCourseAndRoomAsync()
- [x] GetUnpinnedSchedulesFromDateAsync()
- [x] ShiftSchedulesAsync() - 일괄 이동 (Transaction)
- [x] GetOrCreateAsync() - 배치 엔진용
- [x] GetStatsAsync() - 통계 조회
- [x] ScheduleStats 클래스

**파일:** `Repositories/ScheduleRepository.cs` (신규)  
**의존:** 작업 1-3  
**완료일:** 2025-01-23  
**메모:** 고정/결강/완료 처리 메서드 포함  

---

### 작업 1-5: ScheduleUnitMap.cs 신규 생성 ✅
- [x] 기본 필드 정의 (No, ScheduleId, CourseSectionId)
- [x] AllocatedHours, OrderInSlot 필드
- [x] Navigation Properties (Schedule, CourseSection)
- [x] Computed Properties (SectionName, ScheduleSlotDisplay 등)

**파일:** `Models/ScheduleUnitMap.cs` (신규)  
**의존:** 작업 1-3  
**완료일:** 2025-01-23  
**메모:**  

---

### 작업 1-6: ScheduleUnitMapRepository.cs 신규 생성 ✅
- [x] EnsureTableExists()
- [x] CRUD 기본 메서드
- [x] AddUnitToScheduleAsync()
- [x] GetByScheduleAsync()
- [x] GetByScheduleWithSectionAsync() - Section 정보 포함
- [x] GetBySectionAsync()
- [x] DeleteByScheduleAsync()
- [x] RemoveUnitFromScheduleAsync()
- [x] GetNextOrderAsync(), GetTotalAllocatedHoursAsync()

**파일:** `Repositories/ScheduleUnitMapRepository.cs` (신규)  
**의존:** 작업 1-5  
**완료일:** 2025-01-23  
**메모:** 병합 지원을 위한 다양한 조회 메서드 포함  

---

## Phase 2: 배치 엔진 구현 ⏱️ 예상 4~5시간

### 작업 2-1: SchedulingEngine.cs 신규 생성 ✅
- [x] GenerateAvailableSlots() - 시간표+학사일정 기반 가용 슬롯 생성
- [x] PlaceAnchorSectionsAsync() - Step 1: 고정 일정 배치
- [x] FillRemainingSlotsAsync() - Step 2: 일반 수업 순차 배치
- [x] GenerateAlerts (UnplacedSections) - Step 3: 미배치/부족 경고
- [x] GenerateScheduleAsync() - 전체 실행 메서드
- [x] ValidateScheduleAsync() - 유효성 검사
- [x] GetStatsAsync() - 통계 조회
- [x] SchedulingResult, PlacedSectionInfo, AvailableSlot 클래스

**파일:** `Services/SchedulingEngine.cs` (신규)  
**의존:** 작업 1-4, 1-6  
**완료일:** 2025-01-23  
**메모:** Anchor-Fill-Alert 3단계 알고리즘 구현 완료  

---

### 작업 2-2: AnnualLessonPlanPage.xaml 수정 ✅
- [x] 단원 유형 선택 ComboBox 추가 (Normal/Exam/Assessment/Event)
- [x] 고정 날짜 선택 CalendarDatePicker 추가
- [x] 유형 선택 시 고정 날짜 패널 표시/숨김
- [x] 배치 결과 Alert InfoBar 추가
- [x] Undo/Redo 버튼 추가
- [x] 유형 배지 + 고정 표시 (목록 아이템)
- [x] 학급 선택 + 기간 설정 UI
- [x] 유형별 통계 표시

**파일:** `Pages/AnnualLessonPlanPage.xaml`  
**의존:** 작업 1-1  
**완료일:** 2025-01-23  
**메모:** v2 UI 확장 완료  

---

### 작업 2-3: AnnualLessonPlanPage.xaml.cs 수정 ✅
- [x] 단원 추가 시 새 필드 바인딩
- [x] OnSectionTypeChanged() - 유형 변경 핸들러
- [x] SchedulingEngine 인스턴스 생성 및 호출 준비
- [x] OnUndoClick(), OnRedoClick() - placeholder
- [x] UpdateTypeStatistics() - 유형별 통계
- [x] LoadRoomComboBox(), SetDefaultSemesterDates()

**파일:** `Pages/AnnualLessonPlanPage.xaml.cs`  
**의존:** 작업 2-1, 2-2  
**완료일:** 2025-01-23  
**메모:** v2 기능 메서드 추가 완료  

---

## Phase 3: 변동 처리 (Push/Pull + Undo) ⏱️ 예상 3.5시간

### 작업 3-1: UndoAction.cs 신규 생성 ✅
- [x] UndoAction 모델 클래스
- [x] ShiftActionData 클래스 (JSON 직렬화용)
- [x] ScheduleShiftInfo 클래스
- [x] GetData<T>(), SetData<T>() 메서드

**파일:** `Models/UndoAction.cs` (신규)  
**의존:** 없음  
**완료일:** 2025-01-23  
**메모:** UndoActionType enum, ScheduleActionData, BulkGenerateActionData 등 포함  

---

### 작업 3-2: UndoHistoryRepository.cs 신규 생성 ✅
- [x] EnsureTableExists()
- [x] CreateAsync()
- [x] GetLastUndoableActionAsync()
- [x] GetLastRedoableActionAsync()
- [x] MarkAsUndoneAsync()
- [x] ClearOldHistoryAsync() - 오래된 기록 정리

**파일:** `Repositories/UndoHistoryRepository.cs` (신규)  
**의존:** 작업 3-1  
**완료일:** 2025-01-23  
**메모:** CanUndoAsync, CanRedoAsync, ClearRedoStackAsync 포함  

---

### 작업 3-3: ScheduleShiftService.cs 신규 생성 ✅
- [x] PushSchedulesAsync() - 밀리기 (역순 처리)
- [x] PullSchedulesAsync() - 당기기
- [x] UndoLastActionAsync()
- [x] RedoLastActionAsync()
- [x] FindNextAvailableSlot() - 다음 가용 슬롯 찾기
- [x] CanUndoAsync(), CanRedoAsync()

**파일:** `Services/ScheduleShiftService.cs` (신규)  
**의존:** 작업 1-4, 3-2  
**완료일:** 2025-01-23  
**메모:** ShiftResult, UndoRedoResult 클래스 포함  

---

### 작업 3-4: Undo/Redo UI 추가 ✅
- [x] AnnualLessonPlanPage에 Undo/Redo 버튼 추가
- [x] 버튼 활성화/비활성화 상태 바인딩
- [x] Undo/Redo 실행 후 UI 새로고침
- [x] ScheduleShiftService 연동

**파일:** `Pages/AnnualLessonPlanPage.xaml`, `.xaml.cs`  
**의존:** 작업 3-3  
**완료일:** 2025-01-23  
**메모:** RefreshUndoRedoButtonsAsync 메서드 추가  

---

## Phase 4: 진도 관리 모듈 ⏱️ 예상 6.5시간

### 작업 4-1: LessonProgress.cs 신규 생성 ✅
- [x] 기본 필드 (No, CourseSectionId, Room)
- [x] 상태 필드 (IsCompleted, CompletedDate, ProgressType)
- [x] ScheduleId FK (완료된 수업 슬롯 연결)
- [x] Memo 필드
- [x] Computed Properties

**파일:** `Models/LessonProgress.cs` (신규)  
**의존:** 없음  
**완료일:** 2025-01-24  
**메모:** ProgressGap, ProgressMatrixCell 클래스 포함  

---

### 작업 4-2: LessonProgressRepository.cs 신규 생성 ✅
- [x] EnsureTableExists()
- [x] CRUD 기본 메서드
- [x] GetByCourseAsync() - 매트릭스 뷰용 전체 조회
- [x] GetByRoomAsync() - 학급별 조회
- [x] GetProgressGapAsync() - Room별 완료 수 집계
- [x] MarkAsCompletedAsync()

**파일:** `Repositories/LessonProgressRepository.cs` (신규)  
**의존:** 작업 4-1  
**완료일:** 2025-01-24  
**메모:** ProgressStats 클래스, 보강/건너뛰기/결강 처리 메서드 포함  

---

### 작업 4-3: ProgressSyncService.cs 신규 생성 ✅
- [x] AddMakeupLessonAsync() - 보강 추가
- [x] MergeSectionsAsync() - 병합 처리
- [x] SkipSectionAsync() - 건너뛰기 처리
- [x] AnalyzeProgressGapAsync() - 격차 분석
- [x] SuggestSyncActions() - 동기화 제안

**파일:** `Services/ProgressSyncService.cs` (신규)  
**의존:** 작업 4-2, 1-4, 1-6  
**완료일:** 2025-01-24  
**메모:** SyncResult, GapAnalysisResult, SyncSuggestion, ProgressMatrixData 클래스 포함  

---

### 작업 4-4: ProgressMatrixPage.xaml 신규 생성 ✅
- [x] 페이지 기본 구조
- [x] 헤더 (수업 선택 ComboBox)
- [x] 도구 모음 (보강/병합/건너뛰기/엑셀 버튼)
- [x] ScrollViewer + Grid (매트릭스)
- [x] 하단 요약 패널 (격차 정보)

**파일:** `Pages/ProgressMatrixPage.xaml` (신규)  
**의존:** 작업 4-3  
**완료일:** 2025-01-24  
**메모:** 범례 표시, 로딩 오버레이 포함  

---

### 작업 4-5: ProgressMatrixPage.xaml.cs 구현 ✅
- [x] 데이터 로드 (sections, rooms, progress)
- [x] BuildProgressMatrix() - 그리드 동적 생성
- [x] AddHeaderCell(), AddProgressCell() 헬퍼
- [x] 체크박스 이벤트 (OnProgressChecked/Unchecked)
- [x] OnAddMakeupClick() - 보강 다이얼로그
- [x] OnMergeClick() - 병합 다이얼로그
- [x] OnSkipClick() - 건너뛰기 다이얼로그
- [x] OnSuggestSyncClick() - 격차 분석 표시

**파일:** `Pages/ProgressMatrixPage.xaml.cs` (신규)  
**의존:** 작업 4-4  
**완료일:** 2025-01-24  
**메모:** 일정 동기화, 매트릭스 동적 생성, 셀 선택 기능 포함  

---

### 작업 4-6: 네비게이션 메뉴에 추가 ✅
- [x] NavigationView에 "진도 관리" 메뉴 추가
- [x] 아이콘 설정
- [x] 페이지 네비게이션 연결

**파일:** `MainWindow.xaml`, `MainWindow.xaml.cs`  
**의존:** 작업 4-5  
**완료일:** 2025-01-24  
**메모:** 수업 관리 하위 메뉴로 추가  

---

## Phase 5: 리포팅 (선택) ⏱️ 예상 2.5시간

### 작업 5-1: MiniExcel NuGet 패키지 확인 ✅
- [x] MiniExcel 패키지 이미 설치됨 (v1.42.0)
- [x] 프로젝트 빌드 확인

**파일:** `NewSchool.csproj`  
**의존:** 없음  
**완료일:** 2025-01-24  
**메모:** ClosedXML 대신 MiniExcel 사용 (기존 설치됨)  

---

### 작업 5-2: ReportExportService.cs 신규 생성 ✅
- [x] ExportYearPlanToExcelAsync() - NEIS 양식 연간계획
- [x] ExportWeeklyGuideAsync() - 주간 수업 안내문
- [x] ExportProgressMatrixToExcelAsync() - 진도 현황 리포트
- [x] ExportSchedulesToExcelAsync() - 전체 일정 내보내기
- [x] ExportGapAnalysisToExcelAsync() - 격차 분석 보고서

**파일:** `Services/ReportExportService.cs` (신규)  
**의존:** 작업 5-1  
**완료일:** 2025-01-24  
**메모:** MiniExcel 사용, ExportResult 클래스 포함  

---

### 작업 5-3: 내보내기 버튼 UI 추가 ✅
- [x] AnnualLessonPlanPage에 "엑셀 내보내기" 버튼
- [x] ProgressMatrixPage에 "엑셀 내보내기" 버튼
- [x] FileSavePicker 연동
- [x] 내보내기 완료 알림

**파일:** `Pages/AnnualLessonPlanPage.xaml`, `Pages/AnnualLessonPlanPage.xaml.cs`, `Pages/ProgressMatrixPage.xaml.cs`  
**의존:** 작업 5-2  
**완료일:** 2025-01-24  
**메모:** OnExportExcelClick, OnExportClick 메서드 구현  

---

## Phase 6: 정리 작업 ⏱️ 예상 1.5시간

### 작업 6-1: 사용하지 않는 코드 제거 ✅
- [x] CurriculumUnit.cs - 이미 없음 (이전에 삭제됨)
- [x] CurriculumUnitRepository.cs - 이미 없음 (이전에 삭제됨)
- [x] WeeklyUnitPlan.cs - CourseSection 기반으로 수정
- [x] WeeklyUnitPlanRepository.cs - SectionNo 기반으로 수정
- [x] UnitPlanGenerator.cs - 이미 없음 (이전에 삭제됨)
- [x] 참조하는 곳 확인 및 정리

**파일:** `Models/WeeklyUnitPlan.cs`, `Repositories/WeeklyUnitPlanRepository.cs`  
**의존:** 모든 작업 완료 후  
**완료일:** 2025-01-24  
**메모:** CurriculumUnit 대신 CourseSection 기반으로 마이그레이션  

---

### 작업 6-2: AnnualLessonPlanViewModel.cs 정리 ✅
- [x] CurriculumUnit 참조 → CourseSection으로 변경 (이미 CourseSection 기반)
- [x] Units 컬렉션 타입 변경 (이미 CourseSection 기반)
- [x] UnitPlans 관련 코드 제거/수정 (WeeklyUnitPlan 수정됨)
- [x] SchedulingEngine 연동 정리 (Page에서 직접 호출)
- [x] 불필요한 메서드 제거 (주석 정리)

**파일:** `ViewModels/AnnualLessonPlanViewModel.cs`  
**의존:** 작업 6-1  
**완료일:** 2025-01-24  
**메모:** ViewModel은 이미 CourseSection 기반으로 작동 중  

---

## 📝 작업 일지

### 2025-01-23
- 프로젝트 분석 완료
- 개발 계획서 v2 작성
- 작업 체크리스트 생성
- Phase 1~3 완료 (13/24 작업)

### 2025-01-24
- Phase 4 완료: 진도 관리 모듈 (LessonProgress, ProgressMatrixPage)
- Phase 5 완료: 리포팅 (ReportExportService, MiniExcel 엑셀 내보내기)
- Phase 6 완료: 정리 작업 (WeeklyUnitPlan CourseSection 기반 마이그레이션)
- 프로젝트 전체 완료 (24/24 작업) 🎉

---

## 🔗 관련 문서

- [개발 계획서](./DEVELOPMENT_PLAN.md)
- [원본 계획서](/mnt/user-data/uploads/수업_계획_시스템_개발_계획서__최종_.docx)
