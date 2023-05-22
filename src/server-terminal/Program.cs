using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Sms;
using Windows.Devices.Enumeration;
using System.Text;
using System.IO;
using System.IO.Compression;


class Program
{
    private static SmsMessageRegistration messageRegistration;
    private static Dictionary<string, SortedDictionary<int, string>> receivedMessages = new Dictionary<string, SortedDictionary<int, string>>();
    private static string smsRecipient;
    private static bool debugMode = false;
    private static int MAX_SMS_LEN = 160;
    private static string SMS_HEADER = "1337";



    static async Task Main(string[] args)
    {
        var cts = new CancellationTokenSource();

        foreach (var arg in args)
        {
            if (arg == "-d")
            {
                debugMode = true;
            }
            else
            {
                smsRecipient = arg;
            }
        }

        if (smsRecipient == null)
        {
            Console.WriteLine("Usage: server-terminal.exe <mobile number> [-d]");
            return;
        }

        smsRecipient = args[0];
        
        try
        {
            Console.WriteLine("[+] Enumerating SMS devices");
            var deviceSelector = SmsDevice2.GetDeviceSelector();
            var smsDevices = await DeviceInformation.FindAllAsync(deviceSelector);
            if (smsDevices.Count > 0)
            {
                Console.WriteLine("[+] Found {0} devices", smsDevices.Count);
                foreach (var device in smsDevices)
                {
                    Console.WriteLine("\t{0}", device.Id);
                }
            }
            else
            {
                Console.WriteLine("[-] No SMS capable devices found");
                return;
            }


            Console.WriteLine("[+] Registering task");
            SmsFilterRule filter = new(SmsMessageType.Text);
            SmsFilterRules filterRules = new(SmsFilterActionType.Accept);
            IList<SmsFilterRule> rules = filterRules.Rules;
            rules.Add(filter);

            messageRegistration = SmsMessageRegistration.Register("app", filterRules);
            messageRegistration.MessageReceived += MessageReceivedAsync;
            Console.WriteLine("[+] Registered SMS event handler");

            Console.WriteLine("Press Ctrl+C to quit.");

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Exiting...");
                return;
            };

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var prompt = await Task.Run(() =>
                    {
                        Console.Write("> ");
                        return Console.ReadLine();
                    });

                    if (cts.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (prompt == null)
                    {
                        continue;
                    }

                    if (prompt == "exit" || prompt == "quit")
                    {
                        break;
                    }

                    await SendMessage(prompt, smsRecipient);
                }
                await Task.Delay(1000, cts.Token);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error: {ex.Message}");
            }

            finally
            {
                if (messageRegistration != null)
                {
                    messageRegistration.MessageReceived -= MessageReceivedAsync;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[-] Error: {ex.Message}");
        }
    }

    private static void MessageReceivedAsync(SmsMessageRegistration sender, SmsMessageReceivedTriggerDetails message)
    {
        var textMessage = message.TextMessage;
        message.Accept();

        PrintDebug($"Message from {textMessage.From}. Content: {textMessage.Body}");

        if (!textMessage.Body.StartsWith("1337"))
        {
            PrintDebug("Ignoring message");
        }

        if (textMessage.Body.Length < 11)
        {
            PrintDebug("Received message with correct header but invalid length");
            return;
        }

        try
        {
            var msgId = textMessage.Body.Substring(4, 3);
            var msgCounter = Convert.ToInt32(textMessage.Body.Substring(7, 2), 16);
            var totalMessages = Convert.ToInt32(textMessage.Body.Substring(9, 2), 16);
            var gzippedContent = textMessage.Body[11..];

            if (!receivedMessages.ContainsKey(msgId))
            {
                receivedMessages[msgId] = new SortedDictionary<int, string>();
            }
            receivedMessages[msgId][msgCounter] = gzippedContent;

            Console.WriteLine($"\n[+] Received part {receivedMessages[msgId].Count} of {totalMessages} for message {msgId} from {message.TextMessage.From}");

            if (receivedMessages[msgId].Count == totalMessages)
            {
                var smsResponses = receivedMessages[msgId];
                var assembledGzippedContent = string.Join("", smsResponses.Values);

                var decompressedResponse = Decompress(Unhexlify(assembledGzippedContent));
                Console.WriteLine("Output: \n");
                Console.WriteLine(decompressedResponse);
                Console.Write("> ");
                receivedMessages.Remove(msgId);
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"[-] Error: {ex.Message}");
        }        
    }


    private static string Hexlify(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "");
    }
    static byte[] Unhexlify(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return bytes;
    }

    private static byte[] Compress(string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        using (var msi = new MemoryStream(bytes))
        using (var mso = new MemoryStream())
        {
            using (var gs = new GZipStream(mso, CompressionMode.Compress))
            {
                msi.CopyTo(gs);
            }

            return mso.ToArray();
        }
    }

    static string Decompress(byte[] data)
    {
        using (var compressedStream = new MemoryStream(data))
        using (var decompressor = new GZipStream(compressedStream, CompressionMode.Decompress))
        using (var decompressedStream = new MemoryStream())
        {
            decompressor.CopyTo(decompressedStream);
            return Encoding.UTF8.GetString(decompressedStream.ToArray());
        }
    }

    public static string GenerateAlphanumericString(int length)
    {
        Random random = new Random();
        const string alphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        StringBuilder stringBuilder = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            char randomChar = alphanumericChars[random.Next(alphanumericChars.Length)];
            stringBuilder.Append(randomChar);
        }

        return stringBuilder.ToString();
    }

    public static async Task<List<SmsSendMessageResult>> SendMessage(string message, string destinationPhoneNumber)
    {
        string msgId = GenerateAlphanumericString(3);
        List<string> messageParts = new List<string>();
        SmsDevice2 smsDevice = SmsDevice2.GetDefault();

        message = Hexlify(Compress(message));

        int maxPartLength = MAX_SMS_LEN - SMS_HEADER.Length - msgId.Length - 2 - 2; // 153*6 - header - msgId - counter - total
        int totalParts = (int)Math.Ceiling((double)message.Length / maxPartLength);
        PrintDebug($"Message consists of {totalParts} part(s)");

        for (int i = 0; i < message.Length; i += maxPartLength)
        {
            messageParts.Add($"{SMS_HEADER}{msgId}XX{totalParts:X2}{message.Substring(i, Math.Min(maxPartLength, message.Length - i))}");
        }


        List<SmsSendMessageResult> sendResults = new List<SmsSendMessageResult>();
        int counter = 1;
        foreach (string part in messageParts)
        {
            SmsTextMessage2 smsMessage = new SmsTextMessage2
            {
                Body = part.Replace($"{SMS_HEADER}{msgId}XX", $"{SMS_HEADER}{msgId}{counter:X2}"),
                To = destinationPhoneNumber
            };

            Console.WriteLine($"[+] Sending {smsMessage.Body.Length} characters of part {counter}");
            PrintDebug(smsMessage.Body);

            SmsSendMessageResult sendResult = await smsDevice.SendMessageAndGetResultAsync(smsMessage);
            Console.WriteLine($"[+] Success: {sendResult.IsSuccessful}");
            if (!sendResult.IsSuccessful)
            {
                Console.WriteLine($"[-] Error: {sendResult.ModemErrorCode}");
            }
            counter++;
            sendResults.Add(sendResult);
        }

        return sendResults;
    }

    private static void PrintDebug( string message)
    {
        if (debugMode)
        {
            Console.WriteLine($"[DEBUG] {message}");
        }
    }
}
