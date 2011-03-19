using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace MCA
{
    class BindManager
    {
        private Dictionary<string, Dictionary<string, string>> binds;
        private string filename;

        public BindManager(string filename)
        {
            binds = new Dictionary<string, Dictionary<string, string>>();
            this.filename = filename;

            if (File.Exists(filename))
            {
                Load();
            }
        }

        public void Bind(string player, string name, string command)
        {
            if (name == command)
                return;

            if (binds.ContainsKey(player))
            {
                if (binds[player].ContainsKey(name))
                {
                    binds[player][name] = command;
                }
                else
                {
                    binds[player].Add(name, command);
                }
            }
            else
            {
                binds.Add(player, new Dictionary<string, string>());
                binds[player].Add(name, command);
            }
        }

        public void Unbind(string player, string name)
        {
            if (binds.ContainsKey(player) && binds[player].ContainsKey(name))
            {
                binds[player].Remove(name);
            }
        }

        public bool IsBound(string player, string name)
        {
            return binds.ContainsKey(player) && binds[player].ContainsKey(name);
        }

        public string GetCommand(string player, string name)
        {
            if (IsBound(player, name))
            {
                return binds[player][name];
            }
            else
            {
                return null;
            }
        }

        public void Save()
        {
            using (BinaryWriter bw = new BinaryWriter(File.Create(filename)))
            {
                bw.Write(binds.Count);
                foreach (var player in binds)
                {
                    bw.Write(player.Key);
                    bw.Write(player.Value.Count);
                    foreach (var bind in player.Value)
                    {
                        bw.Write(bind.Key);
                        bw.Write(bind.Value);
                    }
                }
            }
        }

        private void Load()
        {
            using (BinaryReader br = new BinaryReader(File.OpenRead(filename)))
            {
                int players = br.ReadInt32();
                for (int i = 0; i < players; i++)
                {
                    string player = br.ReadString();
                    binds.Add(player, new Dictionary<string, string>());

                    int playerBinds = br.ReadInt32();
                    for (int j = 0; j < playerBinds; j++)
                    {
                        binds[player].Add(br.ReadString(), br.ReadString());
                    }
                }
            }
        }
    }
}
