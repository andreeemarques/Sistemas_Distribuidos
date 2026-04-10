using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

class Gateway
{
    // Um mutex por ficheiro para garantir acesso sequencial
    static Dictionary<string, Mutex> fileMutexes = new Dictionary<string, Mutex>();
    static Mutex dictMutex = new Mutex(); // protege o próprio dicionário

    static Mutex dataMutex = new Mutex();

    static Mutex GetFileMutex(string path)
    {
        dictMutex.WaitOne();
        if (!fileMutexes.ContainsKey(path))
            fileMutexes[path] = new Mutex();
        Mutex m = fileMutexes[path];
        dictMutex.ReleaseMutex();
        return m;
    }

    static readonly List<string> Parametros = new List<string>
    {
        "TEMP", "HUM", "RUIDO", "AR", "PM2.5", "PM10", "LUM"
       };

    static List<string> ValidarTipos(string[] tipos)
    {
        List<string> invalidos = new List<string>();
        foreach (string tipo in tipos)
            if (!Parametros.Contains(tipo.Trim()))
                invalidos.Add(tipo.Trim());
        return invalidos;
    }

    static void Main()
    {
        int port = 5000;
        TcpListener server = new TcpListener(IPAddress.Any, port);
        server.Start();
        Console.WriteLine("Gateway iniciado na porta " + port);

        Thread senderThread = new Thread(() => BatchSender(30)); // 30 segundos
        senderThread.IsBackground = true;
        senderThread.Start();

        Thread watchdogThread = new Thread(() => Watchdog(60)); // 60 segundos sem mensagem → inativo
        watchdogThread.IsBackground = true;
        watchdogThread.Start();

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Console.WriteLine("Sensor conectado!");

            // Cria uma thread por sensor — concorrência
            Thread t = new Thread(() => HandleClient(client));
            t.IsBackground = true;
            t.Start();
        }
    }

    static void SendResponse(NetworkStream stream, string message)
    {
        string resposta = message;
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
    }

    static bool SensorExists(string sensorId)
    {
        string path = "Data/sensores.csv";

        Mutex m = GetFileMutex(path);
        m.WaitOne();

        try
        {
            // Se ficheiro não existir, cria-o
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
                    // Sensor já está ativo
                    if (parts[1] == "ATIVO")
                        return true;

                    // Sensor existe mas está inativo
                    if (parts[1] == "INATIVO")
                    {
                        parts[1] = "ATIVO";
                        lines[i] = string.Join(";", parts);

                        File.WriteAllLines(path, lines);

                        return false;
                    }
                }
            }

            // Sensor não existe → adicionar nova linha
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
            // Loop para ler mensagens continuamente
            while (true)
            {
                string message = ReceiveMessage(stream).Trim();

                Console.WriteLine($"[Thread {Thread.CurrentThread.ManagedThreadId}] Recebido: {message}");

                // HELLO;S102

                if (message.StartsWith("HELLO"))
                {
                    // Divide a mensagem pelo separador ';'
                    string[] parts = message.Split(';');

                    // Guarda o ID do sensor
                    sensorId = parts[1];

                    if (SensorExists(sensorId))
                    {
                        SendResponse(stream, "ERROR:SENSOR_IS_ACTIVE");
                        continue;
                    }

                    Console.WriteLine("Sensor ID: " + sensorId);
                    // Responde ao sensor
                    SendResponse(stream, "OK");
                }

                // HEARTBEAT
                else if (message.StartsWith("HEARTBEAT"))
                {
                    Console.WriteLine("Heartbeat de " + message);

                    UpdateSensor(sensorId, null);

                    // Resposta opcional
                    SendResponse(stream, "HEARTBEAT_OK");
                }

                // Dados ambientais
                // formato: timestamp;id;zona;tipo;valor
                else if (message.Split(';').Length == 5 && message.StartsWith("2"))
                {
                    string tipo = message.Split(';')[3];

                    if (!Parametros.Contains(tipo))
                    {
                        Console.WriteLine($"Tipo de dado inválido: {tipo}");
                        SendResponse(stream, $"ERROR:INVALID_TYPE:{tipo}");
                        continue; // ignora este dado
                    }

                    dataMutex.WaitOne();
                    try
                    {
                        Console.WriteLine("Dados recebidos: " + message);

                        SaveData(message);
                        UpdateSensor(sensorId, null);

                        SendResponse(stream, "DATA_RECEIVED");
                        
                    }
                    finally
                    {
                        dataMutex.ReleaseMutex();
                    }
                }

                // Tipos de dados
                else if (message.Contains(";"))
                {
                    string[] tipos = message.Split(';');
                    List<string> invalidos = ValidarTipos(tipos);

                    if (invalidos.Count > 0)
                    {
                        Console.WriteLine($"Tipos inválidos: {string.Join(", ", invalidos)}");
                        SendResponse(stream, $"ERROR:INVALID_TYPES:{string.Join(",", invalidos)}");
                    }
                    else
                        SendResponse(stream, "TYPES_OK");
                }

                // DISCONNECT
                else if (message == "DISCONNECT")
                {
                    SendResponse(stream, "BYE");
                    UpdateSensor(sensorId, null);
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
            // Liga ao servidor (porta 6000)
            TcpClient serverClient = new TcpClient("127.0.0.1", 6000);

            NetworkStream stream = serverClient.GetStream();

            // Envia os dados recebidos do sensor
            SendResponse(stream, data);

            // Espera resposta do servidor
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

    static void BatchSender(int intervalSeconds)
    {
        while (true)
        {
            Thread.Sleep(intervalSeconds * 1000);

            Console.WriteLine("[Batch] A enviar dados ao servidor...");

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

                // Reescreve o ficheiro apenas com as linhas que falharam
                m.WaitOne();
                try
                {
                    if (falhas.Count == 0)
                        File.WriteAllText(file, ""); // tudo enviado, limpa
                    else
                        File.WriteAllLines(file, falhas); // mantém só as falhas
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }

            Console.WriteLine("[Batch] Envio concluído.");
        }
    }

    static void Watchdog(int timeoutSeconds)
    {
        while (true)
        {
            Thread.Sleep(timeoutSeconds * 1000);

            Console.WriteLine("[Watchdog] A verificar sensores...");

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
                    if (parts.Length < 3) continue;

                    string sensorId = parts[0];
                    string estado = parts[1];
                    string timestamp = parts[2];

                    // Só verifica sensores ativos
                    if (estado != "ATIVO") continue;

                    DateTime lastSeen = DateTime.Parse(timestamp);
                    double segundos = (DateTime.Now - lastSeen).TotalSeconds;

                    if (segundos > timeoutSeconds)
                    {
                        Console.WriteLine($"[Watchdog] Sensor '{sensorId}' inativo por timeout ({segundos:F0}s).");
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

            Console.WriteLine("[Watchdog] Verificação concluída.");
        }
    }
}