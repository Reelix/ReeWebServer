using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace ReeWebServer;

public static class HttpUtils
{
    public static SslServerAuthenticationOptions GetSslOptions(X509Certificate2  certificate)
    {
        // This doesn't work on Windows
#pragma warning disable CA1416
        var allowedCipherSuites = new CipherSuitesPolicy([
            TlsCipherSuite.TLS_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_CHACHA20_POLY1305_SHA256,
            TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
            TlsCipherSuite.TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384
        ]);
#pragma warning restore CA1416

        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            // Removing Tls1.2 marks you down on https://www.ssllabs.com/ssltest/
            // I guess that's too secure? :p
            EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            CipherSuitesPolicy = allowedCipherSuites,
            
            
        };

        return sslOptions;
    }
    
    public static async Task RedirectToHttps(StreamWriter writer, string host, string requestUri)
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
            string badRequestResponse = "HTTP/1.1 400 Bad Request\r\n" + 
                                        "Content-Type: text/plain\r\n" + 
                                        "Host header is required for redirect.\r\n\r\n";
            await writer.WriteAsync(badRequestResponse);
        }
    }
}