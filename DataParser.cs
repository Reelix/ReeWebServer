using System.Net.Sockets;
using System.Text;

namespace ReeWebServer;

public class DataParser
{
    public static async Task ParseData(TcpClient client, StreamReader reader, StreamWriter writer, bool isHTTP)
    {
        try
        {
            string remoteIp = client.Client.RemoteEndPoint?.ToString() ?? "Unknown IP";
            string? requestLine = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(requestLine))
            {
                Console.WriteLine($"requestLine from {remoteIp} is empty");
                client.Close();
                return;
            }

            Console.WriteLine($"Request Type: {requestLine}");
            string[] requestParts = requestLine.Split(' ');
            if (requestParts.Length != 3)
            {
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
                    int.TryParse(headerLine.Substring(15).Trim(), out contentLength);
                }
            }

            Console.WriteLine($"[HTTP/S Request] From: {remoteIp} | Path: \"{requestUri}\" | User-Agent: \"{userAgent}\"");

            if (requestUri == "/")
            {
                Console.WriteLine("- Loading index.html instead");
                requestUri = "/index.html";
            }
            // *** HANDLE POST DATA ***
            if (httpMethod == "POST" && contentLength > 0)
            {
                char[] buffer = new char[contentLength];
                int bytesRead = await reader.ReadAsync(buffer, 0, contentLength);
                string postData = new string(buffer, 0, bytesRead);
                Console.WriteLine($"[POST Data]: {postData}");
                // Now you can process the postData as needed
            }

            if (httpMethod == "GET")
            {
                if (isHTTP)
                {
                    // We don't really care about HTTP
                    await HTTPHandler.RedirectToHTTPS(writer, host, requestUri);
                }
                else
                {
                    string statusCode = "200 OK";
                    string contentType = "text/plain";
                    string? responseContent = null;
                    byte[]? responseBytes = null;
                    // TODO: Fix this. Ensure no LFI / RFI and so on (Both full / relative path)
                    if (requestUri == "/index.html")
                    {
                        contentType = "text/html";
                        responseContent = await File.ReadAllTextAsync("/home/reelix/web/index.html",Encoding.UTF8);
                    }
                    else if (requestUri == "/robots.txt")
                    {
                        contentType =  "text/plain";
                        responseContent = await File.ReadAllTextAsync("/home/reelix/web/robots.txt", Encoding.UTF8);
                    }
                    else if (requestUri == "/favicon.ico")
                    {
                        contentType = "image/x-icon";
                        responseBytes = await File.ReadAllBytesAsync("/home/reelix/web/favicon.ico");
                    }
                    else if (requestUri == "/images/bg.png")
                    {
                        contentType = "image/png";
                        responseBytes = await File.ReadAllBytesAsync("/home/reelix/web/images/bg.png");
                    }
                    else
                    {
                        statusCode = "404 Not Found";
                        contentType = "text/html";
                        responseContent = "404 - Try <a href = 'https://reelix.h4ck.me/index.html'>https://reelix.h4ck.me/index.html</a>";
                    }

                    int bodyLength = responseBytes?.Length ?? Encoding.UTF8.GetByteCount(responseContent ?? "");
                    
                    string headers = $"HTTP/{statusCode}\r\n" +
                                     $"Connection: close\r\n" +
                                     $"Content-Type: {contentType}\r\n" +
                                     $"Server: Reelix's Server :p\r\n" +
                                     $"Strict-Transport-Security: max-age=31536000; includeSubDomains\r\n" +
                                     $"Content-Length: {bodyLength}\r\n\r\n";
                    
                    await writer.WriteAsync(headers);
                    await writer.FlushAsync();
                    
                    // Write the body, choosing the correct method based on what was populated
                    if (responseBytes != null)
                    {
                        // It's byte data - We need to use the BaseStream (SSL)
                        await writer.BaseStream.WriteAsync(responseBytes);
                        await writer.BaseStream.FlushAsync();
                    }
                    else if(responseContent != null)
                    {
                        // Text content can still go through the writer
                        await writer.WriteAsync(responseContent);
                        await writer.FlushAsync();
                    }
                }
            }
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
        catch (Exception e)
        {
            // System.IO.IOException: The remote party requested renegotiation when AllowRenegotiation was set to false.
            Console.WriteLine("Error in DataParser: " + e);
            throw;
        }
    }
}