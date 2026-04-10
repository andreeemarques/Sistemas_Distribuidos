using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Sensor
{
    internal class Program
    {
        static readonly List<string> Parametros = new List<string>
        {
            "TEMP", "HUM", "RUIDO", "AR", "PM2.5", "PM10", "LUM"
        };

        static readonly List<string> Zonas = new List<string>
        {
            "ZONA_ESCOLAR", "ZONA_CENTRO", "ZONA_INDUSTRIAL",
            "ZONA_RESIDENCIAL", "ZONA_PARQUE", "ZONA_AVENIDA"
        };

        static int GerarValor(string parametro, Random rnd)
        {
            switch (parametro)
            {
                case "TEMP":
                    return rnd.Next(15, 40);

                case "HUM":
                    return rnd.Next(30, 95);

                case "RUIDO":
                    return rnd.Next(40, 110);

                case "AR":
                    return rnd.Next(0, 200);

                case "PM2.5":
                    return rnd.Next(0, 150);

                case "PM10":
                    return rnd.Next(0, 200);

                case "LUM":
                    return rnd.Next(0, 1000);

                default:
                    return rnd.Next(0, 100);
            }
        }

        static void Main(string[] args)
        {
            TcpClient client = new TcpClient("127.0.0.1", 5000);
            var stream = client.GetStream();

            DateTime ultimoHeartbeat = DateTime.Now;

            Console.Write("ID do Sensor: ");
            string id = Console.ReadLine();

            Send(stream, $"HELLO;{id}");
            string resposta = Receive(stream);

            if (string.Compare(resposta, "OK") != 0)
            {
                Console.WriteLine("Erro na ligação: " + resposta);
                return;
            }

            Console.WriteLine("Ligado ao Gateway!");

            Random rnd = new Random();

            while (true)
            {
                if ((DateTime.Now - ultimoHeartbeat).TotalMinutes >= 2)
                {
                    Send(stream, $"HEARTBEAT;{id}");
                    Console.WriteLine("Heartbeat -> " + Receive(stream));

                    ultimoHeartbeat = DateTime.Now;
                }
                else
                {
                    int numParametros = rnd.Next(2, Parametros.Count + 1);

                    List<string> parametrosSelecionados = new List<string>(Parametros);
                    for (int i = parametrosSelecionados.Count - 1; i > 0; i--)
                    {
                        int j = rnd.Next(i + 1);
                        (parametrosSelecionados[i], parametrosSelecionados[j]) =
                            (parametrosSelecionados[j], parametrosSelecionados[i]);
                    }
                    parametrosSelecionados = parametrosSelecionados.GetRange(0, numParametros);

                    string tiposMsg = string.Join(";", parametrosSelecionados);
                    Send(stream, tiposMsg);
                    string conf_tipos = Receive(stream);

                    if (string.Compare(conf_tipos, "TYPES_OK") == 0)
                    {
                        Console.WriteLine("Tipos registados -> " + conf_tipos);
                        Console.WriteLine($"Parâmetros activos ({numParametros}): {tiposMsg}");
                    }
                    else
                    {
                        Console.WriteLine($"Erro:{conf_tipos}");
                        break;
                    }

                    string zona = Zonas[rnd.Next(Zonas.Count)];
                    string timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

                    foreach (string parametro in parametrosSelecionados)
                    {
                        int valor = GerarValor(parametro, rnd);
                        string msg = $"{timestamp};{id};{zona};{parametro};{valor}";

                        Send(stream, msg);
                        string confirmacao = Receive(stream);

                        if (string.Compare(confirmacao, "DATA_RECEIVED") == 0)
                            Console.WriteLine($"{parametro}={valor} - DATA_RECEIVED");
                        else
                        {
                            Console.WriteLine($"Erro:{confirmacao} - {parametro}");
                            break;
                        }
                    }
                }

                Thread.Sleep(60000);
            }

            Send(stream, "DISCONNECT");
            Console.WriteLine(Receive(stream));
            client.Close();
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
