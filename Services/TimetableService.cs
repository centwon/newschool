using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NewSchool.Models;
using NewSchool.Repositories;
using NewSchool.ViewModels;

namespace NewSchool.Services
{
    /// <summary>
    /// 시간표 Service
    /// 교사별/학급별 시간표 조회 및 ViewModel 변환
    /// ⭐ Lesson 테이블로 통합
    /// </summary>
    public class TimetableService : IDisposable
    {
        private readonly string _dbPath;
        private bool _disposed;

        public TimetableService(string dbPath)
        {
            _dbPath = dbPath;
        }

        #region 교사 시간표

        /// <summary>
        /// 교사별 시간표 조회 (Course + Lesson)
        /// </summary>
        public async Task<TimetableViewModel> GetTeacherTimetableAsync(
            string teacherId, int year, int semester)
        {
            var viewModel = new TimetableViewModel
            {
                Year = year,
                Semester = semester
            };

            try
            {
                // 빈 시간표 초기화 (5일 x 7교시)
                viewModel.InitializeEmptyTimetable();

                // 1. Course 데이터 조회
                using var courseRepo = new CourseRepository(_dbPath);
                var courses = await courseRepo.GetByTeacherAsync(teacherId, year, semester);

                if (courses.Count == 0)
                {
                    Debug.WriteLine($"[TimetableService] 교사 {teacherId}의 수업이 없습니다.");
                    return viewModel;
                }

                // 2. Teacher 이름 조회
                using var teacherRepo = new TeacherRepository(_dbPath);
                var teacher = await teacherRepo.GetByTeacherIdAsync(teacherId);
                string teacherName = teacher?.Name ?? "Unknown";

                viewModel.Title = $"{teacherName} 교사 시간표 ({year}학년도 {semester}학기)";

                // 3. 각 Course의 Lesson(정기수업) 조회 및 배치
                using var lessonRepo = new LessonRepository(_dbPath);

                foreach (var course in courses)
                {
                    // 해당 Course의 정기 수업 조회
                    var lessons = await lessonRepo.GetByCourseAsync(course.No);
                    var recurringLessons = lessons.Where(l => l.IsRecurring).ToList();

                    foreach (var lesson in recurringLessons)
                    {
                        // 유효성 검사
                        if (lesson.DayOfWeek < 1 || lesson.DayOfWeek > 5 ||
                            lesson.Period < 1 || lesson.Period > 7)
                        {
                            Debug.WriteLine($"[TimetableService] 잘못된 시간표: CourseNo={course.No}, Day={lesson.DayOfWeek}, Period={lesson.Period}");
                            continue;
                        }

                        // 시간표 아이템 가져오기
                        var item = viewModel.GetItem(lesson.DayOfWeek, lesson.Period);
                        if (item != null)
                        {
                            item.LessonNo = lesson.No;
                            item.CourseNo = course.No;
                            item.SubjectName = course.Subject;
                            item.Room = lesson.Room;
                            item.IsEmpty = false;
                        }
                    }
                }

                return viewModel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] 교사 시간표 조회 실패: {ex.Message}");
                return viewModel;
            }
        }

        #endregion

        #region 학급 시간표

        /// <summary>
        /// 학급별 시간표 조회 (ClassTimetable 사용)
        /// </summary>
        public async Task<TimetableViewModel> GetClassTimetableAsync(
            string schoolCode, int year, int semester, int grade, int classNo)
        {
            var viewModel = new TimetableViewModel
            {
                Year = year,
                Semester = semester,
                Title = $"{grade}학년 {classNo}반 시간표 ({year}학년도 {semester}학기)"
            };

            try
            {
                // 빈 시간표 초기화 (5일 x 7교시)
                viewModel.InitializeEmptyTimetable();

                // ClassTimetable 직접 조회
                using var timetableRepo = new ClassTimetableRepository(_dbPath);
                var timetables = await timetableRepo.GetByClassAsync(
                    schoolCode, year, semester, grade, classNo);

                if (timetables.Count == 0)
                {
                    Debug.WriteLine($"[TimetableService] {grade}학년 {classNo}반의 시간표가 없습니다.");
                    return viewModel;
                }

                // ClassTimetable → TimetableItem 변환
                foreach (var timetable in timetables)
                {
                    // 유효성 검사
                    if (timetable.DayOfWeek < 1 || timetable.DayOfWeek > 5 ||
                        timetable.Period < 1 || timetable.Period > 7)
                    {
                        Debug.WriteLine($"[TimetableService] 잘못된 시간표: Grade={grade}, Class={classNo}, Day={timetable.DayOfWeek}, Period={timetable.Period}");
                        continue;
                    }

                    // 시간표 아이템 가져오기
                    var item = viewModel.GetItem(timetable.DayOfWeek, timetable.Period);
                    if (item != null)
                    {
                        item.SubjectName = timetable.SubjectName;
                        item.TeacherName = timetable.TeacherName;
                        item.IsEmpty = false;
                    }
                }

                return viewModel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] 학급 시간표 조회 실패: {ex.Message}");
                return viewModel;
            }
        }

        #endregion

        #region Course 관리

        /// <summary>
        /// Course 생성 (시간표 배치 포함 - Lesson 사용)
        /// </summary>
        public async Task<int> CreateCourseWithScheduleAsync(
            Course course, List<Lesson> lessons)
        {
            try
            {
                // 1. Course 생성
                using var courseRepo = new CourseRepository(_dbPath);
                int courseNo = await courseRepo.CreateAsync(course);

                if (courseNo <= 0)
                {
                    Debug.WriteLine($"[TimetableService] Course 생성 실패");
                    return -1;
                }

                // 2. Lesson 생성
                if (lessons != null && lessons.Count > 0)
                {
                    using var lessonRepo = new LessonRepository(_dbPath);
                    
                    foreach (var lesson in lessons)
                    {
                        lesson.Course = courseNo;
                        lesson.Teacher = course.TeacherID;
                        lesson.Year = course.Year;
                        lesson.Semester = course.Semester;
                        lesson.Grade = course.Grade;
                        lesson.IsRecurring = true;
                        await lessonRepo.CreateAsync(lesson);
                    }

                    Debug.WriteLine($"[TimetableService] Lesson {lessons.Count}개 생성 완료");
                }

                return courseNo;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] Course+Lesson 생성 실패: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Course 삭제 (Lesson도 함께 삭제)
        /// </summary>
        public async Task<bool> DeleteCourseAsync(int courseNo)
        {
            try
            {
                // 1. 관련 Lesson 삭제
                using var lessonRepo = new LessonRepository(_dbPath);
                await lessonRepo.DeleteByCourseAsync(courseNo);

                // 2. Course 삭제
                using var courseRepo = new CourseRepository(_dbPath);
                bool success = await courseRepo.DeleteAsync(courseNo);

                if (success)
                {
                    Debug.WriteLine($"[TimetableService] Course 삭제 완료: No={courseNo}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] Course 삭제 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Course 시간표 배치 업데이트 (Lesson 사용)
        /// </summary>
        public async Task<bool> UpdateCourseScheduleAsync(
            int courseNo, List<Lesson> lessons)
        {
            try
            {
                using var lessonRepo = new LessonRepository(_dbPath);

                // 1. 기존 정기 수업 삭제
                await lessonRepo.DeleteByCourseAsync(courseNo);

                // 2. 새 정기 수업 생성
                if (lessons != null && lessons.Count > 0)
                {
                    int count = 0;
                    foreach (var lesson in lessons)
                    {
                        lesson.Course = courseNo;
                        lesson.IsRecurring = true;
                        await lessonRepo.CreateAsync(lesson);
                        count++;
                    }

                    Debug.WriteLine($"[TimetableService] Lesson 업데이트 완료: {count}개");
                    return count > 0;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] Lesson 업데이트 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region ClassTimetable 관리

        /// <summary>
        /// 학급 시간표 일괄 등록
        /// </summary>
        public async Task<int> CreateClassTimetableAsync(
            string schoolCode, int year, int semester, int grade, int classNo,
            List<ClassTimetable> timetables)
        {
            try
            {
                using var timetableRepo = new ClassTimetableRepository(_dbPath);

                // 1. 기존 시간표 삭제
                await timetableRepo.DeleteByClassAsync(schoolCode, year, semester, grade, classNo);

                // 2. 새 시간표 생성
                if (timetables != null && timetables.Count > 0)
                {
                    // 학급 정보 채우기
                    foreach (var timetable in timetables)
                    {
                        timetable.SchoolCode = schoolCode;
                        timetable.Year = year;
                        timetable.Semester = semester;
                        timetable.Grade = grade;
                        timetable.Class = classNo;
                    }

                    int count = await timetableRepo.CreateBatchAsync(timetables);
                    Debug.WriteLine($"[TimetableService] ClassTimetable {count}개 생성 완료");
                    return count;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] ClassTimetable 생성 실패: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 학급 시간표 삭제
        /// </summary>
        public async Task<int> DeleteClassTimetableAsync(
            string schoolCode, int year, int semester, int grade, int classNo)
        {
            try
            {
                using var timetableRepo = new ClassTimetableRepository(_dbPath);
                int count = await timetableRepo.DeleteByClassAsync(
                    schoolCode, year, semester, grade, classNo);

                Debug.WriteLine($"[TimetableService] ClassTimetable 삭제 완료: {count}개");
                return count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TimetableService] ClassTimetable 삭제 실패: {ex.Message}");
                return -1;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        #endregion
    }
}
