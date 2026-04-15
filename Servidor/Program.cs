using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Servidor.Data;
using Servidor.Models;

class Program
{
    static Mutex mutex = new Mutex();
    static string ficheiro = "dados_recebidos.txt";

    static void Main()
    {
        Thread worker = new Thread(ProcessarFicheiro);
        worker.Start();

        TcpListener server = new TcpListener(IPAddress.Any, 6000);
        server.Start();
        Console.WriteLine("Servidor iniciado na porta 6000...");

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Thread t = new Thread(() => HandleClient(client));
            t.Start();
        }
    }

    static void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        try
        {
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string data = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            Console.WriteLine("Recebido: " + data);

            mutex.WaitOne();
            try
            {
                File.AppendAllText(ficheiro, data + Environment.NewLine);
                Console.WriteLine("Guardado no ficheiro");
            }
            finally
            {
                mutex.ReleaseMutex();
            }

            byte[] resposta = Encoding.UTF8.GetBytes("DATA_STORED\r\n");
            stream.Write(resposta, 0, resposta.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro: " + ex.Message);
        }
        finally
        {
            client.Close();
        }
    }

    static void ProcessarFicheiro()
    {
        while (true)
        {
            try
            {
                mutex.WaitOne();
                try
                {
                    if (!File.Exists(ficheiro))
                        continue;

                    string[] linhas = File.ReadAllLines(ficheiro);

                    if (linhas.Length == 0)
                        continue;

                    File.WriteAllText(ficheiro, "");

                    foreach (var linha in linhas)
                    {
                        try
                        {
                            string[] partes = linha.Split(';');

                            if (partes.Length != 3)
                                continue;

                            string idSensor = partes[0];
                            string tipo = partes[1];
                            double valor = double.Parse(partes[2]);

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
    }
}