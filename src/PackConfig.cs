namespace MondayCassettePack;

public sealed class TapeDefinition
{
    public string Key;
    public string Sprite;
    public TapeDefinition(string key, string sprite) { Key = key; Sprite = sprite; }
}

// Legacy build defaults. The Studio builder generates this file per pack.
public static class PackConfig
{
    public const string ModId = "music_card_one";
    public const string ReceiptKey = "delivery_gift_v2";
    public const string GiftSprite = "ffxiv_gift_box_v1";
    public const int DeliveryDelayDays = 1, TapeValue = 150, BoxWidth = 12, BoxHeight = 9;
    public static readonly TapeDefinition[] Tapes = {
        new TapeDefinition("01", "cassette1"), new TapeDefinition("02", "cassette10"),
        new TapeDefinition("03", "cassette3"), new TapeDefinition("04", "cassette4"),
        new TapeDefinition("05", "cassette5"), new TapeDefinition("06", "cassette6"),
        new TapeDefinition("07", "cassette7"), new TapeDefinition("08", "cassette8"),
        new TapeDefinition("09", "cassette9")
    };
}
