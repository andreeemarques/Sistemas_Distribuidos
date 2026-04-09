using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Servidor
{
    static readonly object fileLock = new object();

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
        StringBuilder sb = new StringBuilder();
        byte[] buffer = new byte[1024];

        try
        {
            // Lê todo o conteúdo enviado pelo gateway
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string parte = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                sb.Append(parte);

                // O gateway envia "END" no final para indicar que terminou
                if (sb.ToString().Contains("END"))
                    break;
            }

            // Remove a marca "END" do conteúdo
            string conteudo = sb.ToString().Replace("END", "").Trim();
            Console.WriteLine("Ficheiro recebido:\n" + conteudo);

            // Guarda o conteúdo num ficheiro local com lock
            lock (fileLock)
            {
                string nomeFicheiro = "dados_recebidos.txt";
                File.AppendAllText(nomeFicheiro, conteudo + Environment.NewLine);
                Console.WriteLine("Guardado em " + nomeFicheiro);
            }

            // Responde ao gateway
            byte[] resposta = Encoding.UTF8.GetBytes("FILE_STORED\r\n");
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