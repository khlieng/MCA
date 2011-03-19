using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;

namespace MCA
{
    static class Tools
    {
        public static string CreateTimeString(TimeSpan ts)
        {
            StringBuilder result = new StringBuilder();

            if (ts.Days > 0)
            {
                result.AppendFormat("{0} {1} ", ts.Days, ts.Days > 1 ? "Days" : "Day");
            }
            if (ts.Hours > 0)
            {
                result.AppendFormat("{0} {1} ", ts.Hours, ts.Hours > 1 ? "Hours" : "Hour");
            }
            if (ts.Minutes > 0)
            {
                result.AppendFormat("{0} {1} ", ts.Minutes, ts.Minutes > 1 ? "Minutes" : "Minute");
            }
            if (ts.Seconds > 0)
            {
                result.AppendFormat("{0} {1}", ts.Seconds, ts.Seconds > 1 ? "Seconds" : "Seconds");
            }

            return result.ToString();
        }

        public static void Print(string line)
        {
            Console.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line);
        }

        public static void CopyDirectory(DirectoryInfo source, DirectoryInfo target)
        {
            if (!Directory.Exists(target.FullName))
            {
                Directory.CreateDirectory(target.FullName);
            }

            foreach (FileInfo file in source.GetFiles())
            {
                file.CopyTo(Path.Combine(target.ToString(), file.Name), true);
            }

            foreach (DirectoryInfo sourceSubDir in source.GetDirectories())
            {
                DirectoryInfo targetSubDir = target.CreateSubdirectory(sourceSubDir.Name);
                CopyDirectory(sourceSubDir, targetSubDir);
            }
        }

        public static void NewScrapeIDs(string outputFilename)
        {
            MemoryStream ms;

            Print("Downloading...");
            using (WebClient wc = new WebClient())
            {
                ms = new MemoryStream(wc.DownloadData("http://www.minecraftinfo.com/IDList.htm"));
            }
            
            Print("Scraping and writing result to disk...");
            using (StreamReader r = new StreamReader(ms))
            using (StreamWriter w = new StreamWriter(File.Create(outputFilename)))
            {
                while (!r.EndOfStream)
                {
                    string line = r.ReadLine();
                    if (line.StartsWith("<div class=\"indent\">"))
                    {
                        List<string> subs = new List<string>();
                        for (int i = 0; i < line.Length - 1; i++)
                        {
                            if (line[i] == '>' && line[i + 1] != '<')
                            {
                                i++;
                                int start = i;
                                for (; i < line.Length; i++)
                                {
                                    if (line[i] == '<')
                                    {
                                        string ss = line.Substring(start, i - start);
                                        if (!string.IsNullOrWhiteSpace(ss))
                                        {
                                            subs.Add(line.Substring(start, i - start));
                                        }
                                        break;
                                    }
                                }
                            }
                        }

                        for (int i = 1; i < subs.Count; i += 2)
                        {
                            string id = subs[i];
                            string name = subs[i + 1];

                            if (id.Contains(':'))
                            {
                                id = id.Substring(0, id.Length - (id.Length - id.IndexOf(':')));
                            }

                            if (id.Contains(';'))
                            {
                                int lastIndex = 0;
                                int index = 0;
                                while ((index = id.IndexOf(';', lastIndex + 1)) != -1)
                                {
                                    lastIndex = index;
                                }

                                id = id.Substring(lastIndex + 1, id.Length - (lastIndex + 1));
                            }

                            w.WriteLine(id + ":" + name);
                        }
                    }
                }
            }
            Print("Scrape done!");
        }

        public static void ScrapeIDs(string outputFilename)
        {
            Console.WriteLine("Downloading...");
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFile("http://www.minecraftwiki.net/wiki/Data_values", "id.html");
            }
            Console.WriteLine("Finished downloading!");

            Console.WriteLine("Fixing id.html, writing result to id2.html");
            using (StreamReader r = new StreamReader(File.OpenRead("id.html")))
            using (StreamWriter w = new StreamWriter(File.Create("id2.html")))
            {
                while (!r.EndOfStream)
                {
                    string line = r.ReadLine();
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == ' ')
                        {
                            line = line.Remove(i, 1);
                            i--;
                        }
                    }
                    w.WriteLine(line);
                }
            }
            Console.WriteLine("Finished fixing!");

            Console.WriteLine("Writing result to " + outputFilename);
            using (StreamReader r2 = new StreamReader(File.OpenRead("id.html")))
            using (StreamReader r = new StreamReader(File.OpenRead("id2.html")))
            using (StreamWriter w = new StreamWriter(File.Create(outputFilename)))
            {
                int n = 0;
                int r2Pos = 0;
                int max = 0;
                while (!r.EndOfStream)
                {
                    n++;
                    string line = r.ReadLine();
                    string[] split = line.Split('<');

                    int c = 0;
                    for (int i = 0; i < split.Length; i++)
                    {
                        int result = -1;
                        if (split[i].Split('>').Length > 1 && int.TryParse(split[i].Split('>')[1], out result))
                        {
                            c++;
                        }

                        if (c == 1 && result > 0 && n > 150)
                        {
                            if (result > max)
                            {
                                max = result;
                            }

                            if (result >= max)
                            {
                                string line2 = string.Empty;
                                while (r2Pos < n)
                                {
                                    r2Pos++;
                                    line2 = r2.ReadLine();
                                }
                                string[] split2 = line2.Split(' ');
                                int sslen = 0;
                                var q = split2.Where((s) => s.StartsWith("title"));
                                int start = q.Count() > 0 ? line2.IndexOf(q.Last()) : -1;
                                if (start == -1)
                                    break;
                                start += 7;
                                for (int j = start; j < line2.Length; j++)
                                {
                                    if (line2[j] == '"')
                                    {
                                        sslen = j - start;
                                        break;
                                    }
                                }
                                string name = line2.Substring(start, sslen);
                                w.WriteLine(result + ":" + name);
                            }
                        }
                    }
                }
            }
            Console.WriteLine("Finished writing!");

            File.Delete("id.html");
            File.Delete("id2.html");
        }
    }
}
