using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace SharpRUDP
{
    public class RUDPEndpoint
    {
        public RUDPConnection Connection { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public int Local { get; set; }
        public int Remote { get; set; }
        public int PacketId { get; set; }
        public int MTU { get; set; }

        public List<RUDPPacket> Pending { get; set; }
        public List<RUDPPacket> SendQueue { get; set; }
        public List<RUDPPacket> RecvQueue { get; set; }
        public List<int> ProcessedSequences { get; set; }

        private int _maxMTU { get { return (int)(MTU * 0.80); } }
        private static Logger Log = LogManager.GetCurrentClassLogger();
        private object _seqMutex = new object();
        private object _sendMutex = new object();
        private object _recvMutex = new object();
        private Thread _thSend;
        private Thread _thRecv;

        private void Debug(string text, params object[] args)
        {
            Log.Debug((Connection.IsServer ? "[S] " : "[C] ") + text, args);
        }

        public void Init()
        {
            Pending = new List<RUDPPacket>();
            RecvQueue = new List<RUDPPacket>();
            SendQueue = new List<RUDPPacket>();
            ProcessedSequences = new List<int>();

            PacketId = 1;
            MTU = 1024 * 8;
            Local = Connection.IsServer ? 200 : 100;
            Remote = Connection.IsServer ? 100 : 200;

            _thRecv = new Thread(new ThreadStart(RecvThread));
            _thSend = new Thread(new ThreadStart(SendThread));
            _thRecv.Start();
            _thSend.Start();
        }

        public void Connect()
        {
            SendPacket(RUDPPacketType.SYN);
        }

        private void SendPacket(RUDPPacketType type, byte[] data = null)
        {
            lock (_sendMutex)
                SendQueue.AddRange(PreparePackets(type, data));
        }

        private List<RUDPPacket> PreparePackets(RUDPPacketType type, byte[] data)
        {
            List<RUDPPacket> rv = new List<RUDPPacket>();
            if ((data != null && data.Length <= _maxMTU) || data == null)
            {
                lock (_seqMutex)
                {
                    rv.Add(new RUDPPacket()
                    {
                        Serializer = Connection.Serializer,
                        Quantity = 0,
                        Data = data,
                        Type = type,
                        Sequence = Local,
                        PacketId = PacketId
                    });
                    Local++;
                    PacketId++;
                }
            }
            else if(data != null && data.Length >= _maxMTU)
            {
                int i = 0;
                while (i < data.Length)
                {
                    int min = i;
                    int max = _maxMTU;
                    if ((min + max) > data.Length)
                        max = data.Length - min;
                    byte[] buf = data.Skip(i).Take(max).ToArray();
                    rv.Add(new RUDPPacket()
                    {
                        Serializer = Connection.Serializer,
                        PacketId = PacketId,
                        Sequence = Local,
                        Type = type,
                        Data = buf
                    });
                    i += _maxMTU;
                    Local++;
                }
                foreach (RUDPPacket p in rv)
                    p.Quantity = rv.Count;                
                PacketId++;
            }
            return rv;
        }

        private void SendThread()
        {
            while (Connection.IsAlive)
            {
                if (Pending.Count > 0)
                {
                    lock (_sendMutex)
                    {
                        foreach (RUDPPacket p in Pending)
                        {
                            Debug("RETRANSMIT -> {0}", p);
                            Connection.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.LevelTwoHeader, p));
                        }
                    }
                }
                else
                {
                    lock (_sendMutex)
                    {
                        foreach (RUDPPacket p in SendQueue)
                        {
                            Debug("SEND -> {0}", p);
                            Pending.Add(p);
                            Connection.SendBytes(EndPoint, Connection.Serializer.Serialize(Connection.LevelTwoHeader, p));
                        }
                        SendQueue = new List<RUDPPacket>();
                    }
                }
                Thread.Sleep(10);
            }
        }

        internal void Send(byte[] data)
        {
            SendPacket(RUDPPacketType.DAT, data);
        }

        private void RecvThread()
        {
            while (Connection.IsAlive)
            {
                lock (_recvMutex)
                    ProcessRecvQueue();
                Thread.Sleep(10);
            }
        }

        private void ProcessRecvQueue()
        {
            List<RUDPPacket> RecvPackets = RecvQueue.OrderBy(x => x.Sequence).ToList();
            foreach (RUDPPacket p in RecvPackets)
            {
                if (p.Processed || ProcessedSequences.Contains(p.Sequence))
                    continue;

                if (p.Sequence != Remote)
                {
                    RecvQueue.Add(p);
                    if (p.Sequence > Remote)
                        continue;
                    else
                        break;
                }

                if (p.Quantity == 0)
                {
                    Remote++;
                    p.Processed = true;
                    ProcessedSequences.Add(p.Sequence);

                    if (p.Type == RUDPPacketType.SYN)
                        SendPacket(RUDPPacketType.ACK);

                    if (!Connection.IsServer)
                        if (p.Type == RUDPPacketType.ACK)
                            Connection.State = ConnectionState.OPEN;

                    Connection.CallPacketReceived(p);

                    Debug("RECV <- {0}", p);
                }
                else
                {
                    List<RUDPPacket> multiPackets = RecvPackets.Where(x => x.PacketId == p.PacketId).ToList();
                    multiPackets = multiPackets.GroupBy(x => x.Sequence).Select(g => g.First()).ToList();
                    if (multiPackets.Count == p.Quantity)
                    {
                        Debug("MULTIPACKET {0}", p.PacketId);

                        byte[] buf;
                        MemoryStream ms = new MemoryStream();
                        using (BinaryWriter bw = new BinaryWriter(ms))
                            foreach (RUDPPacket mp in multiPackets)
                            {
                                mp.Processed = true;
                                ProcessedSequences.Add(mp.Sequence);
                                bw.Write(mp.Data);
                                Debug("RECV MP <- {0}: {1}", EndPoint, mp);
                            }
                        buf = ms.ToArray();
                        // Debug("MULTIPACKET ID {0} DATA: {1}", p.PacketId, Encoding.ASCII.GetString(buf));

                        Connection.CallPacketReceived(new RUDPPacket()
                        {
                            Serializer = Connection.Serializer,
                            Data = buf,
                            PacketId = p.PacketId,
                            Quantity = p.Quantity,
                            Type = p.Type,
                            Sequence = p.Sequence
                        });

                        Remote += p.Quantity;
                    }
                    else if (multiPackets.Count < p.Quantity)
                    {
                        RecvQueue.Add(p);
                        break;
                    }
                    else
                    {
                        Debug("P.QTY > MULTIPACKETS.COUNT ({0} > {1})", p.Quantity, multiPackets.Count);
                        throw new Exception();
                    }
                }
            }
            RecvQueue.RemoveAll(x => x.Processed);
        }

        public void Disconnect()
        {
            if (_thSend != null)
                while (_thSend.IsAlive)
                    Thread.Sleep(10);
            if (_thRecv != null)
                while (_thRecv.IsAlive)
                    Thread.Sleep(10);
        }

        public void ReceiveData(byte[] data, int length)
        {
            RUDPPacket p;
            RUDPInternalPacket ip;
            DateTime dtNow = DateTime.Now;
            if (length >= Connection.LevelTwoHeader.Length && data.Take(Connection.LevelTwoHeader.Length).SequenceEqual(Connection.LevelTwoHeader))
            {
                p = Connection.Serializer.Deserialize(Connection.LevelTwoHeader, data);
                p.Serializer = Connection.Serializer;
                lock (_recvMutex)
                    RecvQueue.Add(p);
                ip = new RUDPInternalPacket() { Type = RUDPInternalPacket.RUDPInternalPacketType.ACK, Data = p.Sequence };
                Debug("INTERNAL SEND -> {0}", ip);
                Connection.SendBytes(EndPoint, ip.Serialize(Connection.LevelOneHeader));
            }
            else if (length >= Connection.LevelOneHeader.Length && data.Take(Connection.LevelOneHeader.Length).SequenceEqual(Connection.LevelOneHeader))
            {
                ip = RUDPInternalPacket.Deserialize(Connection.LevelOneHeader, data);
                Debug("INTERNAL RECV <- {0}", ip);
                if (ip.Type == RUDPInternalPacket.RUDPInternalPacketType.ACK)
                    lock (_sendMutex)
                        Pending.RemoveAll(x => x.Sequence == ip.Data);
            }
        }
    }
}