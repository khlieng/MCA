using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace MCA
{
    class PlaytimeManager
    {
        private Dictionary<string, TimeSpan> timePlayed;
        private Dictionary<string, DateTime> loginTimes;
        private string filename;

        public PlaytimeManager(string filename)
        {
            timePlayed = new Dictionary<string, TimeSpan>();
            loginTimes = new Dictionary<string, DateTime>();
            this.filename = filename;

            if (File.Exists(filename))
            {
                Load();
            }
        }

        public TimeSpan TotalTimePlayed(string player)
        {
            if (timePlayed.ContainsKey(player))
            {
                return timePlayed[player] + TimeSinceLogin(player);
            }
            else
            {
                return TimeSinceLogin(player);
            }
        }

        public TimeSpan TimeSinceLogin(string player)
        {
            return DateTime.Now - loginTimes[player];
        }

        public void PlayerLogin(string player)
        {
            if (!loginTimes.ContainsKey(player))
            {
                loginTimes.Add(player, DateTime.Now);
            }
        }

        public void PlayerLogout(string player)
        {
            if (loginTimes.ContainsKey(player))
            {
                if (timePlayed.ContainsKey(player))
                {
                    timePlayed[player] += TimeSinceLogin(player);
                }
                else
                {
                    timePlayed.Add(player, TimeSinceLogin(player));
                }

                loginTimes.Remove(player);
            }
        }

        public void LogoutAll()
        {
            string[] players = loginTimes.Keys.ToArray();
            for (int i = 0; i < players.Length; i++)
            {
                PlayerLogout(players[i]);
            }
        }

        public void Save()
        {
            using (BinaryWriter bw = new BinaryWriter(File.Create(filename)))
            {
                bw.Write(timePlayed.Count);
                foreach (var entry in timePlayed)
                {
                    bw.Write(entry.Key);
                    bw.Write(entry.Value.ToString());
                }
            }
        }

        private void Load()
        {
            using (BinaryReader br = new BinaryReader(File.OpenRead(filename)))
            {
                int entries = br.ReadInt32();
                for (int i = 0; i < entries; i++)
                {
                    timePlayed.Add(br.ReadString(), TimeSpan.Parse(br.ReadString()));
                }
            }
        }
    }
}
