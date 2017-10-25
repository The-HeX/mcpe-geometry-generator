﻿using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WorldEdit.Schematic;

namespace WorldEdit.Output
{
    public class MinecraftCommandService
    {
        private static bool pause;
        private const int SLEEP_WHEN_EMPTY = 5000;
        private const int SLEEP_WHEN_LOOPING = 100;
        private static ConcurrentQueue<string> Commands { get; } = new ConcurrentQueue<string>();
        private static ConcurrentQueue<string> Statuses { get; } = new ConcurrentQueue<string>();
        public static int MessageCount { get; private set; }

        public static bool StopWhenEmpty { get; set; } = false;

        public void Command(string command)
        {
            Commands.Enqueue(command);
        }

        public void Status(string message)
        {
            Statuses.Enqueue(message);
        }

        public static void ShutDown()
        {
            StopWhenEmpty = true;
        }
        public static Task Run()
        {
            Task output = new Task(() =>
            {
                string message;
                using (var httpclient = new HttpClient())
                {
                    while (true)
                    {
                        if (!pause)
                        {
                            while (!Statuses.IsEmpty)
                            {
                                if (Statuses.TryDequeue(out message))
                                {
                                    var result =

                                        httpclient.GetStringAsync(
                                            $"http://localhost:8080/executeasother?origin=@p&position=~%20~%20~&command=tell%20@s%20" +
                                            message);
                                    MessageCount++;
                                    Console.WriteLine(result.Result);
                                }
                            }
                            if (!Commands.IsEmpty)
                            {
                                if (Commands.TryDequeue(out message))
                                {
                                    var result = httpclient.GetStringAsync($"http://localhost:8080/" + message);
                                    MessageCount++;
                                    Console.WriteLine(result.Result);
                                }
                            }
                            if (Statuses.IsEmpty && Commands.IsEmpty)
                            {
                                if (StopWhenEmpty)
                                {
                                    return;
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
                }
            });
            ;
            return output;
        }

        public Position GetLocation()
        {
            CCPosition location;
            pause = true;
            using (var httpclient = new HttpClient())
            {
                var stringResult =
                    httpclient.GetStringAsync($"http://localhost:8080/testforblock?position=~ ~ ~&tileName=air").Result;
                var data = JsonConvert.DeserializeObject<TestForBlock>(stringResult);
                location = data.position;

            }
            pause = false;
                        
            return new Position(location.x,location.y,location.z);
        }

        public void Wait()
        {
            while (!(Commands.IsEmpty && Statuses.IsEmpty))
            {
                Thread.Sleep(1000);
            }
            return;
        }
    }
    public class TestForBlock
    {
        public bool matches { get; set; }
        public CCPosition position { get; set; }
    }

    public class CCPosition
    {
        public int x { get; set; }
        public int y { get; set; }
        public int z { get; set; }
        public override string ToString()
        {
            return $"X: {x} Y:{y} Z:{z}";
        }
    }

}