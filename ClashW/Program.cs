﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClashW.View;
using ClashW.Config;
using ClashW.ProcessManager;
using System.Drawing;
using ClashW.Properties;
using ClashW.Config.Yaml;

namespace ClashW
{
    static class Program
    {
        static TrayMenu trayMenu;
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            ConfigController.EnsureRunningConfig();
            var clashProcessManager = ClashProcessManager.Instance;
            clashProcessManager.ProcessErrorEvnet += new ClashProcessManager.ProcessErrorHandler(clashProcessError);
            clashProcessManager.Start();
            ConfigController.Instance.Init(clashProcessManager);
            trayMenu = new TrayMenu();
            trayMenu.Show();
            Application.ApplicationExit += new EventHandler(application_exit);
            trayMenu.ShowMessage("Running", "ClashW已启动");
            Application.Run();
        }

        static void showMainForm(object sender, EventArgs e)
        {
            var mainForm = new MainForm();
            mainForm.Show();
            mainForm.Activate();
        }

        private static void application_exit(object sender, EventArgs e)
        {
            trayMenu.Close();
            ClashProcessManager.Instance.Kill();
        }

        private static void clashProcessError(ClashProcessManager clashProcessManager, string error)
        {
            if(trayMenu != null)
            {
                trayMenu.ShowErrorMessage("ERROR", error);
            }
        }
    }
}
