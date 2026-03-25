using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Servidor
{
    static void Main()
    {
        TcpListener server = new TcpListener(IPAddress.Any, 6000);
        server.Start();
        Console.WriteLine("Servidor iniciado...");

        while (true)
        {
            var client = server.AcceptTcpClient();  
            var stream = client.GetStream();

            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine("Recebido: " + data);

            string resposta = "DATA_STORED";
            byte[] resp = Encoding.UTF8.GetBytes(resposta);
            stream.Write(resp, 0, resp.Length);

            client.Close();
        }
    }
}