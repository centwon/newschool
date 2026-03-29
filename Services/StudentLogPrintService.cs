using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewSchool.Models;
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
        private static readonly float FontSize = 8f;
        private static readonly float HeaderFontSize = 9f;

        private static string GetOutputDir()
        {
            var dir = Path.Combine(Settings.UserDataPath, "Prints");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>
        /// 개인 누가기록 PDF 생성
        /// </summary>
        public string GenerateStudentLogPdf(
            StudentCardViewModel studentVm,
            List<StudentLogViewModel> logs)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var year = studentVm.Enrollment?.Year ?? Settings.WorkYear.Value;
            var grade = studentVm.Enrollment?.Grade ?? 0;
            var classNo = studentVm.Enrollment?.Class ?? 0;
            var number = studentVm.Enrollment?.Number ?? 0;

            var fileName = $"누가기록_{grade}학년{classNo}반_{number}번_{studentVm.Name}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(GetOutputDir(), fileName);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.PageColor(Colors.White);

                    page.Header().Column(column =>
                    {
                        column.Item().AlignLeft().Text($"{year}학년도")
                            .FontSize(14).FontColor(Colors.Grey.Darken2);

                        column.Item().PaddingTop(8).AlignCenter().Text("누가 기록")
                            .FontSize(26).Bold().FontColor(Colors.Blue.Darken3);

                        column.Item().PaddingTop(10).AlignRight()
                            .Text($"{grade}학년 {classNo}반 {number}번  이름: {studentVm.Name}")
                            .FontSize(16).SemiBold().FontColor(Colors.Grey.Darken2);

                        column.Item().PaddingTop(10).LineHorizontal(2).LineColor(Colors.Blue.Medium);
                    });

                    page.Content().Element(content => ComposeCardContent(content, logs));

                    page.Footer().Column(column =>
                    {
                        column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        column.Item().PaddingTop(5).AlignCenter()
                            .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                            .FontSize(10).FontColor(Colors.Grey.Darken1);
                    });
                });
            }).GeneratePdf(filePath);

            return filePath;
        }

        /// <summary>
        /// 학급 전체 누가기록을 하나의 PDF로 생성 (표 형식)
        /// </summary>
        public string GenerateClassLogPdf(
            int year, int grade, int classNo,
            List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var fileName = $"누가기록_{grade}학년{classNo}반_일괄_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            var filePath = Path.Combine(GetOutputDir(), fileName);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.PageColor(Colors.White);

                    // 헤더: 제목 + 학년/반
                    page.Header().Column(column =>
                    {
                        column.Item().Row(row =>
                        {
                            row.RelativeItem().AlignLeft().Text($"{year}학년도 누가 기록")
                                .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

                            row.AutoItem().AlignRight().Text($"{grade}학년 {classNo}반")
                                .FontSize(14).SemiBold().FontColor(Colors.Grey.Darken2);
                        });
                        column.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Colors.Blue.Medium);
                        column.Item().PaddingBottom(6);
                    });

                    // 본문: 표 형식
                    page.Content().Element(content => ComposeTableContent(content, studentLogs));

                    // 푸터
                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().AlignLeft()
                            .Text($"출력일시: {DateTime.Now:yyyy년 MM월 dd일 HH:mm}")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                        row.AutoItem().AlignRight()
                            .Text(t =>
                            {
                                t.CurrentPageNumber().FontSize(8);
                                t.Span(" / ").FontSize(8);
                                t.TotalPages().FontSize(8);
                            });
                    });
                });
            }).GeneratePdf(filePath);

            return filePath;
        }

        /// <summary>표 형식 본문 (일괄 출력용)</summary>
        private void ComposeTableContent(
            IContainer container,
            List<(StudentCardViewModel Student, List<StudentLogViewModel> Logs)> studentLogs)
        {
            container.Table(table =>
            {
                // 컬럼 정의: 번호, 이름, 날짜, 카테고리, 과목, 활동명, 기록, 주제, 활동내용, 학생부초안
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(28);   // 번호
                    columns.ConstantColumn(48);   // 이름
                    columns.ConstantColumn(62);   // 날짜
                    columns.ConstantColumn(48);   // 카테고리
                    columns.ConstantColumn(48);   // 과목
                    columns.ConstantColumn(70);   // 활동명
                    columns.RelativeColumn(2);    // 기록
                    columns.ConstantColumn(60);   // 주제
                    columns.RelativeColumn(2);    // 활동내용
                    columns.RelativeColumn(2);    // 학생부초안
                });

                // 헤더 행
                table.Header(header =>
                {
                    var headerStyle = TextStyle.Default.FontSize(HeaderFontSize).Bold().FontColor(Colors.White);

                    void HeaderCell(IContainer c, string text) =>
                        c.Background(Colors.Blue.Darken2)
                         .BorderBottom(1).BorderColor(Colors.White)
                         .Padding(4)
                         .AlignCenter().AlignMiddle()
                         .Text(text).Style(headerStyle);

                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "번호");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "이름");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "날짜");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "영역");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "과목");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "활동명");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "기록");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "주제");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "활동내용");
                    HeaderCell(header.Cell().RowSpan(1).ColumnSpan(1), "학생부초안");
                });

                // 데이터 행
                uint currentRow = 1; // 1-based (header is row 0)
                foreach (var (studentVm, logs) in studentLogs)
                {
                    var number = studentVm.Enrollment?.Number ?? 0;
                    var name = studentVm.Name ?? string.Empty;
                    uint logCount = (uint)logs.Count;

                    if (logCount == 0) continue;

                    bool isEven = currentRow % 2 == 0;
                    var bgColor = isEven ? Colors.Grey.Lighten5 : Colors.White;

                    // 번호/이름: 행 병합
                    table.Cell().RowSpan(logCount)
                        .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bgColor)
                        .Padding(3).AlignCenter().AlignMiddle()
                        .Text(number.ToString()).FontSize(FontSize).SemiBold();

                    table.Cell().RowSpan(logCount)
                        .Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Background(bgColor)
                        .Padding(3).AlignCenter().AlignMiddle()
                        .Text(name).FontSize(FontSize).SemiBold();

                    // 각 로그 행
                    foreach (var logVm in logs)
                    {
                        var model = logVm.StudentLog;

                        void DataCell(IContainer c, string text) =>
                            c.Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                             .Background(bgColor)
                             .Padding(3)
                             .Text(text ?? string.Empty).FontSize(FontSize);

                        DataCell(table.Cell(), logVm.Date.ToString("yyyy-MM-dd"));
                        DataCell(table.Cell(), logVm.Category.ToString());
                        DataCell(table.Cell(), logVm.SubjectName ?? string.Empty);
                        DataCell(table.Cell(), logVm.ActivityName ?? string.Empty);
                        DataCell(table.Cell(), logVm.Log ?? string.Empty);
                        DataCell(table.Cell(), logVm.Topic ?? string.Empty);
                        DataCell(table.Cell(), logVm.Description ?? string.Empty);
                        DataCell(table.Cell(),
                            model.HasStructuredData() ? model.DraftSummary : string.Empty);
                    }

                    currentRow++;
                }
            });
        }

        #region 개인 PDF용 카드 형식 (기존)

        private void ComposeCardContent(IContainer container, List<StudentLogViewModel> logs)
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
                    column.Item().Row(row =>
                    {
                        row.AutoItem()
                            .Background(Colors.Blue.Medium)
                            .Padding(6)
                            .Text($"{index}")
                            .FontSize(12).Bold().FontColor(Colors.White);

                        row.AutoItem()
                            .PaddingLeft(8)
                            .Background(GetCategoryColor(logVm.Category))
                            .Padding(6)
                            .Text($"[{logVm.Category}]")
                            .FontSize(11).SemiBold().FontColor(Colors.White);

                        if (!string.IsNullOrWhiteSpace(logVm.SubjectName))
                        {
                            row.AutoItem()
                                .PaddingLeft(4)
                                .Background(Colors.Grey.Medium)
                                .Padding(6)
                                .Text(logVm.SubjectName)
                                .FontSize(11).FontColor(Colors.White);
                        }

                        row.RelativeItem()
                            .AlignRight().PaddingRight(4)
                            .Text(logVm.Date.ToString("yyyy. M. d."))
                            .FontSize(11).FontColor(Colors.Grey.Darken1);
                    });

                    if (log.HasStructuredData())
                    {
                        column.Item().PaddingTop(8)
                            .Element(c => ComposeStructuredLog(c, log));
                    }
                    else if (!string.IsNullOrWhiteSpace(log.Log))
                    {
                        column.Item().PaddingTop(8)
                            .Background(Colors.White)
                            .Border(1).BorderColor(Colors.Grey.Lighten1)
                            .Padding(10)
                            .Text(log.Log)
                            .FontSize(11).LineHeight(1.5f);
                    }
                });
        }

        private void ComposeStructuredLog(IContainer container, StudentLog log)
        {
            container.Background(Colors.White)
                .Border(1).BorderColor(Colors.Grey.Lighten1)
                .Padding(10)
                .Column(column =>
                {
                    if (!string.IsNullOrWhiteSpace(log.ActivityName))
                    {
                        column.Item().Row(row =>
                        {
                            row.AutoItem().Text("활동명: ").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            row.AutoItem().Text(log.ActivityName).FontSize(11);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.Topic))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("주제: ").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            row.AutoItem().Text(log.Topic).FontSize(11);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.Description))
                    {
                        column.Item().PaddingTop(6).Column(col =>
                        {
                            col.Item().Text("활동 내용").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                            col.Item().PaddingTop(2).Text(log.Description).FontSize(11).LineHeight(1.5f);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.Role))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("역할: ").FontSize(11).SemiBold().FontColor(Colors.Green.Darken2);
                            row.AutoItem().Text(log.Role).FontSize(11);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.SkillDeveloped))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("기른 능력: ").FontSize(11).SemiBold().FontColor(Colors.Orange.Darken2);
                            row.AutoItem().Text(log.SkillDeveloped).FontSize(11);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.StrengthShown))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("장점: ").FontSize(11).SemiBold().FontColor(Colors.Purple.Darken2);
                            row.AutoItem().Text(log.StrengthShown).FontSize(11);
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(log.ResultOrOutcome))
                    {
                        column.Item().PaddingTop(4).Row(row =>
                        {
                            row.AutoItem().Text("성취: ").FontSize(11).SemiBold().FontColor(Colors.Red.Darken2);
                            row.AutoItem().Text(log.ResultOrOutcome).FontSize(11);
                        });
                    }

                    if (log.HasStructuredData())
                    {
                        column.Item().PaddingTop(8)
                            .Background(Colors.Grey.Lighten4)
                            .Border(1).BorderColor(Colors.Grey.Lighten2)
                            .Padding(8)
                            .Column(col =>
                            {
                                col.Item().Text("학생부 기록 초안").FontSize(10).Italic().FontColor(Colors.Grey.Darken1);
                                col.Item().PaddingTop(4).Text(log.DraftSummary).FontSize(10).LineHeight(1.5f);
                            });
                    }
                });
        }

        #endregion

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
