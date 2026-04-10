using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Servidor
{
    static Mutex mutex = new Mutex();

    static void Main()
    {
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

            // Mutex garante que apenas um gateway escreve no ficheiro de cada vez
            mutex.WaitOne();
            try
            {
                File.AppendAllText("dados_recebidos.txt", data + Environment.NewLine);
                Console.WriteLine("Guardado em dados_recebidos.txt");
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
}