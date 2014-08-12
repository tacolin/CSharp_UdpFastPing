using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;
using System.Globalization;


namespace udpFastPing
{
    class Program
    {
        static void sendFastPings(string serverIp, int serverPort, int timeBetween2Pings)
        {
            using (UdpClient sender = new UdpClient(serverPort))
            {
                sender.Connect(serverIp, serverPort);
                for (int i = 0; ; i++)
                {
                    byte[] sendbytes = Encoding.ASCII.GetBytes("hello " + i.ToString());
                    sender.Send(sendbytes, sendbytes.Length);
                    if (timeBetween2Pings > 0)
                    {
                        Thread.Sleep(timeBetween2Pings);
                    }
                }
            }
        }

        /// <summary>
        /// Be a client , send udp fast ping packet to server
        /// </summary>
        /// <param name="serverIp">server ipv4 address, ex. 192.168.8.8</param>
        /// <param name="serverPort">server udp port, ex. 5555</param>
        /// <param name="timeBetween2Pings">
        /// the time between 2 pings, unit : ms
        /// the real situation is based by your computer computing power
        /// value '0' is flooding, sending as fast as you can
        /// </param>
        /// <param name="sendDuration">
        /// total sending duration, unit: second
        /// ex. 30 , the total sending procedure will run 30 seconds and then automatically stop.
        /// </param>
        /// <returns>0: success, other value : failure</returns>
        static int udpFastPingClient(string serverIp, int serverPort, int timeBetween2Pings, int sendDuration)
        {
            Thread txThread = new Thread(delegate() { sendFastPings(serverIp, serverPort, timeBetween2Pings); });
            txThread.Start();

            Thread.Sleep(sendDuration * 1000);
            txThread.Abort();

            Thread.Sleep(1000); // wait for thread stop.

            return 0;
        }

        public class MyLog
        {
            public DateTime recvTime;
            public int count;
            public double diffms; // difference time betwenn current reception and previous reception, unit : ms

            public MyLog(DateTime time, int cnt, double diff)
            {
                recvTime = time;
                count = cnt;
                diffms = diff;
            }
        };

        static void recvFastPings(UdpClient listener, int selfPort, List<DateTime> dateList, List<string> stringList)
        {
            IPEndPoint self = new IPEndPoint(IPAddress.Any, selfPort);
            while (true)
            {
                try
                {
                    byte[] recvbytes = listener.Receive(ref self);
                    dateList.Add(DateTime.Now);
                    //string date = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss.ffffff");
                    string recvstring = Encoding.ASCII.GetString(recvbytes);
                    stringList.Add(recvstring);
                }
                catch (ThreadInterruptedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// write reception log to text file
        /// </summary>
        /// <param name="logList"></param>
        /// <param name="logFilePath"></param>
        /// <param name="maxDiffCount"></param>
        /// <returns>0: success, other value : failure</returns>
        static int writeLogsToFile(List<MyLog> logList, string logFilePath, int maxDiffCount, int maxDiffLine)
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    File.Delete(logFilePath);
                }

                using (StreamWriter sw = new StreamWriter(logFilePath))
                {
                    sw.WriteLine("maxDiffLine     : {0} ", maxDiffLine + 6);
                    sw.WriteLine("maxDiffCount    : {0} ", maxDiffCount);
                    sw.WriteLine("maxDiffTime(ms) : {0} ", logList[maxDiffCount].diffms);
                    sw.WriteLine("=================================");
                    sw.WriteLine("");

                    for (int i = 0; i < logList.Count; i++)
                    {
                        sw.WriteLine("{0} | {1} | {2}",
                            logList[i].count,
                            logList[i].recvTime.ToString("yyyy-MM-dd hh:mm:ss.ffffff"),
                            logList[i].diffms);
                    }
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// calculate the difference time between 2 continuous receptions
        /// </summary>
        /// <param name="logList"></param>
        /// <param name="maxDiffCount"></param>
        /// <param name="maxDiffLine"></param>
        /// <returns>0: success, other value : failure</returns>
        static int calculateRecvDiffms(List<MyLog> logList, out int maxDiffCount, out int maxDiffLine)
        {
            double maxDiffms = 0.0;
            maxDiffCount = -1;
            maxDiffLine = -1;

            for (int i = 1; i < logList.Count; i++)
            {
                logList[i].diffms = logList[i].recvTime.Subtract(logList[i - 1].recvTime).TotalMilliseconds;
                if (logList[i].diffms > maxDiffms)
                {
                    maxDiffms = logList[i].diffms;
                    maxDiffCount = logList[i].count;
                    maxDiffLine = i;
                }
            }

            if (maxDiffCount == -1)
            {
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Be a server to receive udp fast pings.
        /// record timestamp for each received ping
        /// after recepction, write the reception results to log file
        /// </summary>
        /// <param name="selfPort"></param>
        /// <param name="recvDuration">unit: second, receving will continue 'recvDuration' seconds and stop automatically</param>
        /// <param name="logFilePath"></param>
        /// <returns>0: success, other value : failure</returns>
        static int udpFastPingServer(int selfPort, int recvDuration, string logFilePath)
        {
            List<string> stringList = new List<string>();
            List<DateTime> dateList = new List<DateTime>();

            Console.WriteLine("start receiving ... ");

            using (UdpClient listener = new UdpClient(selfPort))
            {
                Thread rxThread = new Thread(delegate() { recvFastPings(listener, selfPort, dateList, stringList); });
                rxThread.Start();

                Thread.Sleep(recvDuration * 1000);

                listener.Close();
                rxThread.Abort();

                Thread.Sleep(1000); // wait for thread stop.
            }

            if (stringList.Count == 0)
            {
                Console.WriteLine("[WARN] receive nothing at udp port {0} for {1} seconds.", selfPort, recvDuration);
                return 1;
            }

            List<MyLog> logList = new List<MyLog>();
            for (int i = 0; i < stringList.Count; i++)
            {
                MyLog log = new MyLog(DateTime.MinValue, -1, 0.0);

                log.count = Convert.ToInt32(stringList[i].Split(' ')[1]);
                log.recvTime = dateList[i];

                //string[] param = stringList[i].Split('|');               
                //log.recvTime = DateTime.ParseExact(param[0], "yyyy-MM-dd hh:mm:ss.ffffff", CultureInfo.InvariantCulture);
                //log.count = Convert.ToInt32(param[1].Split(' ')[1]); // hello 100 -> ['hello', '100'] -> 100

                logList.Add(log);
            }

            int maxDiffCount;
            int maxDiffLine;
            if (0 != calculateRecvDiffms(logList, out maxDiffCount, out maxDiffLine))
            {
                Console.WriteLine("[WARN] calculate recv diff ms failed.");
                return 1;
            }

            Console.WriteLine("receiving over");

            return writeLogsToFile(logList, logFilePath, maxDiffCount, maxDiffLine);
        }

        /// <summary>
        /// Trigger remote client to start udp fast ping.
        /// remote client will ping myself.
        /// </summary>
        /// <param name="clientIp"></param>
        /// <param name="clientPort"></param>
        /// <param name="timeBetween2Pings">
        /// the time between 2 pings, unit : ms
        /// the real situation is based by your computer computing power
        /// value '0' is flooding, sending as fast as you can
        /// </param>
        /// <param name="serverPort"></param>
        /// <param name="sendDuration"></param>
        /// <returns>0: success, other value : failure</returns>
        static int udpFastPingTrigger(string clientIp, int clientPort, int timeBetween2Pings, int serverPort, int sendDuration)
        {
            using (UdpClient sender = new UdpClient(clientPort))
            {
                sender.Connect(clientIp, clientPort);
                string sendstring = String.Format("interval {0} | port {1} | duration {2}", timeBetween2Pings, serverPort, sendDuration);
                byte[] sendbytes = Encoding.ASCII.GetBytes(sendstring);
                sender.Send(sendbytes, sendbytes.Length);
            }

            return 0;
        }

        static void udpFastPingRemoteClient(int recvport)
        {
            using (UdpClient listener = new UdpClient(recvport))
            {
                IPEndPoint self = new IPEndPoint(IPAddress.Any, recvport);
                while (true)
                {
                    byte[] recvbytes = listener.Receive(ref self);
                    string recvstring = Encoding.ASCII.GetString(recvbytes);
                    string[] param = recvstring.Split('|');

                    if (param.Length == 3)
                    {
                        for (int i=0; i<param.Length; i++)
                        {
                            param[i] = param[i].Trim();
                        }

                        int interval = Convert.ToInt32(param[0].Split(' ')[1]);
                        int serverport = Convert.ToInt32(param[1].Split(' ')[1]);
                        int duration = Convert.ToInt32(param[2].Split(' ')[1]);
                        string serverip = self.Address.ToString();

                        Console.WriteLine("interval = {0}", interval);
                        Console.WriteLine("serverport = {0}", serverport);
                        Console.WriteLine("duration = {0}", duration);
                        Console.WriteLine("serverip = {0}", serverip);
                        udpFastPingClient(serverip, serverport, interval, duration);
                    }
                    else
                    {
                        Console.WriteLine("[WARN] unknown format trigger packet.");
                    }
                }
            }
        }

        /// <summary>
        /// show help message to notify user how to do correct input.
        /// </summary>
        static void showHelpMessage()
        {
            Console.WriteLine("[ERR] input invalid");
            Console.WriteLine("server mode:");
            Console.WriteLine("    udpFastPing.exe -s [port] [duration(sec)] [logFilePath]");
            Console.WriteLine("    ex. udpFastPing.exe -s 5555 30 d:\\output.txt");
            Console.WriteLine("");
            Console.WriteLine("client mode:");
            Console.WriteLine("    udpFastPing.exe -c [ip] [port] [interval(ms)] [duration(s)]");
            Console.WriteLine("    ex. udpFastPing.exe -c 192.168.8.8 5555 100 30");
            Console.WriteLine("");
            Console.WriteLine("trigger mode:");
            Console.WriteLine("    udpFastPing.exe -t [ip] [port] [interval(ms)] [myPort] [duration(s)]");
            Console.WriteLine("    ex. udpFastPing.exe -t 192.168.8.8 5555 100 6666 30");
            Console.WriteLine("");
            Console.WriteLine("remote client mode:");
            Console.WriteLine("    udpFastPing.exe -r [port]");
            Console.WriteLine("    ex. udpFastPing.exe -r 5555");
            Console.WriteLine("");
        }

        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                showHelpMessage();
                return 1;
            }

            if ((String.Compare(args[0], "-s") == 0) && (args.Length == 4))
            {
                int serverport = Convert.ToInt32(args[1]);
                int duration = Convert.ToInt32(args[2]);
                string logfilepath = args[3];

                return udpFastPingServer(serverport, duration, logfilepath);
            }
            else if ((String.Compare(args[0], "-c") == 0) && (args.Length == 5))
            {
                string serverip = args[1];
                int serverport = Convert.ToInt32(args[2]);
                int between = Convert.ToInt32(args[3]);
                int duration = Convert.ToInt32(args[4]);

                return udpFastPingClient(serverip, serverport, between, duration);
            }
            else if ((String.Compare(args[0], "-t") == 0) && (args.Length == 6))
            {
                string clientip = args[1];
                int clientport = Convert.ToInt32(args[2]);
                int between = Convert.ToInt32(args[3]);
                int serverport = Convert.ToInt32(args[4]);
                int duration = Convert.ToInt32(args[5]);

                return udpFastPingTrigger(clientip, clientport, between, serverport, duration);
            }
            else if ((String.Compare(args[0], "-r") == 0) && (args.Length == 2))
            {
                int recvport = Convert.ToInt32(args[1]);

                udpFastPingRemoteClient(recvport);
            }

            showHelpMessage();
            return 1;
        }
    }
}
