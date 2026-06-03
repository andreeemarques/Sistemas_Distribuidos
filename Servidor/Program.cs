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
    static AnalysisClient analysisClient = new AnalysisClient(); // NOVO

    static void Main()
    {
        TcpListener server = new TcpListener(IPAddress.Any, 6000);
        server.Start();
        Console.WriteLine("[DATA] Servidor iniciado na porta 6000...");

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

    static void SendResponse(NetworkStream stream, string message)
    {
        byte[] resp = Encoding.UTF8.GetBytes(message);
        stream.Write(resp, 0, resp.Length);
    }

    static void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();

            string header = ReceiveMessage(stream);

            mutex.WaitOne();
            try
            {
               // File.AppendAllText("dados_recebidos.txt", header + Environment.NewLine);
                Console.WriteLine("[DATA] Dado guardado!");

                ProcessarFicheiro(header);
            }
            finally
            {
                mutex.ReleaseMutex();
            }

            SendResponse(stream, "DATA_STORED");
        }
        catch (Exception e)
        {
            Console.WriteLine("Erro: " + e.Message);
        }

        client.Close();
    }

    static void ProcessarFicheiro(string mensagem)
    {
        try
        { 
                    try
                    {
                        string[] partes = mensagem.Split(';');

                        string idSensor = partes[1];
                        string tipo = partes[3];
                        double valor = double.Parse(partes[4], System.Globalization.CultureInfo.InvariantCulture);

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

                        Console.WriteLine("[DATA] Inserido na BD!");

                        AnalisarDados(idSensor, tipo, valor); // NOVO

                        File.WriteAllText("dados_recebidos.txt", "");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[DATA] Erro linha: " + e.Message);
                    }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Erro: " + ex.Message);
        }

        Thread.Sleep(5000);
    }

    // NOVO — Invocar serviço de análise via RPC
    static void AnalisarDados(string idSensor, string tipo, double valor)
    {
        try
        {
            var valores = new List<double> { valor };

            // Análise estatística
            analysisClient.GetStatistics(idSensor, tipo, valores);

            // Deteção de padrões de poluição
            var anomalia = analysisClient.DetectAnomalies(idSensor, tipo, valores);
            if (anomalia != null && anomalia.AnomaliaDetetada)
                Console.WriteLine($"[RPC] Poluição detetada no sensor {idSensor}: {anomalia.Descricao}");

            // Previsão de riscos para a saúde pública
            var risco = analysisClient.PredictHealthRisk(idSensor, new List<string> { tipo }, valores);
            if (risco != null)
                Console.WriteLine($"[RPC] Risco saúde pública: {risco.NivelRisco} — {risco.Descricao}");
        }
        catch (Exception e)
        {
            Console.WriteLine("[RPC] Erro análise: " + e.Message);
        }
    }

    static void ReceiveVideo(int porta)
    {
        TcpListener server = new TcpListener(IPAddress.Any, porta);
        server.Start();
        Console.WriteLine("[VIDEO] Servidor à escuta");

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

                    if (header.StartsWith("FRAMES_END"))
                    {
                        string sensorId = header.Split(';')[1];
                        SendResponse(stream, "FRAMES_SAVED");
                        Console.WriteLine($"[VIDEO] Todos os frames recebidos para sensor {sensorId}.");
                        break;
                    }

                    string[] parts = header.Split(';');
                    if (parts.Length < 4 || parts[0] != "FRAME") break;

                    string sensor = parts[1];
                    int frameId = int.Parse(parts[2]);
                    int size = int.Parse(parts[3]);

                    byte[] frame = ReadExact(stream, size);

                    string pasta = $"frames/{sensor}";
                    Directory.CreateDirectory(pasta);
                    File.WriteAllBytes($"{pasta}/frame_{frameId}.jpg", frame);
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
            if (read == 0) return null;
            if (temp[0] == '\n') break;
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
            if (read == 0) throw new Exception("Ligação fechada antes de completar frame");
            total += read;
        }

        return buffer;
    }
}