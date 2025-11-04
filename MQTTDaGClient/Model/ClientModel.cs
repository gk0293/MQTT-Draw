using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MqttDaGClient.Model
{
    /// <summary>
    /// mqtt客户端模型
    /// </summary>
    public partial class MqttClientModel : ObservableObject
    {
        /// <summary>
        /// ip地址
        /// </summary>
        /// 
        [ObservableProperty]
        private string serverIP;
        /// <summary>
        /// 端口
        /// </summary>
        /// 
        [ObservableProperty]
        private string serverPort;
        /// <summary>
        /// 帐号
        /// </summary>
        [ObservableProperty]
        private string serverName;
        /// <summary>
        /// 密码
        /// </summary>
        [ObservableProperty]
        private string serverPwd;
        /// <summary>
        /// 客户端Id
        /// </summary>
        [ObservableProperty]
        private string clientId;

        public MqttClientModel(string ip, string port, string name, string pass, string clientid)
        {
            this.ServerIP = ip;
            this.ServerPort = port;
            this.ServerName = name;
            this.ServerPwd = pass;
            this.ClientId = clientid;
        }
    }
}
