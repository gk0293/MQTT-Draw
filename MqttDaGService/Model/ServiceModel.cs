using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MqttDaGService.ViewModel;

namespace MqttDaGService.Model
{
    /// <summary>
    /// 服务器实体模型
    /// </summary>
    public partial class ServiceModel : ObservableObject
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
        /// 
        [ObservableProperty]
        private string serverName;
        /// <summary>
        /// 密码
        /// </summary>
        /// 
        [ObservableProperty]
        private string serverPwd;

        public ServiceModel(string ip, string port, string name, string pass)
        {
            this.ServerIP = ip;
            this.ServerPort = port;
            this.ServerName = name;
            this.ServerPwd = pass;
        }
    }
}
