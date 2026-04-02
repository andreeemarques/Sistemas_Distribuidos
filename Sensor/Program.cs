using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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

            Send(stream, $"HELLO;{id}");
            string resposta = Receive(stream);

            if (resposta != "OK")
            {
                Console.WriteLine("Erro na ligação.");
                return;
            }

            Console.WriteLine("Ligado ao Gateway!");

            Send(stream, "TEMP;HUM;RUIDO");
            Console.WriteLine("Tipos -> " + Receive(stream));

            Random rnd = new Random();

            while (true)
            {
                int temp = rnd.Next(15, 35);
                int hum = rnd.Next(30, 90);
                int ruido = rnd.Next(40, 100);

                string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                string msgTemp = $"{timestamp};{id};ZONA_ESCOLAR;TEMP;{temp}";
                Send(stream, msgTemp);
                Console.WriteLine("TEMP -> " + Receive(stream));

                string msgHum = $"{timestamp};{id};ZONA_ESCOLAR;HUM;{hum}";
                Send(stream, msgHum);
                Console.WriteLine("HUM -> " + Receive(stream));

                string msgRuido = $"{timestamp};{id};ZONA_ESCOLAR;RUIDO;{ruido}";
                Send(stream, msgRuido);
                Console.WriteLine("RUIDO -> " + Receive(stream));

                Send(stream, $"HEARTBEAT;{id}");
                Console.WriteLine("Heartbeat -> " + Receive(stream));

                Thread.Sleep(60000);
            }
        }
        static void Send(NetworkStream stream, string msg)
        {
            byte[] data = Encoding.UTF8.GetBytes(msg);
            stream.Write(data, 0, data.Length);
        }

        static string Receive(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }
    }
}
