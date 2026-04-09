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
        int port = 5000;
        TcpListener server = new TcpListener(IPAddress.Any, port);
        server.Start();
        Console.WriteLine("Gateway iniciado na porta " + port);

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

    static void UpdateSensor(string sensorId)
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

            var lines = File.ReadAllLines(path, Encoding.UTF8);

            if (lines.Length > 0)
                lines[0] = lines[0].TrimStart('\uFEFF');

            lines = File.ReadAllLines(path);
            bool found = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(';');
                if (parts[0].Trim() == sensorId.Trim())
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
        finally { m.ReleaseMutex(); }
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
                        SendResponse(stream, "ERROR:SENSOR_NOT_REGISTERED");
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

                    UpdateSensor(sensorId);

                    // Resposta opcional
                    SendResponse(stream, "HEARTBEAT_OK");
                }

                // Dados ambientais
                // formato: timestamp;id;zona;tipo;valor
                else if (message.Split(';').Length == 5 && message.StartsWith("2"))
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