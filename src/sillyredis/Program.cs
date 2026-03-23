using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

var ipAddress = IPAddress.Parse("127.0.0.1");
var server = new TcpListener(ipAddress, 6379);

try
{
    server.Start();
    var source = new CancellationTokenSource();
    var token = source.Token;
    using var acceptClinet = await server.AcceptTcpClientAsync(token);
    await using var stream =  acceptClinet.GetStream();
    var message = "+PONG\r\n";
    var messageBytes = Encoding.UTF8.GetBytes(message);
   
    var buffer = new byte[1024];
    while((await stream.ReadAsync(buffer)) > 0)
    await stream.WriteAsync(messageBytes, cancellationToken: token);
server.AcceptSocket();
}
catch(Exception e)
{
    
}
finally
{
    server.Stop();
}
