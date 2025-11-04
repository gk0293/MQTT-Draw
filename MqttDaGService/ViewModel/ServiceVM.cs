using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlzEx.Standard;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using MqttDaGService.Model;
using MqttDaGService.View;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Shapes;


//更改base主题，使其传播的为当前客户端下订阅的主题

/*draw消息单独存储*/
/*利用字典进行传递主题下客户端消息*/

//后续可考虑增加移出服务器，加入黑名单等功能
namespace MqttDaGService.ViewModel
{
    public partial class ServiceVM : ObservableObject
    {
        private MqttServer _mqttServer;
        private readonly List<TopicItem> _topics = new();


        /*服务器初始设置*/
        [ObservableProperty]
        private ServiceModel _server = new("127.0.0.1", "1883", "admin", "1234");

        /*客户端列表*/
        [ObservableProperty]
        private ObservableCollection<string> _clients = new();

        /*主题列表*/
        [ObservableProperty]
        private ObservableCollection<string> _topicsList = new();

        /*订阅的客户端集合*/
        [ObservableProperty]
        private ObservableCollection<string> _topicClient = new ObservableCollection<string>();

        [ObservableProperty]
        private static Dictionary<string, List<string>> _topicSubscribers = new Dictionary<string, List<string>>();

        /*主题选择*/
        [ObservableProperty]
        private string selectedTopic;

        /*对话框*/
        [ObservableProperty]
        private string _pubMessage;

        /*日志内容*/
        [ObservableProperty]
        private string _logContent = string.Empty;

        /*QoS2消息状态*/
        private readonly ConcurrentDictionary<ushort, QoSTwoMessageState> _qos2Messages = new();

        /*设置窗口*/
        public RelayCommand OpenNewServiceCommand { get; }

        private Window _currentWindow;

        /*打开设置窗口*/
        private void OpenNewService()
        {
            var newService = new Service();
            newService.DataContext = this;
            /*设置父窗口，避免主窗口关闭子窗口不关闭*/
            newService.Owner = Application.Current.MainWindow;
            newService.Show();

            _currentWindow = newService;

        }

        /*窗口中第三个客户端窗口刷新代码，当中间的主题窗口select选中发生变更时触发*/
        partial void OnSelectedTopicChanged(string stopic)
        {
            if (stopic != null)
            {
                string result = stopic.Substring(0, stopic.IndexOf(':'));
                TopicClient.Clear();
                List<string> value; // Corrected type to match the dictionary value type
                if (_topicSubscribers.TryGetValue(result, out value) && value != null) // Adjusted the condition order
                {
                    foreach (var client in value)
                    {
                        TopicClient.Add(client.Trim());
                    }
                }
            }
        }

        [RelayCommand]
        private async void UpdateServerSettings()
        {
            // 记录更新操作
            Log($"服务器设置已更新：IP={Server.ServerIP}, 端口={Server.ServerPort}, 用户={Server.ServerName}, 密码={Server.ServerPwd}");
            // 重新构建服务器选项
            var newServerOptions = BuildServerOptions();

            if (newServerOptions == null)
            {
                Log("新配置无效，无法更新服务器设置。");
                return;
            }

            // 停止服务器
            if (_mqttServer.IsStarted)
            {
                await _mqttServer.StopAsync();
                Log("服务端已停止，准备使用新配置重启");
            }

            // 重新启动服务器
            try
            {
                // 重新创建 MqttServer 实例并传入新的配置
                _mqttServer = new MqttServerFactory().CreateMqttServer(newServerOptions);
                SetupEventHandlers();
                await _mqttServer.StartAsync();
                Log("服务端已使用新配置重新启动 (MQTT 5.0)");
                Message.PushMessage("服务器已重新启动", MessageBoxImage.Information);

            }
            catch (Exception ex)
            {
                Log($"使用新配置启动失败：{ex.Message}");
                Message.PushMessage("使用新配置启动失败", MessageBoxImage.Error);
            }

            /*确认即关闭窗口*/
            if (_currentWindow != null)
            {
                _currentWindow.Close();
                _currentWindow = null;
            }
        }


        public ServiceVM()
        {
            OpenNewServiceCommand = new RelayCommand(OpenNewService);

            var serverOptions = BuildServerOptions();
            if (serverOptions == null)
            {
                Log("初始配置无效，无法启动服务器。");
                Message.PushMessage("初始配置无效", MessageBoxImage.Warning);
                return;
            }
            _mqttServer = new MqttServerFactory().CreateMqttServer(serverOptions);
            SetupEventHandlers();
        }

        /*设置事件处理程序*/
        private void SetupEventHandlers()
        {
            _mqttServer.ValidatingConnectionAsync += OnValidateConnection;
            _mqttServer.ClientConnectedAsync += OnClientConnected;
            _mqttServer.ClientDisconnectedAsync += OnClientDisconnected;
            _mqttServer.InterceptingSubscriptionAsync += OnSubscriptionIntercepted;
            _mqttServer.InterceptingUnsubscriptionAsync += OnUnsubscriptionIntercepted;
            _mqttServer.InterceptingPublishAsync += OnMessageIntercepted;
            //_mqttServer.ClientSubscribedTopicAsync += OnMessageReceived;
        }

    

        /*启动服务器*/
        [RelayCommand]
        private async Task StartServerAsync()
        {
            var serverOptions = BuildServerOptions();
            if (serverOptions == null)
            {
                Log("配置无效，无法启动服务器。");
                Message.PushMessage("配置无效", MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (_mqttServer == null)
                {
                    _mqttServer = new MqttServerFactory().CreateMqttServer(serverOptions);
                    SetupEventHandlers();
                }
                await _mqttServer.StartAsync();
                Log("服务端已启动 (MQTT 5.0)");
                Message.PushMessage("服务器已启动！", MessageBoxImage.Information);

                Log($"服务端地址为{Server.ServerIP}:{Server.ServerPort}");


                // 发布一条测试消息到 Base 主题，确保主题创建
                var testMessage = new MqttApplicationMessageBuilder()
                   .WithTopic("Base")
                   .WithPayload(Encoding.UTF8.GetBytes("Initializing Base topic"))
                   .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                   .WithUserProperty("Username", "Base")
                   .WithRetainFlag(true)
                   .Build();
                await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(testMessage));
                Log("已发布测试消息到 Base 主题");


                // 发布一条测试消息到 response_subscribers 主题，确保主题创建
                var testMessage2 = new MqttApplicationMessageBuilder()
                   .WithTopic("response_subscribers")
                   .WithPayload(Encoding.UTF8.GetBytes("Initializing response_subscribers topic"))
                   .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                   .WithUserProperty("Username", "List")
                   .WithRetainFlag(true)
                   .Build();
                await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(testMessage2));
                Log("已发布测试消息到 response_subscribers 主题");
            }
            catch (Exception ex)
            {
                Log($"启动失败：{ex.Message}");
                Message.PushMessage("启动失败", MessageBoxImage.Warning);
            }
        }

        /*停止服务器*/
        [RelayCommand]
        private async Task StopServerAsync()
        {
            try
            {
                await _mqttServer.StopAsync();
                Log("服务端已停止");
                Message.PushMessage("服务器已停止！", MessageBoxImage.Question);
            }
            catch (Exception ex)
            {
                Log($"停止服务器时出错：{ex.Message}");
                Message.PushMessage("停止服务器时出错", MessageBoxImage.Error);
            }
        }

        /*重启服务器*/
        private MqttServerOptions BuildServerOptions()
        {
            if (!IPAddress.TryParse(Server.ServerIP, out _))
            {
                Log($"IP 地址格式有误！请输入有效的 IP 地址。");
                Message.PushMessage("IP 地址格式有误", MessageBoxImage.Error);
                return null;
            }

            try
            {
                return new MqttServerOptionsBuilder()
                   .WithDefaultEndpoint()
                   .WithDefaultEndpointPort(int.Parse(Server.ServerPort))
                   .Build();
            }
            catch (System.FormatException)
            {
                Log($"端口号格式有误！请输入有效的端口号。");
                Message.PushMessage("端口号格式有误", MessageBoxImage.Error);
                return null;
            }
        }

        /*验证连接*/
        //这里可以根据需要进行身份验证，即账户密码
        private Task OnValidateConnection(ValidatingConnectionEventArgs context)
        {
            if (context.UserName != Server.ServerName || context.Password != Server.ServerPwd)
            {
                context.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                return Task.CompletedTask;
            }

            context.ReasonCode = MqttConnectReasonCode.Success;
            return Task.CompletedTask;
        }

        /*客户端连接*/
        private async Task OnClientConnected(ClientConnectedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clients.Add(e.ClientId);
                LogContent += $"客户端 {e.ClientId} 已连接\n";
            });
        }

        /*客户端断开*/
        private Task OnClientDisconnected(ClientDisconnectedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clients.Remove(e.ClientId);
                Log($"客户端 {e.ClientId} 已断开");
            });


            // 复制一份键的列表，以避免在迭代过程中修改字典
            var topics = _topicSubscribers.Keys.ToList();
            foreach (var topic in topics)
            {
                if (_topicSubscribers[topic].Contains(e.ClientId))
                {
                    _topicSubscribers[topic].Remove(e.ClientId);
                    if (_topicSubscribers[topic].Count == 0)
                    {
                        _topicSubscribers.Remove(topic);
                    }
                    // 调用 UpdateTopicList 方法更新主题列表
                    UpdateTopicList(topic, e.ClientId, isSubscribe: false);
                }
            }
            return Task.CompletedTask;
        }

        //用户订阅主题时，触发此事件,
        /*每当有用户订阅A主题，则推送客户端消息到A主题，用户名前缀为List*/
        //当前代码运行模组中不需要此代码
        //private async Task OnMessageReceived(ClientSubscribedTopicEventArgs e)
        //{
          
        //    var subscribers = string.Join(",", _topicSubscribers[e.TopicFilter.Topic]);
        //    var responseMessage = new MqttApplicationMessageBuilder()
        //       .WithTopic(e.TopicFilter.Topic)
        //       .WithPayload(subscribers)
        //       .WithUserProperty("Username", "List")
        //       .Build();
        //    _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(responseMessage));
        

        //}


        /*处理客户端订阅事件，更新主题列表并广播所有主题*/
        private async Task OnSubscriptionIntercepted(InterceptingSubscriptionEventArgs e)
        {
            await UpdateTopicList(e.TopicFilter.Topic, e.ClientId, isSubscribe: true);

            if (e.TopicFilter.Topic == "Base")
            {
                await BroadcastAllTopics();
            }

      

            //字典加入
            var topic = e.TopicFilter.Topic;
            if (!_topicSubscribers.ContainsKey(topic))
            {
                _topicSubscribers[topic] = new List<string>();
            }
            if (!_topicSubscribers[topic].Contains(e.ClientId))
            {
                _topicSubscribers[topic].Add(e.ClientId);
            }
        }


        /*处理客户端取消订阅事件，更新主题列表*/
        private async Task OnUnsubscriptionIntercepted(InterceptingUnsubscriptionEventArgs e)
        {
            await UpdateTopicList(e.Topic, e.ClientId, isSubscribe: false);

            //字典移出
            var topic = e.Topic;
            if (_topicSubscribers.ContainsKey(topic))
            {
                _topicSubscribers[topic].Remove(e.ClientId);
                //当订阅者为0，移出字典
                if (_topicSubscribers[topic].Count == 0)
                {
                    _topicSubscribers.Remove(topic);
                }
            }
        }

        /*更新主题的订阅者信息，并更新 TopicsList 集合*/
        private async Task UpdateTopicList(string topic, string clientId, bool isSubscribe)
        {
            var topicItem = _topics.FirstOrDefault(t => t.Topic == topic);

            if (isSubscribe)
            {
                if (topicItem == null)
                {
                    topicItem = new TopicItem(topic);
                    _topics.Add(topicItem);
                }
                topicItem.AddSubscriber(clientId);
            }
            else
            {
                topicItem?.RemoveSubscriber(clientId);
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                TopicsList.Clear();

                foreach (var item in _topics)
                {
                    TopicsList.Add($"{item.Topic}:{item.SubscriberCount}");
                    /*订阅者为0移出显示*/
                    if (item.SubscriberCount == 0)
                    {
                    TopicsList.Remove($"{item.Topic}:{item.SubscriberCount}");
                    }
                }
             
            });

            // 触发主题列表更新广播
            await BroadcastAllTopics();
        }


        /*记录消息内容和用户名*/
        private async Task OnMessageIntercepted(InterceptingPublishEventArgs arg)
        {
            var payload = Encoding.UTF8.GetString(arg.ApplicationMessage.Payload);
            string username = null;

            //禁止普通用户在base主题和response_subscribers主题下发布信息
            if (arg.ClientId != "" ) 
            {
                if (arg.ApplicationMessage.Topic == "response_subscribers" || arg.ApplicationMessage.Topic == "Base")
                {
                    string payl = arg.ApplicationMessage.ConvertPayloadToString();
                    if (_topicsList.Contains(payl) == false)
                    {
                        arg.ProcessPublish = false;
                        Log($"客户端{arg.ClientId}尝试发布到主题: {arg.ApplicationMessage.Topic}，已拦截");
                    }
                }
            }


            if (arg.ApplicationMessage.UserProperties != null)
            {
                username = arg.ApplicationMessage.UserProperties.FirstOrDefault(p => p.Name == "Username")?.Value;
            }

            if (arg.ApplicationMessage.Topic == "response_subscribers" && arg.ClientId == username)
            {
               await BroadcastTopicsClient(arg);
            }



            if (username != null)
            {
                Log($"收到消息 \n 主题：[{arg.ApplicationMessage.Topic}] 用户: {username} \n 消息内容: {payload}");
            }
            else
            {
                Log($"收到消息 \n  主题：[{arg.ApplicationMessage.Topic}] 用户: 未知 \n 消息内容: {payload}");
            }
            return ;
        }

        /*记录日志*/
        private void Log(string message)
        {
            string ServicelogEntry = $"{DateTime.Now:G} {message}\n";
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogContent += ServicelogEntry;
            });

            /*保存到本地*/
            try
            {

                using (StreamWriter writer = File.AppendText("../../../ServiceLog.txt"))
                {
                    writer.Write(ServicelogEntry);
                }
            }
            catch (Exception ex)
            {
                // 处理文件写入错误
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LogContent += $"{DateTime.Now:HH:mm:ss} 保存日志到文件时出错: {ex.Message}\n";
                    Message.PushMessage($"保存日志到文件时出错", MessageBoxImage.Error);
                });
            }
        }


        /*广播所有主题到 Base 主题*/
        private async Task BroadcastAllTopics()
        {
            var allTopics = string.Join(",", _topics.Select(t => t.Topic));
            var message = new MqttApplicationMessageBuilder()
               .WithTopic("Base")
               .WithPayload(Encoding.UTF8.GetBytes(allTopics))
               .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
               .WithUserProperty("Username", "Base")
               .WithRetainFlag(true)
               .Build();

            try
            {
                await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(message));
                Log($"已广播所有主题到 Base 主题: {allTopics}");
            }
            catch (Exception ex)
            {
                Log($"广播主题到 Base 主题时出错: {ex.Message}");
            }
        }

        /*广播所选中的主题下的客户端*/
        private async Task BroadcastTopicsClient(InterceptingPublishEventArgs arg)
        {
            ///服务端的列表请求
            Log($"客户端{arg.ClientId}发送{arg.ApplicationMessage.ConvertPayloadToString()} 列表请求");
            var subscribers = string.Join(",", _topicSubscribers[arg.ApplicationMessage.ConvertPayloadToString()]);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(arg.ApplicationMessage.Topic)
                .WithPayload(Encoding.UTF8.GetBytes(subscribers))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                .WithUserProperty("Username", arg.ClientId)
                .WithRetainFlag(true)
                .Build();

            try
            {
               await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(message));
                Log($"已发送至客户端{arg.ClientId}");
            }
            catch (Exception ex)
            {
                Log($"发送失败: {ex.Message}");
            }
        }


        //广播
        [RelayCommand]
        private async Task PublishAsync()
        {
            /*对话框在没被点击时即未被触发建立状态，此时pubmessage为null,被点击后即被建立，为""*/
            if ( PubMessage != null && PubMessage != "")
            {
                var message = new MqttApplicationMessageBuilder()
                   .WithTopic("Base")
                   .WithPayload(Encoding.UTF8.GetBytes(PubMessage))
                   .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.ExactlyOnce)
                   .WithUserProperty("Username", "Service")
                   .Build();
                try
                {
                    await _mqttServer.InjectApplicationMessage(new InjectedMqttApplicationMessage(message));
                    Log($"消息已发送:{PubMessage}");
                    PubMessage = ""; // 释放消息对象 
                }
                catch (Exception ex)
                {
                    Log($"发布失败: {ex.Message}");
                }
            }
            else if (PubMessage == "")
            {
                Log($"禁止发送空消息");
            }
            else
            {
                Log($"服务端未启动");
                PubMessage = ""; // 释放消息对象  
            }
        }
    }




    /*用于存储主题及其订阅者信息*/
    public record TopicItem(string Topic)
    {
        private readonly List<string> _subscribers = new();
        public event Action? SubscribersChanged;

        public int SubscriberCount => _subscribers.Count;

        private void NotifySubscribersChanged() => SubscribersChanged?.Invoke();

        public void AddSubscriber(string clientId)
        {
            if (!_subscribers.Contains(clientId))
            {
                _subscribers.Add(clientId);
                NotifySubscribersChanged();
            }
        }

        public void RemoveSubscriber(string clientId)
        {
            if (_subscribers.Remove(clientId))
            {
                NotifySubscribersChanged();
            }
        }
    }

    /*用于存储QoS2消息的状态*/
    public class QoSTwoMessageState
    {
        public MqttApplicationMessage Message { get; set; }
        public MessageProcessingStage Stage { get; set; } = MessageProcessingStage.Published;
    }

    /*消息处理阶段*/
    public enum MessageProcessingStage
    {
        Published,
        PubRecReceived,
        Completed
    }
}