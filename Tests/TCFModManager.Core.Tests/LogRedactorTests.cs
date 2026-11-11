using TCFModManager.Core.Services;
using Xunit;

namespace TCFModManager.Core.Tests;

public sealed class LogRedactorTests
{
    [Fact]
    public void The_machine_name_is_replaced_as_a_whole_word()
    {
        var redacted = LogRedactor.Redact(
            "on DESKTOP-AB12 at noon; desktop-ab12 again; DESKTOP-AB12X stays",
            new RedactionValues { MachineName = "DESKTOP-AB12" });

        Assert.Equal("on <machine> at noon; <machine> again; DESKTOP-AB12X stays", redacted);
    }

    [Fact]
    public void The_server_map_host_is_replaced_with_and_without_a_port()
    {
        var redacted = LogRedactor.Redact(
            "connect https://203.0.113.7:6969/tcfmm/hello; ping 203.0.113.7 ok",
            new RedactionValues { ServerHost = "203.0.113.7" });

        Assert.Equal("connect https://<server>:6969/tcfmm/hello; ping <server> ok", redacted);
    }

    [Fact]
    public void A_server_map_host_name_is_replaced_but_not_inside_a_longer_name()
    {
        var redacted = LogRedactor.Redact(
            "wss://Play.Example.net:6969 then play.example.net. and play.example.net.evil.com and xplay.example.net",
            new RedactionValues { ServerHost = "play.example.net" });

        Assert.Equal(
            "wss://<server>:6969 then <server>. and play.example.net.evil.com and xplay.example.net",
            redacted);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public void A_loopback_server_map_host_is_left_alone(string host)
    {
        var text = $"server https://{host}:6969 ready";

        Assert.Equal(text, LogRedactor.Redact(text, new RedactionValues { ServerHost = host }));
    }

    [Fact]
    public void The_shared_key_and_the_thumbprint_are_replaced_by_value()
    {
        var thumbprint = new string('a', 32) + new string('B', 32);
        var redacted = LogRedactor.Redact(
            $"key=s3cr3t-Key+42 pin={thumbprint.ToUpperInvariant()} done",
            new RedactionValues { SharedKey = "s3cr3t-Key+42", Thumbprint = thumbprint });

        Assert.Equal("key=<key> pin=<thumbprint> done", redacted);
    }

    [Fact]
    public void Values_too_short_to_be_safe_to_replace_are_ignored()
    {
        var text = "PC abc PCs abcdef";

        var redacted = LogRedactor.Redact(text, new RedactionValues { MachineName = "PC", SharedKey = "abc", UserName = "ab" });

        Assert.Equal(text, redacted);
    }

    [Fact]
    public void The_two_argument_overload_is_unchanged()
    {
        var text = @"C:\Users\jdoe\SPT and jdoe on 6ab93267b208402e592581aa from 192.168.1.20:25565";

        Assert.Equal(
            LogRedactor.Redact(text, new RedactionValues { UserProfile = @"C:\Users\jdoe", UserName = "jdoe" }),
            LogRedactor.Redact(text, @"C:\Users\jdoe", "jdoe"));
    }

    [Fact]
    public void The_server_host_is_taken_out_before_the_address_rule_sees_it()
    {
        var redacted = LogRedactor.Redact(
            "host 203.0.113.7:6969 and player 198.51.100.4:25565",
            new RedactionValues { ServerHost = "203.0.113.7" });

        Assert.Equal("host <server>:6969 and player <ip>:25565", redacted);
    }
}
