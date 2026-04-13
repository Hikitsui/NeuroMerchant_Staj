// SessionData.cs
public static class SessionData
{
    // Gelecekte eklenecek Oyuncu modu için şimdiden hazırız!
    public enum GameType { AIVsAI, PlayerVsAI }

    // 5 Efsanevi Modumuz
    public enum GameMode { AltinYolu, AcimasizKis, SarayinElcisi, Tekel, Loncalar }

    // Varsayılan seçimler (Menüde değişecek)
    public static GameType CurrentType = GameType.AIVsAI;
    public static GameMode CurrentMode = GameMode.AltinYolu;

    // Modların kural ayarları (İleride menüden slider ile de değiştirebilirsin)
    public static int AgentCount = 5;
    public static int MaxDays = 1800; // 5 Yıl
}