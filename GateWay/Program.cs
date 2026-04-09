using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

class Gateway
{
    // ── Configurações ──────────────────────────────────────────
    const int PORT = 5000;
    const int MAX_SENSORS = 5;          // limite de sensores ligados
    const int CLIENT_TIMEOUT_MS = 30_000;     // 30s sem mensagem → desliga
    const int MSG_INTERVAL_MS = 2_000;      // intervalo mínimo entre mensagens
    const int QUEUE_FLUSH_SEC = 60;         // envia fila a cada 60s


    // Um mutex por ficheiro para garantir acesso sequencial
    static Dictionary<string, Mutex> fileMutexes = new Dictionary<string, Mutex>();
    static Mutex dictMutex = new Mutex(); // protege o próprio dicionário

    // ── Controlo de sensores ───────────────────────────────────
    static int activeSensors = 0;
    static Mutex sensorCountMutex = new Mutex();

    // ── Fila de ficheiros a enviar ─────────────────────────────
    static Queue<string> fileQueue = new Queue<string>();
    static Mutex queueMutex = new Mutex();

    // ── Rate limiting (ultimo envio por sensor) ────────────────
    static Dictionary<string, DateTime> lastMessageTime = new Dictionary<string, DateTime>();
    static Mutex rateMutex = new Mutex();

    static Mutex GetFileMutex(string path)
    {
        dictMutex.WaitOne();
        if (!fileMutexes.ContainsKey(path))
            fileMutexes[path] = new Mutex();
        Mutex m = fileMutexes[path];
        dictMutex.ReleaseMutex();
        return m;
    }

    static void Main()
    {
        // Thread dedicada a processar a fila de ficheiros
        Thread queueThread = new Thread(ProcessFileQueue);
        queueThread.IsBackground = true;
        queueThread.Start();

        // Cria um servidor TCP a escutar em qualquer IP na porta definida
        TcpListener server = new TcpListener(IPAddress.Any, PORT);
        server.Start();
        Console.WriteLine("Gateway iniciado na porta {PORT}");

        // Loop infinito para aceitar vários sensores
        while (true)
        {
            // Verifica limite de sensores
            sensorCountMutex.WaitOne();
            if (activeSensors >= MAX_SENSORS)
            {
                sensorCountMutex.ReleaseMutex();
                Console.WriteLine("Limite de sensores atingido. Ligação recusada.");

                // Avisa o sensor e fecha
                NetworkStream s = client.GetStream();
                SendResponse(s, "ERROR:MAX_SENSORS_REACHED");
                client.Close();
                continue;
            }
            activeSensors++;
            sensorCountMutex.ReleaseMutex();

            Console.WriteLine($"Sensor conectado! ({activeSensors}/{MAX_SENSORS})");

            // Cria uma thread por sensor — concorrência
            Thread t = new Thread(() => HandleClient(client));
            t.IsBackground = true;
            t.Start();
        }
    }

    // ── Fila de ficheiros ──────────────────────────────────────

    static void EnqueueFile(string path)
    {
        queueMutex.WaitOne();
        if (!fileQueue.Contains(path))
            fileQueue.Enqueue(path);
        queueMutex.ReleaseMutex();
    }

    static void ProcessFileQueue()
    {
        while (true)
        {
            Thread.Sleep(QUEUE_FLUSH_SEC * 1000);

            queueMutex.WaitOne();
            List<string> toSend = new List<string>(fileQueue);
            fileQueue.Clear();
            queueMutex.ReleaseMutex();

            foreach (string path in toSend)
            {
                if (!File.Exists(path)) continue;

                Console.WriteLine($"A enviar ficheiro ao servidor: {path}");
                bool sent = SendFileToServer(path);

                if (sent)
                {
                    // Apaga o ficheiro depois de enviado
                    Mutex m = GetFileMutex(path);
                    m.WaitOne();
                    try { File.Delete(path); Console.WriteLine($"Ficheiro apagado: {path}"); }
                    finally { m.ReleaseMutex(); }
                }
                else
                {
                    // Falhou — volta para a fila
                    Console.WriteLine($"Falhou envio de {path}, será reentado.");
                    EnqueueFile(path);
                }
            }
        }
    }

    static bool SendFileToServer(string path)
    {
        try
        {
            byte[] fileBytes = File.ReadAllBytes(path);
            string fileName = Path.GetFileName(path);

            TcpClient serverClient = new TcpClient("127.0.0.1", 6000);
            NetworkStream stream = serverClient.GetStream();

            // Header com nome e tamanho
            string header = $"FILE;{fileName};{fileBytes.Length}";
            SendResponse(stream, header);

            string ack = ReceiveMessage(stream).Trim();
            if (ack != "FILE_READY") { serverClient.Close(); return false; }

            // Envia os bytes em chunks
            int chunkSize = 4096, offset = 0;
            while (offset < fileBytes.Length)
            {
                int size = Math.Min(chunkSize, fileBytes.Length - offset);
                stream.Write(fileBytes, offset, size);
                offset += size;
            }

            string response = ReceiveMessage(stream).Trim();
            serverClient.Close();
            return response == "FILE_RECEIVED";
        }
        catch
        {
            Console.WriteLine($"Erro ao enviar ficheiro {path} ao servidor.");
            return false;
        }
    }

    // ── Rate limiting ──────────────────────────────────────────

    static bool IsRateLimited(string sensorId)
    {
        rateMutex.WaitOne();
        bool limited = false;
        if (lastMessageTime.ContainsKey(sensorId))
        {
            double elapsed = (DateTime.Now - lastMessageTime[sensorId]).TotalMilliseconds;
            if (elapsed < MSG_INTERVAL_MS) limited = true;
        }
        if (!limited) lastMessageTime[sensorId] = DateTime.Now;
        rateMutex.ReleaseMutex();
        return limited;
    }

    static void SendResponse(NetworkStream stream, string message)
    {
        string resposta = message + "\n";
        byte[] resp = Encoding.UTF8.GetBytes(resposta);
        stream.Write(resp, 0, resp.Length);
    }

    static string ReceiveMessage(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        string resposta = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return resposta;
    }

    static void SaveData(string message)
    {
        string[] parts = message.Split(';');

        string tipo = parts[3]; // TEMP, HUM, etc.

        string path = $"Data/{tipo}.txt";

        // Garante acesso sequencial ao ficheiro
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
        // Adiciona à fila de envio
        EnqueueFile(path);
    }

    static bool SensorExists(string sensorId)
    {
        string path = "Data/sensores.csv";

        Mutex m = GetFileMutex(path);
        m.WaitOne();
        try
        {
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path);
            foreach (var line in lines)
                if (line.StartsWith(sensorId + ";"))
                    return true;
            return false;
        }
        finally
        {
            m.ReleaseMutex();
        }
    }

    static void UpdateSensor(string sensorId)
    {
        string path = "Data/sensores.csv";

        Mutex m = GetFileMutex(path);
        m.WaitOne();
        try
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(';');
                if (parts[0] == sensorId)
                {
                    parts[4] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                    lines[i] = string.Join(";", parts);
                }
            }
            File.WriteAllLines(path, lines);
        }
        finally
        {
            m.ReleaseMutex();
        }
    }

    static void HandleClient(TcpClient client)
    {
        // Timeout: se não chegar mensagem em CLIENT_TIMEOUT_MS → excepção e desliga
        client.ReceiveTimeout = CLIENT_TIMEOUT_MS;

        // Stream de comunicação com o cliente
        NetworkStream stream = client.GetStream();
        string sensorId = "";
        // Timeout: se não chegar mensagem em CLIENT_TIMEOUT_MS → excepção e desliga
        client.ReceiveTimeout = CLIENT_TIMEOUT_MS;

        try
        {
            // Loop para ler mensagens continuamente
            while (true)
            {
                string message = ReceiveMessage(stream);

                Console.WriteLine($"[Thread {Thread.CurrentThread.ManagedThreadId}] Recebido: {message}");

                // Rate limiting (só para dados e heartbeat, não para HELLO)
                if (!message.StartsWith("HELLO") && !message.StartsWith("DISCONNECT"))
                {
                    if (IsRateLimited(sensorId))
                    {
                        Console.WriteLine($"[Thread {threadId}] Sensor {sensorId} em rate limit.");
                        SendResponse(stream, "ERROR:RATE_LIMITED");
                        continue;
                    }
                }

                // HELLO;S102

                if (message.StartsWith("HELLO"))
                {
                    // Divide a mensagem pelo separador ';'
                    string[] parts = message.Split(';');

                    // Guarda o ID do sensor
                    sensorId = parts[1];

                    if (!SensorExists(sensorId))
                    {
                        SendResponse(stream, "ERROR:SENSOR_NOT_REGISTERED");
                        continue;
                    }

                    Console.WriteLine("Sensor ID: " + sensorId);
                    UpdateSensor(sensorId);
                    // Responde ao sensor
                    SendResponse(stream, "OK");
                }

                // HEARTBEAT
                else if (message.StartsWith("HEARTBEAT"))
                {
                    Console.WriteLine("Heartbeat de " + message);

                    UpdateSensor(sensorId);

                    // Resposta opcional
                    SendResponse(stream, "HEARTBEAT_OK");
                }

                // Dados ambientais
                // formato: timestamp;id;zona;tipo;valor
                else if (message.Split(';').Length == 5)
                {
                    Console.WriteLine("Dados recebidos: " + message);

                    SaveData(message);
                    UpdateSensor(sensorId);

                    SendResponse(stream, "DATA_RECEIVED");
                    SendToServer(message);
                }

                // Tipos de dados
                else if (message.Contains(";"))
                {
                    // Aqui assumimos que é a lista de tipos de dados
                    SendResponse(stream, "TYPES_OK");
                }

                // DISCONNECT
                else if (message == "DISCONNECT")
                {
                    SendResponse(stream, "BYE");
                    break; // sai do ciclo
                }

                // Mensagem desconhecida
                else
                {
                    SendResponse(stream, "ERROR:UNKNOWN_COMMAND");
                    break;
                }
            }
        }
        catch (IOException)
        {
            // ReceiveTimeout expirou ou ligação perdida
            Console.WriteLine($"[Thread {threadId}] Sensor {sensorId} timeout ou ligação perdida.");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Thread {Thread.CurrentThread.ManagedThreadId}] Erro: {e.Message}");
        }
        finally
        {
            client.Close();

            // Decrementa contador de sensores ativos
            sensorCountMutex.WaitOne();
            activeSensors--;
            sensorCountMutex.ReleaseMutex();

            Console.WriteLine($"[Thread {threadId}] Sensor desconectado. ({activeSensors}/{MAX_SENSORS})");
        }
    }

    static void SendToServer(string data)
    {
        try
        {
            // Liga ao servidor (porta 6000)
            TcpClient serverClient = new TcpClient("127.0.0.1", 6000);

            NetworkStream stream = serverClient.GetStream();

            // Envia os dados recebidos do sensor
            SendResponse(stream, data);

            // Espera resposta do servidor
            string response = ReceiveMessage(stream);

            Console.WriteLine("Servidor respondeu: " + response);

            serverClient.Close();
        }
        catch
        {
            Console.WriteLine("Erro ao conectar ao servidor.");
        }
    }
}