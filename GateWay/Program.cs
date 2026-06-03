using Grpc.Core;
using Preprocessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Gateway
{
    class Program
    {
        static Dictionary<string, Mutex> fileMutexes = new Dictionary<string, Mutex>();
        static Mutex dictMutex = new Mutex();

        static Mutex GetFileMutex(string path)
        {
            dictMutex.WaitOne();
            try
            {
                if (!fileMutexes.ContainsKey(path))
                    fileMutexes[path] = new Mutex();
                return fileMutexes[path];
            }
            finally
            {
                dictMutex.ReleaseMutex();
            }
        }

        static readonly List<string> Parametros = new List<string>
        {
        "TEMP", "HUM", "RUIDO", "AR", "PM2.5", "PM10", "LUM"
       };

        static void Main(string[] args)
        {
            Thread videoThread = new Thread(() => ReceiveVideo(5000));
            videoThread.IsBackground = true;
            videoThread.Start();

            if (args.Length > 0)
            {
                string id = args[0];
                var config = CsvConfig.LerPorId(id);

                if (config == null)
                {
                    Console.WriteLine($"[ERRO] Gateway '{id}' não encontrada no CSV.");
                    Console.ReadLine();
                    return;
                }

                Console.WriteLine($"[{config.Id}] Zona: {config.Zona} | A ouvir: {config.RoutingPattern}\n");
                CorrerGateway(config);
                return;
            }

            var gateways = CsvConfig.LerTodos();

            if (gateways.Count == 0)
            {
                Console.WriteLine("[AVISO] Nenhuma gateway encontrada no CSV.");
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"[INIT] A arrancar {gateways.Count} gateway(s)...\n");

            var threads = new List<Thread>();

            foreach (var config in gateways)
            {
                var cfg = config;
                var t = new Thread(() =>
                {
                    Console.WriteLine($"[{cfg.Id}] Zona: {cfg.Zona} | A ouvir: {cfg.RoutingPattern}");
                    CorrerGateway(cfg);
                });
                t.Name = cfg.Id;
                t.IsBackground = true;
                threads.Add(t);
                t.Start();
            }

            Console.WriteLine("\nTodas as gateways ativas. Pressiona [Enter] para parar.\n");
            Console.ReadLine();
        }
        static (bool valid, string normalizedValue, string unit) CallPreProcessing(string message)
        {
            string[] parts = message.Split(';');

            try
            {
                var channel = new Channel("localhost", 50051, ChannelCredentials.Insecure);

                var client = new PreProcessingService.PreProcessingServiceClient(channel);

                var response = client.ProcessData(new SensorData
                {
                    Timestamp = parts[0],
                    SensorId = parts[1],
                    Zone = parts[2],
                    Type = parts[3],
                    Value = parts[4]
                });

                channel.ShutdownAsync().Wait();

                if (!response.Valid)
                    Console.WriteLine($"[RPC] Rejeitado: {response.ErrorMessage}");

                return (response.Valid, response.NormalizedValue, response.Unit);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RPC] Serviço indisponível, fallback: {ex.Message}");
                return (true, parts[4], "");
            }
        }

        static void CorrerGateway(GatewayConfig config)
        {
            var sub = new Subscriber(config.RoutingPattern);

            sub.Iniciar(mensagem =>
            {
                var partes = mensagem.Split(';');

                if (partes.Length == 5)
                {
                    Console.WriteLine($"[{partes[0]}] {partes[1]} | {partes[2]}.{partes[3]} = {partes[4]}");

                    var (valid, normalizedValue, unit) = CallPreProcessing(mensagem);

                    if (!valid)
                    {
                        Console.WriteLine($"[RPC] Dado rejeitado: {mensagem}");
                        return;
                    }

                    string mensagemNormalizada = $"{partes[0]};{partes[1]};{partes[2]};{partes[3]};{normalizedValue}";

                    Console.WriteLine($"[RPC] Dado aceite: {mensagemNormalizada} {unit}");

                    SendToServer(mensagemNormalizada);

                    if (!Parametros.Contains(partes[3]))
                    {
                        Console.WriteLine($"[AVISO] Tipo inválido: {partes[3]}");
                        return;
                    }

                    SaveData(mensagemNormalizada);
                }
                else
                {
                    Console.WriteLine($"[AVISO] Mensagem inesperada: {mensagem}");
                }
            });
        }

        static void SendResponse(NetworkStream stream, string message)
        {
            byte[] resp = Encoding.UTF8.GetBytes(message);
            stream.Write(resp, 0, resp.Length);
        }

        static string ReceiveMessage(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        static void SaveData(string message)
        {
            string[] parts = message.Split(';');

            string tipo = parts[3];

            string path = $"Data/{tipo}.txt";

            Mutex m = GetFileMutex(path);
            m.WaitOne();
            try
            {
                Directory.CreateDirectory("Data");

                File.AppendAllText(path, message + Environment.NewLine);
            }
            finally
            {
                m.ReleaseMutex();
            }
        }

        static bool SendToServer(string data)
        {
            try
            {
                TcpClient serverClient = new TcpClient("127.0.0.1", 6000);

                NetworkStream stream = serverClient.GetStream();

                SendResponse(stream, data);

                string response = ReceiveMessage(stream);

                Console.WriteLine("Servidor respondeu: " + response);

                serverClient.Close();
                return true;
            }
            catch
            {
                Console.WriteLine($"Erro ao enviar: {data}");
                return false;
            }
        }

        static List<byte[]> frameBuffer = new List<byte[]>();
        static Mutex bufferMutex = new Mutex();

        static string activeVideoSensor = null;
        static Mutex videoMutex = new Mutex();

        static void ReceiveVideo(int port)
        {
            UdpClient udpServer = new UdpClient(port);
            Console.WriteLine($"[UDP] À escuta de vídeo na porta {port}");

            IPEndPoint sensorEndpoint = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    byte[] packet = udpServer.Receive(ref sensorEndpoint);

                    string text = Encoding.UTF8.GetString(packet);

                    if (text.StartsWith("VIDEO_HELLO"))
                    {
                        string[] parts = text.Split(';');
                        string sensorId = parts[1];

                        videoMutex.WaitOne();
                        try
                        {
                            if (activeVideoSensor == null)
                            {
                                activeVideoSensor = sensorId;
                                Console.WriteLine($"[VIDEO] Sensor ativo: {sensorId}");

                                byte[] resp = Encoding.UTF8.GetBytes("VIDEO_OK");
                                udpServer.Send(resp, resp.Length, sensorEndpoint);
                            }
                            else
                            {
                                Console.WriteLine($"[VIDEO] Sensor em espera: {sensorId}");

                                byte[] resp = Encoding.UTF8.GetBytes("VIDEO_WAIT");
                                udpServer.Send(resp, resp.Length, sensorEndpoint);
                            }
                        }
                        finally
                        {
                            videoMutex.ReleaseMutex();
                        }
                        continue;
                    }

                    if (text.StartsWith("VIDEO_END"))
                    {
                        string[] parts = text.Split(';');
                        string sensorId = parts[1];

                        videoMutex.WaitOne();
                        try
                        {
                            if (activeVideoSensor == sensorId)
                            {
                                Console.WriteLine($"[VIDEO] Sensor terminou: {sensorId}");
                                activeVideoSensor = null;
                            }
                        }
                        finally
                        {
                            videoMutex.ReleaseMutex();
                        }
                        FlushBuffer(sensorId);
                        continue;
                    }

                    int headerEndIndex = Array.IndexOf(packet, (byte)'\n');
                    if (headerEndIndex == -1)
                        continue;

                    string header = Encoding.UTF8.GetString(packet, 0, headerEndIndex);

                    byte[] payload = new byte[packet.Length - headerEndIndex - 1];
                    Array.Copy(packet, headerEndIndex + 1, payload, 0, payload.Length);

                    var partsFrame = header.Split(';');
                    if (partsFrame.Length < 5)
                        continue;

                    int size = int.Parse(partsFrame[4]);

                    string sensorIdFrame = partsFrame[1];

                    videoMutex.WaitOne();
                    try
                    {
                        if (sensorIdFrame != activeVideoSensor)
                            continue;

                        bufferMutex.WaitOne();
                        try
                        {
                            frameBuffer.Add(payload);

                            if (frameBuffer.Count == size / 2)
                            {
                                Console.WriteLine("[VIDEO] Enviando primeira metade...");
                                SendVideo(frameBuffer.ToList(), activeVideoSensor);
                                frameBuffer.Clear();
                            }
                        }
                        finally
                        {
                            bufferMutex.ReleaseMutex();
                        }
                    }
                    finally
                    {
                        videoMutex.ReleaseMutex();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VIDEO] Erro: {ex.Message}");
                }
            }

        }
        static void SendVideo(List<byte[]> frames, string sensorId)
        {
            try
            {
                using (TcpClient client = new TcpClient("127.0.0.1", 7001))
                using (NetworkStream ns = client.GetStream())
                {
                    int i = 0;
                    foreach (var frame in frames)
                    {
                        string header = $"FRAME;{sensorId};{i};{frame.Length}\n";
                        SendResponse(ns, header);
                        ns.Write(frame, 0, frame.Length);
                        i++;
                    }

                    string end = $"FRAMES_END;{sensorId}\n";
                    SendResponse(ns, end);

                    string response = ReceiveMessage(ns);
                    Console.WriteLine("[VIDEO] Resposta do servidor: " + response);

                    if (response == "FRAMES_SAVED")
                    {
                        Console.WriteLine("[VIDEO] Frames guardados com sucesso!");
                    }
                    else
                    {
                        Console.WriteLine("[VIDEO] Erro ao guardar frames!");
                    }

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Video] Erro ao enviar: {ex.Message}");
            }
        }
        static void FlushBuffer(string sensorId)
        {
            bufferMutex.WaitOne();
            try
            {
                if (frameBuffer.Count > 0)
                {
                    Console.WriteLine("[VIDEO] Enviando restante dos frames...");
                    SendVideo(frameBuffer.ToList(), sensorId);
                    frameBuffer.Clear();
                }
            }
            finally
            {
                bufferMutex.ReleaseMutex();
            }
        }
    }
}