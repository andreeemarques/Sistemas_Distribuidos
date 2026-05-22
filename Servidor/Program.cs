using Servidor.Data;
using Servidor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        Console.WriteLine("[DATA] Servidor iniciado na porta 6000...");

        Thread t1 = new Thread(() => ReceiveVideo(7001));
        t1.IsBackground = true;
        t1.Start();

        Thread webThread = new Thread(StartWebServer);
        webThread.IsBackground = true;
        webThread.Start();

        while (true)
        {
            TcpClient client = server.AcceptTcpClient();
            Thread t = new Thread(() => HandleClient(client));
            t.Start();
        }
    }

    static void StartWebServer()
    {
        HttpListener listener = new HttpListener();

        listener.Prefixes.Add("http://localhost:8080/");

        listener.Start();

        Console.WriteLine("[WEB] Dashboard online em http://localhost:8080");

        while (true)
        {
            HttpListenerContext context = listener.GetContext();

            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            string html = GerarDashboard();

            byte[] buffer = Encoding.UTF8.GetBytes(html);

            response.ContentType = "text/html";
            response.ContentLength64 = buffer.Length;

            response.OutputStream.Write(buffer, 0, buffer.Length);

            response.OutputStream.Close();
        }
    }

    static string GerarDashboard()
    {
        StringBuilder html = new StringBuilder();

        html.Append(@"
    <html>
    <head>
        <title>Dashboard</title>

        <style>
            body{
                font-family: Arial;
                background:#1e1e1e;
                color:white;
                padding:20px;
            }

            table{
                width:100%;
                border-collapse:collapse;
            }

            th, td{
                border:1px solid #555;
                padding:10px;
            }

            th{
                background:#333;
            }

            tr:nth-child(even){
                background:#2a2a2a;
            }
        </style>
    </head>

    <body>
        <h1>Dashboard Sensores</h1>

        <table>
            <tr>
                <th>Sensor</th>
                <th>Tipo</th>
                <th>Valor</th>
                <th>Data</th>
            </tr>
    ");

        using (var db = new AppDbContext())
        {
            var leituras = db.Leituras
                .OrderByDescending(x => x.DataHora)
                .Take(50)
                .ToList();

            foreach (var l in leituras)
            {
                html.Append($@"
            <tr>
                <td>{l.IdSensor}</td>
                <td>{l.Tipo}</td>
                <td>{l.Valor}</td>
                <td>{l.DataHora}</td>
            </tr>
            ");
            }
        }

        html.Append(@"
        </table>
    </body>
    </html>
    ");

        return html.ToString();
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
            string data = header;

            mutex.WaitOne();
            try
            {
                File.AppendAllText("dados_recebidos.txt", data + Environment.NewLine);
                Console.WriteLine("[DATA] Dado guardado!");

                ProcessarFicheiro();
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

                        Console.WriteLine("[DATA] Inserido na BD!");
                        File.WriteAllText("dados_recebidos.txt", "");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[DATA] Erro linha: " + e.Message);
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

                    // FRAMES_END;sensorId
                    if (header.StartsWith("FRAMES_END"))
                    {
                        string sensorId = header.Split(';')[1];
                        SendResponse(stream, "FRAMES_SAVED");
                        Console.WriteLine($"[VIDEO] Todos os frames recebidos para sensor {sensorId}.");
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