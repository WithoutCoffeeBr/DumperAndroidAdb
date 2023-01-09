// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Program
{

    public struct PSInfo
    {
        public string USER { get; set; }
        public int PID { get; set; }
        public string NAME { get; set; }
    }

    public struct MapInfo
    {
        public string Path { get; set; }
        public UInt64 StartAddress { get; set; }
        public UInt64 EndAddress { get; set; }
    }

    public class MapsInfo : List<MapInfo>
    {
        public UInt64 Size
        {
            get
            {
                    return this.EndAddress - this.StartAddress;
            }
        }

        public MapsInfo FindAllMaps(Predicate<MapInfo> match)
        {
            var matchs = this.FindAll(match);
            var result = new MapsInfo();
            if (matchs.Count > 0)
            {
                result.AddRange(matchs);
            }
            return result;
        }
        public UInt64 StartAddress { get { return this[0].StartAddress; } }
        public UInt64 EndAddress { get { return this[this.Count - 1].EndAddress; } }
    }
    public class Program
    {
        public static Process RunShellAdb(string cmd)
        {
            Process RunShell = new Process()
            {
                StartInfo = new ProcessStartInfo("HD-adb", $"shell -n \u0022{cmd}\u0022")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            };
            RunShell.Start();
            return RunShell;
        }

        public static void RunShellPull(string cmd)
        {
            string? newline = string.Empty;
            Process RunShell = new Process()
            {
                StartInfo = new ProcessStartInfo("HD-adb", $"pull {cmd}")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            };
            RunShell.Start();
            while ((newline= RunShell.StandardOutput.ReadLine()) != null);
        }


        public static Process RunShellAdbSU(string cmd)
        {
            return RunShellAdb($"su -c \u0022\u0022{cmd}\u0022\u0022");
        }
        public static List<PSInfo> GetProcessAndroid()
        {
            string? newline = string.Empty;
            List<PSInfo> ps = new List<PSInfo>();
            using (var StreamConsole = RunShellAdbSU("ps").StandardOutput)
            {


                while ((newline = StreamConsole.ReadLine()) != null)
                {
                    if (!newline.Contains("USER") )
                    {
                        var split = Regex.Split(newline, @"\s+");
                        ps.Add(new PSInfo()
                        {
                            USER = split[0],
                            PID = int.Parse(split[1]),
                            NAME = split[8],
                        });
                    }
                }
            }

            return ps;
        }

        public static string GetPath(string ProcessName)
        {
            string path = string.Empty;
            int sIndex = ProcessName.IndexOf("/");
            if (sIndex > -1)
                path = ProcessName.Substring(sIndex);
            return path;
        }
        public static MapsInfo parseMaps(int pid)
        {
            string? newline = string.Empty;
            MapsInfo maps = new MapsInfo();
            var Shell = RunShellAdb($"su -c \u0022\u0022cat /proc/{pid}/maps\u0022\u0022");
            using (var StreamConsole = Shell.StandardOutput)
            {
                while ((newline = StreamConsole.ReadLine()) != null)
                {
                    var split = Regex.Split(newline, @"\s+");
                    var split_addres = split[0].Split("-");
                    if(newline != string.Empty)
                    {
                        maps.Add(new MapInfo()
                        {
                            StartAddress = Convert.ToUInt64(split_addres[0], 16),
                            EndAddress = Convert.ToUInt64(split_addres[1], 16),
                            Path = GetPath(newline)
                        });
                    }

                }
            }
            return maps;
        }

        public static byte[] GetMemory(int pid, MapsInfo mapsInfo)
        {
            Console.WriteLine($"Region:{mapsInfo[0].Path}");
            RunShellAdbSU($"dd if=/proc/{pid}/mem of=/data/local/tmp/dump.bin bs=1024 count={mapsInfo.Size / 1024} skip={mapsInfo.StartAddress / 1024}").StandardOutput.ReadLine();
            RunShellAdbSU("chmod 777 /data/local/tmp/dump.bin").StandardOutput.ReadLine();
            var path = Path.GetTempFileName();
            RunShellPull($"/data/local/tmp/dump.bin {path}");
            RunShellAdbSU("rm /data/local/tmp/dump.bin");
            return File.ReadAllBytes(path);
        }





        public static void Main()
        {
            var plist = GetProcessAndroid();
            var psInfo = plist.Find(x => x.NAME.Contains("com.dts.freefiremax"));
            Console.WriteLine($"PROCESS: {psInfo.NAME} PID:{psInfo.PID}");
            MapsInfo maps = parseMaps(psInfo.PID);
            MapsInfo metadatainfo = maps.FindAllMaps(x => x.Path.Contains("/dev/zero (deleted)"));
            MapsInfo binaryinfo = maps.FindAllMaps(x => x.Path.Contains("libil2cpp.so"));
            var metadata = GetMemory(psInfo.PID, metadatainfo);
            var binary = GetMemory(psInfo.PID, binaryinfo);
            metadata[0] = 0xAF;
            metadata[1] = 0x1B;
            metadata[2] = 0xB1;
            metadata[3] = 0xFA;
            
            File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"global-metadata.dat"),metadata);
            File.WriteAllBytes(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,$"{binaryinfo.StartAddress.ToString("X2")}-binary.so"),binary);
            //Console.WriteLine($"StartAddress: {info.StartAddress.ToString("X2")}\nPath:{info.Path}\nSize:{maps.Size}");
        }
    }
}
