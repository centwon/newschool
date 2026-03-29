# Changelog

## v1.0.2 (2026-03-29)

### 보안
- SQL Injection 취약 메서드 제거: `Sqlite.cs`의 `CountRecord`, `GetCondition` (미사용 레거시 코드 삭제)
- XSS 차단: JoditEditor 붙여넣기/드래그앤드롭 시 DOMPurify로 HTML sanitize 적용
  - `<script>`, `<iframe>`, `<embed>`, `onclick=` 등 위험 요소 차단
- 주민번호 DPAPI 암호화: `StudentRepository`에서 저장 시 암호화, 읽기 시 복호화
  - 기존 평문 데이터 호환 (복호화 실패 시 평문으로 간주, 재저장 시 자동 암호화)
- 로그 민감정보 제거: `StudentLogRepository`, `StudentRepository`, `TeacherRepository`
  - 파라미터 값 덤프 삭제, 학생/교사 이름·ID 로깅 제거

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
