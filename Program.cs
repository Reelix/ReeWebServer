using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ReeWebServer;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Renew:
        // sudo certbot certonly -d reelix.h4ck.me --key-type ecdsa --elliptic-curve secp384r1 --force-renewal --staple-ocsp
        string certPath = "/etc/letsencrypt/live/reelix.h4ck.me/fullchain.pem";
        string keyPath = "/etc/letsencrypt/live/reelix.h4ck.me/privkey.pem";

        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            Console.WriteLine("Certificate or private key file not found.");
            Console.WriteLine($"Checked for cert at: {certPath}");
            Console.WriteLine($"Checked for key at:  {keyPath}");
            Console.WriteLine("Please update the paths in Program.cs and ensure the files exist.");
            return;
        }

        var server = new BasicHttpServer(certPath, keyPath);
        await server.Start();
    }
}

public class BasicHttpServer(string certPath, string keyPath)
{
    public async Task Start()
    {
        var httpListener = new TcpListener(IPAddress.Any, 80);
        var httpsListener = new TcpListener(IPAddress.Any, 443);

        httpListener.Start();
        httpsListener.Start();

        Console.WriteLine("Server listening on ports 80 and 443...");

        var httpTask = HandleIncomingHttpConnections(httpListener);
        var httpsTask = HandleIncomingHttpsConnections(httpsListener);

        await Task.WhenAll(httpTask, httpsTask);
    }

    // Handles incoming connections on Port 80
    private async Task HandleIncomingHttpConnections(TcpListener listener)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            await ProcessHttpRequest(client);
        }
    }

    // Handles incoming connections on Port 443
    private async Task HandleIncomingHttpsConnections(TcpListener listener)
    {
        X509Certificate2 serverCertificate = X509Certificate2.CreateFromPemFile(certPath, keyPath);

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            await ProcessHttpRequest(client, serverCertificate);
        }
    }

    private static async Task ProcessHttpRequest(TcpClient client, X509Certificate2? certificate = null)
    {
        try
        {
            string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown IP";
            Console.WriteLine($"Connection from {remoteIp}");

            StreamReader reader;
            StreamWriter writer;

            if (certificate != null)
            {
                // HTTPS
                SslServerAuthenticationOptions sslOptions = HttpUtils.GetSslOptions(certificate);
                var sslStream = new SslStream(client.GetStream(), false);
                await sslStream.AuthenticateAsServerAsync(sslOptions, CancellationToken.None);
                reader = new StreamReader(sslStream, Encoding.ASCII, true, 1024, true);
                writer = new StreamWriter(sslStream, Encoding.ASCII);
                writer.AutoFlush = true;
                await DataParser.ParseData(client, reader, writer, false);
                Console.WriteLine("HTTPS - Completed");
            }
            else
            {
                // HTTP
                var stream = client.GetStream();
                reader = new StreamReader(stream, Encoding.ASCII, true, 1024, true);
                writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };
                await DataParser.ParseData(client, reader, writer, true);
                Console.WriteLine("HTTP - Completed");
            }

            Console.WriteLine("-------------------------------------------------------------------------------------");
        }
        catch (AuthenticationException ae)
        {
            string exception = ae.Message;
            string innerException = "";
            if (ae.InnerException != null)
            {
                innerException = ae.InnerException.Message;
            }
            
            if (!string.IsNullOrEmpty(innerException))
            {
                // Weird SSL Errors - Might deal with them later
                if (innerException.StartsWith("SSL Handshake failed with OpenSSL error - SSL_ERROR_SSL"))
                {
                    // Invalid SSL Handshake
                }
                else
                {
                    Console.WriteLine($"ProcessHttpsClient - AuthenticationException: {exception}");
                    Console.WriteLine($"ProcessHttpsClient - AuthenticationException Inner: {innerException}");
                }
            }
            else
            {
                if (exception.StartsWith("Cannot determine the frame size or a corrupted frame was received."))
                {
                    // Ignore it
                }
                else
                {
                    Console.WriteLine($"ProcessHttpsClient - AuthenticationException: {exception}");
                }
            }
        }
        catch (IOException ioe)
        {
            string errorMessage = ioe.Message;
            if (
                errorMessage == "Received an unexpected EOF or 0 bytes from the transport stream." ||
                errorMessage == "Unable to write data to the transport connection: Connection reset by peer.")
            {
                // Console.WriteLine($"Info: Client disconnected during TLS handshake. This is normal.");
            }
            else
            {
                Console.WriteLine($"Weird IOException: {errorMessage} - Bug Reelix");
            }
        }
        catch (Exception e)
        {
            string errorType = e.GetType().Name;
            Console.WriteLine($"Unknown Error of type {errorType}: {e.Message}");
            // Weird SSL Error
            if (
                e.Message.StartsWith("Received an unexpected EOF or 0 bytes from the transport stream.") ||
                e.Message.StartsWith("Unable to read data from the transport connection: Connection reset by peer.")
            )
            {
                // Ignore it
            }
            else
            {
                Console.WriteLine($"ProcessHttpsClient - Unknown Error: {e.Message}");
            }
        }
        finally
        {
            client.Close();
        }
    }
}