using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Sensor
{
    internal class Program
    {
        static readonly List<string> Parametros = new List<string>
        {
            "TEMP", "HUM", "RUIDO", "AR", "PM2.5", "PM10", "LUM"
        };

        static readonly List<string> Zonas = new List<string>
        {
            "ZONA_ESCOLAR", "ZONA_CENTRO", "ZONA_INDUSTRIAL",
            "ZONA_RESIDENCIAL", "ZONA_PARQUE", "ZONA_AVENIDA"
        };

        static int GerarValor(string parametro, Random rnd)
        {
            switch (parametro)
            {
                case "TEMP":
                    return rnd.Next(15, 40);

                case "HUM":
                    return rnd.Next(30, 95);

                case "RUIDO":
                    return rnd.Next(40, 110);

                case "AR":
                    return rnd.Next(0, 200);

                case "PM2.5":
                    return rnd.Next(0, 150);

                case "PM10":
                    return rnd.Next(0, 200);

                case "LUM":
                    return rnd.Next(0, 1000);

                default:
                    return rnd.Next(0, 100);
            }
        }

        static void Main(string[] args)
        {

            Console.Write("ID do Sensor: ");
            string id = Console.ReadLine();

            Thread videoThread = new Thread(() => StreamVideo(id, "127.0.0.1", 5001));
            videoThread.IsBackground = true;
            videoThread.Start();

            while (true)
            {
                Thread dataThread = new Thread(() => Data(id, "127.0.0.1", 5000));
                dataThread.Start();

                Thread.Sleep(240000);
            }
        }

        static void Data(string sensorId, string gatewayIp, int porta)
        {
           
        }

        // VIDEO

        static volatile bool _videoRunning = false;
        static volatile bool _canStream = false;

        static void StreamVideo(string sensorId, string gatewayIp, int porta)
        {
            IPEndPoint destino = new IPEndPoint(IPAddress.Parse(gatewayIp), porta);
            UdpClient udp = new UdpClient();

            int retryDelay = 2000;
                while (!_canStream)
                {
                    string msg = $"VIDEO_HELLO;{sensorId}";
                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    udp.Send(data, data.Length, destino);

                    string response = ReceiveResponse(udp);

                    if (response != null && response.StartsWith("VIDEO_OK"))
                    {
                        Console.WriteLine("[VIDEO] Acesso autorizado");
                        _canStream = true;
                        break;
                    }
                    else
                    {
                        Console.WriteLine("[VIDEO] Em espera...");

                        Thread.Sleep(retryDelay);
                    }
                }

                _videoRunning = true;
                int frameIndex = 0;
                Random rnd = new Random();

                while (_videoRunning && frameIndex < 100)
                {
                    try
                    {
                        SendFrame(udp, destino, sensorId, frameIndex, rnd);
                        frameIndex++;

                        Thread.Sleep(33);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[VIDEO] Erro: {ex.Message}");
                        _videoRunning = false;
                    }
                }

                byte[] end = Encoding.UTF8.GetBytes($"VIDEO_END;{sensorId}");
                udp.Send(end, end.Length, destino);

                udp.Close();
                Console.WriteLine("[VIDEO] Stream terminada.");
        }

        static string ReceiveResponse(UdpClient udp)
        {
            udp.Client.ReceiveTimeout = 2000; // tempo de espera por resposta (2s)

            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            try
            {
                byte[] data = udp.Receive(ref remote);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }

        static void SendFrame(UdpClient udp, IPEndPoint destino, string sensorId, int frameIndex, Random rnd)
        {
            byte[] frameData = new byte[1024];
            rnd.NextBytes(frameData);

            string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");
            string header = $"FRAME;{sensorId};{frameIndex};{timestamp};{frameData.Length}\n";
            byte[] headerBytes = Encoding.UTF8.GetBytes(header);

            // header e frame 
            byte[] pacote = new byte[headerBytes.Length + frameData.Length];
            Buffer.BlockCopy(headerBytes, 0, pacote, 0, headerBytes.Length);
            Buffer.BlockCopy(frameData, 0, pacote, headerBytes.Length, frameData.Length);

            udp.Send(pacote, pacote.Length, destino);
        }
    }
}
