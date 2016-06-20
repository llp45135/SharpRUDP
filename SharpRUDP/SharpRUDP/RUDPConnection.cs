using NLog;
using SharpRUDP.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SharpRUDP
{
    public class RUDPConnection
    {
        public bool IsAlive { get; set; }
        public bool IsServer { get; set; }
        public byte[] LevelOneHeader { get; set; }
        public byte[] LevelTwoHeader { get; set; }

        public RUDPSerializer Serializer { get; set; }
        public ConnectionState State { get; set; }

        public delegate void dlgSocketError(IPEndPoint ep, Exception ex);
        public delegate void dlgEventPacket(RUDPPacket p);
        public event dlgEventPacket OnPacketReceived;
        public event dlgSocketError OnSocketError;

        private static Logger Log = LogManager.GetCurrentClassLogger();
        private RUDPSocket _socket = new RUDPSocket();
        private IPEndPoint RemoteEndPoint;
        internal Dictionary<string, RUDPEndpoint> _endpoints = new Dictionary<string, RUDPEndpoint>();

        private void Debug(string text, params object[] args)
        {
            Log.Debug((IsServer ? "[S] " : "[C] ") + text, args);
        }

        public RUDPConnection()
        {
            State = ConnectionState.CLOSED;
            Serializer = new RUDPBinarySerializer();
            LevelOneHeader = new byte[] { 0xFF, 0x01 };
            LevelTwoHeader = new byte[] { 0xFF, 0x02 };
            _socket.OnSocketError += SocketError;
            _socket.OnDataReceived += DataReceived;
        }

        private void SocketError(IPEndPoint ep, Exception ex)
        {
            Log.Debug("SOCKET ERROR: {0} | {1}", ep, ex.Message);
            OnSocketError?.Invoke(ep, ex);
        }

        public void Listen(string address, int port)
        {
            IsAlive = true;
            IsServer = true;
            _socket.Server(address, port);
            State = ConnectionState.LISTEN;
        }

        public void Connect(string address, int port)
        {
            IsAlive = true;
            IsServer = false;
            State = ConnectionState.OPENING;
            _socket.Client(address, port);
            RUDPEndpoint ep = GetEndpoint(_socket.RemoteEndPoint);
            RemoteEndPoint = _socket.RemoteEndPoint;
            ep.Connect();
        }

        private RUDPEndpoint GetEndpoint(IPEndPoint remoteEndPoint)
        {
            string ep = remoteEndPoint.ToString();
            if (!_endpoints.ContainsKey(ep))
            {
                _endpoints[ep] = new RUDPEndpoint()
                {
                    Connection = this,
                    EndPoint = remoteEndPoint
                };
                _endpoints[ep].Init();
            }
            return _endpoints[ep];
        }

        private void DataReceived(IPEndPoint ep, byte[] data, int length)
        {
            RUDPEndpoint rep = GetEndpoint(ep);
            rep.ReceiveData(data, length);
        }

        internal void SendBytes(IPEndPoint ep, byte[] data)
        {
            _socket.SendBytes(ep, data);
        }

        public void Send(byte[] data)
        {
            RUDPEndpoint rep = GetEndpoint(RemoteEndPoint);
            rep.Send(data);
        }

        public void CallPacketReceived(RUDPPacket p)
        {
            OnPacketReceived?.Invoke(p);
        }

        public void Disconnect()
        {
            if (State >= ConnectionState.CLOSING)
                return;
            State = ConnectionState.CLOSING;
            IsAlive = false;
            Debug("DISCONNECT");
            new Thread(() =>
            {
                try
                {
                    _socket.Dispose();
                }
                catch (Exception) { }
                foreach (var kvp in _endpoints)
                    kvp.Value.Disconnect();
                State = ConnectionState.CLOSED;
            }).Start();
        }

        public void Status()
        {
            Debug(IsServer ? "SERVER:" : "CLIENT:");
            Debug("{0} connections alive", _endpoints.Count);
            foreach (var kvp in _endpoints)
            {
                Debug("=== {0} ===", kvp.Key);
                lock (kvp.Value.Pending)
                {
                    Debug("  {0} pending", kvp.Value.Pending.Count);
                    foreach (RUDPPacket p in kvp.Value.Pending)
                        Debug("    {0}", p);
                }
                //Debug("  {0} unconfirmed", kvp.Value.Unconfirmed.Count); foreach (RUDPPacket p in kvp.Value.Unconfirmed) Debug("    {0}", p);
            }
            Debug("======================================================");
        }
    }
}