using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;

namespace ConnectionClient
{
    public class SocketInfo
    {
        /// <summary>
        /// Socket receive buffer
        /// </summary>
        public byte[] buffer = new byte[255];
        /// <summary>
        /// Socket itself
        /// </summary>
        public Socket socket = null;
    }

    class Program
    {
        /* server info */
        private static string address;
        private static int port;
        private static IPEndPoint clientEndpoint;

        /// <summary>
        /// List of sockets, because this test client can spam the same server from multiple
        /// connections
        /// </summary>
        private static List<SocketInfo> socketList = new List<SocketInfo>();

        static void Start()
        {
            // Create socket based on stored server info (always connecting to the same server in this app)
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // store socket buffer together with socket in the same class
            SocketInfo info = new SocketInfo();
            info.socket = clientSocket;

            // when accessing socket list, always lock it.
            // if we had no need to so any action with all the sockets, we could do away
            // without locks
            lock (socketList)
            {
                socketList.Add(info);
            }
            Console.Write("Connecting... ");
            try
            {
                info.socket.Connect(clientEndpoint);
                Console.WriteLine("Done.");

                // begin receiving data. We pass SocketInfo over "state" argument
                // to avoid concurency issues, because ReceiveCallback will be called from unknown thread
                info.socket.BeginReceive(info.buffer, 0, info.buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), info);
            }
            catch (Exception e)
            {
                lock (socketList)
                {
                    socketList.Remove(info);
                }
                Console.WriteLine("Fail.");
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Receive callback is called from unknown system thread
        /// </summary>
        /// <param name="result"></param>
        static void ReceiveCallback(IAsyncResult result)
        {
            SocketInfo info = (SocketInfo)result.AsyncState;
            try
            {
                // have to call this to notify that we have received data
                int bytestoread = info.socket.EndReceive(result);
                if (bytestoread > 0)
                {
                    // in this case we are outputing data stream directly into console
                    string text = Encoding.UTF8.GetString(info.buffer, 0, bytestoread);
                    Console.Write(text);

                    // begin receive again, note how we don't need any locks!
                    info.socket.BeginReceive(info.buffer, 0, 255, SocketFlags.None, new AsyncCallback(ReceiveCallback), info);
                }
                else
                {
                    // in case client finished, remove it from the list
                    // this list can be accessed from any thread, so lock
                    lock (socketList)
                    {
                        socketList.Remove(info);
                    }
                    Console.WriteLine("Client finished normally.");
                    info.socket.Close();
                }
            }
            catch (Exception e)
            {
                // if problem occurs, remove socket from list too
                lock (socketList)
                {
                    socketList.Remove(info);
                }
                Console.WriteLine("Client Disconnected.");
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Test TCP client
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            Console.WriteLine("TCP client.");
            Console.WriteLine();
            Console.WriteLine("|--- \"exit\" to exit.                                  ---|");
            Console.WriteLine("|--- \"start {cn}\" to start {cn} number of connections.---|");
            Console.WriteLine();
            Console.Write("Please enter remote address and port: ");
            bool portReady = false;
            string line = Console.ReadLine();

            // input management stuff. boring.

            while (line != "exit")
            {
                if (!portReady)
                {
                    try
                    {
                        string[] ss = line.Split(':');
                        port = int.Parse(ss[1]);
                        IPAddress add = IPAddress.Parse(ss[0]);
                        clientEndpoint = new IPEndPoint(add, port);
                        address = clientEndpoint.Serialize().ToString();
                        if (port > short.MaxValue || port < 2)
                        {
                            Console.WriteLine("Invalid port.");
                            Console.Write("Please enter remote address and port: ");
                        }
                        else
                        {
                            Start();
                            portReady = true;
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Invalid address.");
                        Console.Write("Please enter remote address and port: ");
                    }
                }
                else
                {
                    if (line.StartsWith("start "))
                    {
                        int count = 0;
                        try
                        {
                            count = int.Parse(line.Substring("start ".Length));
                        }
                        catch { }
                        Console.WriteLine("Starting " + count + " connections.");
                        for (int i = 0; i < count; i++)
                        {
                            // this starts another connection
                            Start();
                        }
                    }
                    else
                    {
                        try
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                            lock (socketList)
                            {
                                foreach (SocketInfo info in socketList)
                                {
                                    info.socket.Send(bytes, bytes.Length, SocketFlags.None);
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine("Unable to send data. Connection lost.");
                        }
                    }
                }
                line = Console.ReadLine();
            }
            Console.Write("Shutting down client... ");
            try
            {
                lock (socketList)
                {
                    for (int i = socketList.Count - 1; i >= 0; i--)
                    {
                        try {
                            socketList[i].socket.Shutdown(SocketShutdown.Both);
                            socketList[i].socket.Close();
                        } catch {}
                        socketList.RemoveAt(i);
                    }
                }
            }
            catch { }
            Console.WriteLine("Bye.");
            Thread.Sleep(500);
        }
    }
}
