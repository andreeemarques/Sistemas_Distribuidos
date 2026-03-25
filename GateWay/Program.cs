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

    static void HandleClient(TcpClient client)
    {
        // Stream de comunicação com o cliente
        NetworkStream stream = client.GetStream();

        // Leitura de mensagens (input)
        StreamReader reader = new StreamReader(stream);

        // Escrita de mensagens (output)
        StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };

        string sensorId = "";

        try
        {
            // Loop para ler mensagens continuamente
            while (true)
            {
                string message = reader.ReadLine();

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
                    writer.WriteLine("OK");
                }

                // Tipos de dados (TEMP;HUM;RUIDO)
                else if (message.Contains(";") && !message.StartsWith("HEARTBEAT"))
                {
                    // Aqui assumimos que é a lista de tipos de dados
                    writer.WriteLine("TYPES_OK");
                }

                // Dados ambientais
                // formato: timestamp;id;zona;tipo;valor
                else if (message.Split(';').Length == 5)
                {
                    Console.WriteLine("Dados recebidos: " + message);

                    // Confirma receção ao sensor
                    writer.WriteLine("DATA_RECEIVED");

                    // Encaminha os dados para o servidor
                    SendToServer(message);
                }

                // HEARTBEAT

                else if (message.StartsWith("HEARTBEAT"))
                {
                    Console.WriteLine("Heartbeat de " + message);

                    // Resposta opcional
                    writer.WriteLine("HEARTBEAT_OK");
                }

                // DISCONNECT
                else if (message == "DISCONNECT")
                {
                    writer.WriteLine("BYE");
                    break; // sai do ciclo
                }

                // Mensagem desconhecida
                else
                {
                    writer.WriteLine("ERROR:UNKNOWN_COMMAND");
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

            StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };
            StreamReader reader = new StreamReader(stream);

            // Envia os dados recebidos do sensor
            writer.WriteLine(data);

            // Espera resposta do servidor
            string response = reader.ReadLine();

            Console.WriteLine("Servidor respondeu: " + response);

            serverClient.Close();
        }
        catch
        {
            Console.WriteLine("Erro ao conectar ao servidor.");
        }
    }
}