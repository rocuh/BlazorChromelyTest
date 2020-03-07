using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Chromely.CefGlue.Browser.EventParams;
using System.Diagnostics;
using Chromely.Core;
using Chromely.Core.Configuration;
using Chromely;
using System.Threading;
using System.Net.NetworkInformation;
using System.Net;
using System.IO.MemoryMappedFiles;

namespace BlazorChromelyTest
{
    public class Program
    {
        private const int StartScan = 5050;
        private const int EndScan = 6000;

        private static bool IsPortAvailable(int port)
        {
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();

            foreach (IPEndPoint endpoint in tcpConnInfoArray)
            {
                if (endpoint.Port == port)
                {
                    return false;
                }
            }

            return true;
        }


        public static void Main(string[] args)
        {
            bool firstProcess = false;

            // chromely starts multiple child processes
            // we only want to start the asp core on the first process
            // 
            // ideally it would be nice if chromely allowed things to be passed to 
            // the child processes through args

            Mutex mutex = null;
            try
            {
                // if this succeeds we are not the first process
                mutex = Mutex.OpenExisting("BlazorTestMutex");
            }
            catch
            {
                // must be first process 
                mutex = new Mutex(false, "BlazorTestMutex");
                firstProcess = true;
            }

            int port = -1;

            if (firstProcess)
            {
                // try to find first available local port to host blazor on
                for (int i = StartScan; i < EndScan; i++)
                {
                    if (IsPortAvailable(i))
                    {
                        port = i;
                        break;
                    }
                }

                if (port != -1)
                {
                    // start the kestrel server in a background thread
                    var blazorTask = new Task(() => CreateHostBuilder(args, port).Build().Run(), TaskCreationOptions.LongRunning);
                    blazorTask.Start();

                    // wait till its up
                    while (IsPortAvailable(port))
                    {
                        Thread.Sleep(1);
                    }
                }

                // used to pass the port number to chromely child processes
                MemoryMappedFile mmf = MemoryMappedFile.CreateNew("BlazorTestMap", 4);
                MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor();
                accessor.Write(0, (int)port);
            }
            else
            {
                // fetch port number
                MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen("BlazorTestMap", 4);
                MemoryMappedViewAccessor accessor = mmf.CreateViewAccessor();
                port = accessor.ReadInt32(0);
            }

            if (port != -1)
            {
                // start up chromely
                var core = typeof(IChromelyConfiguration).Assembly;
                var config = DefaultConfiguration.CreateForRuntimePlatform();
                config.CefDownloadOptions = new CefDownloadOptions(true, true);
                config.WindowOptions.Position = new WindowPosition(1, 2);
                config.WindowOptions.Size = new WindowSize(1000, 600);
                config.StartUrl = $"https://127.0.0.1:{port}";
                config.DebuggingMode = true;
                config.WindowOptions.RelativePathToIconFile = "chromely.ico";

                try
                {
                    var builder = AppBuilder.Create();
                    builder = builder.UseApp<TestApp>();
                    builder = builder.UseConfiguration<DefaultConfiguration>(config);
                    builder = builder.Build();
                    builder.Run(args);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    throw;
                }
            }

            mutex.ReleaseMutex();
        }

        internal static void OnBeforeClose(object sender, BeforeCloseEventArgs e)
        {
        }

        internal static void OnFrameLoaded(object sender, FrameLoadEndEventArgs e)
        {
        }

        internal static void OnConsoleMessage(object sender, ConsoleMessageEventArgs e)
        {
        }

        internal static void OnFrameLoadStart(object sender, FrameLoadStartEventArgs eventArgs)
        {
        }

        public static IHostBuilder CreateHostBuilder(string[] args, int port) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                    .UseStartup<Startup>()
                    .UseUrls(new[] { $"https://127.0.0.1:{port}" });
                });
    }

    public class TestApp : ChromelyEventedApp
    {
        protected override void OnFrameLoadStart(object sender, FrameLoadStartEventArgs eventArgs)
        {
            Program.OnFrameLoadStart(sender, eventArgs);
        }

        protected override void OnFrameLoadEnd(object sender, FrameLoadEndEventArgs eventArgs)
        {
            Program.OnFrameLoaded(sender, eventArgs);
        }

        protected override void OnConsoleMessage(object sender, ConsoleMessageEventArgs eventArgs)
        {
            Program.OnConsoleMessage(sender, eventArgs);
        }

        protected override void OnBeforeClose(object sender, BeforeCloseEventArgs eventArgs)
        {
            Program.OnBeforeClose(sender, eventArgs);
        }
    }
}
