using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace ConnectionServer
{
    class Program
    {
        /// <summary>
        /// This lock is used for console output to avoid several clients writing at the same time.
        /// </summary>
        private static object serverLock = new object();
        /// <summary>
        /// Enable/disable console output
        /// </summary>
        private static bool showText = true;

        /// <summary>
        /// Server listen port
        /// </summary>
        private static int port;
        /// <summary>
        /// Server listen socket
        /// </summary>
        private static Socket serverSocket;

        /// <summary>
        /// Client socket and it's receive buffer
        /// </summary>
        private class ConnectionInfo
        {
            public Socket Socket;
            public byte[] Buffer;
        }

        private static List<ConnectionInfo> connections =
            new List<ConnectionInfo>();

        /// <summary>
        /// Initializes server socket which will listen to new connections
        /// </summary>
        private static void SetupServerSocket()
        {
            IPEndPoint myEndpoint = new IPEndPoint(
                IPAddress.Any, port);

            // Create the socket, bind it, and start listening
            serverSocket = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Blocking = false;

            serverSocket.Bind(myEndpoint);
            serverSocket.Listen((int)SocketOptionName.MaxConnections);
        }

        public static void Start()
        {
            Console.Write("Starting TCP server... ");
            try
            {
                SetupServerSocket();

                // you can accept multiple incomming connections at single time
                for (int i = 0; i < 10; i++)
                    serverSocket.BeginAccept(
                        new AsyncCallback(AcceptCallback), serverSocket);
            }
            catch (Exception e)
            {
                Console.WriteLine("Fail.");
                Console.WriteLine(e);
            }
            Console.WriteLine("Done. Listening.");
        }

        /// <summary>
        /// On accept, this callback is called from unknown system thread.
        /// </summary>
        /// <param name="result"></param>
        private static void AcceptCallback(IAsyncResult result)
        {
            Console.WriteLine("Accept!");
            // create new connection info to store new connection information (in this case just socket/buffer)
            ConnectionInfo connection = new ConnectionInfo();
            try
            {
                // Finish Accept
                Socket s = (Socket)result.AsyncState;
                connection.Socket = s.EndAccept(result);
                connection.Socket.Blocking = false;
                connection.Buffer = new byte[255];
                lock (connections) connections.Add(connection);

                Console.WriteLine("New connection from " + s);

                // Start Receive
                connection.Socket.BeginReceive(connection.Buffer, 0,
                    connection.Buffer.Length, SocketFlags.None,
                    new AsyncCallback(ReceiveCallback), connection);

                // Start new Accept, accept other connections
                serverSocket.BeginAccept(new AsyncCallback(AcceptCallback),
                    result.AsyncState);
            }
            catch (SocketException exc)
            {
                CloseConnection(connection);
                Console.WriteLine("Socket exception: " + exc.SocketErrorCode);
            }
            catch (Exception exc)
            {
                CloseConnection(connection);
                Console.WriteLine("Exception: " + exc);
            }
        }

        /// <summary>
        /// On receive, this callback is called from unknown system thread.
        /// </summary>
        /// <param name="result"></param>
        private static void ReceiveCallback(IAsyncResult result)
        {
            ConnectionInfo connection = (ConnectionInfo)result.AsyncState;
            try
            {
                int bytesRead = connection.Socket.EndReceive(result);
                if (0 != bytesRead)
                {
                    // note that this "serverLock" is just for outputing to console.
                    // otherwise no locks would be needed if this method did its work
                    // in isolated way
                    lock (serverLock)
                    {
                        if (showText)
                        {
                            string text = Encoding.UTF8.GetString(connection.Buffer, 0, bytesRead);
                            Console.Write(text);
                        }
                    }
                    // if we needed send simple response to the same socket, this would suffice:
                    // connection.Socket.Send(connection.Buffer, bytesRead, SocketFlags.None);

                    // however, this code sends the same received data to all other connected clients
                    // so we protect connections wile we are iterating over them
                    // to avoid any other thread removing/adding sockets while iteration
                    // is in progress
                    lock (connections)
                    {
                        foreach (ConnectionInfo conn in connections)
                        {
                            if (connection != conn)
                            {
                                conn.Socket.Send(connection.Buffer, bytesRead,
                                    SocketFlags.None);
                            }
                        }
                    }

                    // begin receive additional data
                    connection.Socket.BeginReceive(connection.Buffer, 0,
                        connection.Buffer.Length, SocketFlags.None,
                        new AsyncCallback(ReceiveCallback), connection);
                }
                else CloseConnection(connection);
            }
            catch (SocketException)
            {
                CloseConnection(connection);
            }
            catch (Exception)
            {
                CloseConnection(connection);
            }
        }

        private static void CloseConnection(ConnectionInfo ci)
        {
            ci.Socket.Close();
            lock (connections) { connections.Remove(ci); Console.WriteLine(string.Format("Clients: {0}", connections.Count)); }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("TCP listener and proxy. Default mode is \"text\".");
            Console.WriteLine();
            Console.WriteLine("|--- \"exit\" to exit.                                  ---|");
            Console.WriteLine("|--- \"show text\" to display tcp data as text.         ---|");
            Console.WriteLine("|--- \"hide text\" to stop displaying tcp data as text. ---|");
            Console.WriteLine("|--- \"drop all\" to drop all connections.              ---|");
            Console.WriteLine();
            Console.Write("Please enter listen port number: ");
            bool portReady = false;
            string line = Console.ReadLine();
            while (line != "exit")
            {
                if (!portReady)
                {
                    try
                    {
                        port = int.Parse(line);
                        if (port > short.MaxValue || port < 2)
                        {
                            Console.WriteLine("Invalid port number.");
                            Console.Write("Please enter port number: ");
                        }
                        else
                        {
                            Start();
                            portReady = true;
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Invalid port number.");
                        Console.Write("Please enter port number: ");
                    }
                }
                else
                {
                    if (line == "show text")
                    {
                        lock (serverLock)
                        {
                            if (showText == false)
                            {
                                showText = true;
                                Console.WriteLine("Text output enabled.");
                            }
                        }
                    }
                    else if (line == "hide text")
                    {
                        lock (serverLock)
                        {
                            if (showText == true)
                            {
                                showText = false;
                                Console.WriteLine("Text output disabled.");
                            }
                        }
                    }
                    else if (line == "drop all")
                    {
                        lock (connections)
                        {
                            for (int i = connections.Count - 1; i >= 0; i--)
                            {
                                CloseConnection(connections[i]);
                            }
                        }
                    }
                    else
                    {
                        // send entered data to all clients
                        lock (connections)
                        {
                            foreach (ConnectionInfo conn in connections)
                            {
                                byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
                                conn.Socket.Send(bytes, bytes.Length,
                                    SocketFlags.None);
                            }
                        }
                    }
                }
                line = Console.ReadLine();
            }
            Console.Write("Shutting down server... ");
            lock (connections)
            {
                for (int i = connections.Count - 1; i >= 0; i--)
                {
                    CloseConnection(connections[i]);
                }
            }
            Console.WriteLine("Bye.");
            Thread.Sleep(500);
        }
    }
}
