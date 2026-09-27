using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NewSchool.Models;
using NewSchool.Services;

namespace NewSchool.Controls;

/// <summary>칸 메뉴를 띄운 화면이 해 줄 일 — 메뉴는 저장만 하고, 다시 그리기·알리기는 화면 몫이다.</summary>
public interface ILessonSlotMenuHost
{
    /// <summary>대화상자(대강 입력)를 띄울 자리</summary>
    XamlRoot? XamlRoot { get; }

    /// <summary>그 칸의 변경을 저장했다(장부는 이미 새 내용이다) — 화면을 다시 그린다.</summary>
    Task OnSlotChangedAsync(DateTime date, int period);

    /// <summary>수업 일지를 쓰거나 진도를 표시했다 — 공책 표시 등을 다시 읽는다.</summary>
    Task OnRecordChangedAsync();

    void ShowInfo(string message);
    void ShowWarning(string message);
}

/// <summary>
/// 시간표 칸 메뉴 — 내 수업 칸이면 <b>수업 일지 쓰기 · 진도 완료 표시 ▸ · 수업 변경 ▸</b>,
/// 빈 칸·휴강·대강이면 수업 변경 항목만 바로 늘어놓는다.
///
/// <para>수업 홈·주별 시간표(<see cref="WeeklyTimetableView"/>)와 오늘 화면의 내 수업 목록이 같은 메뉴를 쓴다.
/// 화면마다 따로 만들면 곧 항목이 어긋난다.</para>
/// </summary>
public sealed class LessonSlotMenu(LessonSlotBook book, ILessonSlotMenuHost host)
{
    /// <summary>메뉴는 열 때 만든다 — 수업 일지·진도 항목이 그 순간의 기록을 봐야 한다.</summary>
    public async Task<MenuFlyout> BuildAsync(DateTime date, int period)
    {
        var slot = book.Resolve(date, period);
        var menu = new MenuFlyout();
        var course = book.FindCourse(slot.CourseNo);

        bool mine = course != null && !slot.IsBlank
                    && slot.Kind is not (LessonChangeKind.Cancelled or LessonChangeKind.Substitute);
        if (!mine)
        {
            AddChangeItems(menu.Items, date, period, slot);
            return menu;
        }

        var room = string.IsNullOrWhiteSpace(slot.Room) ? WeeklyHoursCalculator.UnassignedRoom : slot.Room;
        var plan = await book.GetPlanAsync(course!);
        var current = plan?.CurrentSection(room);

        // ① 수업 일지 쓰기 — 써 둔 일지가 있으면 그 글을 연다
        bool written = book.Journals.ContainsKey((date.Date, period));
        var write = new MenuFlyoutItem
        {
            Text = written ? "수업 일지 보기" : "수업 일지 쓰기",
            Icon = new FontIcon { Glyph = "" }
        };
        write.Click += async (_, _) => await RunAsync(async () =>
        {
            var seed = new Dialogs.LessonSlotSeed(date, period, course!.No, course.Subject, slot.Room);
            if (await Dialogs.LessonJournalComposer.OpenOrComposeAsync(seed))
                await AfterRecordChangedAsync();
        }, "수업 일지");
        menu.Items.Add(write);

        // ② 진도 완료 표시 ▸ — 할 차례인 단원부터. 앞날의 수업은 아직 표시할 수 없다.
        var progress = new MenuFlyoutSubItem { Text = "진도 완료 표시", Icon = new FontIcon { Glyph = "" } };
        if (plan == null || plan.Sections.Count == 0)
        {
            progress.Items.Add(new MenuFlyoutItem { Text = "단원이 없습니다 — 수업 관리의 [단원 관리] 에서 넣습니다", IsEnabled = false });
        }
        else if (current == null)
        {
            progress.Items.Add(new MenuFlyoutItem { Text = "이 학급은 모든 단원을 마쳤습니다", IsEnabled = false });
        }
        else
        {
            bool past = date.Date <= DateTime.Today;
            int start = plan.Sections.ToList().FindIndex(s => s.No == current.No);

            foreach (var section in plan.Sections.Skip(start).Take(6))
            {
                var item = new MenuFlyoutItem
                {
                    Text = section.No == current.No
                        ? $"{section.FullPath} {section.SectionName} · 할 차례"
                        : $"{section.FullPath} {section.SectionName}",
                    IsEnabled = past
                };
                item.Click += async (_, _) => await RunAsync(async () =>
                {
                    if (await CourseProgressPlan.MarkCompletedAsync(section.No, room, date, period))
                    {
                        host.ShowInfo($"{room} · {section.SectionName} 을(를) {date:M/d} {period}교시에 완료로 표시했습니다.");
                        await AfterRecordChangedAsync();
                    }
                    else
                    {
                        host.ShowWarning("진도를 표시하지 못했습니다.");
                    }
                }, "진도 표시");
                progress.Items.Add(item);
            }

            if (!past)
                progress.Items.Add(new MenuFlyoutItem { Text = "앞으로의 수업은 그 날이 지나야 표시할 수 있습니다", IsEnabled = false });
        }
        menu.Items.Add(progress);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ③ 수업 변경 ▸
        var change = new MenuFlyoutSubItem { Text = "수업 변경", Icon = new FontIcon { Glyph = "" } };
        AddChangeItems(change.Items, date, period, slot);
        menu.Items.Add(change);

        return menu;
    }

    /// <summary>수업 변경 항목들(휴강·내 수업 넣기·대강·강의실·되돌리기)을 <paramref name="items"/> 에 붙인다.</summary>
    private void AddChangeItems(IList<MenuFlyoutItemBase> items, DateTime date, int period, DaySlot slot)
    {
        if (slot.Kind != LessonChangeKind.Cancelled && !slot.IsBlank)
        {
            var cancel = new MenuFlyoutItem { Text = "이 날 휴강", Icon = new FontIcon { Glyph = "" } };
            cancel.Click += async (_, _) => await SetAsync(date, period, null, string.Empty, string.Empty, null, "휴강 처리");
            items.Add(cancel);
        }

        // 내 수업 넣기 — 수업과 강의실을 함께 고른다.
        // 강의실을 자동으로 첫 번째로 골라 주면, 학급이 여럿인 수업에서 엉뚱한 반이 들어간다.
        if (book.Courses.Count > 0)
        {
            var sub = new MenuFlyoutSubItem { Text = "내 수업 넣기" };

            foreach (var course in book.Courses)
            {
                var rooms = course.RoomList;

                if (rooms.Count == 0)
                {
                    // 강의실이 등록되지 않은 수업은 과목만 넣는다
                    var plain = new MenuFlyoutItem { Text = course.DisplayName };
                    plain.Click += async (_, _) => await SetAsync(date, period, course, string.Empty, string.Empty, null, "수업 넣기");
                    sub.Items.Add(plain);
                    continue;
                }

                var byCourse = new MenuFlyoutSubItem { Text = course.DisplayName };
                foreach (var room in rooms)
                {
                    var item = new MenuFlyoutItem { Text = room };
                    item.Click += async (_, _) => await SetAsync(date, period, course, string.Empty, room, null, "수업 넣기");
                    byCourse.Items.Add(item);
                }

                sub.Items.Add(byCourse);
            }

            items.Add(sub);
        }

        var substitute = new MenuFlyoutItem { Text = "대강 입력…", Icon = new FontIcon { Glyph = "" } };
        substitute.Click += async (_, _) => await RunAsync(() => SubstituteAsync(date, period), "대강 입력");
        items.Add(substitute);

        // 강의실 바꾸기 — 내 수업일 때만 후보를 낼 수 있다
        var mine = book.FindCourse(slot.CourseNo);
        if (mine != null && slot.Kind != LessonChangeKind.Cancelled)
        {
            var rooms = mine.RoomList;
            if (rooms.Count > 0)
            {
                var sub = new MenuFlyoutSubItem { Text = "강의실" };
                foreach (var room in rooms)
                {
                    var item = new MenuFlyoutItem { Text = room };
                    item.Click += async (_, _) => await SetAsync(date, period, mine, slot.Subject, room, null, "강의실 변경");
                    sub.Items.Add(item);
                }
                items.Add(sub);
            }
        }

        if (book.Changes.ContainsKey((date.Date, period)))
        {
            items.Add(new MenuFlyoutSeparator());

            var revert = new MenuFlyoutItem { Text = "평소대로 되돌리기", Icon = new FontIcon { Glyph = "" } };
            revert.Click += async (_, _) => await RevertAsync(date, period);
            items.Add(revert);
        }
    }

    /// <summary>그 칸을 휴강으로 — 메뉴 밖(Delete 키 등)에서도 같은 길로 저장한다.</summary>
    public Task CancelAsync(DateTime date, int period)
        => SetAsync(date, period, null, string.Empty, string.Empty, null, "휴강 처리");

    /// <summary>그 칸을 평소대로 되돌린다 — 메뉴 밖(Delete 키 등)에서도 같은 길로 저장한다.</summary>
    public Task RevertAsync(DateTime date, int period) => RunAsync(async () =>
    {
        if (!await book.RevertSlotAsync(date, period))
        {
            host.ShowWarning("변경을 되돌리지 못했습니다.");
            return;
        }
        await host.OnSlotChangedAsync(date, period);
    }, "되돌리기");

    private Task SetAsync(
        DateTime date, int period, Course? course, string subjectText, string room, string? memo, string context)
        => RunAsync(async () =>
        {
            if (!await book.SetSlotAsync(date, period, course, subjectText, room, memo))
            {
                host.ShowWarning("변경을 저장하지 못했습니다.");
                return;
            }
            await host.OnSlotChangedAsync(date, period);
        }, context);

    private async Task SubstituteAsync(DateTime date, int period)
    {
        book.Changes.TryGetValue((date.Date, period), out var existing);

        var dialog = new Dialogs.SubstituteInputDialog(
            date, period,
            existing?.SubjectText, existing?.Room, existing?.Memo)
        {
            XamlRoot = host.XamlRoot
        };

        if (await MessageBox.ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

        if (!await book.SetSlotAsync(date, period, null, dialog.Subject, dialog.Room, dialog.Memo))
        {
            host.ShowWarning("변경을 저장하지 못했습니다.");
            return;
        }
        await host.OnSlotChangedAsync(date, period);
    }

    /// <summary>일지를 쓰거나 진도를 표시했다 — 진도 계획을 버리고 화면에 알린다.</summary>
    private async Task AfterRecordChangedAsync()
    {
        book.ForgetPlans();
        await host.OnRecordChangedAsync();
    }

    private static async Task RunAsync(Func<Task> action, string context)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await UserErrorReporter.ReportAsync(context, ex);
        }
    }
}
