// See https://aka.ms/new-console-template for more information


using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

Console.WriteLine("server test started");
var ipAddress = IPAddress.Parse("127.0.0.1");
using var client = new TcpClient();

async Task sendData(NetworkStream? stream)
{
    var message = "+PING\r\n";
    var messageBytes = Encoding.UTF8.GetBytes(message);
    await stream.WriteAsync(messageBytes);
}

async Task readData(NetworkStream? stream)
{
    var buffer = new byte[1_024];
      var data = await stream.ReadAsync(buffer);
      var strdata = Encoding.UTF8.GetString(buffer, 0 , data);
      System.Console.WriteLine(strdata);
}

try
{

    //connect -> set stream -> readData
    await client.ConnectAsync(ipAddress, 6379);
    System.Console.WriteLine("server status " + $"{client.Connected}");

     await using var stream = client.GetStream();
     await sendData(stream);
     await readData(stream);
     
}
catch(Exception e)
{
    System.Console.WriteLine(e);
}
finally
{
}


//1.Ping test to TCP 63

