using System.Text;

namespace SillyRedis
{
    public static class RESProtocol
    {
        // Encode Bulk String for RESP protocol
        public static string EncodeBulkString(string element)
        {
            return $"${element.Length}\r\n{element}\r\n";
        }

        //Parse RESP protocol request
        public static string ParseResp(string request)
        {
                var lines = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).
                Where(lines => !(lines.StartsWith('$') || lines.StartsWith('*'))).
                Select(lines => lines.Trim());

                return string.Join(' ', lines);
        }

        //Simple String
        public static string EncodeSimpleString(string message)
        {
            return $"+{message}\r\n";
        }

        //Error String
        public static string EncodeError(string message)
        {
            return $"-{message}\r\n";
        }

        //Integer Response
        public static string EncodeInteger(int number)
        {
            return $":{number}\r\n";
        }

        //Array Response
        public static string EncodeArray(string[] elements)
        {
            var sb = new StringBuilder();
            sb.Append($"*{elements.Length}\r\n");
            foreach (var element in elements)
            {
                sb.Append(EncodeBulkString(element));
            }

            return sb.ToString();
        }
    }
}