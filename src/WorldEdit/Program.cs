﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MinecraftPluginServer;
using MinecraftPluginServer.Protocol;
using WorldEdit;
using WorldEdit.Input;
using WorldEdit.Output;
using WorldEdit.Schematic;

namespace WorldEdit
{
    internal class Program
    {
        private static readonly bool UseCodeConnection = false;
        private static string wsUrl;
        private static string restURL;
        private static bool keepRunning = true;
        //http://localhost:8080/fill?from=3%205%203&to=30 3f0 30&tileName=stone&tileData=0
        private static void Main()
        {
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                keepRunning = false;
            };
            if (UseCodeConnection)
            {
                CodeConnectionLoop();
            }
            else
            {
                WebsocketServerLoop();
            }
        }

        private static void WebsocketServerLoop()
        {
            using (var server = new PluginServer("ws://127.0.0.1:12112")) // will stop on disposal.
            {
                server.Start();
                var minecraftService = new MinecraftWebsocketCommandService(server);
                var cmdHandler = new CommandControl(minecraftService, new WebsocketCommandFormater());
                server.AddHandler(new WorldEditHandler(cmdHandler));
                server.AddHandler(new ConnectionHandler(minecraftService));
                using (var cancelationToken = minecraftService.Run())
                {
                    while (keepRunning)
                    {
                        Thread.Sleep(500);
                    }
                    minecraftService.Command(minecraftService.GetFormater().Title("", "WorldEdit Shutting Down"));
                    minecraftService.Wait();
                    minecraftService.ShutDown();
                    cancelationToken.Cancel();
                }
                server.Stop();
            }
        }

        private static void CodeConnectionLoop()
        {
            using (var codeConnectionProcess = Prerequisites())
            {
                var minecraftService = new MinecraftCodeConnectionCommandService();
                var cmdHandler = new CommandControl(minecraftService, new CodeConnectCommandFormater());
                using (var cancelationToken = minecraftService.Run())
                {
                    //check if connected, if not send connection command through AHK
                    var ahk = AutoHotKey.Run();
                    AutoHotKey.Callback = s =>
                    {
                        Console.WriteLine(s);
                        var args = s.Split(' ');
                        cmdHandler.HandleCommand(args);
                    };
                    Console.WriteLine(@"
Press Ctrl-C to shutdown.");

                    ahk.ExecRaw(@"
WinMinimize Code Connection for Minecraft
WinActivate Minecraft
send {esc}
sleep 500
send /
sleep 200
send connect " + wsUrl + "{enter}");

                    var command = minecraftService.GetFormater().Title("WorldEdit Started", "");
                    minecraftService.Command(command);


                    while (keepRunning)
                    {
                        Thread.Sleep(500);
                    }
                    minecraftService.Command(minecraftService.GetFormater().Title("", "WorldEdit Shutting Down"));

                    minecraftService.Wait();
                    minecraftService.ShutDown();
                    cancelationToken.Cancel();
                }
            }
        }

        private static IDisposable Prerequisites()
        {
            var processes = Process.GetProcesses();
            if (!processes.Any(a => a.ProcessName.ToLower().Contains("minecraft")))
            {
                Console.WriteLine("ERROR: Minecraft is not running");
            }
            if (!processes.Any(a => a.ProcessName.ToLower().Contains("code connection")))
            {
                //Console.WriteLine("ERROR: Code Connection is not running");
                var codeconnectionExe =
                    @"C:\Program Files (x86)\Minecraft Code Connection\Code Connection for Minecraft.exe";
                if (File.Exists(codeconnectionExe))
                {
                    var output = "";
                    var processStartInfo = new ProcessStartInfo(codeconnectionExe);

                    processStartInfo.RedirectStandardOutput = true;
                    processStartInfo.UseShellExecute = false;
                    processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    var process = Process.Start(processStartInfo);
                    process.OutputDataReceived += (s, e) =>
                    {
                        output = e.Data;

                        if (output.Contains("WS server"))
                        {
                            wsUrl = output.Replace("WS server listening at", "").Trim();
                        }
                        if (output.Contains("REST server"))
                        {
                            restURL = output.Replace("REST server listening at ", "").Trim();
                        }
                    };
                    process.BeginOutputReadLine();
                    while (string.IsNullOrEmpty(wsUrl) || string.IsNullOrEmpty(restURL))
                    {
                        Thread.Sleep(500);
                    }
                    process.CancelOutputRead();
                    Console.WriteLine("Started code connection: enter command in minecraft\n/connect " + wsUrl);
                    return new Disposable(process);
                }
                Console.WriteLine("Could not start Code Connection.");
                Console.WriteLine("Download and install from Microsoft at https://aka.ms/meeccwin10");
                Console.WriteLine("Then restart this program.");
                return null;
            }
            return null;
        }
    }

    class ConnectionHandler : IConnectionEventHander
    {
        private readonly MinecraftWebsocketCommandService _minecraftService;

        public ConnectionHandler(MinecraftWebsocketCommandService minecraftService)
        {
            _minecraftService = minecraftService;
        }

        public void OnConnection()
        {
            _minecraftService.Command(_minecraftService.GetFormater().Title("WorldEdit Started", ""));
            _minecraftService.Subscribe((new SubscribeMessage("PlayerMessage")).ToString());
            //_minecraftService.siub
            //wssv.Subscribe((new SubscribeMessage("PlayerMessage")).ToString());
            //_minecraftService.Command("");
        }
    }

    public class WebsocketCommandFormater : ICommandFormater
    {
        public string Fill(int startX, int startY, int startz, int endX, int endY, int endZ, string block = "stone",
            string data = "0")
        {
            return $"fill {startX} {startY} {startz} {endX} {endY} {endZ} {block} {data}";
        }

        public string Title(string title, string subtitle)
        {
            var command = "title @s ";
            if (!string.IsNullOrEmpty(title))
            {
                command = command + "title " + title;
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                command = command + "subtitle " + subtitle;
            }
            return command;
        }

    }


    public class MinecraftWebsocketCommandService : IMinecraftCommandService
    {
        private readonly PluginServer _server;

        public MinecraftWebsocketCommandService(PluginServer server)
        {
            _server = server;
        }


        public void Subscribe(string message)
        {
            _server.Subscribe(message);
        }
        public void Command(string command)
        {
            //_server.Send(command);
            Commands.Enqueue(command);
        }

        public void Status(string message)
        {
            //_server.Send("tell @s " + message);
            Statuses.Enqueue("tell @s " + message);
        }

        public Position GetLocation()
        {
            var result = _server.Send("testforblock ~ ~ ~ air");
            
            return new Position(result.body.position.x, result.body.position.y, result.body.position.z);
        }

        public void Wait()
        {
            while (!(Commands.IsEmpty && Statuses.IsEmpty))
            {
                Thread.Sleep(1000);
            }
            return;
        }

        private static bool pause=false;
        private const int SLEEP_WHEN_EMPTY = 5000;
        private const int SLEEP_WHEN_LOOPING = 100;
        private ConcurrentQueue<string> Commands { get; } = new ConcurrentQueue<string>();
        private ConcurrentQueue<string> Statuses { get; } = new ConcurrentQueue<string>();
        public int MessageCount { get; private set; }
        public static bool StopWhenEmpty { get; set; } = false;

        public CancellationTokenSource Run()
        {
            var tokenSource = new CancellationTokenSource()
                ;
            Task.Run(() =>
            {
                string message;
                    while (true)
                    {
                        if (!pause)
                        {
                            while (!Statuses.IsEmpty)
                            {
                                if (Statuses.TryDequeue(out message))
                                {
                                    _server.Send(message,"",false);
                                    MessageCount++;
                                }
                            }
                            if (!Commands.IsEmpty)
                            {
                                if (Commands.TryDequeue(out message))
                                {
                                    _server.Send(message,"",false);
                                    MessageCount++;
                                }
                            }
                            if (Statuses.IsEmpty && Commands.IsEmpty)
                            {
                                if (StopWhenEmpty)
                                {
                                    tokenSource.Cancel();
                                }
                                Thread.Sleep(SLEEP_WHEN_EMPTY);
                            }
                            else
                            {
                                Thread.Sleep(SLEEP_WHEN_LOOPING);
                            }
                        }
                        else
                        {
                            Thread.Sleep(SLEEP_WHEN_EMPTY);
                        }
                    }
            }, tokenSource.Token);

            return tokenSource;
        }

        public ICommandFormater GetFormater()
        {
            return new WebsocketCommandFormater();
        }

        public void ShutDown()
        {
            StopWhenEmpty = true;
        }

        public Action<string> MessageReceived = (a) => { };
    }

    public class Disposable : IDisposable
    {
        private readonly Process _process;

        public Disposable(Process process)
        {
            _process = process;
        }

        public void Dispose()
        {
            var pid = _process?.Id;
            if (pid != null)
            {
                Process.Start("taskkill.exe", "/PID " + pid);
            }

            _process?.CloseMainWindow();
        }
    }

}