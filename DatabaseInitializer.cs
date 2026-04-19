using System;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NewSchool.Models;

namespace NewSchool.Database
{
    /// <summary>
    /// 데이터베이스 초기화 클래스
    /// NEIS 표준 구조로 전면 재작성
    /// ⭐ 외래키 문제 해결: TeacherID NULL 허용 + ON DELETE SET NULL
    /// ⭐ 시간표 시스템 재설계: Course, CourseSchedule, ClassTimetable 분리
    /// </summary>
    public class DatabaseInitializer : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection? _connection;
        private bool _disposed;

        public DatabaseInitializer(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>
        /// 데이터베이스 초기화 (테이블 생성 + 인덱스)
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _connection = new SqliteConnection($"Data Source={_dbPath}");
                await _connection.OpenAsync();

                Debug.WriteLine("[DatabaseInitializer] 데이터베이스 연결 완료");

                await CreateTablesAsync();
                await CreateIndexesAsync();

                Debug.WriteLine("[DatabaseInitializer] 데이터베이스 초기화 완료");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DatabaseInitializer] 초기화 실패: {ex.Message}");
                throw;
            }
        }

        private async Task CreateTablesAsync()
        {
            if (_connection == null) return;

            using var cmd = _connection.CreateCommand();

            Debug.WriteLine("[DatabaseInitializer] 테이블 생성 시작...");

            // ==========================================
            // 1. School 테이블 (학교 정보 - NEIS 표준)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS School (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT UNIQUE NOT NULL,
                    ATPT_OFCDC_SC_CODE TEXT,
                    ATPT_OFCDC_SC_NAME TEXT,
                    SchoolName TEXT NOT NULL,
                    SchoolType TEXT,
                    FoundationDate TEXT,
                    Address TEXT,
                    Phone TEXT,
                    Fax TEXT,
                    Website TEXT,
                    PrincipalName TEXT,
                    Memo TEXT,
                    IsActive INTEGER DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    IsDeleted INTEGER DEFAULT 0
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] School 테이블 생성 완료");

            // ==========================================
            // 2. Student 테이블 (학생 기본 정보)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Student (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT UNIQUE NOT NULL,
                    Name TEXT NOT NULL,
                    Sex TEXT,
                    BirthDate TEXT,
                    ResidentNumber TEXT,
                    Photo TEXT,
                    Phone TEXT,
                    Email TEXT,
                    Address TEXT,
                    Memo TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    IsDeleted INTEGER DEFAULT 0
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Student 테이블 생성 완료");

            // ==========================================
            // 3. StudentDetail 테이블 (학생 상세 정보)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS StudentDetail (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT UNIQUE NOT NULL,
                    FatherName TEXT,
                    FatherPhone TEXT,
                    FatherJob TEXT,
                    MotherName TEXT,
                    MotherPhone TEXT,
                    MotherJob TEXT,
                    GuardianName TEXT,
                    GuardianPhone TEXT,
                    GuardianRelation TEXT,
                    FamilyInfo TEXT,
                    Friends TEXT,
                    Interests TEXT,
                    Talents TEXT,
                    CareerGoal TEXT,
                    HealthInfo TEXT,
                    Allergies TEXT,
                    SpecialNeeds TEXT,
                    Memo TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] StudentDetail 테이블 생성 완료");

            // ==========================================
            // 4. Teacher 테이블 (교사 정보)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Teacher (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    TeacherID TEXT UNIQUE NOT NULL,
                    LoginID TEXT UNIQUE NOT NULL,
                    Name TEXT NOT NULL,
                    Status TEXT DEFAULT '재직',
                    Position TEXT,
                    Subject TEXT,
                    Phone TEXT,
                    Email TEXT,
                    BirthDate TEXT,
                    HireDate TEXT,
                    Photo TEXT,
                    Memo TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    LastLoginAt TEXT,
                    IsDeleted INTEGER DEFAULT 0
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Teacher 테이블 생성 완료");

            // ==========================================
            // 5. TeacherSchoolHistory 테이블 (교사 근무 이력)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS TeacherSchoolHistory (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    TeacherID TEXT NOT NULL,
                    SchoolCode TEXT NOT NULL,
                    StartDate TEXT NOT NULL,
                    EndDate TEXT,
                    Position TEXT,
                    Role TEXT,
                    IsCurrent INTEGER DEFAULT 1,
                    Memo TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE CASCADE,
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] TeacherSchoolHistory 테이블 생성 완료");

            // ==========================================
            // 6. Enrollment 테이블 (학적 정보 - 핵심!)
            // ⭐ TeacherID NULL 허용 + ON DELETE SET NULL 추가
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Enrollment (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT NOT NULL,
                    Name TEXT NOT NULL DEFAULT '',
                    Sex TEXT DEFAULT '',
                    Photo TEXT DEFAULT '',
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    Number INTEGER NOT NULL,
                    Status TEXT DEFAULT '재학',
                    TeacherID TEXT NULL,
                    AdmissionDate TEXT,
                    GraduationDate TEXT,
                    TransferOutDate TEXT,
                    TransferOutSchool TEXT,
                    TransferInDate TEXT,
                    TransferInSchool TEXT,
                    Memo TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    IsDeleted INTEGER DEFAULT 0,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE,
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode),
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL,
                    UNIQUE(StudentID, SchoolCode, Year, Semester)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Enrollment 테이블 생성 완료");

            // ==========================================
            // 7. Subject 테이블 (교과목)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Subject (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    Curriculum TEXT,
                    Name TEXT NOT NULL,
                    Unit INTEGER,
                    Remark TEXT
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Subject 테이블 생성 완료");

            // ==========================================
            // 8. Course 테이블 (수업 개설) - ⭐ 재설계
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Course (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    TeacherID TEXT NOT NULL,
                    Grade INTEGER NOT NULL,
                    Subject TEXT NOT NULL,
                    Unit INTEGER DEFAULT 0,
                    Type TEXT DEFAULT 'Class',
                    Rooms TEXT,
                    Remark TEXT,
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode),
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE CASCADE
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Course 테이블 생성 완료");

            // ==========================================
            // 9. CourseSchedule 테이블 (교사 시간표) - ⭐ 신규
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CourseSchedule (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    CourseNo INTEGER NOT NULL,
                    DayOfWeek INTEGER NOT NULL,
                    Period INTEGER NOT NULL,
                    Room TEXT NOT NULL,
                    FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE CASCADE,
                    UNIQUE(CourseNo, DayOfWeek, Period)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] CourseSchedule 테이블 생성 완료");

            // ==========================================
            // 10. ClassTimetable 테이블 (학급 시간표) - ⭐ 신규
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ClassTimetable (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    DayOfWeek INTEGER NOT NULL,
                    Period INTEGER NOT NULL,
                    SubjectName TEXT NOT NULL,
                    TeacherName TEXT,
                    Room TEXT,
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode),
                    UNIQUE(SchoolCode, Year, Semester, Grade, Class, DayOfWeek, Period)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] ClassTimetable 테이블 생성 완료");

            // ==========================================
            // 11. CourseEnrollment 테이블 (수강 신청)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CourseEnrollment (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT NOT NULL,
                    CourseNo INTEGER NOT NULL,
                    Status TEXT DEFAULT '수강중',
                    Remark TEXT,
                    Room TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE,
                    FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE CASCADE,
                    UNIQUE(StudentID, CourseNo)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] CourseEnrollment 테이블 생성 완료");

            // ==========================================
            // 14. StudentLog 테이블 (학생 기록부) - ⭐ 확장 버전
            // Category: INTEGER (LogCategory enum 값)
            // 구조화된 활동 기록 필드 추가
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS StudentLog (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT NOT NULL,
                    TeacherID TEXT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Date TEXT NOT NULL,
                    Category INTEGER NOT NULL DEFAULT 0,
                    CourseNo INTEGER,
                    SubjectName TEXT,
                    ClubNo INTEGER,
                    ClubName TEXT,
                    Log TEXT,
                    Tag TEXT,
                    IsImportant INTEGER DEFAULT 0,
                    ActivityName TEXT,
                    Topic TEXT,
                    Description TEXT,
                    Role TEXT,
                    SkillDeveloped TEXT,
                    StrengthShown TEXT,
                    ResultOrOutcome TEXT,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE,
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL,
                    FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE SET NULL
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] StudentLog 테이블 생성 완료");

            // ==========================================
            // 15. StudentSpecial 테이블 (학생 특이사항) - ⭐ 수정
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS StudentSpecial (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Type TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    TeacherID TEXT NULL,
                    CourseNo INTEGER,
                    SubjectName TEXT,
                    IsActive INTEGER DEFAULT 1,
                    Tag TEXT,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE,
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL,
                    FOREIGN KEY (CourseNo) REFERENCES Course(No) ON DELETE SET NULL
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] StudentSpecial 테이블 생성 완료");

            // ==========================================
            // 기존 테이블들 (유지)
            // ==========================================

            // Lesson 테이블
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Lesson (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    Course INTEGER NOT NULL,
                    Teacher TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Date TEXT,
                    DayOfWeek INTEGER NOT NULL,
                    Period INTEGER NOT NULL,
                    Grade INTEGER,
                    Class INTEGER DEFAULT 0,
                    Room TEXT,
                    Topic TEXT,
                    IsRecurring INTEGER DEFAULT 1,
                    IsCompleted INTEGER DEFAULT 0,
                    IsCancelled INTEGER DEFAULT 0,
                    FOREIGN KEY (Course) REFERENCES Course(No) ON DELETE CASCADE
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Lesson 테이블 생성 완료");

            // ==========================================
            // 16. Club 테이블 (동아리 정보)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Club (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    TeacherID TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    ClubName TEXT NOT NULL,
                    ActivityRoom TEXT,
                    Remark TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    IsDeleted INTEGER DEFAULT 0,
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode),
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE CASCADE
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] Club 테이블 생성 완료");

            // ==========================================
            // 17. ClubEnrollment 테이블 (동아리 배정)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ClubEnrollment (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    StudentID TEXT NOT NULL,
                    ClubNo INTEGER NOT NULL,
                    Status TEXT DEFAULT '활동중',
                    Remark TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (StudentID) REFERENCES Student(StudentID) ON DELETE CASCADE,
                    FOREIGN KEY (ClubNo) REFERENCES Club(No) ON DELETE CASCADE,
                    UNIQUE(StudentID, ClubNo)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] ClubEnrollment 테이블 생성 완료");

            // LessonLog 테이블
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS LessonLog (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    Lesson INTEGER,
                    TeacherID TEXT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Date TEXT NOT NULL,
                    Period INTEGER,
                    Subject TEXT,
                    Room TEXT,
                    Topic TEXT,
                    Content TEXT,
                    FOREIGN KEY (Lesson) REFERENCES Lesson(No) ON DELETE SET NULL,
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] LessonLog 테이블 생성 완료");

            // ==========================================
            // 18. ClassDiary 테이블 (학급 일지) - ⭐ 개선 버전
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ClassDiary (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    TeacherID TEXT NULL,
                    Year INTEGER NOT NULL,
                    Semester INTEGER NOT NULL,
                    Date TEXT NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    Absent TEXT DEFAULT '',
                    Late TEXT DEFAULT '',
                    LeaveEarly TEXT DEFAULT '',
                    Memo TEXT DEFAULT '',
                    Notice TEXT DEFAULT '',
                    Life TEXT DEFAULT '',
                    FOREIGN KEY (SchoolCode) REFERENCES School(SchoolCode),
                    FOREIGN KEY (TeacherID) REFERENCES Teacher(TeacherID) ON DELETE SET NULL,
                    UNIQUE(SchoolCode, Year, Semester, Grade, Class, Date)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("[DatabaseInitializer] ClassDiary 테이블 생성 완료");

            // SchoolSchedule 테이블 (학사일정 - NEIS API + 수동 입력)
            cmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS SchoolSchedule (
        No INTEGER PRIMARY KEY AUTOINCREMENT,
        SCHUL_NM TEXT DEFAULT '',
        ATPT_OFCDC_SC_CODE TEXT DEFAULT '',
        ATPT_OFCDC_SC_NM TEXT DEFAULT '',
        SD_SCHUL_CODE TEXT DEFAULT '',
        AY INTEGER NOT NULL,
        AA_YMD TEXT NOT NULL,
        EVENT_NM TEXT NOT NULL,
        EVENT_CNTNT TEXT DEFAULT '',
        SBTR_DD_SC_NM TEXT DEFAULT '해당없음',
        ONE_GRADE_EVENT_YN INTEGER DEFAULT 0,
        TW_GRADE_EVENT_YN INTEGER DEFAULT 0,
        THREE_GRADE_EVENT_YN INTEGER DEFAULT 0,
        FR_GRADE_EVENT_YN INTEGER DEFAULT 0,
        FIV_GRADE_EVENT_YN INTEGER DEFAULT 0,
        SIX_GRADE_EVENT_YN INTEGER DEFAULT 0,
        IsManual INTEGER DEFAULT 0,
        CreatedAt TEXT NOT NULL,
        UpdatedAt TEXT NOT NULL,
        IsDeleted INTEGER DEFAULT 0
    )";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SchoolSchedule 테이블 생성");

            // ==========================================
            // SeatArrangement 테이블 (학급별 좌석 배치 메타)
            // 저장/복원/출력을 위한 레이아웃 정보
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SeatArrangement (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    Jul INTEGER NOT NULL,
                    Jjak INTEGER NOT NULL,
                    Rows INTEGER NOT NULL,
                    ShowPhoto INTEGER DEFAULT 0,
                    Message TEXT DEFAULT '',
                    IsLocked INTEGER DEFAULT 0,
                    OptionsJson TEXT DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    UNIQUE(SchoolCode, Year, Grade, Class)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SeatArrangement 테이블 생성");

            // ==========================================
            // SeatAssignment 테이블 (좌석별 학생 배정)
            // Row/Col 좌표마다 한 학생 또는 미사용 플래그
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SeatAssignment (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    ArrangementNo INTEGER NOT NULL,
                    Row INTEGER NOT NULL,
                    Col INTEGER NOT NULL,
                    StudentID TEXT NULL,
                    IsUnUsed INTEGER DEFAULT 0,
                    IsHidden INTEGER DEFAULT 0,
                    IsFixed INTEGER DEFAULT 0,
                    FOREIGN KEY (ArrangementNo) REFERENCES SeatArrangement(No) ON DELETE CASCADE,
                    UNIQUE(ArrangementNo, Row, Col)
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SeatAssignment 테이블 생성");

            // ==========================================
            // SeatHistory 테이블 (짝 이력 — 지난 짝 배제용)
            // 저장 시점마다 짝(인접 좌석) 관계를 기록
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SeatHistory (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    StudentID_A TEXT NOT NULL,
                    StudentID_B TEXT NOT NULL,
                    Round INTEGER NOT NULL,
                    Kind TEXT NOT NULL DEFAULT 'Pair',
                    SavedAt TEXT NOT NULL
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SeatHistory 테이블 생성");

            // ==========================================
            // SeatPosHistory 테이블 (좌석 위치 이력 — 지난 자리 배제용)
            // ==========================================
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SeatPosHistory (
                    No INTEGER PRIMARY KEY AUTOINCREMENT,
                    SchoolCode TEXT NOT NULL,
                    Year INTEGER NOT NULL,
                    Grade INTEGER NOT NULL,
                    Class INTEGER NOT NULL,
                    StudentID TEXT NOT NULL,
                    Row INTEGER NOT NULL,
                    Col INTEGER NOT NULL,
                    Round INTEGER NOT NULL,
                    SavedAt TEXT NOT NULL
                );";
            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SeatPosHistory 테이블 생성");

            Debug.WriteLine("[DatabaseInitializer] 모든 테이블 생성 완료");
        }

        private async Task CreateIndexesAsync()
        {
            if (_connection == null) return;

            using var cmd = _connection.CreateCommand();

            Debug.WriteLine("[DatabaseInitializer] 인덱스 생성 시작...");

            cmd.CommandText = @"
                -- School 인덱스
                CREATE INDEX IF NOT EXISTS idx_school_code ON School(SchoolCode);
                CREATE INDEX IF NOT EXISTS idx_school_active ON School(IsActive, IsDeleted);

                -- Student 인덱스
                CREATE INDEX IF NOT EXISTS idx_student_id ON Student(StudentID);
                CREATE INDEX IF NOT EXISTS idx_student_name ON Student(Name);
                CREATE INDEX IF NOT EXISTS idx_student_deleted ON Student(IsDeleted);

                -- StudentDetail 인덱스
                CREATE INDEX IF NOT EXISTS idx_studentdetail_studentid ON StudentDetail(StudentID);

                --SchoolSchedule 인덱스
                CREATE INDEX IF NOT EXISTS idx_schedule_year ON SchoolSchedule(AY);
                CREATE INDEX IF NOT EXISTS idx_schedule_date ON SchoolSchedule(AA_YMD);
                CREATE INDEX IF NOT EXISTS idx_schedule_school_year ON SchoolSchedule(SD_SCHUL_CODE, AY);
                CREATE INDEX IF NOT EXISTS idx_schedule_deleted ON SchoolSchedule(IsDeleted);
                -- Teacher 인덱스
                CREATE INDEX IF NOT EXISTS idx_teacher_id ON Teacher(TeacherID);
                CREATE INDEX IF NOT EXISTS idx_teacher_loginid ON Teacher(LoginID);
                CREATE INDEX IF NOT EXISTS idx_teacher_status ON Teacher(Status, IsDeleted);

                -- TeacherSchoolHistory 인덱스
                CREATE INDEX IF NOT EXISTS idx_teacherhistory_teacher ON TeacherSchoolHistory(TeacherID);
                CREATE INDEX IF NOT EXISTS idx_teacherhistory_school ON TeacherSchoolHistory(SchoolCode);
                CREATE INDEX IF NOT EXISTS idx_teacherhistory_current ON TeacherSchoolHistory(IsCurrent);

                -- Enrollment 인덱스 (핵심!)
                CREATE INDEX IF NOT EXISTS idx_enrollment_student ON Enrollment(StudentID);
                CREATE INDEX IF NOT EXISTS idx_enrollment_school ON Enrollment(SchoolCode);
                CREATE INDEX IF NOT EXISTS idx_enrollment_year ON Enrollment(Year, Semester);
                CREATE INDEX IF NOT EXISTS idx_enrollment_class ON Enrollment(SchoolCode, Year, Semester, Grade, Class);
                CREATE INDEX IF NOT EXISTS idx_enrollment_teacher ON Enrollment(TeacherID);

                -- Course 인덱스 (재설계)
                CREATE INDEX IF NOT EXISTS idx_course_teacher ON Course(TeacherID, Year, Semester);
                CREATE INDEX IF NOT EXISTS idx_course_grade ON Course(Year, Semester, Grade);

                -- CourseSchedule 인덱스 (신규)
                CREATE INDEX IF NOT EXISTS idx_courseschedule_course ON CourseSchedule(CourseNo);
                CREATE INDEX IF NOT EXISTS idx_courseschedule_time ON CourseSchedule(DayOfWeek, Period);

                -- ClassTimetable 인덱스 (신규)
                CREATE INDEX IF NOT EXISTS idx_classtimetable_class ON ClassTimetable(Year, Semester, Grade, Class);
                CREATE INDEX IF NOT EXISTS idx_classtimetable_time ON ClassTimetable(DayOfWeek, Period);

                -- CourseEnrollment 인덱스
                CREATE INDEX IF NOT EXISTS idx_courseenrollment_student ON CourseEnrollment(StudentID);
                CREATE INDEX IF NOT EXISTS idx_courseenrollment_course ON CourseEnrollment(CourseNo);
                CREATE INDEX IF NOT EXISTS idx_courseenrollment_status ON CourseEnrollment(Status);

                -- StudentLog 인덱스 (성능 최적화)
                CREATE INDEX IF NOT EXISTS idx_studentlog_student ON StudentLog(StudentID, Year, Semester);
                CREATE INDEX IF NOT EXISTS idx_studentlog_teacher ON StudentLog(TeacherID, Year, Semester);
                CREATE INDEX IF NOT EXISTS idx_studentlog_course ON StudentLog(CourseNo);
                CREATE INDEX IF NOT EXISTS idx_studentlog_category ON StudentLog(Category);
                CREATE INDEX IF NOT EXISTS idx_studentlog_important ON StudentLog(IsImportant);
                CREATE INDEX IF NOT EXISTS idx_studentlog_date ON StudentLog(Date);
                -- 복합 인덱스 (자주 함께 조회되는 경우)
                CREATE INDEX IF NOT EXISTS idx_studentlog_composite ON StudentLog(StudentID, Year, Semester, Date DESC);
                CREATE INDEX IF NOT EXISTS idx_studentlog_search ON StudentLog(StudentID, Category, Date DESC);

                -- StudentSpecial 인덱스 (수정)
                CREATE INDEX IF NOT EXISTS idx_studentspecial_student ON StudentSpecial(StudentID, Year);
                CREATE INDEX IF NOT EXISTS idx_studentspecial_course ON StudentSpecial(CourseNo);
                CREATE INDEX IF NOT EXISTS idx_studentspecial_type ON StudentSpecial(Type);
                CREATE INDEX IF NOT EXISTS idx_studentspecial_active ON StudentSpecial(IsActive);
                CREATE INDEX IF NOT EXISTS idx_studentspecial_teacher ON StudentSpecial(TeacherID);
                CREATE INDEX IF NOT EXISTS idx_studentspecial_date ON StudentSpecial(Date);

                -- 기존 테이블 인덱스
                CREATE INDEX IF NOT EXISTS idx_lesson_teacher ON Lesson(Teacher, Year, Semester);
                CREATE INDEX IF NOT EXISTS idx_lesson_date ON Lesson(Date, Period);
                CREATE INDEX IF NOT EXISTS idx_lessonlog_teacher ON LessonLog(TeacherID, Date);

                -- Club 인덱스
                CREATE INDEX IF NOT EXISTS idx_club_school ON Club(SchoolCode, Year);
                CREATE INDEX IF NOT EXISTS idx_club_teacher ON Club(TeacherID, Year);
                CREATE INDEX IF NOT EXISTS idx_club_deleted ON Club(IsDeleted);

                -- ClubEnrollment 인덱스
                CREATE INDEX IF NOT EXISTS idx_clubenrollment_student ON ClubEnrollment(StudentID);
                CREATE INDEX IF NOT EXISTS idx_clubenrollment_club ON ClubEnrollment(ClubNo);
                CREATE INDEX IF NOT EXISTS idx_clubenrollment_status ON ClubEnrollment(Status);

                -- ClassDiary 인덱스
                CREATE INDEX IF NOT EXISTS idx_classdiary_class ON ClassDiary(SchoolCode, Year, Semester, Grade, Class);
                CREATE INDEX IF NOT EXISTS idx_classdiary_date ON ClassDiary(Date);
                CREATE INDEX IF NOT EXISTS idx_classdiary_teacher ON ClassDiary(TeacherID);

                -- SeatArrangement / SeatAssignment 인덱스
                CREATE INDEX IF NOT EXISTS idx_seatarr_class ON SeatArrangement(SchoolCode, Year, Grade, Class);
                CREATE INDEX IF NOT EXISTS idx_seatassign_arr ON SeatAssignment(ArrangementNo);
                CREATE INDEX IF NOT EXISTS idx_seatassign_student ON SeatAssignment(StudentID);

                -- SeatHistory 인덱스 (짝 이력)
                CREATE INDEX IF NOT EXISTS idx_seathistory_class ON SeatHistory(SchoolCode, Year, Grade, Class, Round DESC);
                CREATE INDEX IF NOT EXISTS idx_seathistory_students ON SeatHistory(StudentID_A, StudentID_B);

                -- SeatPosHistory 인덱스 (자리 이력)
                CREATE INDEX IF NOT EXISTS idx_seatposhistory_class ON SeatPosHistory(SchoolCode, Year, Grade, Class, Round DESC);
                CREATE INDEX IF NOT EXISTS idx_seatposhistory_student ON SeatPosHistory(StudentID);
            ";

            await cmd.ExecuteNonQueryAsync();
            Debug.WriteLine("  ✓ SchoolSchedule 인덱스 생성");

            Debug.WriteLine("[DatabaseInitializer] 인덱스 생성 완료");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}
