# Changelog

## v1.1.0 (2026-07-01)

v1.0.4 이후 누적 대형 릴리스. **핵심은 에디터 엔진 전면 교체** — 웹 기반 Jodit(WebView2 + ~900KB JS)를
네이티브 Win2D 리치에디터(WinUIRichEditor)로 바꾸고 WebView2 런타임 의존을 완전히 제거했다.
함께 WinAppSDK 1.8→2.2 업그레이드, Post 콘텐츠 `.flow`(BLOB) 저장 전환도 포함(상세는 아래 v1.0.6·v1.0.5).
이번 릴리스에서 게시 파이프라인을 Native AOT 로 전환했다.

### 졸업 처리 제거 + 사소한 결함 4건 정리 (2026-07-15)
- **졸업 처리(GraduateAsync) 제거**: 조회가 전부 학년도 기준이라 별도 졸업 마감이 불필요 —
  UI 미노출 상태에서 초등(1~6학년) 진급을 졸업으로 오처리하던 버그의 온상이어서 삭제.
  진급(`PromoteStudentsAsync`)은 다년 이력 연속성(같은 StudentID 유지)의 핵심이라 유지하되
  최고 학년(maxGrade)은 대상에서 제외하도록 수정 — 진급 UI(반/번호 재배정 미리보기)는 추후 과제
- 댓글 수정 시 Post 캐시도 무효화 — 목록 댓글 수가 최대 5분 구버전으로 남던 문제
- 글 저장/삭제 시 카테고리·주제 목록 캐시(30분) 무효화 — 새 주제가 필터에 늦게 나타나던 문제
- OK 전용 대화상자에서 ESC 로 닫으면 None 대신 OK 반환(버튼 구성별 안전한 기본값으로 통일)
- 사진 캐시 LRU 를 접근 순번 기반으로 교체 — Dictionary 순서 의존으로 사실상 무작위 제거되던 문제
- 테스트 236건: 졸업 테스트 5건 → 진급 회귀 테스트 3건(최고 학년 스킵·StudentID 연속성·휴학 제외)

### 전수 조사 버그 수정 5건 (2026-07-15)
- **백그라운드 작업 오류 알림 미표시**: UnobservedTaskException 핸들러가 UI 스레드가 아닌 곳에서
  ContentDialog 를 직접 생성해 COMException 으로 조용히 실패하던 문제 — DispatcherQueue 로
  UI 스레드에 넘겨 알림이 실제로 표시되도록 수정
- **학적 조회 학기 필터 무시**: `GetEnrollmentsAsync` 의 semester 인자가 리포지토리에 전달되지
  않던 문제 수정 + 학급 명부(`GetClassRosterAsync`)는 1·2학기 학적이 모두 있어도 학생당
  최신 학기 행 하나만 반환(좌석 인쇄·학생카드·통합 내보내기 등 중복 방지). 회귀 테스트 2건 추가(총 238)
- **1인석 자리 이력 고정**: 좌석 저장 Round 를 짝 이력(SeatHistory)만으로 계산해 1인석(Jjak<2)
  학급은 위치 이력이 첫 저장에서 멈추던 문제 — 두 이력 테이블 MAX 로 수정("지난 자리 배제" 정상화)
- **학생 사진 교체 후 옛 사진 표시**: 같은 경로 덮어쓰기 시 이미지 캐시가 무효화되지 않던 문제 —
  저장/삭제 시 해당 경로 캐시 제거 + 확장자가 바뀌면 이전 확장자 고아 파일도 정리
- **주기 동기화 중지 경합**: 동기화 진행 중 StopPeriodicSync() 호출 시 루프가 null 이 된 필드를
  읽어 NRE 가 백그라운드로 새던 문제 — 타이머·토큰을 지역 변수로 캡처

### 시작 게이트 오탐 수정 — 일시 오류를 DB 손상으로 판정하던 문제 (2026-07-15)
- 시작 시 무결성 점검(DbIntegrity)이 **모든 예외를 손상으로 간주**해, 백신·클라우드 동기화의
  파일 점유, 읽기 전용 속성, 다른 인스턴스 잠금 같은 일시 오류에도 "데이터베이스 손상 감지"
  경고를 띄우고 시작을 막던 문제 수정 — 이제 SQLite 손상 코드(CORRUPT·NOTADB)와
  quick_check 비정상 결과만 손상으로 판정, 일시 오류는 로그만 남기고 통과(다음 실행 재점검)
- 점검용 연결 풀링 해제 — 점검 후 풀에 남은 파일 핸들이 백업/복원의 파일 교체를 방해하지 않도록
- 회귀 테스트 2건 추가(독점 잠금·읽기 전용 파일은 손상 아님) — 총 236건

### 메모리 점유 최적화 (2026-07-14)
- **GC 메모리 절약 모드**(`System.GC.ConserveMemory=7`) 적용 — 힙을 적극 압축·OS 반납해
  상주 메모리 축소. 이벤트 구동형 앱이라 GC 압력이 낮아 체감 성능 영향 없음
- 게시판 캐시 상한 50MB→15MB — 캐시 5분 만료 특성상 히트율 차이 없이 최악 점유만 축소

### 연간 수업 계획 버그 수정 — CSV 왕복·Undo 화면 갱신 (2026-07-12)
- **CSV 가져오기 파서 재작성(RFC 4180)**: 내보내기는 따옴표 이스케이프("")와 줄바꿈 포함
  필드를 만들지만 가져오기는 단순 줄 분리라, 여러 줄 메모·따옴표가 있는 단원을
  내보냈다 다시 가져오면 데이터가 깨지던 문제 수정 — 이제 왕복이 정확히 일치
- **실행 취소/다시 실행 후 배치 목록 미갱신 수정**: Undo/Redo 성공 시 버튼 상태만 갱신되고
  화면의 배치 목록은 이전 상태로 남던 문제 — 목록도 함께 재로드
- 시수 셀 탭 핸들러의 죽은 매개변수(루프 변수를 잘못 캡처한 컬럼 인덱스) 제거

### 백업 v2 (ZIP) + 좌석 이력 중복 방지 + 리스트 스크롤 유지 (2026-07-12)
- **백업이 ZIP 단일 파일로**: 각 DB를 `VACUUM INTO`(원자적 스냅샷, WAL 포함, 빈 공간 제거)로
  뜬 뒤 하나의 ZIP 으로 압축 — 폴더 복사 대비 용량 대폭 감소, 실패 시 체크포인트+복사 폴백.
  복원은 ZIP 선택(신규)·폴더 백업의 .db 선택(구버전)·단일 .db(하위호환) 모두 지원,
  보관 개수 정리도 ZIP·폴더 통합 최신순. 스냅샷 동작 회귀 테스트 추가(228→229)
- **좌석 이력 UNIQUE 인덱스**: SeatHistory(짝)·SeatPosHistory(자리)에 라운드 단위 UNIQUE
  인덱스 추가 — 기존 중복 행은 초기화 시 자동 정리, 저장은 INSERT OR IGNORE 로 안전화
- **리스트 소량 변경 시 스크롤 유지**: OptimizedObservableCollection 이 8건 이하 추가/제거에는
  Reset 대신 개별 알림 사용 — 전체 재가상화·스크롤 초기화 회피

### Google 동기화 실패 알림 + 자동 재시도 (2026-07-12)
- 백그라운드(시작 시·주기적) 동기화 실패가 디버그 로그에만 남아 사용자가 알 수 없던 문제 —
  메인 창 하단 우측에 **경고 InfoBar**(첫 오류 + 건수)를 띄우고 **'다시 시도'** 버튼 제공
  (재시도 성공 시 성공 표시 후 3초 뒤 자동 닫힘). 오프라인·미인증 등 사전 체크 탈락은
  주기마다 반복 알림이 되지 않도록 표시하지 않음
- **실패분 자동 재시도**: 동기화에 오류가 있으면 마지막 동기화 시각을 전진시키지 않도록 변경 —
  기존에는 Push 실패한 수정분이 다음 주기 비교 대상에서 빠져 재수정 전까지 유실됐음.
  이제 다음 주기(또는 재시도 버튼)에서 자동으로 다시 올라감

### 요일별 교시 수 설정화 (2026-07-12)
- 현재 교시 판정에 하드코딩돼 있던 "월·수 6교시, 그 외 7교시"를 **설정 → 교시 시간 설정 →
  요일별 교시 수**(월~금, 1~12)로 이동 — 다른 시정을 쓰는 학교에서도 홈 화면 현재 교시
  배지·시간표 행 강조가 정확해짐. 기본값은 기존 동작과 동일(월 6·화 7·수 6·목 7·금 7)
- `PeriodCounts` 타입 신설("6,7,6,7,7" 직렬화, 형식·범위 오류 시 기본값 폴백),
  `GetPeriodAt`은 순수 함수 유지. 4교시 이하 시정에서 점심을 종료 시각에 넣지 않도록
  하교 시각 계산도 일반화. 테스트 17개 추가(211→228)

### 전수 코드 검사 — 데이터 안전성·동기화 버그 수정 (2026-07-12)
- **school.db 외래키 활성화**: 연결 PRAGMA에 `foreign_keys=ON` 누락으로 스키마의
  `ON DELETE CASCADE/SET NULL`이 전부 무시되던 문제 수정(Board·Scheduler는 이미 켜져 있었음) —
  수업/동아리 하드 삭제 시 수강신청·시간표 고아 행이 조용히 쌓이던 원인. DB 초기화 시
  기존 고아 행 일괄 정리(CASCADE 삭제·SET NULL 보정·누락 School 스텁 생성) 단계 추가
- **WAL 백업 데이터 누락 수정**: 백업이 `.db` 파일만 복사해 `-wal`에만 있는 최근 커밋이
  빠질 수 있던 문제 — 복사 전 `wal_checkpoint(TRUNCATE)` 수행. 복원 시에는 연결 풀 정리 +
  잔여 `-wal`/`-shm` 삭제 후 덮어써 복원본 오염 방지 (전체 백업·school.db 백업 공통)
- **학사일정 Google 동기화 크래시 수정**: 같은 행사명이 비연속 날짜에 반복되면(예: 재량휴업일)
  제목 중복으로 동기화 전체가 실패하던 문제 — 재조정 자연키를 "제목+시작일"로 변경
- **홈 자정 롤오버**: 앱을 켜둔 채 날짜가 바뀌면 날짜 헤더·시간표·급식이 어제 것으로 남던 문제 —
  1분 타이머에서 날짜 변경 감지 시 전체 재로드
- **자잘한 수정**: Windows 시작 시 자동 실행 토글이 설정 DB에 저장되지 않던 것, 급식 메뉴명
  띄어쓰기 잘림("친환경 쌀밥"→"친환경"), NEIS API 키가 디버그 로그에 노출되던 것(마스킹)
- **성능**: NEIS 바이트 카운트를 문자별 정규식→범위 비교로 교체(실시간 글자수 카운트),
  날짜 파싱 성공 시마다 찍히던 디버그 로그 제거(대량 로드 병목), 로그 파일 정리를 플러시마다→시간당 1회로,
  `Log.Debug`가 파일 로그에도 기록되도록(레벨 Debug 설정 시 릴리스 진단 가능)

### 홈 현재 교시 행 강조 (2026-07-12)
- 오늘 시간표(**내 수업 / 우리 반**)에서 지금 진행 중인 교시 행에 **은은한 틴트 배경 + '지금' 배지** 표시.
  상단 현재 교시 배지와 같은 1분 주기 타이머로 갱신되며, 쉬는시간·점심·방과후 등 수업 외 시간에는
  아무 행도 강조되지 않음
- 비강조 행은 오버레이가 접혀 레이아웃 변화 없음. 교시 판정은 기존 순수 함수 `Functions.GetPeriodNow`(Index) 재사용

### 오늘 페이지 재설계 + 대시보드 카드 테마 통일 (2026-07-06)
- **대시보드 카드 공용 스타일**(`App.xaml`: `DashboardCard`/`DashboardCardHeader`/`Icon`/`Title`) 신설.
  시간표·학사일정·급식·메모·할일 카드에 흩어져 있던 하드코딩 색(`CornflowerBlue` 테두리, `Lavender`
  헤더, 이모지 제목)을 테마 리소스(`CardStrokeColorDefaultSolidBrush` 등) + 강조색 `FontIcon` + 통일 라운드로
  일괄 교체 → **라이트/다크 자동 대응**, 4벌 중복 마크업을 단일 소스로
- **오늘 페이지**(Avalonia SaemDesk 설계 이식)
  - 상단 **날짜 헤더** 추가: 오늘 날짜 · 요일(강조) · 오늘 학사일정(있을 때만) · **현재 교시 배지**(1분 주기 갱신)
  - 시간표를 정적 주간 그리드 → **내 수업 / 우리 반** 오늘 슬롯 표시로 변경(담임이 아니면 '우리 반' 열 접힘)
  - 교시 배지는 인포 블루 칩, 카드 베이스는 중립 그레이 유지
- 학사일정·할일 항목 글자 12→13px, 할일 카테고리 배지 9→11px

### 창 항상-위에(TopMost) 실동작 + 다이얼로그 z-order + 에디터 API 정리 (2026-07-06)
- **TopMost 실제 구현**: 설정 토글이 저장만 하고 적용되지 않던 걸 `OverlappedPresenter.IsAlwaysOnTop`으로
  실동작화(`MainWindow.SetAlwaysOnTop`), 시작 시 저장값 적용 + 토글 즉시 반영. 기본값 `true`→`false`(옵트인)
- **다이얼로그 숨김 방지**: 메인 창이 항상-위에일 때 별도 `Window` 다이얼로그가 뒤로 숨던 문제 수정 —
  MemoEditDialog·RichTextEditorWin·ClassDiaryListWin·StudentLogDialog·StudentSpecBatchDialog 표시 시
  같은 topmost 레벨로 올림(`ContentDialog`는 인-윈도우라 영향 없음)
- **에디터 API 대응**: WinUIRichEditor 에서 `EditorMode` 프리셋이 제거되어(→ `IsReadOnly` + 툴바) 어댑터가
  빌드되지 않던 문제 수정 — `ApplyMode`를 `IsReadOnly` 매핑으로 전환

### 학생·수업·설정 메뉴 버그·정합성 수정 + 아카이브 메모 확인 표시 (2026-07-05)
- **학생 메뉴 6개 페이지**
  - 학생 관리: 인라인 편집한 이름/비고가 저장되지 않던 문제 수정(이름은 정본 `Student` 경유로 갱신해
    denormalized `Enrollment.Name`까지 동기화), 저장 대상을 `IsModified||IsSelected`로 확장
  - 누가 기록: 특정 학기 선택 시 `semester` 인자 누락으로 학기 필터가 무시되던 버그 수정
  - 학생 정보: 페이지 이탈 시 자동저장을 `await`해 Dispose 경합 방지, 죽은 코드·미사용 서비스·루프 내 N+1 정리
  - 학생부 기록: `Unloaded` 이중 등록 정리 / 학생정보 출력: 사용자정의·비고 컬럼명 중복 예외 방지
  - 학생 추가: **성별 열 추가**(수동입력 콤보 + 엑셀 파싱 + 저장/목록/내보내기) — 전원 "남" 고정 제거,
    중복검사 DB오류 시 중복 간주로 안전화, 엑셀 읽기 비동기화
- **수업 메뉴**: 수업 시간표 `Insert` 인자 뒤바뀜 크래시 수정, MessageBox 제목/메시지 뒤바뀜 19곳 정정
  (수업·동아리 관리·학급 시간표), 학급 시간표 편집 취소 시 뜨던 "저장됨" 오탐 제거, 누가 기록의
  `ConfirmLeave` 반환값 무시(저장 실패 시 전환 강행) 수정
- **설정 메뉴**: 학사일정 인라인 편집(`IsModified`) 항목을 체크 없이도 저장, 학년도 종료일 `2/28`
  하드코딩 → 2월 말일 계산(윤년 2/29 포함)
- **아카이브**: 게시글 보기/아카이브에서 메모의 확인(완료) 상태 표시 + 상세 페이지 컴팩트화

### 달력 UI 정리 + 학사일정 전용 Google 캘린더 + 아젠다 목록 재설계 (2026-07-03)
- **달력 색상 정리**
  - 날짜 숫자와 학사일정(`TbDateName`) 텍스트 색 규칙을 분리 — 날짜는 공휴일(최우선)/일요일/토요일/평일,
    학사일정 텍스트는 공휴일/휴업일만 반영(요일 무관)
  - 일정 항목 배경을 하드코딩 구글 블루에서 캘린더별 색상 점(dot) + 투명 배경으로 교체 — 이벤트 자체
    색상이 없으면 소속 캘린더 색으로 폴백(`KEvent.CalendarColor`)
  - 오늘 날짜 원형 배지(`TodayCircle`) 제거 — 셀 전체 테두리(`TodayHighlight`)만으로 충분해 중복 정리
  - 날짜 숫자·학사일정 텍스트를 `VerticalAlignment="Bottom"`으로 통일해 폰트 크기가 달라도 아랫단 정렬
  - 요일 헤더 굵게, 요일/날짜/년월 선택(`MonthPicker.DisplayFontSize`) 폰트 크기 통일
- **폰트 설정 정리**: 설정창 라벨과 실제 대상이 어긋나 있던 걸 명확히 함 —
  `EventFontSize`="학사일정 폰트"(TbDateName), `TaskFontSize`="할 일 폰트"(DayCell 이벤트+할일 목록 둘 다),
  `DateFontSize`="요일/날짜 폰트". 이벤트/할일 목록 폰트는 `ItemsRepeater` 컨테이너 재활용 타이밍에 기대는
  code-behind(`ElementPrepared`) 대신, `EventColorToBrush(DisplayColor)`와 동일하게 검증된 x:Bind 정적
  메서드 패턴으로 통일해 신뢰성 확보
- **학사일정 → Google 전용 캘린더**: 학사일정을 기존 "수업" 캘린더(수업/학급/업무와 공유)가 아닌
  **학교별 전용 Google 캘린더**에 동기화하도록 재설계
  - `KCalendarList.SchoolCode` 추가 — 학교가 바뀌면 새 캘린더를 생성해 예전 학교 학사일정과 분리
  - `GoogleSyncService.UploadSchoolSchedulesAsync`를 단순 업로드에서 **재조정(reconcile) 동기화**로
    전면 재작성 — 신규/날짜변경/삭제를 모두 비교 처리, `school.db` 수정 후 재실행하면 구글도 맞춰짐
  - 로컬 KEvent(`ItemType="schoolschedule"`)에 `GoogleId`를 저장해두는 근본 수정으로, 향후 어떤 경로로
    Pull이 일어나도 중복 생성되지 않도록 함(기존엔 Push 후 로컬에 흔적을 안 남겨 Pull 시 매번 중복 생성됐음)
  - 빈 학사일정 목록이 들어오면(NEIS 조회 실패 등) 기존 동기화분을 삭제로 오인하지 않도록 안전 가드 추가
- **아젠다 목록(KAgendaControl) 재설계**: 날짜별 그룹 헤더 방식 → 단일 평탄 목록으로 변경
  - 각 행 `[분류 배지][날짜][시간][제목][완료/진행]` — 유형 아이콘(○/●/▶) 대신 캘린더 분류 배지로 교체
  - `AgendaHeader`/`AgendaTemplateSelector`/`TypeIcon`/`AccentColor` 등 더는 안 쓰는 코드 정리
  - 캘린더 조회 실패(레거시 `CalendarId=0` 등) 시 배지가 빈 이름으로 숨겨지던 버그 수정("기타"로 폴백)

### 학년도/학급/과목 공용 필터 컨트롤 + 스케줄러 심층 리뷰 + 메모창 리사이즈 (2026-07-03)
- **공용 필터 컨트롤 도입** — `SchoolFilterPicker`를 `YearSemesterPicker`/`ClassPicker`/`CoursePicker` 3개로 분리해
  9개 페이지(학생관리·학생부·학급일지·학급시간표관리·수업관리·학생정보·학생기록·학생정보출력·통합내보내기)에서 재사용.
  과목·강의실 단위로 교과세특을 조회/작성하는 `CourseSpecPage` 신설
  - CoursePicker가 Native AOT에서 과목명을 못 띄우던 리플렉션 바인딩(`DisplayMemberPath`) → `x:Bind` ItemTemplate 전환
  - YearSemesterPicker+ClassPicker 조합 페이지에서 이중 초기화 로드되던 문제 → `ClassPicker.AutoLoad` 옵션 추가
  - 수강생 조회 학생별 순차 쿼리(N+1) → `EnrollmentRepository.GetCurrentByStudentIdsAsync` 배치 조회로 교체
  - `StudentSpecBox` 저장 실패 시에도 학생이 전환되던 데이터 유실 위험 → `ConfirmLeaveAsync()` 반환값 확인하도록 수정
  - `CourseSpecPage` 일괄 저장 미트랜잭션 처리 → `StudentSpecialService.SaveManyAsync` 트랜잭션 도입
- **스케줄러 심층 리뷰 수정**
  - `KtaskList` 참조로 신규 설치 시 스케줄러 DB 초기화가 항상 실패하던 문제
  - Google Calendar 증분 동기화(`syncToken`)가 `orderBy`와 함께 전송되어 두 번째 동기화부터 영구 실패하던 문제(400 응답 시 전체 동기화로 폴백하는 방어도 추가)
  - DB 초기화/복원 시 풀링된 연결이 파일을 잠가 실패하던 문제 → `SqliteConnection.ClearAllPools()` 추가
  - Google 이벤트 색상 팔레트가 실제 Google Calendar 값과 어긋나 있던 문제
  - 할 일(task)의 `End`가 `Start`보다 앞서 저장되던 문제
  - 학사일정 Google 업로드 재실행 시 중복 등록되던 문제 (기존 이벤트 조회 후 스킵)
  - 최초 동기화(`GoogleLastSyncTime` 미설정) 시 로컬 수정분 Push가 통째로 스킵되던 문제
  - 달력 로드 시 이벤트를 두 번 조회하던 중복 쿼리 제거, 인위적 `Task.Delay` 누적 제거(달력 초기 로드·다이얼로그 오픈)
  - 캘린더 삭제 시 소속 이벤트가 고아로 남던 문제 → 트랜잭션으로 함께 삭제
  - **반복 할 일 시리즈 삭제** — `KEvent.SeriesId` 신설, 반복 생성된 항목을 "이 항목만 삭제 / 이후 반복 항목 모두 삭제"로 선택 삭제 가능
- **메모 편집 창을 `ContentDialog`에서 `Window`로 전환** — 사용자가 자유롭게 드래그로 크기 조절 가능(기존 `RichTextEditorWin` 패턴 재사용)
- **WinUIRichEditor 캐럿 버그 수정**(별도 프로젝트 참조) — 빈 문서에서 캐럿이 에디터 세로 중앙에 뜨던 문제(Win2D 캔버스의
  `Stretch`+고정 `Height` 조합이 원인) → 캔버스를 좌상단(`Top`/`Left`)에 고정, 상단에 좌측 들여쓰기와 동일한 여백 추가

### Native AOT 게시 전환 + 미사용 자산 제외 (2026-07-01)
- **R2R+SingleFile → Native AOT 게시** — `PublishAot` 실활성화(기존엔 SingleFile/R2R 와 상호배타라 무시되던 설정).
  게시본 **~165MB → 46.6MB**(네이티브 exe 32.3MB + 네이티브 dll 소수), 파일 335 → 10개
- **미사용 전이 자산 게시 제외** — WinAppSDK 2.2 가 끌고 오는 WebView2·Windows AI(ML) 자산 제거(`RemoveUnusedWinAppSdkAssets` 타깃):
  managed projection(~2MB) + 네이티브 ML 런타임 `onnxruntime.dll`(20.7MB)·`DirectML.dll`(17.8MB) = **~40MB 절감**.
  Win2D(`Microsoft.Graphics.Canvas`)는 에디터가 사용하므로 유지
- **AOT 안전성 조정** — `ReportExportService` 의 엑셀 저장을 `MiniExcel.SaveAsAsync`(비동기 어댑터 `MakeGenericType`→IL3050
  동적코드 위험) → 백그라운드 동기 `SaveAs` 로 전환. MiniExcel·QuestPDF 는 `TrimmerRootAssembly` 로 보존(IL2104 억제)
- **게시 오케스트레이션 수정** — VS 프로필 게시 시 `PublishProfile` 전역속성이 ProjectReference(WinUIRichEditor)로 전파돼
  라이브러리에 ILLink 단독 실행→IL1034 실패하던 문제 해결(라이브러리 pubxml 의 앱스타일 트림/단일파일 설정 비활성화)
- 검증: 프로필 AOT 게시 성공, IL1034·IL3050·IL2104 0, exe 기동 정상

## v1.0.6 (2026-06-18 ~ 2026-06-27)

### Post 콘텐츠 저장 .flow 전환 + 검색 평문 분리 (2026-06-27)
- **에디터 내용을 WinUIRichEditor 네이티브 `.flow` 패키지(BLOB)로 저장** — 압축·무손실, 이미지는 base64 팽창 없이 원본 바이트
  - 스키마: `Post.Content` TEXT → BLOB(.flow), `PlainText TEXT` 컬럼 신설(검색·미리보기·인쇄용)
  - `Post.Content` 모델 `string` → `byte[]`, `PostRepository` BLOB I/O, 게시판 내용검색 `Content LIKE` → `PlainText LIKE`
  - 어댑터에 `GetFlowBytes()`/`LoadFlow(byte[])` 추가, 소비처(메모·게시판·자료) 로드=flow·저장=flow+평문 전환
  - `Controls/FlowText` — 평문→.flow 헬퍼(MaterialEditDialog 등 평문 입력용, 헤드리스 모델/포매터)
  - 미배포 전제로 하위호환/스니핑 없이 .flow 전용. 기존 DB 변환은 별도 콘솔 툴 `MemoFlowMigrator`(저장소 외부)로 1회 수행
  - 실측: 실 DB(이미지 포함 글) 변환 후 VACUUM 시 **3.89MB → 0.51MB(−87%)**
- **할일/일정 다이얼로그(UnifiedItemDialog) 노트** → 리치에디터 대신 일반 멀티라인 `TextBox`(KEvent.Notes 는 평문 위주)

### MemoBoard 재설계 — 최신 메모 고정 에디터 + compact 목록 (2026-06-27)
- WebView2/Jodit 우회책이던 **단일 에디터 reparent 트릭 제거** (1062→465줄)
- 인라인 = 가장 최근 활성 메모 1개 고정 편집(첨부 없음), 나머지 = compact 목록 → 클릭 시 다이얼로그 편집
- 완료 체크 = 숨김(아카이브 조회), 정식 게시물 승격은 게시판에서 별도
- `RichTextEditor.ShowToolbar` 옵션 추가: false=bare `RichEditor`(편집면만, 퀵잡용)/true=`RichEditorView`(툴바+크롬)
- 메모 에디터는 bare 컨트롤 — HTML 붙여넣기·단축키 서식 유지하되 군더더기 UI 제거

### 에디터 메모리 — 호스트 종료 시 즉시 해제 (2026-06-26)
- `RichTextEditor.Dispose()` 가 `Clear()` 로 네이티브 CanvasTextLayout 캐시 + GPU 이미지 비트맵 결정적 반환(공유 D3D 디바이스는 유지)
- 다이얼로그·페이지(MemoEditDialog·UnifiedItemDialog·Export·PostEdit/Detail) 닫힐 때 에디터 해제 연결

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
