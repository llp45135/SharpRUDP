using SharpRUDP.Serializers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace SharpRUDP
{
    public class RUDPPacket
    {
        [ScriptIgnore]
        public RUDPSerializer Serializer { get; set; }

        public int Sequence { get; set; }
        public int PacketId { get; set; }
        public int Quantity { get; set; }
        public RUDPPacketType Type { get; set; }
        public byte[] Data { get; internal set; }

        public IPEndPoint Src { get; set; }
        public bool Processed { get; set; }

        public override string ToString()
        {
            return Serializer.AsString(this);
        }
    }
}
