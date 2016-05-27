﻿using System;
using System.Collections.Generic;
using System.Net;

namespace SharpRUDP
{
    public class RUDPConnectionData
    {
        public IPEndPoint EndPoint { get; set; }
        public int Local { get; set; }
        public int? Remote { get; set; }
        public int PacketId { get; set; }
        public List<RUDPPacket> ReceivedPackets { get; set; }
        public List<RUDPPacket> Pending { get; set; }
        public ConnectionState State { get; set; }
        public DateTime LastPacketDate { get; set; }

        public RUDPConnectionData()
        {
            PacketId = 0;
			State = ConnectionState.OPEN;
            ReceivedPackets = new List<RUDPPacket>();
            Pending = new List<RUDPPacket>();
        }

        public override string ToString()
        {
            return string.Format("[{0}] Local: {1} | Remote: {2}", EndPoint, Local, Remote);
        }
    }
}
