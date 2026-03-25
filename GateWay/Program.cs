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
        string resposta = message + "\n"; // IMPORTANTE
        byte[] resp = Encoding.UTF8.GetBytes(resposta);
        stream.Write(resp, 0, resp.Length);
    }

    static string ReceiveMessage(NetworkStream stream)
    {
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
        {
            string message = reader.ReadLine();
            return message;
        }
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

                // Se for null, significa que o cliente desligou
                if (message == null) break;

                Console.WriteLine("Recebido: " + message);


                // HELLO;S102

                if (message.StartsWith("HELLO"))
                {
                    // Divide a mensagem pelo separador ';'
                    string[] parts = message.Split(';');

                    // Guarda o ID do sensor
                    sensorId = parts[1];

                    Console.WriteLine("Sensor ID: " + sensorId);

                    // Responde ao sensor
                    SendResponse(stream, "OK");
                }

                // Tipos de dados (TEMP;HUM;RUIDO)
                else if (message.Contains(";") && !message.StartsWith("HEARTBEAT"))
                {
                    // Aqui assumimos que é a lista de tipos de dados
                    SendResponse(stream, "TYPES_OK");
                }

                // Dados ambientais
                // formato: timestamp;id;zona;tipo;valor
                else if (message.Split(';').Length == 5)
                {
                    Console.WriteLine("Dados recebidos: " + message);

                    // Confirma receção ao sensor
                    SendResponse(stream, "DATA_RECEIVED");

                    // Encaminha os dados para o servidor
                    SendToServer(message);
                }

                // HEARTBEAT

                else if (message.StartsWith("HEARTBEAT"))
                {
                    Console.WriteLine("Heartbeat de " + message);

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