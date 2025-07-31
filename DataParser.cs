using System.Net.Sockets;
using System.Text;

namespace ReeWebServer;

// This class really needs a better name...
internal static class DataParser
{
    public static async Task ParseData(TcpClient client, StreamReader reader, StreamWriter writer, bool isHttp)
    {
        try
        {
            string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown IP";
            string? requestLine = await reader.ReadLineAsync();
            // If someone connects, then just sends a blank line
            if (string.IsNullOrEmpty(requestLine))
            {
                // Console.WriteLine($"requestLine from {remoteIp} is empty");
                client.Close();
                return;
            }
            
            string[] requestParts = requestLine.Split(' ');
            if (requestParts.Length != 3 || !requestParts[2].StartsWith("HTTP/"))
            {
                Console.WriteLine("Invalid Request - Discarding...");
                Console.WriteLine($"Request: {requestLine}");
                
                // Could give a 400 Bad Request, but this is for humans :p
                await writer.WriteAsync("This is a Web Server - Act like it!\r\n");
                return;
            }

            string httpMethod = requestParts[0];
            string requestUri = requestParts[1];

            string host = string.Empty;
            string userAgent = "Not Provided";
            int contentLength = 0;
            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
            {
                if (headerLine.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                {
                    host = headerLine.Substring(5).Trim();
                }
                else if (headerLine.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase))
                {
                    userAgent = headerLine.Substring(11).Trim();
                }
                else if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(headerLine.Substring(15).Trim(), out contentLength))
                    {
                        Console.WriteLine("Invalid Content-Length - Discarding...");
                        await writer.WriteAsync("Invalid Content-Length - Discarding...\r\n");
                        return;
                    }
                }
            }

            Console.WriteLine($"Request From: {remoteIp} | Path: \"{requestUri}\" | User-Agent: \"{userAgent}\"");

            // Hackiest URL Rewrite ever
            if (requestUri == "/")
            {
                Console.WriteLine("- Loading index.html instead");
                requestUri = "/index.html";
            }

            // Our Server doesn't yet support POST's, so if someone does a POST, let's see what they're doing 
            if (httpMethod == "POST" && contentLength > 0)
            {
                char[] buffer = new char[contentLength];
                int bytesRead = await reader.ReadAsync(buffer, 0, contentLength);
                string postData = new string(buffer, 0, bytesRead);
                Console.WriteLine($"[POST Data]: {postData}");
            }

            if (httpMethod == "GET")
            {
                if (isHttp)
                {
                    // We don't really care about HTTP
                    await HttpUtils.RedirectToHttps(writer, host, requestUri);
                }
                else
                {
                    string statusCode = "200 OK";
                    string contentType = "text/plain";
                    string? responseContent = null;
                    byte[]? responseBytes = null;

                    // Should fix LFI and so on (Hopefully :p)
                    const string webRoot = "/home/reelix/web/";
                    string requestedFilePath = Path.Combine(webRoot, requestUri.TrimStart('/'));
                    string canonicalPath = Path.GetFullPath(requestedFilePath);
                    if (canonicalPath.StartsWith(webRoot) && File.Exists(canonicalPath))
                    {
                        Console.WriteLine("- Valid File!");
                        contentType = GetContentType(canonicalPath);
                        // Decide whether to read as bytes or text based on content type
                        if (IsBinary(contentType))
                        {
                            responseBytes = await File.ReadAllBytesAsync(canonicalPath);
                        }
                        else
                        {
                            responseContent = await File.ReadAllTextAsync(canonicalPath, Encoding.UTF8);
                        }
                    }
                    else
                    {
                        // If path is invalid or file doesn't exist, return 404
                        Console.WriteLine("Invalid File");
                        statusCode = "404 Not Found";
                        contentType = "text/html";
                        responseContent = "404 - Not Found";
                    }

                    int bodyLength = responseBytes?.Length ?? Encoding.UTF8.GetByteCount(responseContent ?? "");

                    List<string> headerFluff =
                    [
                        "Reelix-Says: You're a Teapot! :p",
                        "Server: Reelix's Server :p",
                        $"Server: Apache",
                        "Server: nginx",
                        "Server: Microsoft/IIS",
                        "Server: Kestrel",
                        "Server: ALL THE SERVERS!"
                    ];
                    string headers = $"HTTP/1.1 {statusCode}\r\n" +
                                     $"Connection: close\r\n" +
                                     $"Content-Type: {contentType}\r\n" +
                                     string.Join("\r\n", headerFluff) + "\r\n" +
                                     $"Strict-Transport-Security: max-age=31536000; includeSubDomains\r\n" +
                                     $"Content-Length: {bodyLength}\r\n\r\n";

                    await writer.WriteAsync(headers);
                    await writer.FlushAsync();

                    // Data and images are weird
                    if (responseBytes != null)
                    {
                        // It's byte data - We need to use the BaseStream (SSL)
                        await writer.BaseStream.WriteAsync(responseBytes);
                        await writer.BaseStream.FlushAsync();
                    }
                    else if (responseContent != null)
                    {
                        // Text content can still go through the writer
                        await writer.WriteAsync(responseContent);
                        await writer.FlushAsync();
                    }
                }
            }
            // HEAD, OPTIONS, etc, etc.
            else
            {
                Console.WriteLine("Unsupported HTTP Method: " + httpMethod);
                string responseContent = "You're a Teapot! :)";
                string httpResponse = $"HTTP/1.1 418\r\n" + // I'm a Teapot!
                                      $"Connection: close\r\n" +
                                      $"Content-Type: text/html\r\n" +
                                      $"Content-Length: {Encoding.UTF8.GetByteCount(responseContent)}\r\n\r\n" +
                                      $"{responseContent}";
                await writer.WriteAsync(httpResponse);
            }
        }
        catch (IOException iox)
        {
            //  System.IO.IOException: Unable to read data from the transport connection: Connection reset by peer.
            Console.WriteLine("IO Error in DataParser: " + iox.Message);
        }
        catch (Exception e)
        {
            // System.IO.IOException: The remote party requested renegotiation when AllowRenegotiation was set to false.
            Console.WriteLine("Fatal Error in DataParser: " + e);
        }
    }

    private static string GetContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".txt" => "text/plain",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".ico" => "image/x-icon",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream", // Default for unknown binary files
        };
    }

    // Helper to decide if content type is binary
    private static bool IsBinary(string contentType)
    {
        return !contentType.StartsWith("text/") && contentType != "application/javascript";
    }
}