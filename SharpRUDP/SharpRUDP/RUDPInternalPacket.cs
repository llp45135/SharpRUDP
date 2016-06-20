using System.IO;

namespace SharpRUDP
{
    public class RUDPInternalPacket
    {
        public enum RUDPInternalPacketType
        {
            ACK = 1,
            PING = 2
        }

        public RUDPInternalPacketType Type { get; set; }
        public int Data { get; set; }

        public byte[] Serialize(byte[] header)
        {
            MemoryStream ms = new MemoryStream();
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(header);
                bw.Write((byte)Type);
                bw.Write(Data);
            }
            return ms.ToArray();
        }

        public static RUDPInternalPacket Deserialize(byte[] header, byte[] data)
        {
            RUDPInternalPacket p = new RUDPInternalPacket();
            MemoryStream ms = new MemoryStream(data);
            using (BinaryReader br = new BinaryReader(ms))
            {
                br.ReadBytes(header.Length);
                p.Type = (RUDPInternalPacketType)br.ReadByte();
                p.Data = br.ReadInt32();
            }
            return p;
        }

        public override string ToString()
        {
            return string.Format("INTERNAL ({0}: {1})", Type.ToString(), Data);
        }
    }
}
