using System;
using System.Collections.Generic;
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
            Console.Write("ID do Sensor: ");
            string id = Console.ReadLine();

            while (true)
            {
                Console.WriteLine("\n1 - HELLO");
                Console.WriteLine("2 - Enviar tipos");
                Console.WriteLine("3 - Enviar dados");
                Console.WriteLine("4 - Heartbeat");
                Console.WriteLine("5 - Disconnect");

                string op = Console.ReadLine();
                string msg = "";

                switch (op)
                {
                    case "1":
                        msg = $"HELLO;{id}";
                        break;
                    case "2":
                        msg = "TEMP;HUM;RUIDO";
                        break;
                    case "3":
                        msg = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss};{id};ZONA_ESCOLAR;TEMP;23";
                        break;
                    case "4":
                        msg = $"HEARTBEAT;{id}";
                        break;
                    case "5":
                        msg = "DISCONNECT";
                        break;
                }

                TcpClient client = new TcpClient("127.0.0.1", 5000);
                var stream = client.GetStream();

                byte[] data = Encoding.UTF8.GetBytes(msg);
                stream.Write(data, 0, data.Length);

                byte[] buffer = new byte[1024];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);

                string resposta = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine("Gateway respondeu: " + resposta);

                // lógica baseada na resposta
                if (resposta == "OK")
                {
                    Console.WriteLine("Ligação estabelecida com sucesso!");
                }
                else if (resposta == "TYPES_OK")
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

                client.Close();
            }
        }
    }
}
