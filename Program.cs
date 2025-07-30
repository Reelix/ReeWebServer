using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using ReeWebServer;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Replace with the actual path to your .pfx file and the password you set.
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

public class BasicHttpServer
{
    private readonly string _certPath;
    private readonly string _keyPath;

    public BasicHttpServer(string certPath, string keyPath)
    {
        _certPath = certPath;
        _keyPath = keyPath;
    }

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
            await HTTPHandler.ProcessHttpClient(client);
        }
    }

    // Handles incoming connections on Port 443
    private async Task HandleIncomingHttpsConnections(TcpListener listener)
    {
        X509Certificate2 serverCertificate = X509Certificate2.CreateFromPemFile(_certPath, _keyPath);

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            await ProcessHttpsClient(client, serverCertificate);
        }
    }

    private async Task ProcessHttpsClient(TcpClient client, X509Certificate2 certificate)
    {
        using (var sslStream = new SslStream(client.GetStream(), false))
        {
            try
            {
                string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown IP";
                Console.WriteLine($"HTTPS - Connection from {remoteIp}");
                
                var allowedCipherSuites = new CipherSuitesPolicy(new[]
                {
                    TlsCipherSuite.TLS_AES_256_GCM_SHA384,
                    TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
                    TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
                    TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384,
                });
                
                var sslOptions = new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    // Removing Tls1.2 marks you down on https://www.ssllabs.com/ssltest/
                    // I guess that's too secure? :pd
                    EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    CipherSuitesPolicy = allowedCipherSuites
                };
                await sslStream.AuthenticateAsServerAsync(sslOptions, CancellationToken.None);

                // await sslStream.AuthenticateAsServerAsync(certificate, clientCertificateRequired: false, SslProtocols.Tls13 | SslProtocols.Tls12, checkCertificateRevocation: true);
                using (var reader = new StreamReader(sslStream, Encoding.ASCII, true, 1024, true))
                using (var writer = new StreamWriter(sslStream, Encoding.ASCII) { AutoFlush = true })
                {
                    await DataParser.ParseData(client, reader, writer, false);
                }

                Console.WriteLine("HTTPS - Completed");
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
                    if (
                        innerException.StartsWith("SSL Handshake failed with OpenSSL error - SSL_ERROR_SSL") ||
                        innerException.StartsWith("Received an unexpected EOF or 0 bytes from the transport stream.")
                        )
                    {
                        // Just an invalid SSL Handshake - Ignore it
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
            catch (Exception e)
            {
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
}