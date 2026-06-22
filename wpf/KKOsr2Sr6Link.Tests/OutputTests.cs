using System.Text.Json.Nodes;
using KKOsr2Sr6Link.Wpf.Engine;

namespace KKOsr2Sr6Link.Tests;

public class SerialOutputTests
{
    [Fact]
    public void FormatLine_PadsTo3Digits_WithIntervalSuffix()
    {
        Assert.Equal("L0123I100\r\n", SerialOutput.FormatLine(Axis.L0, 123, 100));
        Assert.Equal("L0000I100\r\n", SerialOutput.FormatLine(Axis.L0, 0, 100));
        Assert.Equal("R2999I300\r\n", SerialOutput.FormatLine(Axis.R2, 999, 300));
    }

    [Fact]
    public void Scale_MapsRawToAxisRange_Truncated()
    {
        Assert.Equal(0, SerialOutput.Scale(0, 0, 999));
        Assert.Equal(999, SerialOutput.Scale(999, 0, 999));
        // raw 500 into [100,900]: 800/999*500+100 = 500.3 -> 500
        Assert.Equal(500, SerialOutput.Scale(500, 100, 900));
    }
}

public class ButtplugClientTests
{
    [Fact]
    public void BuildHandshake_HasExpectedClientName()
    {
        var root = JsonNode.Parse(ButtplugClient.BuildHandshake())!.AsArray();
        var rsi = root[0]!["RequestServerInfo"]!;
        Assert.Equal("Link_osr2_sr6_to_kk_studio", (string)rsi["ClientName"]!);
        Assert.Equal(1, (int)rsi["MessageVersion"]!);
    }

    [Fact]
    public void BuildLinearCmd_NormalizesPositionAndRoutesFields()
    {
        var root = JsonNode.Parse(ButtplugClient.BuildLinearCmd(featureIndex: 2, deviceIndex: 3, durationMs: 100, move: 500))!.AsArray();
        var cmd = root[0]!["LinearCmd"]!;
        Assert.Equal(3, (int)cmd["DeviceIndex"]!);
        var vec = cmd["Vectors"]!.AsArray()[0]!;
        Assert.Equal(2, (int)vec["Index"]!);
        Assert.Equal(100, (int)vec["Duration"]!);
        Assert.Equal(0.5, (double)vec["Position"]!, 3);
    }

    [Fact]
    public void HandleMessage_DeviceList_PopulatesLinearDevice()
    {
        var client = new ButtplugClient();
        var msg = """
        [{"DeviceList":{"Devices":[
          {"DeviceIndex":1,"DeviceName":"The Handy","DeviceMessages":{"LinearCmd":{"FeatureCount":1}}},
          {"DeviceIndex":2,"DeviceName":"Buzzer","DeviceMessages":{"VibrateCmd":{"FeatureCount":2}}}
        ]}}]
        """;
        client.HandleMessage(msg);

        Assert.Equal(2, client.Devices.Count);
        var handy = client.Devices[0];
        Assert.Equal("The Handy", handy.Name);
        Assert.True(handy.IsLinear);
        Assert.Single(handy.Feature);
        Assert.False(client.Devices[1].IsLinear);
    }

    [Fact]
    public void HandleMessage_DeduplicatesByIndex()
    {
        var client = new ButtplugClient();
        client.HandleMessage("""[{"DeviceAdded":{"DeviceIndex":1,"DeviceName":"A","DeviceMessages":{"LinearCmd":{"FeatureCount":1}}}}]""");
        client.HandleMessage("""[{"DeviceAdded":{"DeviceIndex":1,"DeviceName":"A again","DeviceMessages":{"LinearCmd":{"FeatureCount":1}}}}]""");
        Assert.Single(client.Devices);
    }
}
