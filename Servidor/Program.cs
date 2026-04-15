using Servidor.Data;
using Servidor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;

class Program
{
    static Mutex mutex = new Mutex();

    static void Main()
    {
        TcpListener server = new TcpListener(IPAddress.Any, 6000);
        server.Start();
        Console.WriteLine("Servidor iniciado na porta 6000...");
        Thread t1 = new Thread(() => ReceiveVideo(7001));
        t1.IsBackground = true;
        t1.Start();

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Thread t = new Thread(() => HandleClient(client));
            t.Start();
        }
    }

    static string ReceiveMessage(NetworkStream stream)
    {
        byte[] buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }

    static void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();

            string header = ReceiveMessage(stream);

            Console.WriteLine("Recebido: " + header);

            if (header.StartsWith("FRAME"))
            {
                string[] parts = header.Split(';');
                int frameId = int.Parse(parts[1]);
                int size = int.Parse(parts[2]);

                Console.WriteLine("Chega aqui");

                byte[] frame = new byte[size];
                int total = 0;

                while (total < size)
                {
                    int lidos = stream.Read(frame, total, size - total);
                    total += lidos;
                }

                Directory.CreateDirectory("frames");
                File.WriteAllBytes($"frames/frame_{frameId}.jpg", frame);

                Console.WriteLine($"Frame {frameId} guardado!");

                byte[] resposta = Encoding.UTF8.GetBytes("OK\n");
                stream.Write(resposta, 0, resposta.Length);
            }
            else
            {
                string data = header;

                mutex.WaitOne();
                try
                {
                    File.AppendAllText("dados_recebidos.txt", data + Environment.NewLine);
                    Console.WriteLine("Dados guardados!");

                    ProcessarFicheiro();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }

                byte[] resposta = Encoding.UTF8.GetBytes("DATA_STORED\n");
                stream.Write(resposta, 0, resposta.Length);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Erro: " + e.Message);
        }

        client.Close();
    }

    static void ProcessarFicheiro()
    {
        try
        {
            mutex.WaitOne();
            try
            {
                string[] linhas = File.ReadAllLines("dados_recebidos.txt");

                foreach (var linha in linhas)
                {
                    try
                    {
                        string[] partes = linha.Split(';');

                        string idSensor = partes[1];
                        string tipo = partes[3];
                        double valor = double.Parse(partes[4]);

                        using (var db = new AppDbContext())
                        {
                            var sensor = db.Sensores.Find(idSensor);

                            if (sensor == null)
                            {
                                sensor = new Sensor { IdSensor = idSensor };
                                db.Sensores.Add(sensor);
                            }

                            db.Leituras.Add(new Leitura
                            {
                                IdSensor = idSensor,
                                Tipo = tipo,
                                Valor = valor,
                                DataHora = DateTime.Now
                            });

                            db.SaveChanges();
                        }

                        Console.WriteLine("Inserido na BD!");
                        File.WriteAllText("dados_recebidos.txt", "");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Erro linha: " + e.Message);
                    }
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro: " + ex.Message);
        }

        Thread.Sleep(5000);
    }

    static void ReceiveVideo(int porta)
    {
        TcpListener server = new TcpListener(IPAddress.Any, porta);
        server.Start();

        Console.WriteLine("Servidor à escuta");

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();

            Thread t = new Thread(() => HandleVideoClient(client));
            t.IsBackground = true;
            t.Start();
        }
    }

    static void HandleVideoClient(TcpClient client)
    {
        try
        {
            using (NetworkStream stream = client.GetStream())
            {
                while (true)
                {
                    string header = ReadLine(stream);
                    if (string.IsNullOrEmpty(header)) break;

                    // FRAMES_END;sensorId
                    if (header.StartsWith("FRAMES_END"))
                    {
                        string sensorId = header.Split(';')[1];
                        Console.WriteLine($"[VIDEO] Batch concluído para sensor {sensorId}.");
                        break;
                    }

                    // FRAME;sensorId;frameId;size
                    string[] parts = header.Split(';');
                    if (parts.Length < 4 || parts[0] != "FRAME") break;

                    string sensor = parts[1];
                    int frameId = int.Parse(parts[2]);
                    int size = int.Parse(parts[3]);

                    byte[] frame = ReadExact(stream, size);

                    string pasta = $"frames/{sensor}";
                    Directory.CreateDirectory(pasta);
                    File.WriteAllBytes($"{pasta}/frame_{frameId}.jpg", frame);

                    Console.WriteLine($"[VIDEO] Sensor {sensor} — frame {frameId} guardado ({size} bytes)");
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("[VIDEO] Erro: " + e.Message);
        }
        finally
        {
            client.Close();
        }
    }

    static string ReadLine(NetworkStream stream)
    {
        List<byte> buffer = new List<byte>();
        byte[] temp = new byte[1];

        while (true)
        {
            int read = stream.Read(temp, 0, 1);
            if (read == 0)
                return null;

            if (temp[0] == '\n')
                break;

            buffer.Add(temp[0]);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    static byte[] ReadExact(NetworkStream stream, int size)
    {
        byte[] buffer = new byte[size];
        int total = 0;

        while (total < size)
        {
            int read = stream.Read(buffer, total, size - total);

            if (read == 0)
                throw new Exception("Ligação fechada antes de completar frame");

            total += read;
        }

        return buffer;
    }
}