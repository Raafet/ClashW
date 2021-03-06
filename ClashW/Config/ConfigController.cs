﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClashW.Config.Api;
using ClashW.Config.Yaml;
using ClashW.Config.Yaml.Dao;
using ClashW.ProcessManager;
using ClashW.Utils;
using ClashW.View;
using Newtonsoft.Json;
using static ClashW.Config.Api.ClashApi;

namespace ClashW.Config
{
    public enum RunningMode { DIRECT, GLOBAL, RULE };
    public enum LogLevel { INFO, WARNING, ERROR, DEBUG};
    public sealed class ConfigController
    {
        private static ConfigController controller;
        private static object _lock = new object();
       

        #region clash 运行模式定义
        private const string RUNNING_MODE_RULE = "Rule";
        private const string RUNNING_MODE_DIRECT = "Direct";
        private const string RUNNING_MODE_GLOBAL = "Global";
        #endregion

        public const string LOG_LEVEL_INFO = "info";
        public const string LOG_LEVEL_WARING = "warning";
        public const string LOG_LEVEL_ERROR = "error";
        public const string LOG_LEVEL_DEBUG = "debug";

        private YamlConfig yamlConfig;
        private ClashProcessManager clashProcessManager;

        public delegate void TrafficChangedHandler(ConfigController configController);
        public event TrafficChangedHandler TrafficChangedEvent;

        public delegate void ProxyListChangedHandler(ConfigController configController, List<Proxy> proxyList);
        public event ProxyListChangedHandler ProxyChangedEvent;

        public delegate void RunningModeChangedHandler(ConfigController configController, RunningMode runningMode);
        public event RunningModeChangedHandler RunningModeChangedEvent;

        public delegate void SystemProxyChangedHandler(ConfigController configController, string proxyHost, bool enable);
        public event SystemProxyChangedHandler SystemProxyChangedEvent;

        public delegate void SelectedProxyChangedHandler(ConfigController configController, Proxy proxy);
        public event SelectedProxyChangedHandler SelectedProxyChangedEvent;

        private ClashApi clashApi;

        private const string DEFAULT_PROXY_HOST = "127.0.0.1:7891";
        public static ConfigController Instance
        {
            get
            {
                if(controller == null)
                {
                    lock(_lock) {
                        if(controller == null)
                        {
                            controller = new ConfigController();
                        }
                    }
                }
                return controller;
            }
        }
        private ConfigController()
        {

        }

        public void Init(ClashProcessManager clashProcessManager)
        {
            this.clashProcessManager = clashProcessManager;
            if (yamlConfig == null)
            {
                yamlConfig = YalmConfigManager.Instance.GetYamlConfig();
            }
            initClashWConfig();
        }

        public void RefreshYamlConfig()
        {
            yamlConfig = YalmConfigManager.Instance.GetYamlConfig();
            clashApi.StopLoadLogMessage();
            clashApi = new ClashApi($"http://{yamlConfig.ExternalController}");
        }

        private void initClashWConfig()
        {
            clashApi = new ClashApi($"http://{yamlConfig.ExternalController}");
            if (!String.IsNullOrEmpty(Properties.Settings.Default.SelectedServerName))
            {
                foreach (Proxy proxy in yamlConfig.ProxyList)
                {
                    if (Properties.Settings.Default.SelectedServerName.Equals(proxy.Name))
                    {
                        SelecteProxy(proxy);
                    }
                }
            }
            EnableSystemProxy(Properties.Settings.Default.EnableSystemProxy);
        }

        public List<Proxy> AddProxy(Proxy proxy)
        {
            yamlConfig.ProxyList.Add(proxy);
            reCreateProxyGroups();
            saveYamlConfigFile();
            List<Proxy> newProxyList = new List<Proxy>(yamlConfig.ProxyList);
            ProxyChangedEvent?.Invoke(Instance, newProxyList);
            return newProxyList;
        }

        public List<Proxy> RemoveProxy(Proxy proxy)
        {
            yamlConfig.ProxyList.Remove(proxy);
            saveYamlConfigFile();
            List<Proxy> newProxyList = new List<Proxy>(yamlConfig.ProxyList);
            ProxyChangedEvent?.Invoke(Instance, newProxyList);
            return new List<Proxy>(yamlConfig.ProxyList);
        }

        public List<Proxy> GetProxyList()
        {
            if (yamlConfig.ProxyList == null)
            {
                yamlConfig.ProxyList = new List<Proxy>();
            }
            return new List<Proxy>(yamlConfig.ProxyList);
        }

        public List<string> GetSeletableProxyName()
        {
            if (yamlConfig.ProxyGroups != null)
            {
                foreach(var proxyGroup in yamlConfig.ProxyGroups)
                {
                    if (proxyGroup.Type.Equals("select"))
                    {
                        return new List<string>(proxyGroup.Proxies);
                    }
                }
            }
            return null;
        }

        public void SelecteProxy(Proxy proxy)
        {
            clashApi.SelectedProxy(proxy.Name);
            Properties.Settings.Default.SelectedServerName = proxy.Name;
            Properties.Settings.Default.Save();
        }

        public void SelecteProxyByName(string name)
        {
            clashApi.SelectedProxy(name);
            SelectedProxyChangedEvent?.Invoke(Instance, GetSelectedProxy());
            Properties.Settings.Default.SelectedServerName = name;
            Properties.Settings.Default.Save();
        }

        public Proxy GetSelectedProxy()
        {
            var content = clashApi.ProxyInfo("Proxy");
            Dictionary<string, object> valuse = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
            if(valuse.ContainsKey("now"))
            {
                var proxyName = valuse["now"];
                foreach(Proxy proxy in yamlConfig.ProxyList)
                {
                    if (proxyName.Equals(proxy.Name))
                    {
                        return proxy;
                    }
                }
            }
            return null;
        }

        public void EnableSystemProxy(bool enable)
        {
            var proxyhost = yamlConfig.Port == 0 ? DEFAULT_PROXY_HOST : $"127.0.0.1:{yamlConfig.Port}";
            var systemProxyEnable = ProxyUtils.ProxyEnabled(proxyhost);
            if (enable && !systemProxyEnable)
            {
                ProxyUtils.SetProxy(proxyhost, true);
                SystemProxyChangedEvent?.Invoke(Instance, proxyhost, enable);
            }
            
            if(!enable && systemProxyEnable)
            {
                ProxyUtils.SetProxy("", false);
                SystemProxyChangedEvent?.Invoke(Instance, proxyhost, enable);
            }
        }

        public bool CheckSystemProxyEnable()
        {
            var proxyhost = yamlConfig.Port == 0 ? DEFAULT_PROXY_HOST : $"127.0.0.1:{yamlConfig.Port}";
            return ProxyUtils.ProxyEnabled(proxyhost);
        }

        public void EnableStartup(bool enable)
        {

        }

        public void SwitchRunningMode(RunningMode mode)
        {
            if(GetRunningMode() == mode)
            {
                return;
            }

            switch (mode)
            {
                case RunningMode.DIRECT:
                    yamlConfig.Mode = RUNNING_MODE_DIRECT;
                    break;
                case RunningMode.GLOBAL:
                    yamlConfig.Mode = RUNNING_MODE_GLOBAL;
                    break;
                case RunningMode.RULE:
                    yamlConfig.Mode = RUNNING_MODE_RULE;
                    break;
                default:
                    break;
            }
            saveYamlConfigFile();
            RunningModeChangedEvent?.Invoke(Instance, GetRunningMode());
        }

        public RunningMode GetRunningMode()
        {
            RunningMode runningMode = RunningMode.DIRECT;
            switch (yamlConfig.Mode)
            {
                case RUNNING_MODE_RULE:
                    runningMode = RunningMode.RULE;
                    break;
                case RUNNING_MODE_DIRECT:
                    runningMode = RunningMode.DIRECT;
                    break;
                case RUNNING_MODE_GLOBAL:
                    runningMode = RunningMode.GLOBAL;
                    break;
                default:
                    break;
            }
            return runningMode;
        }

        public void StartLoadMessage(LogMessageHandler logMessageHandler)
        {
            clashApi.LogMessageOutputEvent += logMessageHandler;
            clashApi.LoadLogMessage();
        }

        public void StopLoadMessage(LogMessageHandler logMessageHandler)
        {
            clashApi.LogMessageOutputEvent -= logMessageHandler;
            clashApi.StopLoadLogMessage();
        }

        public int GetListenedSocksPort()
        {
            return yamlConfig.SocksPort;
        }

        public int GetListenedHttpProt()
        {
            return yamlConfig.Port;
        }

        public bool IsAllowLan()
        {
            return yamlConfig.AllowLan;
        }

        public string GetExternalController()
        {
            return yamlConfig.ExternalController;
        }

        public string GetExternalControllerSecret()
        {
            return yamlConfig.Secret;
        }

        public LogLevel GetLogLevel()
        {
            LogLevel logLevel;
            switch(yamlConfig.LogLevel)
            {
                case LOG_LEVEL_INFO:
                    logLevel = LogLevel.INFO;
                    break;
                case LOG_LEVEL_WARING:
                    logLevel = LogLevel.WARNING;
                    break;
                case LOG_LEVEL_ERROR:
                    logLevel = LogLevel.ERROR;
                    break;
                case LOG_LEVEL_DEBUG:
                    logLevel = LogLevel.DEBUG;
                    break;
                default:
                    logLevel = LogLevel.INFO;
                    break;
            }
            return logLevel;
        }

        public static ConfigEditor GetConfigEditor()
        {
            return new ConfigEditor();
        }
         
        private void reCreateProxyGroups()
        {
            if (yamlConfig.ProxyGroups == null)
            {
                yamlConfig.ProxyGroups = new List<ProxyGroup>();
            }
            else
            {
                yamlConfig.ProxyGroups.Clear();
            }

            var selectProxyGroup = new ProxyGroup();
            selectProxyGroup.Name = "Proxy";
            selectProxyGroup.Type = "select";
            selectProxyGroup.Proxies = new List<string>();
            var autoProxyGroup = new ProxyGroup();
            autoProxyGroup.Name = "Auto";
            autoProxyGroup.Type = "url-test";
            autoProxyGroup.Url = "https://www.bing.com";
            autoProxyGroup.Interval = 500;
            autoProxyGroup.Proxies = new List<string>();
            var fallbackAutoGroup = new ProxyGroup();
            fallbackAutoGroup.Name = "FallbackAuto";
            fallbackAutoGroup.Type = "fallback";
            fallbackAutoGroup.Url = "https://www.bing.com";
            fallbackAutoGroup.Interval = 500;
            fallbackAutoGroup.Proxies = new List<string>();

            foreach (Proxy proxy in yamlConfig.ProxyList)
            {
                var proxyName = proxy.Name;
                selectProxyGroup.Proxies.Add(proxyName);
                autoProxyGroup.Proxies.Add(proxyName);
                fallbackAutoGroup.Proxies.Add(proxyName);
            }
            selectProxyGroup.Proxies.Add("Auto");
            selectProxyGroup.Proxies.Add("FallbackAuto");
            yamlConfig.ProxyGroups.Add(autoProxyGroup);
            yamlConfig.ProxyGroups.Add(fallbackAutoGroup);
            yamlConfig.ProxyGroups.Add(selectProxyGroup);
        }

        private void saveYamlConfigFile()
        {
            YalmConfigManager.Instance.SaveYamlConfigFile(yamlConfig);
            clashProcessManager.Restart();
        }

        public static void EnsureRunningConfig()
        {
            YalmConfigManager.Instance.EnsureYamlConfig();
        }
    }
}
