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

    // --- Dinamik Ayarlar ---
    public static int AgentCount = 5;
    public static int MaxDays = 1800; // 5 Yıl

    // YENİ EKLENEN: Modlara özel ek veriler
    public static int DifficultyLevel = 1; // 0: Kolay, 1: Orta, 2: Zor
    public static float CustomMultiplier = 1.0f; // İlerideki modlar (Sarayın Sabrı vs) için hazır bulunsun
}