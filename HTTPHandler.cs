using System.Net.Sockets;
using System.Text;

namespace ReeWebServer;

public class HTTPHandler
{
    public static async Task ProcessHttpClient(TcpClient client)
    {
        try
        {
            using (var stream = client.GetStream())
            {
                string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown IP";
                Console.WriteLine($"HTTP - Connection from {remoteIp}");
                using (var reader = new StreamReader(stream, Encoding.ASCII, true, 1024, true)) // Leave stream open for body reading
                using (var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true })
                {
                    await DataParser.ParseData(client, reader, writer, true);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Major Fatal Error in ProcessHttpClient: " + ex.Message);
        }
        finally
        {
            Console.WriteLine("HTTP - Completed");
            Console.WriteLine("-------------------------------------------------------------------------------------");
            client.Close();
        }
    }

    public static async Task RedirectToHTTPS(StreamWriter writer, string host, string requestUri)
    {
        Console.WriteLine("--> Redirecting to HTTPS.");
        if (!string.IsNullOrEmpty(host))
        {
            string redirectUrl = $"https://{host}{requestUri}";
            // Response Headers and such intentionally left out - We want to be secure for that :)
            string redirectResponse = "HTTP/1.1 301 Moved Permanently\r\n" +
                                      $"Location: {redirectUrl}\r\n" +
                                      "Connection: close\r\n\r\n";
            await writer.WriteAsync(redirectResponse);
        }
        else
        {
            string badRequestResponse =
                "HTTP/1.1 400 Bad Request\r\nContent-Type: text/plain\r\n\r\nHost header is required for redirect.";
            await writer.WriteAsync(badRequestResponse);
        }
    }
}