using System.Text.Json;
using Clawsharp.Channels.WeCom;

namespace Clawsharp.Tests.Channels;

[TestFixture]
public sealed class WeComChannelTests
{
    // ── Message deserialization tests ─────────────────────────────────────

    [Test]
    public void TextMessage_Deserializes()
    {
        const string json = """
                            {
                                "msgid": "id1",
                                "aibotid": "bot1",
                                "chatid": "sess1",
                                "chattype": "single",
                                "from": {"userid": "user123"},
                                "response_url": "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc",
                                "msgtype": "text",
                                "text": {"content": "hello world"}
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.MsgId.ShouldBe("id1");
        msg.AiBotId.ShouldBe("bot1");
        msg.ChatId.ShouldBe("sess1");
        msg.ChatType.ShouldBe("single");
        msg.From.ShouldNotBeNull();
        msg.From!.UserId.ShouldBe("user123");
        msg.ResponseUrl.ShouldBe("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc");
        msg.MsgType.ShouldBe("text");
        msg.Text.ShouldNotBeNull();
        msg.Text!.Content.ShouldBe("hello world");
    }

    [Test]
    public void VoiceMessage_Deserializes()
    {
        const string json = """
                            {
                                "msgid": "id2",
                                "chattype": "single",
                                "from": {"userid": "user456"},
                                "response_url": "https://example.com",
                                "msgtype": "voice",
                                "voice": {"content": "transcribed speech"}
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.MsgType.ShouldBe("voice");
        msg.Voice.ShouldNotBeNull();
        msg.Voice!.Content.ShouldBe("transcribed speech");
    }

    [Test]
    public void MixedMessage_Deserializes()
    {
        const string json = """
                            {
                                "msgid": "id3",
                                "chattype": "group",
                                "chatid": "group123",
                                "from": {"userid": "user789"},
                                "response_url": "https://example.com",
                                "msgtype": "mixed",
                                "mixed": {
                                    "msg_item": [
                                        {"msgtype": "text", "text": {"content": "look at this"}},
                                        {"msgtype": "image", "image": {"url": "https://img.example.com/pic.jpg"}},
                                        {"msgtype": "text", "text": {"content": "what do you think?"}}
                                    ]
                                }
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.MsgType.ShouldBe("mixed");
        msg.Mixed.ShouldNotBeNull();
        msg.Mixed!.MsgItem.ShouldNotBeNull();
        msg.Mixed.MsgItem!.Count.ShouldBe(3);
        msg.Mixed.MsgItem[0].MsgType.ShouldBe("text");
        msg.Mixed.MsgItem[0].Text!.Content.ShouldBe("look at this");
        msg.Mixed.MsgItem[1].MsgType.ShouldBe("image");
        msg.Mixed.MsgItem[1].Image!.Url.ShouldBe("https://img.example.com/pic.jpg");
    }

    [Test]
    public void ImageMessage_Deserializes()
    {
        const string json = """
                            {
                                "msgid": "id4",
                                "chattype": "single",
                                "from": {"userid": "user000"},
                                "response_url": "https://example.com",
                                "msgtype": "image",
                                "image": {"url": "https://img.example.com/photo.png"}
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.MsgType.ShouldBe("image");
        msg.Image.ShouldNotBeNull();
        msg.Image!.Url.ShouldBe("https://img.example.com/photo.png");
    }

    // ── Reply serialization tests ────────────────────────────────────────

    [Test]
    public void ReplyMessage_Serializes()
    {
        var reply = new WeComReplyMessage
        {
            Text = new WeComReplyText { Content = "Hello back!" }
        };

        var json = JsonSerializer.Serialize(reply, WeComBotJsonContext.Default.WeComReplyMessage);

        json.ShouldContain("\"msgtype\":\"text\"");
        json.ShouldContain("\"content\":\"Hello back!\"");
    }

    [Test]
    public void ApiResponse_Deserializes()
    {
        const string json = """{"errcode":0,"errmsg":"ok"}""";

        var resp = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComApiResponse);

        resp.ShouldNotBeNull();
        resp.ErrCode.ShouldBe(0);
        resp.ErrMsg.ShouldBe("ok");
    }

    // ── Group vs single chat tests ──────────────────────────────────────

    [Test]
    public void GroupChatType_HasChatId()
    {
        const string json = """
                            {
                                "msgid": "g1",
                                "chattype": "group",
                                "chatid": "grp_abc",
                                "from": {"userid": "user1"},
                                "response_url": "https://example.com",
                                "msgtype": "text",
                                "text": {"content": "group msg"}
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.ChatType.ShouldBe("group");
        msg.ChatId.ShouldBe("grp_abc");
    }

    // ── Null handling ────────────────────────────────────────────────────

    [Test]
    public void MinimalMessage_Deserializes()
    {
        const string json = """{"msgtype":"text"}""";

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.MsgType.ShouldBe("text");
        msg.From.ShouldBeNull();
        msg.Text.ShouldBeNull();
        msg.MsgId.ShouldBeNull();
    }

    [Test]
    public void EmptyMixed_Deserializes()
    {
        const string json = """
                            {
                                "msgtype": "mixed",
                                "mixed": {"msg_item": []}
                            }
                            """;

        var msg = JsonSerializer.Deserialize(json, WeComBotJsonContext.Default.WeComBotMessage);

        msg.ShouldNotBeNull();
        msg.Mixed.ShouldNotBeNull();
        msg.Mixed!.MsgItem.ShouldNotBeNull();
        msg.Mixed.MsgItem!.Count.ShouldBe(0);
    }
}