using Grpc.Core;
using Preprocessing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices.ComTypes;
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

        static List<string> ValidarTipos(string[] tipos)
        {
            List<string> invalidos = new List<string>();
            foreach (string tipo in tipos)
                if (!Parametros.Contains(tipo))
                    invalidos.Add(tipo);
            return invalidos;
        }

        static void Main(string[] args)
        {
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

        static bool SensorExists(string sensorId)
        {
            string path = "Data/sensores.csv";

            Mutex m = GetFileMutex(path);
            m.WaitOne();

            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory("Data");
                    File.WriteAllText(path, "");
                }

                var lines = File.ReadAllLines(path);

                for (int i = 0; i < lines.Length; i++)
                {
                    var parts = lines[i].Split(';');

                    if (parts[0] == sensorId)
                    {
                        if (parts[1] == "ATIVO")
                            return true;

                        if (parts[1] == "INATIVO")
                        {
                            parts[1] = "ATIVO";
                            lines[i] = string.Join(";", parts);

                            File.WriteAllLines(path, lines);

                            return false;
                        }
                    }
                }

                string novaLinha = $"{sensorId};ATIVO;{DateTime.Now:yyyy-MM-ddTHH:mm:ss}";

                File.AppendAllText(path, novaLinha + Environment.NewLine);

                return false;
            }
            finally
            {
                m.ReleaseMutex();
            }
        }

        static void UpdateSensor(string sensorId, string mensagem)
        {
            string path = "Data/sensores.csv";
            Mutex m = GetFileMutex(path);
            m.WaitOne();
            try
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine("ERRO: sensores.csv não encontrado.");
                    return;
                }

                var lines = File.ReadAllLines(path);
                bool found = false;

                if (mensagem == "DISCONNECT")
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split(';');
                        if (parts[0] == sensorId)
                        {
                            parts[1] = "INATIVO";
                            parts[2] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                            lines[i] = string.Join(";", parts);
                            found = true;
                        }
                    }
                }
                else
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split(';');
                        if (parts[0] == sensorId)
                        {
                            parts[2] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                            lines[i] = string.Join(";", parts);
                            found = true;
                        }
                    }
                if (!found)
                    Console.WriteLine($"AVISO: Sensor '{sensorId}' não encontrado no CSV.");
                else
                    Console.WriteLine($"Sensor '{sensorId}' atualizado no CSV.");

                File.WriteAllLines(path, lines);
            }
            finally
            {
                m.ReleaseMutex();
            }
        }


        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            string sensorId = "";

            int threadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                while (true)
                {
                    string message = ReceiveMessage(stream);

                    if (message.StartsWith("HELLO"))
                    {
                        string[] parts = message.Split(';');

                        sensorId = parts[1];

                        if (SensorExists(sensorId))
                        {
                            SendResponse(stream, "ERROR:SENSOR_IS_ACTIVE");
                            continue;
                        }

                        Console.WriteLine("[DATA] Sensor ID: " + sensorId);
                        SendResponse(stream, "OK");
                    }

                    else if (message.StartsWith("HEARTBEAT"))
                    {
                        Console.WriteLine("Heartbeat de " + message);

                        UpdateSensor(sensorId, null);

                        SendResponse(stream, "HEARTBEAT_OK");
                    }

                    else if (message.Split(';').Length == 5 && message.StartsWith("2"))
                    {
                        string tipo = message.Split(';')[3];

                        if (!Parametros.Contains(tipo))
                        {
                            Console.WriteLine($"Tipo de dado inválido: {tipo}");
                            SendResponse(stream, $"ERROR:INVALID_TYPE:{tipo}");
                            continue;
                        }

                        Console.WriteLine("[DATA] Dados recebidos: " + message);

                        SaveData(message);
                        UpdateSensor(sensorId, null);

                        SendResponse(stream, "DATA_RECEIVED");
                    }

                    else if (message.Contains(";"))
                    {
                        string[] tipos = message.Split(';');
                        List<string> invalidos = ValidarTipos(tipos);

                        if (invalidos.Count > 0)
                        {
                            Console.WriteLine($"[DATA] Tipos inválidos: {string.Join(", ", invalidos)}");
                            SendResponse(stream, $"ERROR:INVALID_TYPES:{string.Join(",", invalidos)}");
                        }
                        else
                            SendResponse(stream, "TYPES_OK");
                    }

                    else if (message == "DISCONNECT")
                    {
                        SendResponse(stream, "BYE");
                        UpdateSensor(sensorId, message);
                        break;
                    }

                    else
                    {
                        SendResponse(stream, "ERROR:UNKNOWN_COMMAND");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Thread {Thread.CurrentThread.ManagedThreadId}] Erro: {e.Message}");
            }
            finally
            {
                client.Close();

                Console.WriteLine($"[Thread {threadId}] Sensor desconectado.");
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

        static void DataSender(int intervalSeconds)
        {
            while (true)
            {
                Thread.Sleep(intervalSeconds * 1000);

                string[] files = Directory.GetFiles("Data", "*.txt");

                foreach (string file in files)
                {
                    Mutex m = GetFileMutex(file);
                    m.WaitOne();
                    string[] lines;
                    try
                    {
                        lines = File.ReadAllLines(file);
                    }
                    finally
                    {
                        m.ReleaseMutex();
                    }
                    if (lines != null)
                    {
                        Console.WriteLine("[DATA] A enviar dados ao servidor...");
                        List<string> falhas = new List<string>();

                        foreach (string line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                bool sucesso = SendToServer(line);
                                if (!sucesso)
                                    falhas.Add(line); // guarda as linhas que falharam
                            }
                        }

                        m.WaitOne();
                        try
                        {
                            if (falhas.Count == 0)
                                File.WriteAllText(file, "");
                            else
                                File.WriteAllLines(file, falhas);
                        }
                        finally
                        {
                            m.ReleaseMutex();
                        }
                    }
                }
                Console.WriteLine("[DATA] Envio concluído.");

            }
        }

        static void Heatbeat_Check(int timeoutSeconds)
        {
            while (true)
            {
                Thread.Sleep(timeoutSeconds * 1000);

                Console.WriteLine("[HB_Check] A verificar sensores...");

                string path = "Data/sensores.csv";
                Mutex m = GetFileMutex(path);
                m.WaitOne();
                try
                {
                    if (!File.Exists(path)) return;

                    var lines = File.ReadAllLines(path);
                    bool alterado = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split(';');
                        if (parts.Length < 3)
                            continue;

                        string sensorId = parts[0];
                        string estado = parts[1];
                        string timestamp = parts[2];

                        if (estado != "ATIVO")
                            continue;

                        DateTime lastSeen = DateTime.Parse(timestamp);
                        double segundos = (DateTime.Now - lastSeen).TotalSeconds;

                        if (segundos > timeoutSeconds)
                        {
                            Console.WriteLine($"[HB_Check] Sensor '{sensorId}' inativo por timeout ({segundos:F0}s).");
                            parts[1] = "INATIVO";
                            lines[i] = string.Join(";", parts);
                            alterado = true;
                        }
                    }

                    if (alterado)
                        File.WriteAllLines(path, lines);
                }
                finally
                {
                    m.ReleaseMutex();
                }

                Console.WriteLine("[HB_Check] Verificação concluída.");
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