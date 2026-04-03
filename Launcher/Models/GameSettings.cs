namespace LauncherWinUI.Models
{
    public class GameSettingsFile_Camera
    {
        public float max_height_only_when_lobby_host { get; set; } = 310f;
        public float min_height { get; set; } = 210f;
        public float move_speed_ratio { get; set; } = 1f;
    }
    public class GameSettingsFile_Chat
    {
        public int duration_seconds_until_fade_out { get; set; } = 30;
    }
    public class GameSettingsFile_Render
    {
        public int fps_limit { get; set; } = 60;
        public bool limit_framerate { get; set; } = true;
        public bool stats_overlay { get; set; } = true;
    }
    public class GameSettingsFile_Social
    {
        public bool notification_friend_comes_online_menus { get; set; } = true;
        public bool notification_friend_comes_online_gameplay { get; set; } = true;
        public bool notification_friend_goes_offline_menus { get; set; } = true;
        public bool notification_friend_goes_offline_gameplay { get; set; } = true;
        public bool notification_player_accepts_request_menus { get; set; } = true;
        public bool notification_player_accepts_request_gameplay { get; set; } = true;
        public bool notification_player_sends_request_menus { get; set; } = true;
        public bool notification_player_sends_request_gameplay { get; set; } = true;
    }
    public class GameSettingsFile_Network
    {
        public int http_version { get; set; } = 0;
        public bool use_alternative_endpoint { get; set; } = false;
    }
    public class GameSettingsFile
    {
        public GameSettingsFile_Camera camera { get; set; } = new();
        public GameSettingsFile_Chat chat { get; set; } = new();
        public GameSettingsFile_Render render { get; set; } = new();
        public GameSettingsFile_Social social { get; set; } = new();
        public GameSettingsFile_Network network { get; set; } = new();
    }
    public class LauncherSettingsFile
    {
        public bool prefer_experiemental_client { get; set; } = true;
    }

    public static class LaunchOptions
    {
        public static bool Windowed { get; set; } = false;
        public static int WindowedWidth { get; set; } = 1920;
        public static int WindowedHeight { get; set; } = 1080;
    }
}
