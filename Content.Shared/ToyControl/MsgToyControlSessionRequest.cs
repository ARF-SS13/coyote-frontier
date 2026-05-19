using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.ToyControl;

public sealed class MsgToyControlSessionRequest : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public ToyControlSessionRequest Request = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var length = buffer.ReadVariableInt32();
        using var stream = new MemoryStream(length);
        buffer.ReadAlignedMemory(stream, length);
        serializer.DeserializeDirect(stream, out Request);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Request);
        buffer.WriteVariableInt32((int) stream.Length);
        stream.TryGetBuffer(out var segment);
        buffer.Write(segment);
    }
}
