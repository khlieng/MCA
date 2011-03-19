using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Dynamic;

namespace MCA
{
    class Config : DynamicObject
    {
        private Dictionary<string, object> vars = new Dictionary<string, object>();
        private string filename;

        public Config(string filename)
        {
            if (File.Exists(filename))
            {
                Load(filename);
            }
            else if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(filename)) && 
                !Directory.Exists(Path.GetDirectoryName(filename)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
            }

            this.filename = filename;
        }
        
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            return Get(binder.Name, out result);
        }

        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            return Set(binder.Name, value);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            return Get(indexes[0].ToString(), out result);
        }

        public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value)
        {
            return Set(indexes[0].ToString(), value);
        }

        private bool Get(string name, out object result)
        {
            if (vars.ContainsKey(name))
            {
                result = vars[name];
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }

        private bool Set(string name, object value)
        {
            if (vars.ContainsKey(name))
            {
                vars[name] = value;
                Save();
                return true;
            }
            else
            {
                vars.Add(name, value);
                Save();
                return true;
            }
        }

        private void Load(string filename)
        {
            using (StreamReader reader = new StreamReader(File.OpenRead(filename)))
            {
                while (!reader.EndOfStream)
                {
                    string line = reader.ReadLine();
                    string[] split = line.Split('=');

                    if (split.Length == 2)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            split[i] = split[i].Trim();
                        }

                        vars.Add(split[0], Read(split[1]));
                    }
                }
            }
        }

        private void Save()
        {
            using (StreamWriter writer = new StreamWriter(File.Create(filename)))
            {
                foreach (var param in vars)
                {
                    writer.WriteLine(param.Key + "=" + param.Value);
                }
            }
        }

        private object Read(string param)
        {
            double d;
            int i;
            bool b;

            if (int.TryParse(param, out i))
            {
                return i;
            }
            else if (double.TryParse(param, out d))
            {
                return d;
            }

            else if (bool.TryParse(param, out b))
            {
                return b;
            }
            else
            {
                return param;
            }
        }
    }
}
