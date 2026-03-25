using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Sensor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            TcpClient client = new TcpClient("127.0.0.1", 5000);
            var stream = client.GetStream();

            Console.Write("ID do Sensor: ");
            string id = Console.ReadLine();

            string first_msg = $"HELLO;{id}";

            byte[] data_first = Encoding.UTF8.GetBytes(first_msg);
            stream.Write(data_first, 0, data_first.Length);

            byte[] buffer_first = new byte[1024];
            int bytesRead_first = stream.Read(buffer_first, 0, buffer_first.Length);
            string resposta_first = Encoding.UTF8.GetString(buffer_first, 0, bytesRead_first);

            if (resposta_first == "OK")
            {
                Console.WriteLine("Ligação estabelecida com sucesso!");

                while (true)
                {
                    Console.WriteLine("\n1 - Enviar tipos");
                    Console.WriteLine("2 - Enviar dados");
                    Console.WriteLine("3 - Heartbeat");
                    Console.WriteLine("4 - Disconnect");

                    string op = Console.ReadLine();
                    string msg = "";

                    switch (op)
                    {
                        case "1":
                            msg = "TEMP;HUM;RUIDO";
                            break;
                        case "2":
                            msg = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss};{id};ZONA_ESCOLAR;TEMP;23";
                            break;
                        case "3":
                            msg = $"HEARTBEAT;{id}";
                            break;
                        case "4":
                            msg = "DISCONNECT";
                            break;
                    }

                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    stream.Write(data, 0, data.Length);

                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);

                    string resposta = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine("Gateway respondeu: " + resposta);

                    // lógica baseada na resposta
                    if (resposta == "TYPES_OK")
                    {
                        Console.WriteLine("Tipos de dados aceites.");
                    }
                    else if (resposta == "DATA_RECEIVED")
                    {
                        Console.WriteLine("Dados enviados com sucesso.");
                    }
                    else if (resposta == "HEARTBEAT_OK")
                    {
                        Console.WriteLine("Sensor ativo confirmado.");
                    }
                    else if (resposta == "BYE")
                    {
                        Console.WriteLine("Ligação terminada.");
                        break;
                    }
                    else if (resposta.StartsWith("ERROR"))
                    {
                        Console.WriteLine("Erro do gateway: " + resposta);
                    }
                }
            }

            client.Close();
        }
    }
}
