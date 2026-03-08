# 수업홈 대시보드형 (안 A) 구현 계획

## 변경 대상 파일
- `Pages/LessonHomePage.xaml` — 레이아웃 전면 재설계
- `Pages/LessonHomePage.xaml.cs` — 오늘의 수업 로딩 로직 추가, 전체 흐름 재구성

## 새 레이아웃

```
┌──────────────────────────────────────────────────────────────────┐
│  [과목 ▼]  [학급 ▼]  (32명)                          ← 필터바  │
├─────────────────────────────────┬────────────────────────────────┤
│  좌측 (메인 영역, *)            │  우측 (사이드, 280px)            │
│                                 │                                │
│  ┌─ 오늘의 수업 ─────────────┐  │  ┌─ 내 시간표 ──────────────┐ │
│  │ 1교시  국어  2-3    ✅완료 │  │  │  TimetableControl       │ │
│  │ 2교시  국어  2-1    기록▸  │  │  │  (LoadMyScheduleAsync)  │ │
│  │ 4교시  국어  2-5    ○예정  │  │  └──────────────────────────┘ │
│  │ 6교시  국어  2-2    ○예정  │  │                                │
│  └────────────────────────────┘  │  ┌─ 할 일 ──────────────────┐ │
│                                 │  │  KAgendaControl           │ │
│  ┌─ 최근 수업 기록 ─── [+][↻]┐  │  │  (LoadPendingAndFuture)  │ │
│  │ 3/8  2교시 2-3  주제...   │  │  │                          │ │
│  │ 3/7  5교시 2-1  주제...   │  │  └──────────────────────────┘ │
│  │ ...  (LessonLogList)      │  │                                │
│  └────────────────────────────┘  │                                │
└─────────────────────────────────┴────────────────────────────────┘
```

## 구현 상세

### 1단계: XAML 레이아웃 재설계 (LessonHomePage.xaml)

**전체 구조:**
- Row 0: 필터바 (기존 유지)
- Row 1: 2-column Grid
  - Column 0 (*): 좌측 — 오늘의 수업 + 최근 수업 기록
  - Column 1 (280px): 우측 — 시간표 + 할일

**오늘의 수업 카드 (새로 생성):**
- Border 카드 (CardBackgroundFillColor, CornerRadius=8)
- 헤더: "오늘의 수업" + 날짜 표시 (예: "3월 8일 토요일")
- ItemsRepeater로 교시별 수업 표시
- 각 아이템: [교시] [과목] [학급] [상태 버튼]
- 수업 없는 날: "오늘은 수업이 없습니다" 메시지

**우측 변경:**
- 시간표와 할일 배치는 기존과 동일 (좌→우로 이동만)

**수업 자료 탭:**
- Pivot 제거, PostListPage Frame 제거
- 수업 자료는 별도 메뉴에서 접근 (이미 게시판으로 존재)

### 2단계: 오늘의 수업 데이터 로직 (LessonHomePage.xaml.cs)

**내부 클래스 TodayLessonItem:**
```csharp
private class TodayLessonItem
{
    public Lesson Lesson { get; set; }
    public string Subject { get; set; }      // Course에서 가져옴
    public int CourseNo { get; set; }
    public LessonLog? ExistingLog { get; set; } // 매칭된 기록

    // 바인딩용 속성
    public string PeriodText => $"{Lesson.Period}교시";
    public string ClassText => Lesson.ClassDisplay;
    public string StatusText => ExistingLog != null ? "완료"
        : Lesson.Period == currentPeriod ? "기록" : "예정";
    public string StatusIcon => ExistingLog != null ? "✅"
        : Lesson.Period == currentPeriod ? "▸" : "○";
}
```

**데이터 로딩 흐름:**
```
LoadTodayLessonsAsync():
  1. LessonService.GetTodayLessonsAsync() → 오늘 예정 수업 목록
  2. CourseService.GetMyCoursesAsync() → 과목 정보 (Subject 매핑)
  3. LessonLogService.GetTodayLessonsAsync() → 오늘 이미 작성된 기록
  4. 매칭: Lesson(Period, Grade, Class) ↔ LessonLog(Period, Grade, Class)
  5. TodayLessonItem 리스트 생성 → ObservableCollection에 바인딩
```

**아이템 클릭 처리:**
- 기존 기록 있음 → `LessonLogEditDialog(existingLog, courseNo)` 편집
- 기존 기록 없음 → `LessonLogEditDialog(teacherId, subject, room, grade, class, courseNo, period)` 새 기록

### 3단계: 필터 로직 정리

**필터 영향 범위:**
- 과목/학급 필터 → **최근 수업 기록(LessonLogList)에만** 영향
- 오늘의 수업 → 항상 전체 표시 (필터 무관)
- 시간표/할일 → 필터 무관

**기존 필터 로직 유지:**
- CBoxSubject_SelectionChanged → LoadLessonLogsAsync() 호출
- CBoxRoom_SelectionChanged → LoadStudentsAsync() + LoadLessonLogsAsync()
- 수업 자료 관련 코드 (InitMaterialFrame, UpdateMaterialSubjectAsync) 제거

### 4단계: 기존 코드 정리

**제거 항목:**
- Pivot 컨트롤 전체
- MaterialFrame (PostListPage 내장)
- _postListPage 필드, InitMaterialFrame(), UpdateMaterialSubjectAsync()
- MaterialCategory 상수

**유지 항목:**
- TimetableControl, KAgendaControl, LessonLogList — 모두 재사용
- LoadCoursesAsync(), LoadRooms(), LoadStudentsAsync() — 기존 필터 로직
- LessonLogList 이벤트 핸들러 (LessonSelected, AddRequested) — 기존 유지

## 구현 순서

1. LessonHomePage.xaml 레이아웃 재설계 (좌우 반전 + 오늘의 수업 카드 추가)
2. LessonHomePage.xaml.cs에 TodayLessonItem 클래스 + LoadTodayLessonsAsync() 추가
3. 오늘의 수업 클릭 핸들러 구현
4. 수업 자료 관련 코드 제거
5. 빌드 검증
