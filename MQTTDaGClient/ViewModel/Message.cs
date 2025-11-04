using MqttDaGClient;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MqttDaGClient.ViewModel
{
    public static class Message
    {
        private static MessageAdorner messageAdorner;
        public static void PushMessage(string message, MessageBoxImage type = MessageBoxImage.Information)
        {
            // 使用 Dispatcher 确保在 UI 线程上执行
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (messageAdorner != null)
                    {
                        messageAdorner.PushMessage(message, type);
                        return;
                    }

                    Window win = null;
                    if (Application.Current?.Windows != null && Application.Current.Windows.Count > 0)
                    {
                        // 优先获取活动窗口
                        win = Application.Current.Windows.OfType<Window>().FirstOrDefault(o => o.IsActive);

                        // 如果没有活动窗口,获取主窗口
                        if (win == null)
                        {
                            win = Application.Current.MainWindow;
                        }

                        // 如果主窗口也不存在,获取第一个窗口
                        if (win == null)
                        {
                            win = Application.Current.Windows.OfType<Window>().FirstOrDefault();
                        }
                    }

                    // 如果仍然没有窗口,则无法显示消息
                    if (win == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"无法显示消息: {message} - 没有可用的窗口");
                        return;
                    }

                    // 确保窗口已加载
                    if (!win.IsLoaded)
                    {
                        win.Loaded += (s, e) => PushMessage(message, type);
                        return;
                    }

                    var layer = GetAdornerLayer(win);
                    if (layer == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"无法显示消息: {message} - AdornerLayer 为 null");
                        return;
                    }

                    messageAdorner = new MessageAdorner(layer);
                    layer.Add(messageAdorner);
                    messageAdorner.PushMessage(message, type);
                }
                catch (Exception ex)
                {
                    // 捕获异常,避免消息显示失败导致程序崩溃
                    System.Diagnostics.Debug.WriteLine($"显示消息时出错: {ex.Message}");
                }
            }), DispatcherPriority.Background);
        }

        static AdornerLayer GetAdornerLayer(Visual visual)
        {
            var decorator = visual as AdornerDecorator;
            if (decorator != null)
                return decorator.AdornerLayer;
            var presenter = visual as ScrollContentPresenter;
            if (presenter != null)
                return presenter.AdornerLayer;
            var visualContent = (visual as Window)?.Content as Visual;
            return AdornerLayer.GetAdornerLayer(visualContent ?? visual);
        }
    }

    public class MessageAdorner : Adorner
    {
        private ListBox listBox;
        private UIElement _child;
        private FrameworkElement adornedElement;
        public MessageAdorner(UIElement adornedElement) : base(adornedElement)
        {
            this.adornedElement = adornedElement as FrameworkElement;
        }

        public void PushMessage(string message, MessageBoxImage type = MessageBoxImage.Information)
        {
            if (listBox == null)
            {
                listBox = new ListBox() { Style = null, BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                Child = listBox;
            }
            var item = new MessageItem { Content = message, MessageType = type };
            var timer = new DispatcherTimer();

            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (sender, e) =>
            {
                var storyboard = new Storyboard();
                var animation = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(0.3))
                };
                Storyboard.SetTarget(animation, item);
                Storyboard.SetTargetProperty(animation, new PropertyPath("Opacity"));
                storyboard.Children.Add(animation);
                storyboard.Completed += (s, args) => listBox.Items.Remove(item);
                storyboard.Begin();
                timer.Stop();
            };
            listBox.Items.Insert(0, item);
            timer.Start();
        }

        public UIElement Child
        {
            get => _child;
            set
            {
                if (value == null)
                {
                    RemoveVisualChild(_child);
                    _child = value;
                    return;
                }
                AddVisualChild(value);
                _child = value;
            }
        }
        protected override int VisualChildrenCount
        {
            get
            {
                return _child != null ? 1 : 0;
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var x = (adornedElement.ActualWidth - _child.DesiredSize.Width) / 2;
            _child.Arrange(new Rect(new Point(x, 0), _child.DesiredSize));
            return finalSize;
        }

        protected override Visual GetVisualChild(int index)
        {
            if (index == 0 && _child != null) return _child;
            return base.GetVisualChild(index);
        }
    }
}
