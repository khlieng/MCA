using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WF = System.Windows.Forms;

namespace MCA
{
    class Program
    {
        struct Item
        {
            public int ID;
            public string Name;

            public Item(int id, string name)
                : this()
            {
                ID = id;
                Name = name;
            }
        }

        enum VersionStatus { New, None, Unable }

        static Process p;
        static bool running;
        static DateTime startTime;
        static List<string> players;
        static Dictionary<string, string> previousCommand = new Dictionary<string, string>();
        static List<string> ops;
        static List<Item> items;
        static PlaytimeManager playtime;
        static BindManager binds;

        static bool externalUpdating;

        static byte[] downloadBuffer;
        static object downloadBufferLock = new object();

        static MCAServer mcaServer;
        static MinecraftServerOutputMonitor minecraftServerOutput;

        static Queue<Action> changes;

        public const string SETTINGS_FILENAME = "Settings.cfg";
        public const string MINECRAFT_SETTINGS_FILENAME = "server.PROPERTIES";

        public static dynamic Settings { get; private set; }
        public static dynamic MinecraftSettings { get; private set; }

        static Timer backupTimer;

        //static WF.NotifyIcon notifyIcon;
        
        static void Main(string[] args)
        {
            //Task.Factory.StartNew(() =>
            //    {
            //        notifyIcon = new WF.NotifyIcon();
            //        notifyIcon.Icon = new System.Drawing.Icon("Game.ico");
            //        notifyIcon.Visible = true;

            //        WF.Application.Run();
            //    });
            
            PreviousProcCheck();
            MinecraftServerCheck();
            LoadSettings();
            InitMCAServer();
            InputLoop();
            Cleanup();
        }

        static void PreviousProcCheck()
        {
            if (File.Exists("pid.bin"))
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead("pid.bin")))
                {
                    int id = br.ReadInt32();
                    DateTime start = DateTime.FromBinary(br.ReadInt64());

                    try
                    {
                        Process proc = Process.GetProcessById(id);
                        if (proc.StartTime == start)
                        {
                            Console.Write("Previously started minecraft server process (PID: {0}) found. Kill it? Y/N: ", id);
                            if (string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase))
                            {
                                proc.Kill();
                                Console.WriteLine("Process killed");
                            }
                        }
                    }
                    catch { }
                }

                File.Delete("pid.bin");
            }
        }

        static void MinecraftServerCheck()
        {
            if (File.Exists("minecraft_server.jar"))
            {
                Task.Factory.StartNew(() =>
                {
                    if (NewVersion() == VersionStatus.New)
                    {
                        Console.WriteLine("\nNew minecraft server version available! Use \"install\" to install it\n");
                    }
                });
            }
            else
            {
                GrabMinecraftServer();
            }
        }

        static void LoadSettings()
        {
            if (File.Exists(SETTINGS_FILENAME))
            {
                Settings = new Config(SETTINGS_FILENAME);
                Tools.Print("MCA settings loaded");
            }
            else
            {
                SetDefaultSettings();
                Tools.Print("No MCA settings file found, using defaults");
            }

            LoadMinecraftSettings();
        }

        static void LoadMinecraftSettings()
        {
            if (File.Exists(MINECRAFT_SETTINGS_FILENAME))
            {
                MinecraftSettings = new Config(MINECRAFT_SETTINGS_FILENAME);
                Tools.Print("Minecraft server settings loaded");
            }
            else
            {
                Tools.Print("No Minecraft server settings file found");
            }
        }

        static void SetDefaultSettings()
        {
            Settings = new Config("Settings.cfg");
            Settings.MCAIP = IPAddress.Any.ToString();
            Settings.MCAPort = 25566;
            Settings.BackupInterval = 0;
            Settings.BackupDir = string.Empty;
        }

        static void InitMCAServer()
        {
            mcaServer = new MCAServer(IPAddress.Parse(Settings.MCAIP), Settings.MCAPort);
            mcaServer.ClientConnected += (client) => mcaServer.SendMessage(CreateWelcomeMessage(), client);
            mcaServer.ClientCommand += (cmd, client) => ExecuteCommand(cmd, client);
        }

        static void InputLoop()
        {
            string input = string.Empty;
            do
            {
                input = Console.ReadLine();
                ExecuteCommand(input, null);
            }
            while (input != "close");
        }

        static void Cleanup()
        {
            if (playtime != null)
            {
                playtime.LogoutAll();
                playtime.Save();
            }
            
            mcaServer.Stop();

            if (File.Exists("pid.bin"))
            {
                File.Delete("pid.bin");
            }
        }

        static void LoadItems()
        {
            if (File.Exists("ids.txt"))
            {
                Tools.Print("Loading items from ids.txt");
                items = new List<Item>();
                using (StreamReader r = new StreamReader(File.OpenRead("ids.txt")))
                {
                    while (!r.EndOfStream)
                    {
                        string[] split = r.ReadLine().Split(':');
                        items.Add(new Item(int.Parse(split[0]), split[1]));
                    }
                }
            }
            else
            {
                Tools.Print("No itemlist found, starting scrape");
                Tools.NewScrapeIDs("ids.txt");
                LoadItems();
            }
        }

        static IEnumerable<Item> FindItems(string name)
        {
            return from item in items
                   where item.Name.ToLower().Contains(name.ToLower())
                   select item;
        }

        static void RunServer()
        {
            p = new Process();

            p.StartInfo = new ProcessStartInfo("java", "-Xmx1024M -Xms1024M -jar minecraft_server.jar nogui")
            {
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            players = new List<string>();
            playtime = new PlaytimeManager("played.bin");
            binds = new BindManager("binds.bin");

            changes = new Queue<Action>();
            
            InitServerOutput();
            p.Start();
            minecraftServerOutput.Start();

            startTime = DateTime.Now;

            using (BinaryWriter bw = new BinaryWriter(File.Create("pid.bin")))
            {
                bw.Write(p.Id);
                bw.Write(p.StartTime.ToBinary());                
            }            

            running = true;
        }

        static void InitServerOutput()
        {
            minecraftServerOutput = new MinecraftServerOutputMonitor(p);
            minecraftServerOutput.LineOutput += (line) =>
                {
                    Console.WriteLine(line);
                    mcaServer.BroadcastMessage(line);
                };
            minecraftServerOutput.PlayerJoined += (player) =>
                {
                    //notifyIcon.ShowBalloonTip(1000, "Player connected!", player + " has connected.", WF.ToolTipIcon.None);

                    players.Add(player);
                    playtime.PlayerLogin(player);
                    Say(player + " has joined the server!");
                    Tell(player, "Use /commands for a list of new/enhanced commands");
                };
            minecraftServerOutput.PlayerLeft += (player) =>
                {
                    if (previousCommand.ContainsKey(player))
                    {
                        previousCommand.Remove(player);
                    }
                    players.Remove(player);
                    playtime.PlayerLogout(player);
                    playtime.Save();                    
                };
            minecraftServerOutput.PlayerSaid += (player, msg) =>
                {
                    //if (msg.StartsWith("."))
                    //{
                    //    string cmd = msg.Substring(1, msg.Length - 1);
                    //    ExecuteIngameCommand(cmd, player);

                    //    if (cmd != "r")
                    //    {
                    //        if (previousCommand.ContainsKey(player))
                    //        {
                    //            previousCommand[player] = cmd;
                    //        }
                    //        else
                    //        {
                    //            previousCommand.Add(player, cmd);
                    //        }
                    //    }
                    //}
                };
            minecraftServerOutput.PlayerCommand += (player, cmd) =>
                {
                    ExecuteIngameCommand(cmd, player);

                    if (cmd != "r")
                    {
                        if (previousCommand.ContainsKey(player))
                        {
                            previousCommand[player] = cmd;
                        }
                        else
                        {
                            previousCommand.Add(player, cmd);
                        }
                    }
                };
            minecraftServerOutput.ServerStartupDone += () =>
                {
                    LoadMinecraftSettings();
                    LoadOps();
                    LoadItems();

                    BackupWorld();
                    if (Settings.BackupInterval != 0 && !string.IsNullOrWhiteSpace(Settings.BackupDir))
                    {
                        backupTimer = new Timer(new TimerCallback((o) => BackupWorld()), null,
                            TimeSpan.FromMinutes(Settings.BackupInterval), 
                            TimeSpan.FromMinutes(Settings.BackupInterval));
                    }
                };
        }

        static void StopServer()
        {
            if (running)
            {
                playtime.LogoutAll();
                playtime.Save();

                p.StandardInput.WriteLine("stop");

                System.Threading.Thread.Sleep(1000);
                Console.WriteLine("Terminating server output");
                minecraftServerOutput.Stop();

                BackupWorld();
                if (backupTimer != null)
                {
                    backupTimer.Dispose();
                    backupTimer = null;
                }

                PerformChanges();                

                Console.WriteLine("Shutdown done!");

                running = false;
            }
        }

        static void RestartServer()
        {
            StopServer();            
            RunServer();
        }

        static void PerformChanges()
        {
            if (changes.Count > 0)
            {
                Console.WriteLine("Writing config changes");
            }

            for (int i = 0; i < changes.Count; i++)
            {
                Action change = changes.Dequeue();

                bool success = false;
                while (!success)
                {
                    try
                    {
                        change();
                        success = true;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                        Console.WriteLine("Retrying in 2 sec...");
                        System.Threading.Thread.Sleep(2000);
                    }
                }
            }
        }

        static void LoadOps()
        {
            if (File.Exists("ops.txt"))
            {
                Tools.Print("Loading ops");
                
                ops = new List<string>();
                using (StreamReader reader = new StreamReader(File.OpenRead("ops.txt")))
                {
                    while (!reader.EndOfStream)
                    {
                        ops.Add(reader.ReadLine());
                    }
                }
            }
        }

        static IEnumerable<string> GetLevels()
        {
            string currentDir = Directory.GetCurrentDirectory();

            foreach (var dir in Directory.EnumerateDirectories(currentDir, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(dir + "\\level.dat"))
                {
                    yield return dir.Substring(currentDir.Length + 1, dir.Length - currentDir.Length - 1).Replace("\\", "/");
                }
            }
        }

        static string CreateWelcomeMessage()
        {
            string message = "Welcome to MCA!";
            message += Environment.NewLine;
            message += CreateStatusMessage();

            return message;
        }

        static string CreateStatusMessage()
        {
            string message = string.Empty;

            if (running)
            {
                message += "Minecraft server running";
                message += Environment.NewLine + "\t";
                message += "Players: " + players.Count;
                if (MinecraftSettings != null)
                {
                    message += "/" + MinecraftSettings["max-players"];
                }
                message += Environment.NewLine + "\t";
                message += Uptime();
            }
            else
            {
                message += "There is currently no minecraft server running";
            }

            return message;
        }

        static string Uptime()
        {
            TimeSpan uptime = DateTime.Now - startTime;
            return "Uptime: " + Tools.CreateTimeString(uptime);
        }

        static VersionStatus NewVersion()
        {
            using (WebClient wc = new WebClient())
            using (Stream file = File.OpenRead("minecraft_server.jar"))
            {
                try
                {
                    lock (downloadBufferLock)
                    {
                        downloadBuffer = wc.DownloadData("http://www.minecraft.net/download/minecraft_server.jar");
                    }
                }
                catch
                {
                    return VersionStatus.Unable;
                }

                if (downloadBuffer != null)
                {
                    if (downloadBuffer.Length != file.Length)
                    {
                        return VersionStatus.New;
                    }

                    for (int i = 0; i < file.Length; i++)
                    {
                        if (file.ReadByte() != downloadBuffer[i])
                        {
                            return VersionStatus.New;
                        }
                    }
                }
            }

            return VersionStatus.None;         
        }

        static void InstallDownloadBuffer()
        {
            if (downloadBuffer != null)
            {
                bool wasRunning = running;
                if (wasRunning)
                {
                    StopServer();
                }

                if (File.Exists("minecraft_server_old.jar"))
                {
                    File.Delete("minecraft_server_old.jar");
                }

                File.Move("minecraft_server.jar", "minecraft_server_old.jar");

                using (Stream s = File.Create("minecraft_server.jar"))
                {
                    s.Write(downloadBuffer, 0, downloadBuffer.Length);
                    downloadBuffer = null;
                }

                if (!externalUpdating)
                {
                    Console.WriteLine("Install done");
                }
                else
                {
                    externalUpdating = false;
                    mcaServer.SendMessage("Install done", mcaServer.LastCommandSender);
                }

                if (wasRunning)
                {
                    RunServer();
                }
            }
        }

        static void GrabMinecraftServer()
        {
            using (WebClient wc = new WebClient())
            {
                Tools.Print("No minecraft server found. Downloading...");
                wc.DownloadFile("http://www.minecraft.net/download/minecraft_server.jar", "minecraft_server.jar");
                Tools.Print("Finished downloading");
            }
        }

        static bool BackupWorld()
        {
            if (!string.IsNullOrWhiteSpace(Settings.BackupDir))
            {
                if (MinecraftSettings == null)
                {
                    Console.WriteLine("Minecraft server settings not loaded, unable to perform backup");
                    return false;
                }

                string worldName = MinecraftSettings["level-name"];

                try
                {
                    Tools.CopyDirectory(new DirectoryInfo(worldName),
                        new DirectoryInfo(Path.Combine(Settings.BackupDir, worldName)));
                    Tools.Print("World backup done!");
                }
                catch (Exception)
                {
                    Console.WriteLine("Unable to perform backup, is the backup directory valid?");
                    return false;
                }
                return true;
            }
            else
            {
                Console.WriteLine("No backup directory set");
                return false;
            }
        }

        static void Say(string message)
        {
            ExecuteCommand("say " + message, null);
        }

        static void Tell(string player, string message)
        {
            ExecuteCommand("tell " + player + " " + message, null);
        }

        static string GetIngameCommandDescription(string command)
        {
            switch (command)
            {
                case "players":
                    return "players - Lists the players currently online";

                case "ops":
                    return "ops - Lists all the ops";

                case "give":
                    return "give - Usage: /give <player> <id> <amount> <stacks>";

                case "get":
                    return "get - Usage: /get <id> <amount> <stacks>, same as /give, without needing to type your own name";

                case "restart":
                    return "restart - Restarts the server";

                case "uptime":
                    return "uptime - The amount of time the server has been up";

                case "id":
                    return "id - Usage: /id <name>, Example: /id cob will return \"Cobblestone ID: 4\"";

                case "r":
                    return "r - Repeats the previous command";

                case "desc":
                    return "desc - Usage: /desc <command>, returns a description of the command";

                case "played":
                    return "played - The amount of time you have been on this server";

                case "bind":
                    return "bind - Usage: /bind <name> <cmd>, <cmd> can now be run with /<name>";

                case "unbind":
                    return "unbind - Usage: /unbind <name>, deletes the bind if it exists";
                    
                case "commands":
                    return "commands - Lists all available commands";

                case "level":
                    return "level - Returns the name of the current level";

                case "levels":
                    return "levels - Lists all available levels";

                case "setlevel":
                    return "setlevel - Usage: /setlevel <levelname>, sets the current level, restart the server to load it";
                    
                default:
                    return "No such command :/";
            }
        }

        static void ExecuteIngameCommand(string command, string player)
        {
            string[] split = command.Split(' ');
            switch (split[0])
            {
                case "commands":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        Tell(player, "players, ops, give, get, restart, uptime, id, r, played, bind, unbind, level, levels, setlevel, desc, commands");                      
                    }
                    else
                    {
                        Tell(player, "players, ops, r, played, bind, unbind, desc, commands");
                    }
                    Tell(player, "Use /desc <command> for more information");
                    break;

                case "ops":
                    Tell(player, "Ops: " + string.Join(", ", ops));
                    break;

                case "give":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase) && split.Length > 4)
                    {
                        ExecuteCommand(command, null);
                    }
                    break;

                case "get":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        ExecuteCommand("give " + player + " " + string.Join(" ", split, 1, split.Length - 1), null);
                    }
                    break;

                case "restart":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        RestartServer();
                    }
                    break;

                case "uptime":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        Tell(player, Uptime());
                    }
                    break;

                case "players":
                    string s = players.Count > 1 ? "s" : "";
                    Tell(player, players.Count + " Player" + s + " online: " + string.Join(", ", players));
                    break;

                case "id":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        if (split.Length > 1)
                        {
                            foreach (Item item in FindItems(split[1]))
                            {
                                Tell(player, item.Name + " ID: " + item.ID);
                            }
                        }
                    }
                    break;

                case "r":
                    if (previousCommand.ContainsKey(player))
                    {
                        ExecuteIngameCommand(previousCommand[player], player);
                    }
                    break;

                case "played":
                    TimeSpan sinceLogin = playtime.TimeSinceLogin(player);
                    TimeSpan total = playtime.TotalTimePlayed(player);

                    Tell(player, "Since last login: " + Tools.CreateTimeString(sinceLogin));
                    Tell(player, "Total: " + Tools.CreateTimeString(total));
                    break;

                case "bind":
                    if (split.Length >= 3)
                    {
                        binds.Bind(player, split[1], string.Join(" ", split, 2, split.Length - 2));
                        binds.Save();
                    }
                    break;

                case "unbind":
                    if (split.Length >= 2)
                    {
                        binds.Unbind(player, split[1]);
                        binds.Save();
                    }
                    break;

                case "level":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        Tell(player, "Current level: " + MinecraftSettings["level-name"]);
                    }
                    break;

                case "levels":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase))
                    {
                        Tell(player, "Levels:");
                        foreach (string level in GetLevels())
                        {
                            Tell(player, level);
                        }
                    }
                    break;

                case "setlevel":
                    if (ops.Contains(player, StringComparer.OrdinalIgnoreCase) && split.Length > 1)
                    {
                        changes.Enqueue(() => MinecraftSettings["level-name"] = split[1]);
                        Tell(player, "Level will be set to \"" + split[1] + "\" on the next restart");
                    }
                    break;

                case "desc":
                    if (split.Length > 1)
                    {
                        Tell(player, GetIngameCommandDescription(split[1]));
                    }
                    break;

                default:
                    if (binds.IsBound(player, split[0]))
                    {
                        ExecuteIngameCommand(binds.GetCommand(player, split[0]), player);
                    }
                    break;
            }
        }

        static void ExecuteCommand(string command, TcpClient client)
        {
            bool external = client != null;

            string[] split = command.Split(' ');
            if (command == "start")
            {
                if (!running)
                {
                    RunServer();
                }
                else
                {
                    if (!external)
                    {
                        Console.WriteLine("Minecraft server already running");
                    }
                    else
                    {
                        mcaServer.SendMessage("Minecraft server already running", client);
                    }
                }
            }
            else if (command == "restart")
            {
                RestartServer();
            }
            else if (command == "stop")
            {
                StopServer();
            }
            else if (command == "close")
            {
                if (!external)
                {
                    StopServer();
                }
            }
            else if (command == "update")
            {
                if (!external)
                {
                    Console.WriteLine("Checking for new version...");

                    switch (NewVersion())
                    {
                        case VersionStatus.New:
                            Console.Write("There is a new version available! Install it? Y/N: ");
                            if (string.Equals(Console.ReadLine(), "y", StringComparison.OrdinalIgnoreCase))
                            {
                                InstallDownloadBuffer();
                            }
                            break;

                        case VersionStatus.None:
                            Console.WriteLine("No new version available");
                            break;

                        case VersionStatus.Unable:
                            Console.WriteLine("Unable to check for new version :(");
                            break;
                    }
                }
                else
                {
                    mcaServer.SendMessage("Checking for new version...", client);

                    externalUpdating = true;

                    switch (NewVersion())
                    {
                        case VersionStatus.New:
                            Console.WriteLine("[MCA] Sending install prompt");
                            mcaServer.SendMessage("There is a new version available! Install it? Y/N: ", client);
                            break;

                        case VersionStatus.None:
                            mcaServer.SendMessage("No new version available", client);
                            externalUpdating = false;
                            break;

                        case VersionStatus.Unable:
                            mcaServer.SendMessage("Unable to check for new version :(", client);
                            externalUpdating = false;
                            break;
                    }
                }
            }
            else if (string.Equals("y", command, StringComparison.OrdinalIgnoreCase))
            {
                if (externalUpdating)
                {
                    InstallDownloadBuffer();
                    mcaServer.SendMessage("Install done", client);
                    externalUpdating = false;
                }
            }
            else if (string.Equals("n", command, StringComparison.OrdinalIgnoreCase))
            {
            }
            else if (command == "install")
            {
                externalUpdating = external;
                InstallDownloadBuffer();
            }
            else if (command == "revert")
            {
                if (File.Exists("minecraft_server_old.jar"))
                {
                    File.Delete("minecraft_server.jar");
                    File.Move("minecraft_server_old.jar", "minecraft_server.jar");

                    if (!external)
                    {
                        Console.WriteLine("Reverted last install");
                    }
                    else
                    {
                        mcaServer.SendMessage("Reverted last install", client);
                    }
                }
                else
                {
                    if (!external)
                    {
                        Console.WriteLine("No previous version found");
                    }
                    else
                    {
                        mcaServer.SendMessage("No previous version found", client);
                    }
                }
            }
            else if (command == "status")
            {
                if (!external)
                {
                    Console.WriteLine(CreateStatusMessage());
                }
                else
                {
                    mcaServer.SendMessage(CreateStatusMessage(), client);
                }
            }
            else if (split[0] == "backup")
            {
                if (split.Length == 1)
                {
                    BackupWorld();
                }
                else if (split.Length == 2 && split[1] == "interval")
                {
                    Console.WriteLine(Settings.BackupInterval);
                }
                else if (split.Length >= 3 && split[1] == "interval")
                {
                    double interval;
                    if (double.TryParse(split[2], out interval))
                    {
                        Settings.BackupInterval = interval;
                        if (interval > 0)
                        {
                            if (backupTimer != null)
                            {
                                backupTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(interval));
                            }
                            else
                            {
                                if (!string.IsNullOrWhiteSpace(Settings.BackupDir))
                                {
                                    backupTimer = new Timer(new TimerCallback((o) => BackupWorld()), null,
                                        TimeSpan.Zero, TimeSpan.FromMinutes(interval));
                                }
                            }
                        }
                        Console.WriteLine("Backup interval set to {0} minutes", interval);
                    }
                }
                else if (split.Length == 2 && split[1] == "dir")
                {
                    if (!string.IsNullOrWhiteSpace(Settings.BackupDir))
                    {
                        Console.WriteLine(Settings.BackupDir);
                    }
                    else
                    {
                        Console.WriteLine("No backup directory set");
                    }
                }
                else if (split.Length >= 3 && split[1] == "dir")
                {
                    if (!string.IsNullOrWhiteSpace(split[2]))
                    {
                        Settings.BackupDir = split[2];

                        if (Settings.BackupInterval > 0 && backupTimer == null)
                        {
                            backupTimer = new Timer(new TimerCallback((o) => BackupWorld()), null,
                                    TimeSpan.Zero, TimeSpan.FromMinutes(Settings.BackupInterval));
                        }
                    }
                }
            }
            else if (split[0] == "online")
            {
                if (split.Length == 1)
                {
                    Console.WriteLine("Online-mode is {0}", MinecraftSettings["online-mode"] ? "on" : "off");
                }
                else if (split.Length >= 2 && MinecraftSettings != null && !running)
                {
                    if (string.Equals(split[1], "on", StringComparison.OrdinalIgnoreCase))
                    {
                        MinecraftSettings["online-mode"] = true;
                    }
                    else if (string.Equals(split[1], "off", StringComparison.OrdinalIgnoreCase))
                    {
                        MinecraftSettings["online-mode"] = false;
                    }
                }
                else if (running)
                {
                    Console.WriteLine("Cannot change online-mode while the minecraft server is running");
                }
                else if (MinecraftSettings == null)
                {
                    Console.WriteLine("Minecraft server settings not loaded");
                }
            }
            else if (split[0] == "level")
            {
                Console.WriteLine("Current level: " + MinecraftSettings["level-name"]);
            }
            else if (command == "levels")
            {
                Console.WriteLine("Levels:");
                foreach (string level in GetLevels())
                {
                    Console.WriteLine(level);
                }
            }
            else if (split[0] == "mca")
            {
                if (!external)
                {
                    if (split.Length == 1)
                    {
                        mcaServer.PrintStatusMessage();
                    }
                    else if (split[1] == "start")
                    {
                        mcaServer.Start();
                    }
                    else if (split[1] == "stop")
                    {
                        mcaServer.Stop();
                    }
                    else if (split[1] == "clients")
                    {
                        if (mcaServer.Running)
                        {
                            Console.WriteLine("[MCA] {0} clients(s):", mcaServer.ClientCount);
                            foreach (TcpClient c in mcaServer.Clients)
                            {
                                IPEndPoint ep = (IPEndPoint)c.Client.RemoteEndPoint;
                                Console.WriteLine("\t{0}:{1}", ep.Address, ep.Port);
                            }
                        }
                        else
                        {
                            Console.WriteLine("[MCA] No server running");
                        }
                    }
                }
            }
            else if (running)
            {
                if (split[0] == "give" && split.Length == 5)
                {
                    string player = split[1];
                    int stacks;

                    if (int.TryParse(split[4], out stacks))
                    {
                        if (stacks > 16)
                        {
                            stacks = 16;
                        }

                        for (int i = 0; i < stacks; i++)
                        {
                            p.StandardInput.WriteLine("give " + string.Join(" ", split, 1, split.Length - 2));
                        }
                    }
                    else
                    {
                        p.StandardInput.WriteLine(command);
                    }
                }
                else if (split[0] == "op" || split[0] == "deop")
                {
                    p.StandardInput.WriteLine(command);
                    new DelayedCall(LoadOps, TimeSpan.FromSeconds(5));
                }
                else
                {
                    p.StandardInput.WriteLine(command);
                }
            }
        }
    }
}
