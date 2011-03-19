using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MCA
{
    class MinecraftServerOutputMonitor
    {
        private Process serverProcess;

        public bool Running { get; private set; }

        public event Action<string> LineOutput;
        public event Action<string> PlayerJoined;
        public event Action<string> PlayerLeft;
        public event Action<string, string> PlayerSaid;
        public event Action<string, string> PlayerCommand;
        public event Action ServerStartupDone;

        public MinecraftServerOutputMonitor(Process process)
        {
            serverProcess = process;
        }

        public void Start()
        {
            Running = true;

            Task.Factory.StartNew(() =>
            {
                while (Running)
                {
                    string line = serverProcess.StandardError.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        OnLineOutput(line);

                        string[] split = line.Split(' ');

                        if (split.Length >= 6 && split[5] == "logged")
                        {
                            OnPlayerJoined(split[3]);
                        }
                        else if (split.Length >= 5 && split[4] == "lost")
                        {
                            OnPlayerLeft(split[3]);
                        }
                        else if (split.Length >= 5 && split[3].StartsWith("<"))
                        {
                            string player = split[3].Substring(1, split[3].Length - 2);
                            string message = line.Substring(line.IndexOf(split[4]), line.Length - line.IndexOf(split[4]));

                            OnPlayerSaid(player, message);
                        }
                        else if (split.Length >= 8 && split[4] == "issued")
                        {
                            string player = split[3];
                            string command = string.Join(" ", split, 7, split.Length - 7);

                            OnPlayerCommand(player, command);
                        }
                        else if (split.Length >= 4 && split[3] == "Done")
                        {
                            OnServerStartupDone();
                        }
                    }
                }
            });
        }

        public void Stop()
        {
            Running = false;
        }

        protected virtual void OnLineOutput(string line)
        {
            if (LineOutput != null)
            {
                LineOutput(line);
            }
        }

        protected virtual void OnPlayerJoined(string player)
        {
            if (PlayerJoined != null)
            {
                PlayerJoined(player);
            }
        }

        protected virtual void OnPlayerLeft(string player)
        {
            if (PlayerLeft != null)
            {
                PlayerLeft(player);
            }
        }

        protected virtual void OnPlayerSaid(string player, string message)
        {
            if (PlayerSaid != null)
            {
                PlayerSaid(player, message);
            }
        }

        protected virtual void OnPlayerCommand(string player, string command)
        {
            if (PlayerCommand != null)
            {
                PlayerCommand(player, command);
            }
        }

        protected virtual void OnServerStartupDone()
        {
            if (ServerStartupDone != null)
            {
                ServerStartupDone();
            }
        }
    }
}
