using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Gateway
{


    static void Main()
    {
        int port = 5000;

        // Cria um servidor TCP a escutar em qualquer IP na porta definida
        TcpListener server = new TcpListener(IPAddress.Any, port);
        server.Start();

        Console.WriteLine("Gateway iniciado na porta " + port);

        // Loop infinito para aceitar vários sensores
        while (true)
        {
            // Espera até um sensor se conectar
            TcpClient client = server.AcceptTcpClient();
            Console.WriteLine("Sensor conectado!");

            // Trata a comunicação com esse sensor
            HandleClient(client);
        }
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

        //Directory.CreateDirectory("data");

        File.AppendAllText(path, message + Environment.NewLine);
    }

    static bool SensorExists(string sensorId)
    {
        string path = "Data/sensores.csv";

        if (!File.Exists(path)) return false;

        var lines = File.ReadAllLines(path);

        foreach (var line in lines)
        {
            if (line.StartsWith(sensorId + ";"))
                return true;
        }

        return false;
    }

    static void UpdateSensor(string sensorId)
    {
        string path = "Data/sensores.csv";

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

    static void HandleClient(TcpClient client)
    {
        // Stream de comunicação com o cliente
        NetworkStream stream = client.GetStream();

        string sensorId = "";

        try
        {
            // Loop para ler mensagens continuamente
            while (true)
            {
                string message = ReceiveMessage(stream);

                Console.WriteLine("Recebido: " + message);


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

                    // Responde ao sensor
                    SendResponse(stream, "OK");
                }

                // Tipos de dados (TEMP;HUM;RUIDO)
                else if (message.Contains(";") && !message.StartsWith("HEARTBEAT") && !message.StartsWith("2"))
                {
                    // Aqui assumimos que é a lista de tipos de dados
                    SendResponse(stream, "TYPES_OK");
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

                // HEARTBEAT

                else if (message.StartsWith("HEARTBEAT"))
                {
                    Console.WriteLine("Heartbeat de " + message);

                    UpdateSensor(sensorId);

                    // Resposta opcional
                    SendResponse(stream, "HEARTBEAT_OK");
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
            Console.WriteLine("Erro: " + e.Message);
        }

        // Fecha a ligação com o sensor
        client.Close();
        Console.WriteLine("Sensor desconectado.");
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