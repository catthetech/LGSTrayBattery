﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace LGSTrayGUI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public async void App_Startup(object sender, StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CrashHandler);
            if (dir != null)
            {
                Directory.SetCurrentDirectory(dir);
            }

            using IHost host = Host.CreateDefaultBuilder(e.Args).ConfigureAppConfiguration((hostingContext, configuration) =>
            {
                configuration.Sources.Clear();

                IHostEnvironment env = hostingContext.HostingEnvironment;

                configuration.AddIniFile("appsettings.ini", optional: true, reloadOnChange: true);

                AppSettings.Settings = configuration.Build().Get<AppSettings.AppSettingsInstace>();
            }).Build();

            MainWindow mw = new();
            mw.ShowActivated = false;
            mw.Show();

            await host.RunAsync();
        }

        private void CrashHandler(object sender, UnhandledExceptionEventArgs args)
        {
            Exception e = (Exception)args.ExceptionObject;
            long unixTime = DateTimeOffset.Now.ToUnixTimeSeconds();

            string dir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            using (StreamWriter writer = new StreamWriter(dir + $"/crashlog_{unixTime}.log", false))
            {
                writer.WriteLine(e.ToString());

                Console.WriteLine(e.ToString());
            }
        }
    }
}
