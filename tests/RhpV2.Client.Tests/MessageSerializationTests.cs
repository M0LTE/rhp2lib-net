using System.Text;
using System.Text.Json;
using RhpV2.Client.Protocol;
using Xunit;

namespace RhpV2.Client.Tests;

public class MessageSerializationTests
{
    private static string Json(RhpMessage m) =>
        Encoding.UTF8.GetString(RhpJson.Serialize(m));

    [Fact]
    public void Auth_Serializes_With_Type_User_Pass()
    {
        var json = Json(new AuthMessage { User = "g8pzt", Pass = "secret", Id = 1 });
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("auth", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("g8pzt", doc.RootElement.GetProperty("user").GetString());
        Assert.Equal("secret", doc.RootElement.GetProperty("pass").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Open_Omits_Null_Fields()
    {
        var json = Json(new OpenMessage
        {
            Pfam = ProtocolFamily.Ax25,
            Mode = SocketMode.Stream,
            Local = "G8PZT",
            Flags = (int)OpenFlags.Passive,
        });
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("local", out _));
        Assert.False(doc.RootElement.TryGetProperty("remote", out _));
        Assert.False(doc.RootElement.TryGetProperty("port", out _));
        Assert.False(doc.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public void OpenReply_Deserializes_From_Spec_Example()
    {
        var wire = """{"type":"openReply","id":7,"handle":1234,"errcode":0,"errtext":"Ok"}""";
        var msg = (OpenReplyMessage)RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        Assert.Equal(7, msg.Id);
        Assert.Equal(1234, msg.Handle);
        Assert.Equal(0, msg.ErrCode);
        Assert.Equal("Ok", msg.ErrText);
    }

    [Fact]
    public void AuthReply_Deserializes_With_CapitalC_ErrCode()
    {
        // Per the spec AUTHREPLY uses "errCode"/"errText" with capital C.
        var wire = """{"type":"authReply","id":1,"errCode":14,"errText":"Unauthorised"}""";
        var msg = (AuthReplyMessage)RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        Assert.Equal(14, msg.ErrCode);
        Assert.Equal("Unauthorised", msg.ErrText);
    }

    [Fact]
    public void Recv_With_TraceFields_Roundtrips()
    {
        var wire = """{"type":"recv","seqno":11,"handle":50,"data":"hi","action":"rcvd","srce":"M0XYZ","dest":"G8PZT","ctrl":3,"frametype":"RR","rseq":4,"cr":"R","pf":"F"}""";
        var msg = (RecvMessage)RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        Assert.Equal(11, msg.Seqno);
        Assert.Equal(50, msg.Handle);
        Assert.Equal("RR", msg.FrameType);
        Assert.Equal("rcvd", msg.Action);
        Assert.Equal(4, msg.Rseq);
    }

    [Fact]
    public void Status_FromServer_Decodes_Flags()
    {
        var wire = """{"type":"status","seqno":2,"handle":9,"flags":6}""";
        var msg = (StatusMessage)RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        var flags = (StatusFlags)(msg.Flags ?? 0);
        Assert.True(flags.HasFlag(StatusFlags.Connected));
        Assert.True(flags.HasFlag(StatusFlags.Busy));
        Assert.False(flags.HasFlag(StatusFlags.ConOk));
    }

    [Fact]
    public void Accept_Decodes_Child_And_Remote()
    {
        var wire = """{"type":"accept","seqno":3,"handle":1,"child":2,"remote":"M0XYZ","local":"G8PZT","port":2}""";
        var msg = (AcceptMessage)RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        Assert.Equal(1, msg.Handle);
        Assert.Equal(2, msg.Child);
        Assert.Equal("M0XYZ", msg.Remote);
    }

    [Fact]
    public void Unknown_Type_Yields_UnknownMessage()
    {
        var wire = """{"type":"newFutureMessage","id":99,"foo":"bar"}""";
        var msg = RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        var unk = Assert.IsType<UnknownMessage>(msg);
        Assert.Equal("newFutureMessage", unk.Type);
        Assert.Equal(99, unk.Id);
    }

    [Fact]
    public void Missing_Type_Throws_ProtocolException()
    {
        var wire = """{"id":1,"errcode":0}""";
        Assert.Throws<RhpProtocolException>(
            () => RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire)));
    }

    [Fact]
    public void ConnectReply_Tolerates_PascalCase_Variant()
    {
        // The PWP-0222 spec writes the type as "ConnectReply" — be forgiving.
        var wire = """{"type":"ConnectReply","id":1,"handle":50,"errcode":0,"errtext":"Ok"}""";
        var msg = RhpJson.Deserialize(Encoding.UTF8.GetBytes(wire));
        var typed = Assert.IsType<ConnectReplyMessage>(msg);
        Assert.Equal(50, typed.Handle);
    }

    [Fact]
    public void DataEncoding_PreservesBinaryBytes()
    {
        var bytes = new byte[] { 0x00, 0x7F, 0x80, 0xFF, 0x01, 0x0A };
        var wire = RhpDataEncoding.ToWireString(bytes);
        var back = RhpDataEncoding.FromWireString(wire);
        Assert.Equal(bytes, back);
    }
}
