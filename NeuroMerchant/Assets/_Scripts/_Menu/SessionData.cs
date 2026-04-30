// SessionData.cs
public static class SessionData
{
    // Gelecekte eklenecek Oyuncu modu için şimdiden hazırız!
    public enum GameType { AIVsAI, PlayerVsAI }

    // 5 Efsanevi Modumuz
    public enum GameMode { AltinYolu, AcimasizKis, SarayinElcisi, TekelSavaslari, LoncalarIttifaki }

    // Varsayılan seçimler (Menüde değişecek)
    public static GameType CurrentType = GameType.AIVsAI;
    public static GameMode CurrentMode = GameMode.AltinYolu;

    // --- Dinamik Ayarlar ---
    public static int AgentCount = 5;
    public static int MaxDays = 1800; // 5 Yıl
    public static int GuildCount = 5; // 5 lonca sayisi

    public static int DifficultyLevel = 1; // 0: Kolay, 1: Orta, 2: Zor

}