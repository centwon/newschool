using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewSchool.Models;
using NewSchool.Pages;
using NewSchool.ViewModels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace NewSchool.Services
{
    /// <summary>
    /// 누가기록 PDF 생성 서비스
    /// </summary>
    public class StudentLogPrintService
    {
        /// <summary>
        /// 누가기록 PDF 생성
        /// </summary>
        public string GenerateStudentLogPdf(
            StudentCardViewModel studentVm,
            List<StudentLogViewModel> logs)
        {
            // QuestPDF 라이선스 설정
            QuestPDF.Settings.License = LicenseType.Community;

            // 리팩토링 후: Enrollment를 통해 접근
            var year = studentVm.Enrollment?.Year ?? Settings.WorkYear.Value;
            var grade = studentVm.Enrollment?.Grade ?? 0;
            var classNo = studentVm.Enrollment?.Class ?? 0;
            var number = studentVm.Enrollment?.Number ?? 0;

            // PDF 파일 경로 생성
            var fileName = $"누가기록_{grade}학년{classNo}반_{number}번_{studentVm.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NewSchool",
                "Prints",
                fileName);

            // 디렉토리가 없으면 생성
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // PDF 생성
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);

                    // 헤더
                    page.Header().Element(ComposeHeader);

                    // 본문
                    page.Content().Element(content => ComposeContent(content, logs));

                    // 푸터
                    page.Footer().Element(ComposeFooter);
                });

                void ComposeHeader(IContainer container)
                {
                    container.Column(column =>
                    {
                        // 학년도
                        column.Item().AlignLeft().Text($"{year}학년도")
                            .FontSize(14)
                            .FontColor(Colors.Grey.Darken2);

                        // 제목
                        column.Item().PaddingTop(8).AlignCenter().Text("누가 기록")
                            .FontSize(26)
                            .Bold()
                            .FontColor(Colors.Blue.Darken3);

                        // 학생 정보
                        column.Item().PaddingTop(10).AlignRight().Text($"{grade}학년 {classNo}반 {number}번  이름: {studentVm.Name}")
                            .FontSize(16)
                            .SemiBold()
                            .FontColor(Colors.Grey.Darken2);

                        column.Item().PaddingTop(10).LineHorizontal(2).LineColor(Colors.Blue.Medium);
                    });
                }

                void ComposeFooter(IContainer container)
                {
                    container.Column(column =>
                    {
                        column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        column.Item().PaddingTop(5).AlignCenter().Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Darken1);
                    });
                }
            }).GeneratePdf(filePath);

            return filePath;
        }

        private void ComposeContent(IContainer container, List<StudentLogViewModel> logs)
        {
            container.Column(column =>
            {
                if (logs == null || logs.Count == 0)
                {
                    column.Item().PaddingTop(40).AlignCenter()
                        .Text("누가기록이 없습니다.")
                        .FontSize(14)
                        .FontColor(Colors.Grey.Medium);
                    return;
                }

                int index = 1;
                foreach (var logVm in logs)
                {
                    column.Item().PaddingTop(index == 1 ? 20 : 16)
                        .Element(c => ComposeLogItem(c, logVm, index));
                    index++;
                }
            });
        }

        private void ComposeLogItem(IContainer container, StudentLogViewModel logVm, int index)
        {
            var log = logVm.StudentLog;

            container.Border(1).BorderColor(Colors.Grey.Lighten2)
                .Background(Colors.Grey.Lighten5)
                .Padding(12)
                .Column(column =>
                {
                    // 헤더: 번호, 카테고리, 날짜
                    column.Item().Row(row =>
                    {
                        // 번호
                        row.AutoItem()
                            .Background(Colors.Blue.Medium)
                            .Padding(6)
                            .Text($"{index}")
                            .FontSize(12)
                            .Bold()
                            .FontColor(Colors.White);

                        // 카테고리
                        row.AutoItem()
                            .PaddingLeft(8)
                            .Background(GetCategoryColor(logVm.Category))
                            .Padding(6)
                            .Text($"[{logVm.Category}]")
                            .FontSize(11)
                            .SemiBold()
                            .FontColor(Colors.White);

                        // 과목 (있는 경우)
                        if (!string.IsNullOrWhiteSpace(logVm.SubjectName))
                        {
                            row.AutoItem()
                                .PaddingLeft(4)
                                .Background(Colors.Grey.Medium)
                                .Padding(6)
                                .Text(logVm.SubjectName)
                                .FontSize(11)
                                .FontColor(Colors.White);
                        }

                        // 날짜
                        row.RelativeItem()
                            .AlignRight()
                            .PaddingRight(4)
                            .Text(logVm.Date.ToString("yyyy. M. d."))
                            .FontSize(11)
                            .FontColor(Colors.Grey.Darken1);
                    });

                    // 구조화된 데이터가 있으면 상세 표시
                    if (log.HasStructuredData())
                    {
                        column.Item().PaddingTop(8)
                            .Element(c => ComposeStructuredLog(c, log));
                    }
                    else if (!string.IsNullOrWhiteSpace(log.Log))
                    {
                        // 일반 기록
                        column.Item().PaddingTop(8)
                            .Background(Colors.White)
                            .Border(1)
                            .BorderColor(Colors.Grey.Lighten1)
                            .Padding(10)
                            .Text(log.Log)
                            .FontSize(11)
                            .LineHeight(1.5f);
                    }
                });
        }

        /// <summary>구조화된 누가기록 표시</summary>
        private void ComposeStructuredLog(IContainer container, StudentLog log)
        {
            container.Background(Colors.White)
                .Border(1)
                .BorderColor(Colors.Grey.Lighten1)
                .Padding(10)
                .Column(column =>
                {
                    // 활동명
                    if (!string.IsNullOrWhiteSpace(log.ActivityName))
                    {
                        column.Item().Row(row =>
                        {
                            row.AutoItem().Text("활동명: ").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            row.AutoItem().Text(log.ActivityName).FontSize(11);
                        });
                    }

                    // 주제
                    if (!string.IsNullOrWhiteSpace(log.Topic))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("주제: ").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            row.AutoItem().Text(log.Topic).FontSize(11);
                        });
                    }

                    // 활동 내용
                    if (!string.IsNullOrWhiteSpace(log.Description))
                    {
                        column.Item().PaddingTop(6).Column(col =>
                        {
                            col.Item().Text("활동 내용").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            col.Item().PaddingTop(2).Text(log.Description).FontSize(11).LineHeight(1.5f);
                        });
                    }

                    // 역할
                    if (!string.IsNullOrWhiteSpace(log.Role))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("역할: ").FontSize(11).SemiBold().FontColor(Colors.Green.Darken2);
                            row.AutoItem().Text(log.Role).FontSize(11);
                        });
                    }

                    // 기른 능력
                    if (!string.IsNullOrWhiteSpace(log.SkillDeveloped))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("기른 능력: ").FontSize(11).SemiBold().FontColor(Colors.Orange.Darken2);
                            row.AutoItem().Text(log.SkillDeveloped).FontSize(11);
                        });
                    }

                    // 드러난 장점
                    if (!string.IsNullOrWhiteSpace(log.StrengthShown))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("장점: ").FontSize(11).SemiBold().FontColor(Colors.Purple.Darken2);
                            row.AutoItem().Text(log.StrengthShown).FontSize(11);
                        });
                    }

                    // 성취 및 결과
                    if (!string.IsNullOrWhiteSpace(log.ResultOrOutcome))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("성취: ").FontSize(11).SemiBold().FontColor(Colors.Red.Darken2);
                            row.AutoItem().Text(log.ResultOrOutcome).FontSize(11);
                        });
                    }

                    // 학생부 초안 (하단에 회색 배경으로)
                    column.Item().PaddingTop(8)
                        .Background(Colors.Grey.Lighten4)
                        .Border(1)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(8)
                        .Column(col =>
                        {
                            col.Item().Text("학생부 기록 초안").FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                            col.Item().PaddingTop(4).Text(log.DraftSummary).FontSize(10).LineHeight(1.5f);
                        });
                });
        }

        /// <summary>카테고리별 색상</summary>
        private string GetCategoryColor(LogCategory category)
        {
            return category switch
            {
                LogCategory.교과활동 => Colors.Blue.Medium,
                LogCategory.개인별세특 => Colors.Indigo.Medium,
                LogCategory.자율활동 => Colors.Green.Medium,
                LogCategory.동아리활동 => Colors.Orange.Medium,
                LogCategory.봉사활동 => Colors.Pink.Medium,
                LogCategory.진로활동 => Colors.Purple.Medium,
                LogCategory.종합의견 => Colors.Teal.Medium,
                LogCategory.상담기록 => Colors.Cyan.Medium,
                _ => Colors.Grey.Medium
            };
        }
    }
}
