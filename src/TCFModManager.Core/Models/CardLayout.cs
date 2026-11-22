using System.Text.Json.Serialization;

namespace TCFModManager.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<CardLayoutPage>))]
public enum CardLayoutPage
{
    Installed,
    Browse,
}

[JsonConverter(typeof(JsonStringEnumConverter<CardSize>))]
public enum CardSize
{
    Compact,
    Normal,
    Roomy,
}

[JsonConverter(typeof(JsonStringEnumConverter<CardWidth>))]
public enum CardWidth
{
    Narrow,
    Normal,
    Wide,
}

[JsonConverter(typeof(JsonStringEnumConverter<CardPicture>))]
public enum CardPicture
{
    Off,
    Small,
    Banner,
}

public sealed class CardLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public CardLayoutPage Page { get; set; }

    public List<string> Fields { get; set; } = [];

    public List<string>? ListFields { get; set; }

    public CardSize Size { get; set; } = CardSize.Normal;

    public CardWidth Width { get; set; } = CardWidth.Normal;

    public CardPicture Picture { get; set; } = CardPicture.Off;

    public bool DetailsInWindow { get; set; }

    public int? FieldsRevision { get; set; }
}
