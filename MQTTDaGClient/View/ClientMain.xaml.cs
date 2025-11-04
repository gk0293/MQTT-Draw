using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using MqttDaGClient.ViewModel;
using System.Windows.Ink;

namespace MqttDaGClient.View
{
    /// <summary>
    /// Interaction logic for ClientMain.xaml
    /// </summary>
    public partial class ClientMain : MetroWindow
    {
        private MainVM _viewModel;
        private bool _isDrawing = false;
        private Stroke _currentStroke; // 当前正在绘制的笔画

        public ClientMain()
        {
            InitializeComponent();

            _viewModel = DataContext as MainVM ?? new MainVM();
            DataContext = _viewModel;

            // 禁用 InkCanvas 的默认绘画模式，改为手动控制
            inkCanvas.EditingMode = InkCanvasEditingMode.None;

            // 实时绘画事件（触控笔）
            inkCanvas.StylusDown += InkCanvas_StylusDown;
            inkCanvas.StylusMove += InkCanvas_StylusMove;
            inkCanvas.StylusUp += InkCanvas_StylusUp;

            // 鼠标事件支持
            inkCanvas.MouseDown += InkCanvas_MouseDown;
            inkCanvas.MouseMove += InkCanvas_MouseMove;
            inkCanvas.MouseUp += InkCanvas_MouseUp;
        }

        // ===== 触控笔事件 =====
        private void InkCanvas_StylusDown(object sender, StylusDownEventArgs e)
        {
            _isDrawing = true;
            var point = e.GetStylusPoints(inkCanvas)[0];

            // 创建新笔画
            var stylusPoints = new StylusPointCollection();
            stylusPoints.Add(point);
            _currentStroke = new Stroke(stylusPoints);
            _currentStroke.DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone();
            inkCanvas.Strokes.Add(_currentStroke);

            // 通知 ViewModel 开始绘画
            _viewModel?.HandleStylusDownCommand.Execute(null);
            // 发送第一个点
            _viewModel?.HandleStylusMoveCommand.Execute(point);

            e.Handled = true;
        }

        private void InkCanvas_StylusMove(object sender, StylusEventArgs e)
        {
            if (!_isDrawing || e.StylusDevice.InAir) return;

            var points = e.GetStylusPoints(inkCanvas);
            if (points.Count > 0)
            {
                var point = points[0];
                // 添加点到当前笔画
                _currentStroke?.StylusPoints.Add(point);
                // 实时发送点
                _viewModel?.HandleStylusMoveCommand.Execute(point);
            }

            e.Handled = true;
        }

        private void InkCanvas_StylusUp(object sender, StylusEventArgs e)
        {
            if (!_isDrawing) return;

            _isDrawing = false;
            _currentStroke = null;

            // 通知 ViewModel 结束绘画
            _viewModel?.HandleStylusUpCommand.Execute(null);

            e.Handled = true;
        }

        // ===== 鼠标事件 =====
        private void InkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || e.LeftButton != MouseButtonState.Pressed)
                return;

            _isDrawing = true;
            var position = e.GetPosition(inkCanvas);
            var point = new StylusPoint(position.X, position.Y);

            // 创建新笔画
            var stylusPoints = new StylusPointCollection();
            stylusPoints.Add(point);
            _currentStroke = new Stroke(stylusPoints);
            _currentStroke.DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone();
            inkCanvas.Strokes.Add(_currentStroke);

            // 捕获鼠标
            inkCanvas.CaptureMouse();

            // 通知 ViewModel 开始绘画
            _viewModel?.HandleStylusDownCommand.Execute(null);
            // 发送第一个点
            _viewModel?.HandleStylusMoveCommand.Execute(point);

            e.Handled = true;
        }

        private void InkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing || e.StylusDevice != null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var position = e.GetPosition(inkCanvas);
            var point = new StylusPoint(position.X, position.Y);

            // 添加点到当前笔画
            _currentStroke?.StylusPoints.Add(point);
            // 实时发送点
            _viewModel?.HandleStylusMoveCommand.Execute(point);

            e.Handled = true;
        }

        private void InkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.StylusDevice != null || !_isDrawing)
                return;

            _isDrawing = false;
            _currentStroke = null;

            // 释放鼠标捕获
            inkCanvas.ReleaseMouseCapture();

            // 通知 ViewModel 结束绘画
            _viewModel?.HandleStylusUpCommand.Execute(null);

            e.Handled = true;
        }
    }
}