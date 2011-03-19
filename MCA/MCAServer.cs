using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace MCA
{
    class MCAServer
    {
        private TcpListener listener;
        private IPEndPoint localEP;
        private List<TcpClient> clients;

        public IEnumerable<TcpClient> Clients
        {
            get { return clients; }
        }

        public int ClientCount
        {
            get { return clients.Count; }
        }

        public TcpClient LastCommandSender { get; private set; }
        public bool Running { get; private set; }

        public event Action<TcpClient> ClientConnected;
        public event Action<string, TcpClient> ClientCommand;

        public MCAServer(IPAddress ip, int port)
        {
            listener = new TcpListener(ip, port);
            localEP = (IPEndPoint)listener.LocalEndpoint;
            clients = new List<TcpClient>();
        }

        public void Start()
        {
            if (!Running)
            {
                Running = true;
                listener.Start();

                AsyncCallback callback = (a) =>
                {
                    if (Running)
                    {
                        TcpClient client = listener.EndAcceptTcpClient(a);
                        clients.Add(client);
                        OnClientConnected(client);

                        IPEndPoint ep = (IPEndPoint)client.Client.RemoteEndPoint;
                        Console.WriteLine("[MCA] ({0}:{1}) Connected", ep.Address, ep.Port);

                        listener.BeginAcceptTcpClient((AsyncCallback)a.AsyncState, a.AsyncState);

                        BinaryReader br = new BinaryReader(client.GetStream());

                        while (client.Connected)
                        {
                            if (client.GetStream().DataAvailable)
                            {
                                string data = br.ReadString();
                                if (data == "dc")
                                {
                                    break;
                                }

                                Console.WriteLine("[MCA] ({0}:{1}) Command: {2}", ep.Address, ep.Port, data);

                                LastCommandSender = client;
                                OnClientCommand(data, client);
                            }

                            System.Threading.Thread.Sleep(200);
                        }

                        Console.WriteLine("[MCA] ({0}:{1}) Disconnected", ep.Address, ep.Port);
                        clients.Remove(client);
                        client.Close();
                    }
                };

                listener.BeginAcceptTcpClient(callback, callback);
                Console.WriteLine("[MCA] Server started on ({0}:{1})", localEP.Address, localEP.Port);
            }
            else
            {
                Console.WriteLine("[MCA] Server already running");
            }
        }

        public void Stop()
        {
            if (Running)
            {
                Running = false;
                listener.Stop();
                DropConnections();
                
                Console.WriteLine("[MCA] Server stopped");
            }
            else
            {
                Console.WriteLine("[MCA] No server running");
            }
        }

        public void PrintStatusMessage()
        {
            if (Running)
            {
                Console.WriteLine("[MCA] Status: Server running");
            }
            else
            {
                Console.WriteLine("[MCA] Status: No server running");
            }
        }

        public void BroadcastMessage(string message)
        {
            foreach (TcpClient client in clients)
            {
                new BinaryWriter(client.GetStream()).Write(message);
            }
        }

        public void SendMessage(string message, TcpClient client)
        {
            new BinaryWriter(client.GetStream()).Write(message);
        }

        private void DropConnections()
        {
            BroadcastMessage("shutdown");
            foreach (TcpClient client in clients)
            {
                client.Close();
            }
            clients = new List<TcpClient>();
        }

        protected virtual void OnClientConnected(TcpClient client)
        {
            if (ClientConnected != null)
            {
                ClientConnected(client);
            }
        }

        protected virtual void OnClientCommand(string command, TcpClient client)
        {
            if (ClientCommand != null)
            {
                ClientCommand(command, client);
            }
        }
    }
}
