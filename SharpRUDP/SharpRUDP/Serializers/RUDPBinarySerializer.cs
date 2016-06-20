using System.IO;

namespace SharpRUDP.Serializers
{
    public class RUDPBinarySerializer : RUDPSerializer
    {
        public override RUDPPacket Deserialize(byte[] header, byte[] data)
        {
            RUDPPacket p = new RUDPPacket();
            MemoryStream ms = new MemoryStream(data);
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(header.Length);
                p.Sequence = br.ReadInt32();
                p.PacketId = br.ReadInt32();
                p.Quantity = br.ReadInt32();
                p.Type = (RUDPPacketType)br.ReadByte();
                int dataLen = br.ReadInt32();
                p.Data = br.ReadBytes(dataLen);
            }
            return p;
        }

        public override byte[] Serialize(byte[] header, RUDPPacket p)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(header);
                bw.Write(p.Sequence);
                bw.Write(p.PacketId);
                bw.Write(p.Quantity);
                bw.Write((byte)p.Type);
                bw.Write(p.Data == null ? 0 : p.Data.Length);
                if (p.Data != null)
                    bw.Write(p.Data);
            }
            return ms.ToArray();
        }

        public override string AsString(RUDPPacket p)
        {
            return string.Format("SEQ:{0} | ID:{1} | TYPE:{2} | QTY:{3} | DATA:{4}",
                p.Sequence,
                p.PacketId,
                p.Type.ToString(),
                p.Quantity,
                p.Data == null ? "" : (p.Data.Length > 32 ? p.Data.Length.ToString() : string.Join(",", p.Data))                
            );
        }
    }
}
