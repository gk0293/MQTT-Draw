using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MqttDaGClient.Model;
using MqttDaGClient.View;
using MQTTnet;
using MQTTnet.Packets;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;

//增加取消订阅和展示当前订阅



namespace MqttDaGClient.ViewModel
{
    public partial class MainVM : ObservableObject
    {
        private readonly IMqttClient _mqttClient;
        private readonly Dispatcher _dispatcher;
        private MqttClientOptions _clientOptions;
        private readonly ConcurrentQueue<MqttApplicationMessage> _pendingMessages = new();
        private readonly List<MqttTopicFilter> _persistedSubscriptions = new();

        /*订阅的主题集合*/
        [ObservableProperty]
        private ObservableCollection<string> _topics = new ObservableCollection<string>();


        /*订阅的客户端集合*/
        [ObservableProperty]
        private ObservableCollection<string> _topicClient = new ObservableCollection<string>();

        /*客户端预设数据*/
        [ObservableProperty]
        private MqttClientModel _client = new("127.0.0.1", "1883", "admin", "1234", "client01");

        /*主题*/
        [ObservableProperty]
        private string _topic;

        /*对话框*/
        [ObservableProperty]
        private string _pubMessage;

        /*消息栏*/
        [ObservableProperty]
        private StringBuilder _logBuilder = new StringBuilder();

        [ObservableProperty]
        private string _connectionStatus = "未连接";

        [ObservableProperty]
        private string _disTopic; // 客户端ID

        string oldtopic;

        // 添加绘画模式标志和节流控制
        private bool _isDrawing = false;
        private DateTime _lastDrawSendTime = DateTime.MinValue;
        private const int DrawThrottleMs = 50; // 每50ms最多发送一次绘画数据

        /*用于转换，stringbuilder无法直接绑定前端*/
        public string LogContent
        {
            get => _logBuilder.ToString();
            set
            {
                _logBuilder.Clear();
                _logBuilder.Append(value);
                OnPropertyChanged();
            }
        }

        /*绑定客户端窗口*/
        public RelayCommand OpenNewClientCommand { get; }

        public MainVM()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _mqttClient = new MqttClientFactory().CreateMqttClient();
            InitializeEventHandlers();

            /*客户端窗口*/
            OpenNewClientCommand = new RelayCommand(OpenNewClient);
        }

        private Window _currentWindow;
        /*打开设置窗口*/
        private void OpenNewClient()
        {
            var newClient = new Client();
            newClient.DataContext = this;
            /*设置父窗口，避免主窗口关闭子窗口不关闭*/
            newClient.Owner = Application.Current.MainWindow;
            newClient.Show();

            _currentWindow = newClient;
        }

        private void InitializeEventHandlers()
        {
            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        }


        //主题栏select
        partial void OnDisTopicChanged(string topic)
        {
            TopicChangs(DisTopic);
        }
        private async Task TopicChangs(string topic)
        {
            if (DisTopic != null && topic != oldtopic && _mqttClient.IsConnected)
            {

                oldtopic = DisTopic;
                //向服务端发送消息但此行为不属于订阅服务器，所以不会触发OnMessageReceivedAsync，
                //发送消息到服务端，服务端会将当前服务器下所有主题发送到订阅者
                var message = new MqttApplicationMessageBuilder()
                   .WithTopic("response_subscribers")
                   .WithPayload(DisTopic)
                   .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                   .WithUserProperty("Username", Client.ClientId)
                   .Build();


                await _mqttClient.PublishAsync(message);

            }
        }

        /*连接服务器按键*/
        [RelayCommand]
        private async Task ConnectAsync()
        {
            try
            {
                // 每次连接时动态构建新配置并保存到clientoptions中用于重连
                // cleanSession 有持久订阅时保留会话
                var currentOptions = new MqttClientOptionsBuilder()
                   .WithClientId(Client.ClientId) // 获取最新值
                   .WithTcpServer(Client.ServerIP, int.Parse(Client.ServerPort))
                   .WithCredentials(Client.ServerName, Client.ServerPwd)
                   .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
                   .WithCleanSession(_persistedSubscriptions.Count == 0)
                   .Build();

                _clientOptions = currentOptions;

                await _mqttClient.ConnectAsync(currentOptions);
                //AddLog("成功连接到服务器");
                //会与241行发送重复,二选一
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // 网络连接错误(服务器未开启或网络不可达)
                AddLog($"连接失败: 无法连接到服务器 {Client.ServerIP}:{Client.ServerPort}");
                AddLog($"详细信息: {ex.Message}");
                AddLog("请确认服务器是否已启动,以及IP地址和端口是否正确");

                // 延迟显示消息,确保窗口已完全加载
                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
                {
                    Message.PushMessage($"无法连接到服务器\n请检查服务器是否已启动", MessageBoxImage.Error);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (MQTTnet.Exceptions.MqttCommunicationException ex)
            {
                // MQTT通信异常
                AddLog($"MQTT通信异常: {ex.Message}");
                AddLog("可能原因: 服务器未响应或网络中断");

                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
         {
             Message.PushMessage($"MQTT通信失败\n{ex.Message}", MessageBoxImage.Error);
         }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (System.TimeoutException ex)
            {
                // 连接超时
                AddLog($"连接超时: {ex.Message}");
                AddLog("服务器响应超时,请检查网络连接或服务器状态");

                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
                {
                    Message.PushMessage("连接超时\n服务器无响应", MessageBoxImage.Warning);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (FormatException ex)
            {
                // 端口格式错误
                AddLog($"配置错误: 端口号格式不正确");
                AddLog($"详细信息: {ex.Message}");

                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
                {
                    Message.PushMessage("端口号格式错误\n请输入有效的端口号", MessageBoxImage.Warning);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (ArgumentException ex)
            {
                // 参数错误(如IP地址格式错误)
                AddLog($"配置错误: {ex.Message}");
                AddLog("请检查服务器IP地址、端口、用户名和密码是否正确");

                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
                {
                    Message.PushMessage($"配置参数错误\n{ex.Message}", MessageBoxImage.Warning);
                }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                // 其他未预料的错误
                AddLog($"连接失败: 发生未知错误");
                AddLog($"错误类型: {ex.GetType().Name}");
                AddLog($"详细信息: {ex.Message}");
                if (ex.InnerException != null)
                {
                    AddLog($"内部错误: {ex.InnerException.Message}");
                }

                await Task.Delay(100);
                await _dispatcher.InvokeAsync(() =>
           {
               Message.PushMessage($"连接失败\n{ex.Message}", MessageBoxImage.Error);
           }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            /*确认即关闭窗口*/
            if (_currentWindow != null)
            {
                _currentWindow.Close();
                _currentWindow = null;
            }
        }

        /*断开连接按钮*/
        [RelayCommand]
        private async Task DisconnectAsync()
        {
            if (_mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
                // AddLog("已断开与服务器的连接");
                //与256消息重复，二选一
            }
        }


        /*订阅主题*/
        [RelayCommand]
        private async Task SubscribeAsync()
        {
            if (string.IsNullOrWhiteSpace(Topic))
            {
                AddLog("订阅主题不能为空");
                Message.PushMessage("订阅主题不能为空", MessageBoxImage.Warning);

                return;
            }

            var topicFilter = new MqttTopicFilter
            {
                Topic = Topic,
                QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce,
            };

            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
               .WithTopicFilter(topicFilter)
               .Build();



            try
            {
                var result = await _mqttClient.SubscribeAsync(subscribeOptions);

                if (result.Items.Any(x => x.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                {
                    AddLog($"订阅失败: {result.ReasonString}");
                    return;
                }

                _persistedSubscriptions.Add(topicFilter);
                AddLog($"成功订阅: {Topic} (QoS: {result.Items.First().ResultCode})");
                Message.PushMessage("成功订阅", MessageBoxImage.Information);


            }
            catch (Exception ex)
            {
                AddLog($"订阅失败: {ex.Message}");
            }
        }

        /*取消订阅主题*/
        [RelayCommand]
        private async Task UnSubscribeAsync()
        {
            if (string.IsNullOrWhiteSpace(DisTopic))
            {
                AddLog("取消订阅主题不能为空");
                Message.PushMessage("取消订阅主题不能为空", MessageBoxImage.Warning);
                return;
            }
            else
            {
                if (DisTopic == "response_subscribers" || DisTopic == "Base")
                {
                    AddLog($"禁止取消基础主题{DisTopic}");
                }
                else
                {
                    AddLog($"取消订阅: {DisTopic}");
                    _mqttClient.UnsubscribeAsync(DisTopic);
                }
            }

        }

        /*发送按钮*/
        //发送消息以及判断
        [RelayCommand]
        private async Task PublishAsync()
        {
            /*对话框在没被点击时即未被触发建立状态，此时pubmessage为null,被点击后即被建立，为""*/
            if (DisTopic != null && PubMessage != null && PubMessage != "" && DisTopic != "")
            {
                var message = new MqttApplicationMessageBuilder()
                   .WithTopic(DisTopic)
                   .WithPayload(Encoding.UTF8.GetBytes(PubMessage))
                   .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                   .WithUserProperty("Username", Client.ClientId)
                   .Build();

                if (!_mqttClient.IsConnected)
                {
                    _pendingMessages.Enqueue(message);
                    AddLog("客户端未连接，消息已加入队列");
                    return;
                }

                try
                {
                    var result = await _mqttClient.PublishAsync(message);
                    AddLog($"消息已发送: {result.ReasonString}");
                    PubMessage = ""; // 释放消息对象 
                }
                catch (Exception ex)
                {
                    AddLog($"发布失败: {ex.Message}");
                }
            }
            else if (PubMessage == "")
            {
                AddLog($"禁止发送空消息");
            }
            else if (DisTopic == null || DisTopic == "")
            {
                AddLog("未选中发送主题");
            }
            else
            {
                AddLog($"主题未订阅或客户端未启动");
                PubMessage = ""; // 释放消息对象  
            }
        }


        /*处理 MQTT 客户端连接成功事件，包括处理待发送消息、恢复订阅和订阅基础主题*/
        private async Task OnConnectedAsync(MqttClientConnectedEventArgs arg)
        {
            // 使用 Task.Run 避免阻塞 UI 线程
            await Task.Run(async () =>
              {
                  await ProcessPendingMessages();
                  await RestoreSubscriptions();
                  await SubscribeBaseTopicAsync();
              });

            // UI 更新在 Dispatcher 中异步执行
            await _dispatcher.InvokeAsync(() =>
                  {
                      ConnectionStatus = "已连接";
                      AddLog("成功连接到服务器");
                  });

            // 消息提示异步执行,不阻塞
            _dispatcher.BeginInvoke(() => Message.PushMessage("连接成功", MessageBoxImage.Information));
        }


        /*处理 MQTT 客户端断开连接事件，包括重连*/
        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
        {
            await _dispatcher.InvokeAsync(() =>
           {
               ConnectionStatus = "未连接";
               AddLog($"已断开与服务器的连接: {arg.Reason}");

               // 如果有异常信息,记录详细错误
               if (arg.Exception != null)
               {
                   AddLog($"断开原因: {arg.Exception.Message}");
                   if (arg.Exception.InnerException != null)
                   {
                       AddLog($"详细信息: {arg.Exception.InnerException.Message}");
                   }
               }
           });

            // 消息提示异步执行,不阻塞
            _dispatcher.BeginInvoke(() => Message.PushMessage($"连接断开: {arg.Reason}", MessageBoxImage.Warning));

            //服务端未指定错误导致关闭，触发
            //第二个为服务器自动关闭，shuttingdown
            if (arg.Reason == MqttClientDisconnectReason.UnspecifiedError)
            {
                AddLog($"检测到未知错误断开,将在20秒后尝试重连");
                await Task.Delay(20000);

                try
                {
                    await _mqttClient.ConnectAsync(_clientOptions);
                    AddLog("重连成功");
                }
                catch (Exception ex)
                {
                    AddLog($"重连失败: {ex.Message}");
                    AddLog("请手动重新连接服务器");
                    Message.PushMessage("自动重连失败\n请手动重新连接", MessageBoxImage.Error);
                }
            }
            else if (arg.Reason == MqttClientDisconnectReason.ServerShuttingDown)
            {
                AddLog($"服务器正在关闭,将在20秒后尝试重连");
                await Task.Delay(20000);

                try
                {
                    await _mqttClient.ConnectAsync(_clientOptions);
                    AddLog("重连成功");
                }
                catch (Exception ex)
                {
                    AddLog($"重连失败: {ex.Message}");
                    AddLog("服务器可能仍未启动,请稍后手动重新连接");
                    Message.PushMessage("自动重连失败\n服务器未启动", MessageBoxImage.Warning);
                }
            }
        }



        /*接收消息*/
        // 处理接收的消息以及过滤墨迹消息
        private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
        {
            var payload = arg.ApplicationMessage.ConvertPayloadToString();

            string? username = null;

            // 检查 UserProperties 是否为 null，
            // .WithUserProperty("Username", Client.ClientId)为MQTTent5.0版本添加的属性
            // 其他客户端如果不添加此属性，则会导致 UserProperties 为 null
            if (arg.ApplicationMessage.UserProperties != null)
            {
                username = arg.ApplicationMessage.UserProperties.FirstOrDefault(p => p.Name == "Username")?.Value;

            }

            /*接收主题下列表客户端消息*/
            /*初始都会订阅Base主题，不过滤会显示base主题下的所有客户端，base主题本意就是后台维护主题，不应该在客户端前台显示*/
            if (arg.ApplicationMessage.Topic == "response_subscribers" && username == Client.ClientId && arg.ApplicationMessage.ConvertPayloadToString() != DisTopic)
            {
                ProcessResponseTopicMessage(arg.ApplicationMessage.ConvertPayloadToString());
                return Task.CompletedTask;
            }
            /*消息过滤*/
            /*判断顺序为从左到右，先判断是否为null，否则会报错*/
            /*过滤Base主题和Username==List*/
            if (payload != null && !payload.StartsWith("draw") && username != "Base" && arg.ApplicationMessage.Topic != "response_subscribers")
            {
                if (username != null && username != "List")
                {
                    if (!payload.StartsWith("draw"))
                    {
                        AddLog($"收到消息 \n 主题为： [{arg.ApplicationMessage.Topic}] \n 用户: {username}\n 消息内容: {payload}");
                    }
                }
                else
                {
                    if (!payload.StartsWith("draw"))
                    {
                        AddLog($"收到消息 \n 主题为：[{arg.ApplicationMessage.Topic}] \n 用户: 未知 \n 消消息内容: {payload}");
                    }
                }
            }

            //接收到当前服务器下所有主题的消息
            if (arg.ApplicationMessage.Topic == "Base" && username == "Base")
            {
                ProcessBaseTopicMessage(arg.ApplicationMessage.ConvertPayloadToString());
                return Task.CompletedTask;
            }

            /*DaG主题的墨迹判断*/
            // 处理 DaG 主题的墨迹消息并添加到 InkCanvas
            // 修复: 使用 username != Client.ClientId 来过滤自己发送的消息
            if (arg.ApplicationMessage.Topic == "DaG" && username != Client.ClientId)
            {
                // 处理实时绘画点
                if (payload.StartsWith("drawpoint:"))
                {
                    var pointData = payload.Substring(10);
                    var parts = pointData.Split(',');
                    if (parts.Length == 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                    {
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var inkCanvas = Application.Current.Windows.OfType<ClientMain>().FirstOrDefault()?.inkCanvas;
                            if (inkCanvas != null)
                            {
                                // 使用发送方的ClientId作为Tag的Key来区分不同用户的笔画
                                string strokeKey = $"remote_{username}";

                                // 获取或创建当前正在绘制的笔画
                                if (inkCanvas.Tag is Dictionary<string, Stroke> strokes)
                                {
                                    if (strokes.TryGetValue(strokeKey, out Stroke currentStroke))
                                    {
                                        currentStroke.StylusPoints.Add(new StylusPoint(x, y));
                                    }
                                    else
                                    {
                                        // 创建新笔画
                                        var stylusPoints = new StylusPointCollection();
                                        stylusPoints.Add(new StylusPoint(x, y));
                                        var stroke = new Stroke(stylusPoints);
                                        inkCanvas.Strokes.Add(stroke);
                                        strokes[strokeKey] = stroke;
                                    }
                                }
                                else
                                {
                                    // 初始化字典
                                    var strokeDict = new Dictionary<string, Stroke>();
                                    var stylusPoints = new StylusPointCollection();
                                    stylusPoints.Add(new StylusPoint(x, y));
                                    var stroke = new Stroke(stylusPoints);
                                    inkCanvas.Strokes.Add(stroke);
                                    strokeDict[strokeKey] = stroke;
                                    inkCanvas.Tag = strokeDict;
                                }
                            }
                        }, System.Windows.Threading.DispatcherPriority.Render);
                    }
                    return Task.CompletedTask;
                }
                // 处理绘画结束
                else if (payload == "drawend")
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var inkCanvas = Application.Current.Windows.OfType<ClientMain>().FirstOrDefault()?.inkCanvas;
                if (inkCanvas != null)
                {
                    // 移除该用户的笔画引用
                    if (inkCanvas.Tag is Dictionary<string, Stroke> strokes)
                    {
                        string strokeKey = $"remote_{username}";
                        strokes.Remove(strokeKey);
                    }
                }
            });
                    return Task.CompletedTask;
                }
                // 处理完整笔画 (兼容原有方式)
                else if (payload.StartsWith("draw"))
                {
                    //AddLog("收到 DaG主题 完整笔画消息");
                    var coordinates = payload.Substring(4).Split(';');
                    var stylusPoints = new StylusPointCollection();
                    foreach (var coordinate in coordinates)
                    {
                        var parts = coordinate.Split(',');
                        if (parts.Length == 2 && double.TryParse(parts[0], out double x) && double.TryParse(parts[1], out double y))
                        {
                            stylusPoints.Add(new StylusPoint(x, y));
                        }
                    }
                    if (stylusPoints.Count > 0)
                    {
                        var stroke = new Stroke(stylusPoints);
                        Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        var inkCanvas = Application.Current.Windows.OfType<ClientMain>().FirstOrDefault()?.inkCanvas;
                                        if (inkCanvas != null)
                                        {
                                            inkCanvas.Strokes.Add(stroke);
                                        }
                                    }, System.Windows.Threading.DispatcherPriority.Render);
                    }
                }
            }

            return Task.CompletedTask;
        }


        /*处理待发送消息*/
        private async Task ProcessPendingMessages()
        {
            while (_pendingMessages.TryDequeue(out var message))
            {
                await _mqttClient.PublishAsync(message);
            }
        }


        /*订阅基础主题*/
        private async Task SubscribeBaseTopicAsync()
        {
            var topicFilter = new MqttTopicFilter
            {
                Topic = "Base",
                QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
            };

            await _mqttClient.SubscribeAsync(topicFilter);
            _persistedSubscriptions.Add(topicFilter);

            //订阅列表成员主题
            var topicFilter2 = new MqttTopicFilter
            {
                Topic = "response_subscribers",
                QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce
            };
            await _mqttClient.SubscribeAsync(topicFilter2);
            _persistedSubscriptions.Add(topicFilter2);

            // 自动订阅绘画主题 DaG，保证能实时接收绘画数据
            var dagFilter = new MqttTopicFilter
            {
                Topic = "DaG",
                QualityOfServiceLevel = MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce
            };
            await _mqttClient.SubscribeAsync(dagFilter);
            _persistedSubscriptions.Add(dagFilter);
        }


        /*处理基础主题消息*/
        /*为topics列表内增加当前服务器下所有主题*/
        private void ProcessBaseTopicMessage(string payload)
        {
            _dispatcher.Invoke(() =>
            {
                Topics.Clear();
                foreach (var topic in payload.Split(',').Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    Topics.Add(topic.Trim());
                }
                OnPropertyChanged(nameof(Topics));
            });
        }



        /*处理成员主题消息*/
        private void ProcessResponseTopicMessage(string payload)
        {
            _dispatcher.Invoke(() =>
            {
                TopicClient.Clear();
                foreach (var client in payload.Split(',').Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    TopicClient.Add(client.Trim());
                }
                OnPropertyChanged(nameof(TopicClient));
            });
        }




        /*恢复订阅*/
        private async Task RestoreSubscriptions()
        {
            if (_persistedSubscriptions.Any())
            {
                // 使用 Distinct 去重
                var uniqueFilters = _persistedSubscriptions
                    .GroupBy(f => f.Topic)
                    .Select(g => g.First());


                var subscribeOptionsBuilder = new MqttClientSubscribeOptionsBuilder();

                foreach (var topicFilter in _persistedSubscriptions)
                {
                    subscribeOptionsBuilder.WithTopicFilter(topicFilter);
                }

                var subscribeOptions = subscribeOptionsBuilder.Build();

                await _mqttClient.SubscribeAsync(subscribeOptions);
            }
        }

        /*前端消息栏*/
        /*对轨迹消息进行过滤*/
        private void AddLog(string message)
        {
            string ClientlogEntry = $"{DateTime.Now:G}  {message}\n";

            if (!message.Contains("draw"))
            {

                _dispatcher.Invoke(() =>
                {
                    _logBuilder.AppendLine(ClientlogEntry);
                    OnPropertyChanged(nameof(LogContent));

                    // 性能优化：限制日志长度
                    if (_logBuilder.Length > 10000)
                    {
                        _logBuilder.Remove(0, 5000);
                    }
                });
            }


            ///*保存到本地*/
            try
            {
                using (StreamWriter writer = File.AppendText("../../../ClientLog.txt"))
                {
                    writer.Write(ClientlogEntry);
                }
            }
            catch (Exception ex)
            {
                //不处理该报错，该报错为“假报错”，文件保存成功，消息传输成功

                // 处理文件写入错误
                //Application.Current.Dispatcher.Invoke(() =>
                //{
                //    LogContent += $"{DateTime.Now:HH:mm:ss} 保存日志到文件时出错: {ex.Message}\n";
                //});
            }
        }


        /*DaG核心控件，负责实时抓取轨迹并传递*/
        [RelayCommand]
        public void HandleInkCanvasStrokeCollected()
        {
            // 修复: 使用 DisTopic (当前选中的发布主题) 而不是 Topic (订阅主题)
            if (DisTopic == "DaG" && _mqttClient.IsConnected)
            {
                var inkCanvas = Application.Current.Windows.OfType<ClientMain>().FirstOrDefault()?.inkCanvas;
                if (inkCanvas != null && inkCanvas.Strokes.Count > 0)
                {
                    var stroke = inkCanvas.Strokes.Last();
                    var coordinates = new StringBuilder("draw");
                    foreach (var point in stroke.StylusPoints)
                    {
                        // 限制小数位为两位，避免过长
                        coordinates.Append($"{point.X.ToString("F2")},{point.Y.ToString("F2")};");
                    }


                    var message = new MqttApplicationMessageBuilder()
                           .WithTopic("DaG")
                  .WithPayload(Encoding.UTF8.GetBytes(coordinates.ToString()))
                         .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce) // 改为 QoS 0 提高性能
                     .WithUserProperty("Username", Client.ClientId)
                   .Build();

                    // 异步发送不等待,避免阻塞绘画
                    _ = _mqttClient.PublishAsync(message);

                    //AddLog("已发送绘画坐标消息");  // 移除日志避免频繁输出
                }
                else
                {
                    AddLog("InkCanvas 中没有可用的笔画");
                }
            }
            else
            {
                AddLog("未满足发送条件：主题不是 DaG 或客户端未连接");
            }
        }


        /*处理实时绘画 - StylusMove 事件*/
        [RelayCommand]
        public void HandleStylusMove(StylusPoint point)
        {
            if (!_mqttClient.IsConnected)
                return;

            // 节流控制：避免发送过于频繁
            var now = DateTime.Now;
            if ((now - _lastDrawSendTime).TotalMilliseconds < DrawThrottleMs)
                return;

            _lastDrawSendTime = now;

            // 发送单个点的坐标
            var coordinates = $"drawpoint:{point.X.ToString("F2")},{point.Y.ToString("F2")}";

            var message = new MqttApplicationMessageBuilder()
            .WithTopic("DaG")
        .WithPayload(Encoding.UTF8.GetBytes(coordinates))
      .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce) // QoS 0
          .WithUserProperty("Username", Client.ClientId)
   .Build();

            // 异步发送不等待
            _ = _mqttClient.PublishAsync(message);
        }

        /*处理绘画开始事件*/
        [RelayCommand]
        public void HandleStylusDown()
        {
            _isDrawing = true;
        }

        /*处理绘画结束事件*/
        [RelayCommand]
        public void HandleStylusUp()
        {
            _isDrawing = false;

            // 发送绘画结束标记
            if (_mqttClient.IsConnected)
            {
                var message = new MqttApplicationMessageBuilder()
                .WithTopic("DaG")
                .WithPayload("drawend")
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce)
           .WithUserProperty("Username", Client.ClientId)
             .Build();
                _ = _mqttClient.PublishAsync(message);
            }
        }


        /*仅清除当前客户端画板内容*/
        [RelayCommand]
        private void ClearInkCanvas()
        {
            var inkCanvas = Application.Current.Windows.OfType<ClientMain>().FirstOrDefault()?.inkCanvas;
            if (inkCanvas != null)
            {
                inkCanvas.Strokes.Clear();
                // 清除字典引用
                if (inkCanvas.Tag is Dictionary<string, Stroke>)
                {
                    inkCanvas.Tag = new Dictionary<string, Stroke>();
                }
                else
                {
                    inkCanvas.Tag = null;
                }
                AddLog("已清除 InkCanvas 中的所有笔画");
            }
            else
            {
                AddLog("无法找到 InkCanvas 控件");
            }
        }

    }
}