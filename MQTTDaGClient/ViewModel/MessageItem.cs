using System;
using System.Windows;
using System.Windows.Controls;

namespace MqttDaGClient.ViewModel
{
    public class MessageItem : ListBoxItem
    {
        static MessageItem()
        {
            // 确保静态构造函数注册默认样式
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MessageItem),
         new FrameworkPropertyMetadata(typeof(MessageItem)));
        }

        public MessageBoxImage MessageType
        {
            get { return (MessageBoxImage)GetValue(MessageTypeProperty); }
            set { SetValue(MessageTypeProperty, value); }
        }

        public static readonly DependencyProperty MessageTypeProperty =
            DependencyProperty.Register("MessageType", typeof(MessageBoxImage), typeof(MessageItem),
                new PropertyMetadata(MessageBoxImage.Information));

        public override void OnApplyTemplate()
        {
            try
            {
                base.OnApplyTemplate();

                // 尝试获取模板部分,如果失败则记录日志
                var partGrid = GetTemplateChild("PART_Grid") as FrameworkElement;
                if (partGrid == null)
                {
                    System.Diagnostics.Debug.WriteLine("MessageItem: PART_Grid 模板部分未找到");
                }
            }
            catch (Exception ex)
            {
                // 捕获模板应用异常,避免崩溃
                System.Diagnostics.Debug.WriteLine($"MessageItem.OnApplyTemplate 错误: {ex.Message}");
            }
        }
    }
}
