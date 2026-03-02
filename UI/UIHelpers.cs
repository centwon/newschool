using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Dispatching;

namespace NewSchool.UI
{
    /// <summary>
    /// UI 헬퍼 - WinUI 3 유틸리티 (Native AOT 호환)
    /// </summary>
    public static class UIHelpers
    {
        #region Navigation

        /// <summary>
        /// 페이지 네비게이션
        /// </summary>
        public static void NavigateTo(Frame frame, Type pageType, object? parameter = null)
        {
            frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
        }

        /// <summary>
        /// 뒤로 가기
        /// </summary>
        public static bool GoBack(Frame frame)
        {
            if (frame.CanGoBack)
            {
                frame.GoBack(new SlideNavigationTransitionInfo
                {
                    Effect = SlideNavigationTransitionEffect.FromLeft
                });
                return true;
            }
            return false;
        }

        #endregion

        #region DispatcherQueue Helpers

        /// <summary>
        /// UI 스레드에서 실행
        /// </summary>
        public static void RunOnUIThread(DispatcherQueue dispatcher, Action action)
        {
            if (dispatcher.HasThreadAccess)
            {
                action();
            }
            else
            {
                dispatcher.TryEnqueue(() => action());
            }
        }

        /// <summary>
        /// UI 스레드에서 비동기 실행
        /// </summary>
        public static async Task RunOnUIThreadAsync(DispatcherQueue dispatcher, Func<Task> asyncAction)
        {
            if (dispatcher.HasThreadAccess)
            {
                await asyncAction();
            }
            else
            {
                var tcs = new TaskCompletionSource<bool>();

                dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        await asyncAction();
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });

                await tcs.Task;
            }
        }

        #endregion

        #region Visibility Helpers

        /// <summary>
        /// bool을 Visibility로 변환
        /// </summary>
        public static Visibility BoolToVisibility(bool value)
        {
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 역 bool을 Visibility로 변환
        /// </summary>
        public static Visibility InverseBoolToVisibility(bool value)
        {
            return value ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion

        #region Toast Notification (간단한 버전)

        /// <summary>
        /// 간단한 토스트 메시지 표시
        /// </summary>
        public static void ShowToast(Grid rootGrid, string message, TimeSpan? duration = null)
        {
            var toastDuration = duration ?? TimeSpan.FromSeconds(3);

            var toast = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.Colors.Black),
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 48),
                Opacity = 0,
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.White)
                }
            };

            rootGrid.Children.Add(toast);

            // 애니메이션
            var storyboard = new Storyboard();

            // Fade In
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 0.9,
                Duration = new Duration(TimeSpan.FromMilliseconds(200))
            };
            Storyboard.SetTarget(fadeIn, toast);
            Storyboard.SetTargetProperty(fadeIn, "Opacity");
            storyboard.Children.Add(fadeIn);

            storyboard.Begin();

            // 자동 제거
            var timer = new DispatcherTimer
            {
                Interval = toastDuration
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();

                // Fade Out
                var fadeOut = new Storyboard();
                var fadeOutAnim = new DoubleAnimation
                {
                    From = 0.9,
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200))
                };
                Storyboard.SetTarget(fadeOutAnim, toast);
                Storyboard.SetTargetProperty(fadeOutAnim, "Opacity");
                fadeOut.Children.Add(fadeOutAnim);

                fadeOut.Completed += (ss, ee) => rootGrid.Children.Remove(toast);
                fadeOut.Begin();
            };
            timer.Start();
        }

        #endregion

        #region Input Helpers

        /// <summary>
        /// TextBox에서 숫자만 입력 허용
        /// </summary>
        public static void AllowOnlyNumbers(TextBox textBox)
        {
            textBox.BeforeTextChanging += (sender, args) =>
            {
                args.Cancel = !int.TryParse(args.NewText, out _);
            };
        }

        /// <summary>
        /// TextBox 최대 길이 제한
        /// </summary>
        public static void LimitLength(TextBox textBox, int maxLength)
        {
            textBox.MaxLength = maxLength;
        }

        #endregion

        #region Focus Helpers

        /// <summary>
        /// 첫 번째 입력 가능한 컨트롤에 포커스
        /// </summary>
        public static void FocusFirstInput(UIElement root)
        {
            if (FindFirstFocusableElement(root) is Control control)
            {
                control.Focus(FocusState.Programmatic);
            }
        }

        private static UIElement? FindFirstFocusableElement(UIElement element)
        {
            if (element is Control control && control.IsTabStop && control.IsEnabled)
            {
                return control;
            }

            if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    var result = FindFirstFocusableElement(child);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }

        #endregion
    }

    #region ValueConverters

    /// <summary>
    /// bool을 Visibility로 변환하는 컨버터
    /// </summary>
    public partial class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// 역 bool을 Visibility로 변환하는 컨버터
    /// </summary>
    public partial class InverseBoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Collapsed;
            }
            return true;
        }
    }

    #endregion
}
